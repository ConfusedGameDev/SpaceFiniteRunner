using System.Collections;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Stats;
using ConfusedGameDev.FiniteRunner.PoliceEscape.UI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.UI;
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
    /// now); SurviveTime, GoToTarget, ChaseCar, DestroyCars and CollectObjects
    /// are PROGRESS steps (a timer, an arrival, a kill, a kill count, a
    /// pickup count — the last two tallied from the static CarHealth.Died and
    /// Collectible.Collected events while the step is current). In <see cref="CompletionMode.Independent"/> a finished step
    /// stays finished. In <see cref="CompletionMode.AllMustHold"/> a finished
    /// state step is re-checked every frame (speed with a small hysteresis):
    /// the lowest one that no longer holds becomes current again and every
    /// later step's progress resets — timers to zero, arrivals forgotten — so
    /// the level only completes when everything holds at once. Regressed
    /// steps re-activate silently; the HUD carries them.
    ///
    /// Any step can carry a <see cref="TimeRule"/> on top of its condition:
    /// a DEADLINE (Complete Within) counts from activation and, if the
    /// condition has not been met when it runs out, shows the level's
    /// time-up line and asks for the same reboot full corruption does; a
    /// SUSTAIN (Hold For) completes only once the condition has stayed true
    /// for the whole span, a lapse restarting the count (speed with the same
    /// small hysteresis the all-must-hold check uses, so a flicker on the
    /// threshold never wipes the held seconds).
    ///
    /// A finished step may carry a COMPLETION MESSAGE and a DELAY before the
    /// next step: the advance (<see cref="BeginAdvance"/>) raises the
    /// <c>advancing</c> gate — the car keeps driving, challenges keep
    /// counting, but the main list waits — until the line has cleared and
    /// <c>nextDelaySeconds</c> have passed, then the next step activates and
    /// briefs. An All-Must-Hold regression during that wait cancels it.
    ///
    /// Optional challenges accepted at the brief are full steps too
    /// (<see cref="OptionalChallenge"/>), but they run BESIDE the list, not in
    /// it: every accepted one is checked each frame for the whole level until
    /// it completes and latches (a challenge never regresses), a Complete
    /// Within deadline that runs out fails the CHALLENGE rather than the run,
    /// and Destroy Cars challenges tally every matching kill while live. Each
    /// outcome gets its own dialogue line, and <see cref="EarnedReward"/> —
    /// the reward base × the multipliers of the challenges actually completed
    /// — is the running payout; <see cref="MissionReward"/> is only the offer.
    /// Nothing is banked HERE: completion records every objective row and
    /// challenge outcome into the profile, and the runner's Mission Complete
    /// panel (after the escape run this level hands over to) pays the whole
    /// mission once.
    ///
    /// A step flagged with a cinema plays its clip through the scene's
    /// <see cref="CinemaSystem"/> the moment it activates — the world frozen
    /// under it when the step pauses the game (the default), the objective
    /// loop gated on <c>cinemaOpen</c> exactly as the mission brief gates
    /// it; a step whose cinema lets the game run raises no gate, so it is
    /// live under the picture — and briefs its line only once the cinema
    /// has handed back (or been displaced by another) with time running.
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
    /// <see cref="Screens.GameOverScreen"/> asks RETRY? — YES reloads the scene,
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

        [TitleGroup("Level")]
        [Tooltip("Skip the mission-brief screen and launch straight into the run — no challenges accepted, base reward.")]
        public bool skipMissionBrief;

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
        [Tooltip("Impact speed at which the crash rumble hits full strength; between the scrape threshold and this it scales up with how hard you hit.")]
        [PropertyRange(5f, 40f), SuffixLabel("m/s", true)]
        public float crashRumbleFullSpeed = 20f;

        [TitleGroup("Damage")]
        [Tooltip("How long a full-strength crash rumble lasts (lighter hits are shorter).")]
        [PropertyRange(0.05f, 1f), SuffixLabel("s", true)]
        public float crashRumbleDuration = 0.4f;

        [TitleGroup("Damage")]
        [Tooltip("How long the screen holds at full corruption before the level reboots.")]
        [PropertyRange(0.2f, 4f), SuffixLabel("s", true)]
        public float resetDelaySeconds = 1.2f;

        static readonly Color DoneAccent = new(0.45f, 1f, 0.55f);

        /// <summary>Per-step runtime progress. Never on the asset: it is shared and read live.</summary>
        class ObjectiveState
        {
            public bool done;
            public float timer;      // SurviveTime: seconds current; deadline: seconds elapsed; hold: seconds held in a row
            public bool huntSeen;    // EscapePolice: a pursuit was observed since activation
            public bool carKilled;   // ChaseCar: the escaping car was seen dead
            public int tally;        // DestroyCars: matching cars that died / CollectObjects: matching pickups, while the step was current
            public float jumpBest;   // Jump: the best landed jump so far, in the step's measure (metres or seconds)
            public bool briefed;     // the step's dialogue line has played
            public bool failed;      // challenges only: the deadline ran out — the multiplier is lost
            public bool warnedMissingTarget;
        }

        ObjectiveState[] states = System.Array.Empty<ObjectiveState>();
        ObjectiveState[] challengeStates = System.Array.Empty<ObjectiveState>(); // one per ACCEPTED challenge, parallel to acceptedChallenges
        readonly System.Collections.Generic.List<OptionalChallenge> acceptedChallenges = new();
        bool briefOpen;
        bool cinemaOpen;    // a step's cinema is FREEZING the world — the gate; a cinema the game runs under never raises it
        bool cinemaPlaying; // a step's cinema is up at all (frozen or not) — what OnDisable has to cancel
        bool advancing;    // the current step is done; its completion line / delay is playing out before the next one starts
        int advanceToken;  // bumped by a regression so a pending advance knows it was cancelled
        int current;
        CarController player;
        PoliceCarInput[] patrols = System.Array.Empty<PoliceCarInput>();
        float retargetTimer;
        float promoteTimer;
        bool loading;
        bool resetting;
        bool timedOut;
        string lastDamageReason; // what last filled the corruption meter — a police hit makes the reboot an arrest
        bool warnedEmpty;

        public LevelDefinition Level => level;
        public CarController Player => player;

        /// <summary>Index of the active step; equals the count once the level is complete.</summary>
        public int CurrentIndex => current;

        /// <summary>Every objective done — the completion line / handoff is playing.</summary>
        public bool Completed { get; private set; }

        /// <summary>True while the finished current step's completion message / delay plays out before the next step activates.</summary>
        public bool Advancing => advancing;

        /// <summary>The optional challenges the player toggled on at the brief (empty when it was skipped).</summary>
        public System.Collections.Generic.IReadOnlyList<OptionalChallenge> AcceptedChallenges => acceptedChallenges;

        /// <summary>The payout on OFFER at the brief: the reward base (flat bonus + objective rewards) × every accepted challenge's multiplier. <see cref="EarnedReward"/> is the running payout.</summary>
        public int MissionReward { get; private set; }

        /// <summary>The payout earned so far: the reward base × the multiplier of every accepted challenge that has COMPLETED. The runner's panel adds the run's rows before banking.</summary>
        public int EarnedReward
        {
            get
            {
                long reward = level != null ? level.RewardBase : 0;
                for (int i = 0; i < acceptedChallenges.Count && i < challengeStates.Length; i++)
                    if (challengeStates[i].done) reward *= Mathf.Max(1, acceptedChallenges[i].multiplier);
                return (int)System.Math.Min(reward, int.MaxValue);
            }
        }

        /// <summary>How many accepted challenges have completed.</summary>
        public int ChallengesCompleted
        {
            get
            {
                int count = 0;
                foreach (ObjectiveState state in challengeStates) if (state.done) count++;
                return count;
            }
        }

        /// <summary>Position of a challenge in the accepted list, or -1 when the player did not take it on.</summary>
        public int AcceptedIndex(OptionalChallenge challenge) => acceptedChallenges.IndexOf(challenge);

        /// <summary>
        /// Takes on a challenge mid-level — a <see cref="ChallengeTrigger"/>
        /// found on the road: it joins the accepted list with a fresh state,
        /// starts counting on the next frame, shows on the map and raises the
        /// offered payout by its multiplier. False when the level is ending
        /// or the same challenge is already accepted.
        /// </summary>
        public bool AcceptChallenge(OptionalChallenge challenge)
        {
            if (challenge == null || Completed || resetting || acceptedChallenges.Contains(challenge)) return false;
            acceptedChallenges.Add(challenge);
            var next = new ObjectiveState[acceptedChallenges.Count];
            challengeStates.CopyTo(next, 0);
            next[next.Length - 1] = new ObjectiveState { briefed = true };
            challengeStates = next;
            MissionReward = (int)System.Math.Min((long)MissionReward * Mathf.Max(1, challenge.multiplier), int.MaxValue);
            Debug.Log($"[Level] challenge accepted on the road — {challenge.ChallengeSummary}", this);
            return true;
        }

        public bool IsChallengeDone(int index) => index >= 0 && index < challengeStates.Length && challengeStates[index].done;

        public bool IsChallengeFailed(int index) => index >= 0 && index < challengeStates.Length && challengeStates[index].failed;

        /// <summary>An accepted challenge's clock — the same meaning as <see cref="Timer"/>.</summary>
        public float ChallengeTimer(int index) => index >= 0 && index < challengeStates.Length ? challengeStates[index].timer : 0f;

        /// <summary>An accepted Jump challenge's best landed jump so far, in its own measure.</summary>
        public float ChallengeJumpBest(int index) => index >= 0 && index < challengeStates.Length ? challengeStates[index].jumpBest : 0f;

        /// <summary>An accepted challenge's tally — matching cars died (Destroy Cars) or pickups made (Collect Objects).</summary>
        public int ChallengeTally(int index) => index >= 0 && index < challengeStates.Length ? challengeStates[index].tally : 0;

        /// <summary><see cref="TryGetTargetDistance(int, out float)"/> for an accepted challenge.</summary>
        public bool TryGetChallengeTargetDistance(int index, out float meters)
        {
            meters = 0f;
            return index >= 0 && index < acceptedChallenges.Count && TryGetTargetDistance(acceptedChallenges[index], out meters);
        }

        /// <summary>The active step, or null when there is none.</summary>
        public LevelObjective CurrentObjective =>
            level != null && current >= 0 && current < level.Count ? level.objectives[current] : null;

        public bool IsDone(int index) => index >= 0 && index < states.Length && states[index].done;

        /// <summary>
        /// The step's clock: seconds a SurviveTime step has been current, seconds
        /// elapsed on a Complete-Within deadline, or seconds a Hold-For step's
        /// condition has held in a row (0 for a step with no clock).
        /// </summary>
        public float Timer(int index) => index >= 0 && index < states.Length ? states[index].timer : 0f;

        /// <summary>
        /// Horizontal distance from the player to a GoToTarget step's target
        /// or a ChaseCar step's escaping car. False when the step is another
        /// kind, there is no player, or the id resolves to nothing right now
        /// (target not in the scene, escapee not yet promoted).
        /// </summary>
        public bool TryGetTargetDistance(int index, out float meters)
        {
            meters = 0f;
            var step = Objective(index);
            return step != null && TryGetTargetDistance(step, out meters);
        }

        bool TryGetTargetDistance(LevelObjective step, out float meters)
        {
            meters = 0f;
            if (player == null) return false;
            switch (step.type)
            {
                case ObjectiveType.GoToTarget:
                    if (!TargetObject.TryFind(step.targetId, out var target)) return false;
                    meters = TargetObject.HorizontalDistance(player.transform.position, target.Position);
                    return true;
                case ObjectiveType.ChaseCar:
                    if (!TrafficCarInput.TryFindEscaping(step.targetId, out var escapee)) return false;
                    meters = TargetObject.HorizontalDistance(player.transform.position, escapee.transform.position);
                    return true;
            }
            return false;
        }

        /// <summary>A Jump step's best landed jump so far, in its own measure (metres or seconds); 0 for other kinds.</summary>
        public float JumpBest(int index) => index >= 0 && index < states.Length ? states[index].jumpBest : 0f;

        /// <summary>The step's tally — matching cars died (Destroy Cars) or pickups made (Collect Objects); 0 for other kinds.</summary>
        public int Tally(int index) => index >= 0 && index < states.Length ? states[index].tally : 0;

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

        // The brief goes up in Start, after every Awake (city boot, HUD, menu
        // singletons) has run — it freezes scaled time itself, so the whole
        // scene holds under it until the player accepts.
        void Start()
        {
            MissionReward = (int)System.Math.Min(level.RewardBase, int.MaxValue);
            if (skipMissionBrief) return;
            briefOpen = true;
            MissionBriefScreen.Show(level, OnBriefAccepted);
        }

        void OnBriefAccepted(System.Collections.Generic.List<OptionalChallenge> challenges, int reward)
        {
            acceptedChallenges.Clear();
            acceptedChallenges.AddRange(challenges);
            // Challenges neither brief nor play a cinema — the brief presented them.
            challengeStates = new ObjectiveState[acceptedChallenges.Count];
            for (int i = 0; i < challengeStates.Length; i++) challengeStates[i] = new ObjectiveState { briefed = true };
            MissionReward = reward;
            briefOpen = false; // objectives (and the first briefing line) start now

            // Brief the first step right here rather than on the next Update:
            // the brief has just unfrozen time, and a first step with a cinema
            // must re-freeze it in the same frame, not a physics step later.
            RefreshTargets(0f);
            SyncStates();
            if (player != null && level.Count > 0 && current < states.Length && !states[current].briefed)
                Brief(current);
        }

        void OnEnable()
        {
            CarHealth.Died += OnCarDied;
            Collectible.Collected += OnCollected;
            CityStatsRecorder.JumpLanded += OnJumpLanded;
        }

        // A scene going away mid-cinema (reload, exit to menu) must not leave
        // the world frozen or let the cinema call back into a dead manager.
        void OnDisable()
        {
            CarHealth.Died -= OnCarDied;
            Collectible.Collected -= OnCollected;
            CityStatsRecorder.JumpLanded -= OnJumpLanded;
            if (cinemaPlaying) CinemaSystem.Instance?.Cancel();
            cinemaOpen = false;
            cinemaPlaying = false;
        }

        /// <summary>
        /// A car died: if the CURRENT step is a Destroy Cars step and the car
        /// fits its filter, it counts — the death arrives while the
        /// controller (and so the identity) is still on the object. Only the
        /// active step tallies, so kills before it briefs are not banked;
        /// deaths while the level is frozen (brief, cinema) or ending fall
        /// through the same gate the loop uses. Every LIVE accepted Destroy
        /// Cars challenge tallies the same death in parallel.
        /// </summary>
        void OnCarDied(CarHealth health)
        {
            if (briefOpen || cinemaOpen || Completed || resetting || timedOut) return;
            var controller = health.GetComponent<CarController>();
            VehicleIdentity identity = controller != null ? controller.identity : default;

            for (int i = 0; i < acceptedChallenges.Count && i < challengeStates.Length; i++)
            {
                ObjectiveState challengeState = challengeStates[i];
                if (challengeState.done || challengeState.failed || !acceptedChallenges[i].CountsKill(identity)) continue;
                challengeState.tally++;
            }

            if (level == null || current < 0 || current >= level.Count || current >= states.Length) return;
            LevelObjective step = level.objectives[current];
            if (advancing || !step.CountsKill(identity)) return; // includes the type check; a done step absorbs nothing
            states[current].tally++;
            Debug.Log($"[Level] destroyed {identity} — {states[current].tally}/{step.destroyCount}", this);
        }

        /// <summary>
        /// A collectible was picked up: the same shape as <see cref="OnCarDied"/>
        /// — every live accepted Collect Objects challenge whose id matches
        /// tallies it, and so does the current step when it is one. Money is
        /// the CollectibleManager's business — objectives count items, so a
        /// coin never fills a "collect anything" step.
        /// </summary>
        void OnCollected(Collectible collectible)
        {
            if (collectible.Kind == CollectibleKind.Money) return;
            if (briefOpen || cinemaOpen || Completed || resetting || timedOut) return;
            string id = collectible.Id;

            for (int i = 0; i < acceptedChallenges.Count && i < challengeStates.Length; i++)
            {
                ObjectiveState challengeState = challengeStates[i];
                if (challengeState.done || challengeState.failed || !acceptedChallenges[i].CountsCollectible(id)) continue;
                challengeState.tally++;
            }

            if (level == null || current < 0 || current >= level.Count || current >= states.Length) return;
            LevelObjective step = level.objectives[current];
            if (advancing || !step.CountsCollectible(id)) return; // includes the type check; a done step absorbs nothing
            states[current].tally++;
            Debug.Log($"[Level] collected '{id}' — {states[current].tally}/{step.collectCount}", this);
        }

        /// <summary>
        /// The player landed a jump (measured by the CityStatsRecorder): every
        /// live accepted Jump challenge and a current Jump step keep their best
        /// so far in their own measure — a step completes on the first jump
        /// that reaches its target.
        /// </summary>
        void OnJumpLanded(float meters, float seconds)
        {
            if (briefOpen || cinemaOpen || Completed || resetting || timedOut) return;

            for (int i = 0; i < acceptedChallenges.Count && i < challengeStates.Length; i++)
            {
                ObjectiveState challengeState = challengeStates[i];
                if (challengeState.done || challengeState.failed || acceptedChallenges[i].type != ObjectiveType.Jump) continue;
                challengeState.jumpBest = Mathf.Max(challengeState.jumpBest, acceptedChallenges[i].JumpValue(meters, seconds));
            }

            if (level == null || current < 0 || current >= level.Count || current >= states.Length) return;
            LevelObjective step = level.objectives[current];
            if (advancing || step.type != ObjectiveType.Jump) return;
            states[current].jumpBest = Mathf.Max(states[current].jumpBest, step.JumpValue(meters, seconds));
            Debug.Log($"[Level] jump landed — {states[current].jumpBest:0.0}/{step.JumpTarget:0.0} {step.JumpUnit}", this);
        }

        void Update()
        {
            if (briefOpen || cinemaOpen || Completed || resetting || timedOut) return;
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

            EvaluateChallenges(dt);
            if (advancing) return; // the finished step's line / delay is playing out; challenges above keep counting

            ObjectiveState state = states[current];
            if (!state.briefed) Brief(current);

            if (!Evaluate(current, dt)) return;
            state.done = true;
            BeginAdvance(current);
        }

        /// <summary>
        /// The step just finished: speak its completion message if it has one
        /// (the world keeps running under it — the RPG box types on scaled
        /// time, so a frozen clock would never let it end), then wait the
        /// step's delay, then move on. The token lets a regression during the
        /// wait cancel the pending advance.
        /// </summary>
        void BeginAdvance(int index)
        {
            LevelObjective step = level.objectives[index];
            advancing = true;
            int token = ++advanceToken;
            if (step.HasCompletionMessage)
                RpgMessageSystem.Instance.ShowMessage(level.speakerName, step.CompletionText, level.messageHoldSeconds, DoneAccent,
                                                      onFinished: () => StartCoroutine(FinishAdvance(index, token)));
            else
                StartCoroutine(FinishAdvance(index, token));
        }

        IEnumerator FinishAdvance(int index, int token)
        {
            LevelObjective step = index < level.Count ? level.objectives[index] : null;
            if (step != null && step.nextDelaySeconds > 0f)
                yield return new WaitForSeconds(step.nextDelaySeconds); // scaled: freezes with the pause menu and the map
            if (token != advanceToken || resetting || Completed || timedOut) yield break;
            advancing = false;
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

        /// <summary>
        /// Is this step complete right now, its clock advanced by dt? Without a
        /// time rule that is the bare condition. A deadline counts the seconds
        /// since activation and fails the run when they run out unmet; a hold
        /// counts the seconds the condition has stayed true and completes only
        /// once they cover the span.
        /// </summary>
        bool Evaluate(int index, float dt)
        {
            LevelObjective step = level.objectives[index];
            ObjectiveState state = states[index];
            string label = $"objective {index + 1}";
            if (step.HasDeadline)
            {
                if (Satisfied(step, state, dt, false, label)) return true;
                state.timer += dt;
                if (state.timer >= step.timeSeconds) TimeOut(index);
                return false;
            }
            if (step.MustHold)
            {
                // Once seconds are banked the check gets the hysteresis, so a
                // flicker on the threshold never throws them away.
                state.timer = Satisfied(step, state, dt, state.timer > 0f, label) ? state.timer + dt : 0f;
                return state.timer >= step.timeSeconds;
            }
            return Satisfied(step, state, dt, false, label);
        }

        /// <summary>
        /// The accepted challenges' own loop, run beside the main list every
        /// frame: each live one is checked until it completes and latches; a
        /// deadline that runs out fails the challenge, not the run; a hold
        /// counts its seconds exactly like a main step. Each outcome gets the
        /// level's challenge line.
        /// </summary>
        void EvaluateChallenges(float dt)
        {
            for (int i = 0; i < acceptedChallenges.Count && i < challengeStates.Length; i++)
            {
                OptionalChallenge challenge = acceptedChallenges[i];
                ObjectiveState state = challengeStates[i];
                if (state.done || state.failed) continue;
                string label = $"challenge {i + 1}";

                bool met;
                if (challenge.HasDeadline)
                {
                    met = Satisfied(challenge, state, dt, false, label);
                    if (!met)
                    {
                        state.timer += dt;
                        if (state.timer >= challenge.timeSeconds) FailChallenge(i);
                        continue;
                    }
                }
                else if (challenge.MustHold)
                {
                    state.timer = Satisfied(challenge, state, dt, state.timer > 0f, label) ? state.timer + dt : 0f;
                    met = state.timer >= challenge.timeSeconds;
                }
                else
                {
                    met = Satisfied(challenge, state, dt, false, label);
                }

                if (met) CompleteChallenge(i);
            }
        }

        void CompleteChallenge(int index)
        {
            OptionalChallenge challenge = acceptedChallenges[index];
            challengeStates[index].done = true;
            PlayerStats.CompleteBonusObjective();
            Debug.Log($"[Level] challenge {index + 1} complete — {challenge.ChallengeSummary}", this);
            // A challenge speaks its own completion message, like any step; nothing level-wide.
            if (challenge.HasCompletionMessage)
                RpgMessageSystem.Instance.ShowMessage(level.speakerName, challenge.CompletionText, level.messageHoldSeconds, DoneAccent);
        }

        void FailChallenge(int index)
        {
            OptionalChallenge challenge = acceptedChallenges[index];
            challengeStates[index].failed = true;
            Debug.Log($"[Level] challenge {index + 1} failed — {challenge.ChallengeSummary}", this);
            ShowChallengeLine(level.challengeFailedMessage, challenge, LevelObjective.DefaultAccent(ObjectiveType.EscapePolice));
        }

        // {0} = the challenge's condition, {1} = its multiplier. An empty
        // template means the designer wants no line for that outcome.
        void ShowChallengeLine(string template, OptionalChallenge challenge, Color accent)
        {
            if (string.IsNullOrWhiteSpace(template)) return;
            string text;
            try { text = string.Format(template, challenge.Summary, challenge.multiplier); }
            catch (System.FormatException) { text = template; }
            RpgMessageSystem.Instance.ShowMessage(level.speakerName, text, level.messageHoldSeconds, accent);
        }

        /// <summary>
        /// A deadline ran out with its step unmet: the level's time-up line,
        /// then the shared death once it has cleared the screen. The flag
        /// stops evaluation at once so the step cannot complete during the
        /// line; the reboot itself gates on <c>resetting</c> as ever.
        /// </summary>
        void TimeOut(int index)
        {
            if (timedOut) return;
            timedOut = true;
            Debug.Log($"[Level] objective {index + 1} ran out of time", this);
            RpgMessageSystem.Instance.ShowMessage(
                level.speakerName, level.timeUpMessage, level.messageHoldSeconds, LevelObjective.DefaultAccent(ObjectiveType.EscapePolice),
                onFinished: () => RequestReboot("time limit"));
        }

        /// <summary>
        /// The step's bare condition (advancing its progress by dt if it is a
        /// timer). <paramref name="holding"/> asks the lenient reading — the
        /// speed check with the hold tolerance — for a sustain already in
        /// progress. Shared by the main list and the challenges, which is why
        /// it takes the step and its state rather than a list index;
        /// <paramref name="label"/> names the step in log lines.
        /// </summary>
        bool Satisfied(LevelObjective step, ObjectiveState state, float dt, bool holding, string label)
        {
            switch (step.type)
            {
                case ObjectiveType.ReachSpeed:
                    return player.SpeedKmh >= step.targetSpeedKmh - (holding ? HoldToleranceKmh : 0f);

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
                            Debug.LogWarning($"[Level] {label} wants TargetObject '{step.targetId}' but none is in the scene — it can never complete.", this);
                        state.warnedMissingTarget = true;
                        return false;
                    }
                    return TargetObject.HorizontalDistance(player.transform.position, target.Position) <= step.arriveRadius;
                }

                case ObjectiveType.ChaseCar:
                {
                    if (TrafficCarInput.TryFindEscaping(step.targetId, out TrafficCarInput escapee))
                    {
                        // Done means SEEN DEAD, not deregistered: the death fuse
                        // burns for seconds before the wreck tears the driver
                        // off, and the kill should count the moment it lands.
                        var health = escapee.GetComponent<CarHealth>();
                        if (health == null || !health.IsDead) return false;
                        state.carKilled = true;
                        return true;
                    }
                    if (state.carKilled) return true;
                    // No escapee under this id: never promoted, or it vanished
                    // without being killed (fell out of the world) — promote a
                    // fresh civilian rather than softlocking the step.
                    TryPromoteEscapeCar(step, state, label);
                    return false;
                }

                case ObjectiveType.DestroyCars:
                    // The tally is fed by OnCarDied; here it only has to be read.
                    return state.tally >= step.destroyCount;

                case ObjectiveType.CollectObjects:
                    // Fed by OnCollected, the same way.
                    return state.tally >= step.collectCount;

                case ObjectiveType.Jump:
                    // Fed by OnJumpLanded: one landed jump reaching the target.
                    return state.jumpBest >= step.JumpTarget;
            }
            return false;
        }

        /// <summary>
        /// Turn the nearest live civilian into the step's escaping car — any
        /// NPC car can be the getaway driver, which is the point: the chase
        /// starts from whatever traffic is already on the street. On a slow
        /// tick, because the fleet may simply not have spawned yet and a
        /// scene sweep per frame while waiting is wasted work. The escapee's
        /// top speed is the PLAYER'S top speed, read off the player's own
        /// config, so the chase is winnable on cornering, not raw pace.
        /// </summary>
        void TryPromoteEscapeCar(LevelObjective step, ObjectiveState state, string label)
        {
            if (string.IsNullOrEmpty(step.targetId))
            {
                if (!state.warnedMissingTarget)
                    Debug.LogWarning($"[Level] {label} is a Chase Car step with no id — it can never complete.", this);
                state.warnedMissingTarget = true;
                return;
            }

            promoteTimer -= Time.deltaTime;
            if (promoteTimer > 0f) return;
            promoteTimer = 1f;

            TrafficCarInput best = null;
            float bestSqr = float.MaxValue;
            foreach (TrafficCarInput car in FindObjectsByType<TrafficCarInput>(FindObjectsSortMode.None))
            {
                if (car.Fleeing) continue;
                var health = car.GetComponent<CarHealth>();
                if (health != null && health.IsDead) continue;
                float sqr = (car.transform.position - player.transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = car;
            }
            if (best == null) return; // no traffic alive yet — retry next tick

            float topSpeedKmh = player.config != null ? player.config.topSpeedKmh : 140f;
            best.BecomeEscapeCar(step.targetId, topSpeedKmh);
            Debug.Log($"[Level] '{best.name}' promoted to escaping car '{step.targetId}'", this);
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
            // A pending advance (completion line / delay of the step that just
            // finished) is void: the level is going back to an earlier step.
            advanceToken++;
            advancing = false;
            for (int i = index; i < states.Length; i++)
            {
                states[i].done = false;
                // Later progress is forfeited; the regressed step keeps its own
                // state (a seen hunt, a kill) but its clock restarts — a
                // deadline gets its full span again, a hold starts from zero.
                states[i].timer = 0f;
                if (i > index) { states[i].carKilled = false; states[i].tally = 0; states[i].jumpBest = 0f; }
            }
            current = index;
        }

        /// <summary>
        /// The step's activation beat: its cinema first, when it has one and
        /// the scene's cinema system is on (a disabled one means cinemas are
        /// switched off), then the dialogue line. <c>briefed</c> goes up
        /// before either so an All-Must-Hold regression never replays it.
        /// Only a cinema that freezes the world raises the <c>cinemaOpen</c>
        /// gate; one the game runs under leaves the step live at once. The
        /// callback also fires when another cinema displaces this one, so
        /// the gate can never outlive the picture it guarded.
        /// </summary>
        void Brief(int index)
        {
            LevelObjective step = level.objectives[index];
            states[index].briefed = true;

            CinemaSystem cinema = step.HasCinema ? CinemaSystem.Ensure(gameObject.scene) : null;
            if (cinema == null)
            {
                ShowBriefLine(step);
                return;
            }
            cinemaOpen = step.cinemaPausesGame;
            cinemaPlaying = true;
            cinema.Play(step, () =>
            {
                cinemaOpen = false;
                cinemaPlaying = false;
                ShowBriefLine(step);
            });
        }

        void ShowBriefLine(LevelObjective step)
        {
            RpgMessageSystem.Instance.ShowMessage(level.speakerName, step.BriefingText, level.messageHoldSeconds, step.Accent);
        }

        // Completion: the line first; the glitch handoff only once it is gone.
        // Completed goes up before the message so evaluation and police
        // damage stop — a hit during the line must not start a reboot too.
        void Complete()
        {
            Completed = true;
            // The save: level count, the last-level summary, the progression
            // list — and the rows the runner's Mission Complete panel will
            // print and pay: every objective with its reward, every ACCEPTED
            // challenge with its multiplier and outcome, the flat bonus and
            // the rank table. No money moves here. Written to disk now —
            // this scene is about to be unloaded under the glitch.
            var objectiveRows = new System.Collections.Generic.List<ObjectiveResult>(level.Count);
            for (int i = 0; i < level.Count; i++)
            {
                LevelObjective step = level.objectives[i];
                objectiveRows.Add(new ObjectiveResult(step.Summary, step.reward, done: true));
            }
            var challengeRows = new System.Collections.Generic.List<ChallengeResult>(acceptedChallenges.Count);
            for (int i = 0; i < acceptedChallenges.Count; i++)
            {
                OptionalChallenge challenge = acceptedChallenges[i];
                challengeRows.Add(new ChallengeResult(challenge.Summary, challenge.multiplier, IsChallengeDone(i)));
            }
            PlayerStats.RecordLevelCompleted(level.name, level.levelName,
                level.Count > 0 ? level.objectives[level.Count - 1].Summary : string.Empty,
                level.baseReward, objectiveRows, challengeRows, level.rankTable);
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
            // Every accepted reboot is a death; one the police caused — the
            // meter filled by a shunt — is also an arrest. The city AI has no
            // capture state, so the last damage source is the best signal.
            PlayerStats.RecordDeath(arrested: reason == "full corruption" && lastDamageReason == "police hit");
            PlayerProfileStore.SaveIfDirty();
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
            Debug.Log("[Level] retry — reloading scene under the loading screen", this);
            LoadingScreen.Reload(gameObject.scene);
        }

        // MainMenu is build index 0, same as the pause menu's exit — but from a
        // game over the trip goes under the loading curtain.
        void ExitToMainMenu() => LoadingScreen.LoadMainMenu();

        /// <summary>
        /// Player impact, relayed by the sensor: hard hits rumble the pad and
        /// pulse the glitch, police hits also add permanent corruption — the
        /// damage meter. The rumble scales with impact speed (a kerb tap is a
        /// tick, a wall at speed is a slam) and fires before the glitch check,
        /// so a scene without a glitch controller still shakes the pad.
        /// </summary>
        void OnPlayerImpact(Collision collision)
        {
            if (resetting || Completed || cinemaOpen) return;
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < minImpactSpeed) return;

            RumbleCrash(impactSpeed);

            var glitch = GlitchController.Instance;
            if (glitch == null) return;
            glitch.Pulse(collisionPulse);

            bool policeHit = collision.rigidbody != null
                && collision.rigidbody.GetComponent<PoliceCarInput>() != null;
            if (policeHit) ApplyDamage(policeHitIntensity, "police hit");
        }

        /// <summary>
        /// Crash rumble: 0..1 off how far the impact sits between the scrape
        /// threshold and <see cref="crashRumbleFullSpeed"/>. The heavy motor
        /// carries the thud, the light one a shorter rattle on top; overlapping
        /// pulses keep the strongest, so a pile-up never stacks into a buzz.
        /// </summary>
        void RumbleCrash(float impactSpeed)
        {
            var haptics = HapticsSystem.Instance;
            if (haptics == null) return;

            float t = Mathf.InverseLerp(minImpactSpeed, Mathf.Max(minImpactSpeed + 0.01f, crashRumbleFullSpeed), impactSpeed);
            float strength = Mathf.Lerp(0.25f, 1f, t);
            haptics.Pulse(strength, strength * 0.6f, Mathf.Lerp(0.12f, crashRumbleDuration, t));
        }

        /// <summary>
        /// Permanent corruption from anything that is not a police shunt — an
        /// exploding barrel today, a scripted hazard tomorrow. One entry point
        /// so every source fills the same meter and trips the same reboot at
        /// full. Returns false when the hit was swallowed (the run is already
        /// ending, or there is no glitch controller in the scene), so a caller
        /// knows not to expect a reaction.
        /// </summary>
        public bool ApplyDamage(float amount, string reason)
        {
            if (resetting || Completed || cinemaOpen) return false;

            var glitch = GlitchController.Instance;
            if (glitch == null) return false;
            lastDamageReason = reason;

            // Never quieter than the hit is heavy: a barrel to the face should
            // white out the feed even if the collision pulse is tuned gentle.
            glitch.Pulse(Mathf.Max(collisionPulse, amount));
            glitch.SetBaseIntensity(glitch.baseIntensity + amount);
            Debug.Log($"[Level] {reason} — corruption {glitch.baseIntensity:F2}", this);
            if (glitch.baseIntensity >= 0.999f) RequestReboot("full corruption");
            return true;
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
