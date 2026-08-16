using System.Collections;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.FX;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using FiniteRunner;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// The city chase's objective flow, told through the FiniteRunner
    /// RpgMessageSystem dialogue box (shared across both games — it
    /// auto-creates its own overlay canvas, so no scene wiring is needed):
    /// reach the target speed to run the "hack", shake any police pursuit,
    /// and the level hands over to the FiniteRunner scene. Phases:
    /// Briefing (opening message once the player car exists) → BuildSpeed →
    /// Escape (entered only if a patrol is hunting when the target speed is
    /// first reached) → Complete ("Hack complete" message, then the next
    /// scene loads when it finishes). "Hunting" means Chase or Search — the
    /// player has only truly escaped once every patrol has dropped back to
    /// Patrol, and completing always requires holding the target speed.
    ///
    /// The fullscreen glitch doubles as the damage meter and the scene
    /// transition: every hard impact pulses it, police hits permanently raise
    /// the base level, and at full corruption the level reboots (scene
    /// reload). Completing instead slams the glitch to max and hands over to
    /// the next scene ADDITIVELY — its own GlitchController starts at max and
    /// fades in, so the swap hides entirely inside the corruption.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        [TitleGroup("Objective")]
        [Tooltip("Speed the player must reach to complete the hack.")]
        [PropertyRange(50f, 300f), SuffixLabel("km/h", true)]
        public float targetSpeedKmh = 130f;

        [TitleGroup("Objective")]
        [Tooltip("Scene loaded once the hack completes — must be listed in Build Settings.")]
        public string nextSceneName = "FiniteRunner_Test";

        [TitleGroup("Messages")]
        [Tooltip("Name shown next to the dialogue portrait.")]
        public string speakerName = "OPERATOR";

        [TitleGroup("Messages")]
        [Tooltip("How long each objective message stays on screen after typing out.")]
        [PropertyRange(1f, 10f), SuffixLabel("s", true)]
        public float messageHoldSeconds = 4f;

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

        enum Phase { Briefing, BuildSpeed, Escape, Complete }

        static readonly Color BriefAccent = new(0.45f, 0.9f, 1f);
        static readonly Color AlertAccent = new(1f, 0.4f, 0.35f);
        static readonly Color DoneAccent = new(0.45f, 1f, 0.55f);

        Phase phase = Phase.Briefing;
        CarController player;
        PoliceCarInput[] patrols = System.Array.Empty<PoliceCarInput>();
        float retargetTimer;
        bool loading;
        bool resetting;

        void Update()
        {
            if (phase == Phase.Complete || resetting) return;
            RefreshTargets(Time.deltaTime);
            if (player == null) return; // the car spawns a beat after play starts

            if (phase == Phase.Briefing)
            {
                RpgMessageSystem.Instance.ShowMessage(
                    speakerName, $"We need to get to {targetSpeedKmh:0} km/h!", messageHoldSeconds, BriefAccent);
                phase = Phase.BuildSpeed;
                return;
            }

            bool fastEnough = player.SpeedKmh >= targetSpeedKmh;
            bool hunted = AnyPatrolHunting();

            if (phase == Phase.BuildSpeed && fastEnough)
            {
                if (hunted)
                {
                    RpgMessageSystem.Instance.ShowMessage(
                        speakerName, "We need to escape the police, NOW!", messageHoldSeconds, AlertAccent);
                    phase = Phase.Escape;
                }
                else Complete();
            }
            else if (phase == Phase.Escape && fastEnough && !hunted)
            {
                Complete();
            }
        }

        void Complete()
        {
            phase = Phase.Complete;
            // Slam the corruption to max — the whole handoff hides inside it.
            // Healing stops here: the transition must hold at full glitch.
            if (GlitchController.Instance != null)
            {
                GlitchController.Instance.baseFadePerSecond = 0f;
                GlitchController.Instance.SetBaseIntensity(1f);
                GlitchController.Instance.Pulse(1f);
            }
            RpgMessageSystem.Instance.ShowMessage(
                speakerName, "Hack complete. LFG!", messageHoldSeconds, DoneAccent,
                onFinished: LoadNextScene);
        }

        void LoadNextScene()
        {
            if (loading) return; // the message system may invoke duplicate-dropped callbacks
            loading = true;
            StartCoroutine(TransitionToNextScene());
        }

        /// <summary>
        /// Additive handoff behind the maxed glitch: load the next scene, make
        /// it active (its GlitchController claims the effect, starting at max
        /// and fading in), then unload this one — which takes this manager,
        /// the city and the cars with it.
        /// </summary>
        IEnumerator TransitionToNextScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(nextSceneName, LoadSceneMode.Additive);
            yield return load;
            Scene next = SceneManager.GetSceneByName(nextSceneName);
            if (next.IsValid()) SceneManager.SetActiveScene(next);
            SceneManager.UnloadSceneAsync(gameObject.scene);
        }

        /// <summary>Full corruption from police damage: hold the maxed glitch a beat, then reboot the level.</summary>
        IEnumerator ResetLevel()
        {
            resetting = true;
            Debug.Log("[Level] full corruption — rebooting level", this);
            // Healing stops here: the death screen must hold at full glitch.
            if (GlitchController.Instance != null)
            {
                GlitchController.Instance.baseFadePerSecond = 0f;
                GlitchController.Instance.SetBaseIntensity(1f);
                GlitchController.Instance.Pulse(1f);
            }
            yield return new WaitForSeconds(resetDelaySeconds);
            Debug.Log("[Level] reloading scene now", this);
            SceneManager.LoadScene(gameObject.scene.name);
        }

        /// <summary>
        /// Player impact, relayed by the sensor: hard hits pulse the glitch,
        /// police hits also add permanent corruption — the damage meter.
        /// </summary>
        void OnPlayerImpact(Collision collision)
        {
            if (resetting || phase == Phase.Complete) return;
            if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

            var glitch = GlitchController.Instance;
            if (glitch == null) return;
            glitch.Pulse(collisionPulse);

            bool policeHit = collision.rigidbody != null
                && collision.rigidbody.GetComponent<PoliceCarInput>() != null;
            if (!policeHit) return;
            glitch.SetBaseIntensity(glitch.baseIntensity + policeHitIntensity);
            Debug.Log($"[Level] police hit — corruption {glitch.baseIntensity:F2}", this);
            if (glitch.baseIntensity >= 0.999f) StartCoroutine(ResetLevel());
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
