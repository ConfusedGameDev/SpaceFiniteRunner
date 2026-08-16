using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
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

        enum Phase { Briefing, BuildSpeed, Escape, Complete }

        static readonly Color BriefAccent = new(0.45f, 0.9f, 1f);
        static readonly Color AlertAccent = new(1f, 0.4f, 0.35f);
        static readonly Color DoneAccent = new(0.45f, 1f, 0.55f);

        Phase phase = Phase.Briefing;
        CarController player;
        PoliceCarInput[] patrols = System.Array.Empty<PoliceCarInput>();
        float retargetTimer;
        bool loading;

        void Update()
        {
            if (phase == Phase.Complete) return;
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
            RpgMessageSystem.Instance.ShowMessage(
                speakerName, "Hack complete. LFG!", messageHoldSeconds, DoneAccent,
                onFinished: LoadNextScene);
        }

        void LoadNextScene()
        {
            if (loading) return; // the message system may invoke duplicate-dropped callbacks
            loading = true;
            SceneManager.LoadScene(nextSceneName);
        }

        /// <summary>Player and patrols come and go at runtime (spawn managers), so re-find them on a slow tick instead of every frame.</summary>
        void RefreshTargets(float dt)
        {
            retargetTimer -= dt;
            if (player != null && retargetTimer > 0f) return;
            retargetTimer = 1f;
            player = PatrolManager.FindPlayerCar();
            patrols = FindObjectsByType<PoliceCarInput>(FindObjectsSortMode.None);
        }

        bool AnyPatrolHunting()
        {
            foreach (PoliceCarInput patrol in patrols)
                if (patrol != null && patrol.State != PoliceCarInput.AiState.Patrol)
                    return true;
            return false;
        }
    }
}
