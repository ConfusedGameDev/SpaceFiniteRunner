using System.Collections;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.FX;
using ConfusedGameDev.FiniteRunner.PoliceEscape.UI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using FiniteRunner;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// Runs a <see cref="LevelDefinition"/>: the city chase's objective list,
    /// told through the FiniteRunner RpgMessageSystem dialogue box (shared
    /// across both games — it auto-creates its own overlay canvas) and the
    /// <see cref="ObjectiveHud"/> line this manager spawns. Objectives play
    /// top to bottom; each step briefs once when it first becomes active.
    ///
    /// Step kinds: ReachSpeed and EscapePolice are STATE steps (true right
    /// now); SurviveTime and GoToTarget are PROGRESS steps (a timer, an
    /// arrival). In <see cref="CompletionMode.Independent"/> a finished step
    /// stays finished. In <see cref="CompletionMode.AllMustHold"/> a finished
    /// state step is re-checked every frame (speed with a small hysteresis):
    /// the lowest one that no longer holds becomes current again and every
    /// later step's progress resets — timers to zero, arrivals forgotten — so
    /// the level only completes when everything holds at once. Regressed
    /// steps re-activate silently; the HUD carries them.
    ///
    /// The asset is read LIVE every frame — no runtime clone — so the debug
    /// menu's objective sliders apply instantly (and persist straight into
    /// the asset, like the other city pages). Completing shows the level's
    /// completion line, and only once it has disappeared slams the fullscreen
    /// glitch to max, holds, and hands over to the next scene ADDITIVELY —
    /// its own GlitchController starts at max and fades in, so the swap hides
    /// inside the corruption.
    ///
    /// The glitch doubles as the damage meter: every hard impact pulses it,
    /// police hits permanently raise the base level, and at full corruption
    /// the run ends: the glitch holds at max, then the
    /// <see cref="UI.GameOverScreen"/> asks RETRY? — YES reloads the scene,
    /// NO returns to the main menu. Damage knobs stay here — they are the
    /// chase's feel, not a level's design.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        /// <summary>A held speed step only regresses this far under its target — no flicker on the threshold.</summary>
        const float HoldToleranceKmh = 3f;

        [TitleGroup("Level")]
        [Tooltip("The objective list this scene plays. Edited right here — it is the asset itself, shared with the debug menu's LEVEL page.")]
        [Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public LevelDefinition level;

        [TitleGroup("Damage")]
        [Tooltip("Glitch pulse on any hard impact.")]
        [PropertyRange(0f, 1f)]
        public float collisionPulse = 0.4f;

        [TitleGroup("Damage")]
        [Tooltip("Permanent glitch added per police hit — at full corruption the level reboots. Default: three hits and you're out.")]
        [PropertyRange(0.05f, 1f)]
        public float policeHitIntensity = 0.34f;

        [TitleGroup("Damage")]
        [Tooltip("Impacts slower than this are scrapes: no pulse, no damage.")]
        [PropertyRange(0f, 10f), SuffixLabel("m/s", true)]
        public float minImpactSpeed = 3f;

        [TitleGroup("Damage")]
        [Tooltip("How long the screen holds at full corruption before the level reboots.")]
        [PropertyRange(0.2f, 4f), SuffixLabel("s", true)]
        public float resetDelaySeconds = 1.2f;

        static readonly Color DoneAccent = new(0.45f, 1f, 0.55f);

        /// <summary>Per-step runtime progress. Never on the asset: it is shared and read live.</summary>
        class ObjectiveState
        {
            public bool done;
            public float timer;      // SurviveTime: seconds this step has been current
            public bool huntSeen;    // EscapePolice: a pursuit was observed since activation
            public bool briefed;     // the step's dialogue line has played
            public bool warnedMissingTarget;
        }

        ObjectiveState[] states = System.Array.Empty<ObjectiveState>();
        int current;
        CarController player;
        PoliceCarInput[] patrols = System.Array.Empty<PoliceCarInput>();
        float retargetTimer;
        bool loading;
        bool resetting;
        bool warnedEmpty;

        public LevelDefinition Level => level;
        public CarController Player => player;

        /// <summary>Index of the active step; equals the count once the level is complete.</summary>
        public int CurrentIndex => current;

        /// <summary>Every objective done — the completion line / handoff is playing.</summary>
        public bool Completed { get; private set; }

        /// <summary>The active step, or null when there is none.</summary>
        public LevelObjective CurrentObjective =>
            level != null && current >= 0 && current < level.Count ? level.objectives[current] : null;

        public bool IsDone(int index) => index >= 0 && index < states.Length && states[index].done;

        /// <summary>Seconds a SurviveTime step has been held (0 for other kinds).</summary>
        public float Timer(int index) => index >= 0 && index < states.Length ? states[index].timer : 0f;

        /// <summary>
        /// Horizontal distance from the player to a GoToTarget step's target.
        /// False when the step is not a go-to, there is no player, or the id
        /// is not registered in the scene (see <see cref="IsTargetMissing"/>).
        /// </summary>
        public bool TryGetTargetDistance(int index, out float meters)
        {
            meters = 0f;
            var step = Objective(index);
            if (step == null || step.type != ObjectiveType.GoToTarget || player == null) return false;
            if (!TargetObject.TryFind(step.targetId, out var target)) return false;
            meters = TargetObject.HorizontalDistance(player.transform.position, target.Position);
            return true;
        }

        /// <summary>True for a go-to step whose id has no enabled TargetObject in the scene.</summary>
        public bool IsTargetMissing(int index)
        {
            var step = Objective(index);
            return step != null && step.type == ObjectiveType.GoToTarget && !TargetObject.TryFind(step.targetId, out _);
        }

        LevelObjective Objective(int index) =>
            level != null && index >= 0 && index < level.Count ? level.objectives[index] : null;

        void Awake()
        {
            // Never run without a level: an in-memory default (the pre-data
            // flow) keeps the scene playable instead of throwing every frame.
            if (level == null)
            {
                Debug.LogError($"{nameof(LevelManager)} has no {nameof(LevelDefinition)} assigned — playing the default level (reach 130 km/h, escape the police).", this);
                level = LevelDefinition.CreateDefault();
            }
            SyncStates();
            ObjectiveHud.Spawn(this);
        }

        void Update()
        {
            if (Completed || resetting) return;
            float dt = Time.deltaTime;
            RefreshTargets(dt);
            if (player == null) return; // the car spawns a beat after play starts

            if (level.Count == 0)
            {
                if (!warnedEmpty) Debug.LogWarning($"{nameof(LevelDefinition)} '{level.name}' has no objectives — completing immediately.", this);
                warnedEmpty = true;
                Complete();
                return;
            }
            SyncStates();

            // All-must-hold: a finished state step that stopped holding pulls
            // the level back to it and wipes everything after it.
            if (level.mode == CompletionMode.AllMustHold)
            {
                int regressed = FindRegressed();
                if (regressed >= 0) Regress(regressed);
            }

            ObjectiveState state = states[current];
            if (!state.briefed) Brief(current);

            if (!Evaluate(current, dt)) return;
            state.done = true;
            current++;
            if (current >= level.Count) Complete();
        }

        /// <summary>The runtime state array tracks the asset's list (rows may be added in the inspector mid-play).</summary>
        void SyncStates()
        {
            int count = level.Count;
            if (states.Length == count) return;
            var next = new ObjectiveState[count];
            for (int i = 0; i < count; i++) next[i] = i < states.Length ? states[i] : new ObjectiveState();
            states = next;
            current = Mathf.Clamp(current, 0, Mathf.Max(0, count - 1));
        }

        /// <summary>Is this step satisfied right now (advancing its progress by dt if it is a timer)?</summary>
        bool Evaluate(int index, float dt)
        {
            LevelObjective step = level.objectives[index];
            ObjectiveState state = states[index];
            switch (step.type)
            {
                case ObjectiveType.ReachSpeed:
                    return player.SpeedKmh >= step.targetSpeedKmh;

                case ObjectiveType.EscapePolice:
                {
                    bool hunted = AnyPatrolHunting();
                    if (hunted) state.huntSeen = true;
                    return !hunted && (!step.mustBeHuntedFirst || state.huntSeen);
                }

                case ObjectiveType.SurviveTime:
                    state.timer += dt;
                    return state.timer >= step.surviveSeconds;

                case ObjectiveType.GoToTarget:
                {
                    if (!TargetObject.TryFind(step.targetId, out TargetObject target))
                    {
                        if (!state.warnedMissingTarget)
                            Debug.LogWarning($"[Level] objective {index + 1} wants TargetObject '{step.targetId}' but none is in the scene — it can never complete.", this);
                        state.warnedMissingTarget = true;
                        return false;
                    }
                    return TargetObject.HorizontalDistance(player.transform.position, target.Position) <= step.arriveRadius;
                }
            }
            return false;
        }

        /// <summary>Does a finished STATE step still hold? Progress steps always do — they latch.</summary>
        bool StillHolds(int index)
        {
            LevelObjective step = level.objectives[index];
            switch (step.type)
            {
                case ObjectiveType.ReachSpeed:
                    return player.SpeedKmh >= step.targetSpeedKmh - HoldToleranceKmh;
                case ObjectiveType.EscapePolice:
                    return !AnyPatrolHunting();
                default:
                    return true;
            }
        }

        int FindRegressed()
        {
            for (int i = 0; i < current && i < states.Length; i++)
                if (states[i].done && !StillHolds(i)) return i;
            return -1;
        }

        void Regress(int index)
        {
            for (int i = index; i < states.Length; i++)
            {
                states[i].done = false;
                if (i > index) states[i].timer = 0f; // later progress is forfeited; the regressed step's own state stays
            }
            current = index;
        }

        void Brief(int index)
        {
            LevelObjective step = level.objectives[index];
            states[index].briefed = true;
            RpgMessageSystem.Instance.ShowMessage(level.speakerName, step.BriefingText, level.messageHoldSeconds, step.Accent);
        }

        // Completion: the line first; the glitch handoff only once it is gone.
        // Completed goes up before the message so evaluation and police
        // damage stop — a hit during the line must not start a reboot too.
        void Complete()
        {
            Completed = true;
            RpgMessageSystem.Instance.ShowMessage(
                level.speakerName, level.completionMessage, level.messageHoldSeconds, DoneAccent,
                onFinished: BeginGlitchHandoff);
        }

        void BeginGlitchHandoff()
        {
            if (loading) return; // the message system may invoke duplicate-dropped callbacks
            loading = true;
            StartCoroutine(GlitchHandoff());
        }

        /// <summary>Slam the corruption to max, hold it a beat, then swap scenes behind it.</summary>
        IEnumerator GlitchHandoff()
        {
            // Healing stops here: the transition must hold at full glitch.
            if (GlitchController.Instance != null)
            {
                GlitchController.Instance.baseFadePerSecond = 0f;
                GlitchController.Instance.SetBaseIntensity(1f);
                GlitchController.Instance.Pulse(1f);
            }
            yield return new WaitForSeconds(level.completionGlitchHoldSeconds);
            yield return TransitionToNextScene();
        }

        /// <summary>
        /// Additive handoff behind the maxed glitch: load the next scene, make
        /// it active (its GlitchController claims the effect, starting at max
        /// and fading in), then unload this one — which takes this manager,
        /// the city and the cars with it.
        /// </summary>
        IEnumerator TransitionToNextScene()
        {
            string nextSceneName = level.nextSceneName;
            AsyncOperation load = SceneManager.LoadSceneAsync(nextSceneName, LoadSceneMode.Additive);
            yield return load;
            Scene next = SceneManager.GetSceneByName(nextSceneName);
            if (next.IsValid()) SceneManager.SetActiveScene(next);
            SceneManager.UnloadSceneAsync(gameObject.scene);
        }

        /// <summary>
        /// External game-over switch: anything that ends the run (the car
        /// wrecked on its roof, a scripted fail) asks for the same death the
        /// damage meter gives — glitch slammed to max, held, game-over screen.
        /// False when the level is already resetting or completing, so a
        /// caller knows the request was swallowed and must not fall back to
        /// its own recovery (a teleport during the glitch would be visible).
        /// </summary>
        public bool RequestReboot(string reason)
        {
            if (resetting || Completed) return false;
            Debug.Log($"[Level] {reason} — rebooting level", this);
            StartCoroutine(ResetLevel());
            return true;
        }

        /// <summary>
        /// The shared death: hold the maxed glitch a beat, then ask instead of
        /// rebooting on our own — GAME OVER, RETRY? YES reloads the level, NO
        /// abandons the run back to the main menu. The screen freezes scaled
        /// time itself and unfreezes before the chosen callback runs.
        /// </summary>
        IEnumerator ResetLevel()
        {
            resetting = true;
            // Healing stops here: the death screen must hold at full glitch.
            if (GlitchController.Instance != null)
            {
                GlitchController.Instance.baseFadePerSecond = 0f;
                GlitchController.Instance.SetBaseIntensity(1f);
                GlitchController.Instance.Pulse(1f);
            }
            yield return new WaitForSeconds(resetDelaySeconds);
            GameOverScreen.Show(onRetry: ReloadLevel, onGiveUp: ExitToMainMenu);
        }

        void ReloadLevel()
        {
            Debug.Log("[Level] retry — reloading scene now", this);
            SceneManager.LoadScene(gameObject.scene.name);
        }

        void ExitToMainMenu() => SceneManager.LoadScene(0); // MainMenu is build index 0, same as the pause menu's exit

        /// <summary>
        /// Player impact, relayed by the sensor: hard hits pulse the glitch,
        /// police hits also add permanent corruption — the damage meter.
        /// </summary>
        void OnPlayerImpact(Collision collision)
        {
            if (resetting || Completed) return;
            if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

            var glitch = GlitchController.Instance;
            if (glitch == null) return;
            glitch.Pulse(collisionPulse);

            bool policeHit = collision.rigidbody != null
                && collision.rigidbody.GetComponent<PoliceCarInput>() != null;
            if (!policeHit) return;
            glitch.SetBaseIntensity(glitch.baseIntensity + policeHitIntensity);
            Debug.Log($"[Level] police hit — corruption {glitch.baseIntensity:F2}", this);
            if (glitch.baseIntensity >= 0.999f) RequestReboot("full corruption");
        }

        /// <summary>Player and patrols come and go at runtime (spawn managers), so re-find them on a slow tick instead of every frame.</summary>
        void RefreshTargets(float dt)
        {
            retargetTimer -= dt;
            if (player != null && retargetTimer > 0f) return;
            retargetTimer = 1f;
            player = PatrolManager.FindPlayerCar();
            patrols = FindObjectsByType<PoliceCarInput>(FindObjectsSortMode.None);

            // The car is spawned from a prefab at runtime — bolt the impact
            // sensor on when we first see it (survives respawns of the same object).
            if (player != null && player.GetComponent<PlayerImpactSensor>() == null)
                player.gameObject.AddComponent<PlayerImpactSensor>().Impacted += OnPlayerImpact;
        }

        /// <summary>"Hunting" means Chase or Search — the player has only escaped once every patrol is back on Patrol.</summary>
        bool AnyPatrolHunting()
        {
            foreach (PoliceCarInput patrol in patrols)
                if (patrol != null && patrol.State != PoliceCarInput.AiState.Patrol)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Tiny relay the LevelManager bolts onto the player car at runtime:
    /// collision callbacks only land on the rigidbody's own object, and the
    /// car is a prefab the level shouldn't own — so this forwards them out.
    /// </summary>
    public class PlayerImpactSensor : MonoBehaviour
    {
        public event System.Action<Collision> Impacted;

        void OnCollisionEnter(Collision collision) => Impacted?.Invoke(collision);
    }
}
