using UnityEngine;
using UnityEngine.Serialization;

namespace FiniteRunner
{
    /// <summary>
    /// Owns the win/lose conditions of the chase. Win: reach Light Speed
    /// before the countdown ends. Lose: the police patrol catches up, the
    /// timer runs out, or the ship bleeds down to a standstill.
    /// Also spawns the PolicePatrol at runtime and restarts it with the run.
    /// The timer only ticks while the ship is actually flying (not while
    /// the tuning screen has the simulation paused).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] TrackGenerator generator;
        [SerializeField] TuningScreen tuningScreen;

        [Header("Win condition")]
        [Tooltip("Light Speed — the speed that wins the run, in km/h.")]
        [FormerlySerializedAs("targetSpeedKmh")]
        [SerializeField, Min(1f)] float lightSpeedKmh = 400f;

        [Header("Time limit")]
        [Tooltip("Seconds to reach Light Speed before the patrol catches you.")]
        [SerializeField, Min(1f)] float timeLimitSeconds = 60f;

        [Header("Police patrol")]
        [Tooltip("Spawn the chasing patrol at runtime (no scene object needed).")]
        [SerializeField] bool patrolEnabled = true;
        [Tooltip("Patrol launch speed in km/h, and the rubber band's minimum — the patrol never goes slower than this. Keep it above the ship's launch speed (the Fighter launches at 288).")]
        [SerializeField, Min(1f)] float patrolSpeedKmh = 320f;
        [Tooltip("How much the rubber band's minimum speed grows per second, in km/h — the chase tightens the longer the run lasts.")]
        [SerializeField, Min(0f)] float patrolRampKmhPerSecond = 3f;
        [Tooltip("Rubber band: the patrol chases the ship's current speed times this factor (1.05 = always 5% faster), but never below the minimum above.")]
        [SerializeField, Min(0.5f)] float patrolRubberBandFactor = 1.05f;
        [Tooltip("How fast the patrol's speed adapts toward its rubber-band target, in km/h per second. Lower = boosts buy more breathing room before the patrol matches them.")]
        [SerializeField, Min(1f)] float patrolCatchUpKmhPerSecond = 60f;
        [Tooltip("Meters behind the start line the patrol launches from.")]
        [SerializeField, Min(0f)] float patrolStartGap = 250f;
        [Tooltip("Gap in meters at which the patrol catches the ship.")]
        [SerializeField, Min(0f)] float patrolCatchDistance = 10f;
        [Tooltip("Gap below which 'PATROL x M' warnings pop over the ship.")]
        [SerializeField, Min(0f)] float patrolWarnDistance = 130f;
        [Tooltip("Gap that puts the patrol icon at the very bottom of the chase minimap.")]
        [SerializeField, Min(50f)] float minimapRangeMeters = 400f;

        [Header("Power-ups")]
        [Tooltip("Base speed gain of a power-up orb in m/s (×3.6 for km/h). Each orb tier multiplies this — green 1x, blue 2.5x, purple 10x (tiers live on the TrackGenerator).")]
        [SerializeField, Min(0f)] float powerUpSpeedBoost = 15f;

        [Header("Story messages")]
        [Tooltip("Seconds an RPG message stays on screen after it finishes typing.")]
        [SerializeField, Min(0f)] float messageHoldSeconds = 2.5f;
        [Tooltip("Orb tier (by name, see TrackGenerator.orbTiers) whose pickup triggers the pilot's hype line. Empty = no orb message.")]
        [SerializeField] string messageOrbTierName = "Purple";
        [SerializeField, TextArea] string purpleOrbMessage =
            "A purple charge! The engines are SCREAMING — hold on to something!";
        [SerializeField, TextArea] string winMessage =
            "LIGHT SPEED! Not even the whole patrol fleet can touch us now!";
        [SerializeField, TextArea] string loseMessage =
            "Gotcha, hotshot. Party's over — powering you down.";
        [SerializeField] Color pilotMessageColor = new(0.35f, 0.9f, 1f);
        [SerializeField] Color patrolMessageColor = new(1f, 0.4f, 0.35f);

        [Header("Floating text offsets")]
        [Tooltip("How far ahead of the ship the '+N' boost popups spawn, in meters.")]
        [SerializeField, Min(0f)] float boostTextLeadMeters = 60f;
        [Tooltip("How far ahead of the ship the 'PATROL x M' alerts spawn, in meters.")]
        [SerializeField, Min(0f)] float patrolAlertLeadMeters = 180f;

        PolicePatrol patrol;

        public float BoostTextLeadMeters => boostTextLeadMeters;

        /// <summary>Base speed gain of a power-up orb in m/s; orb tiers multiply this.</summary>
        public float PowerUpSpeedBoost => powerUpSpeedBoost;

        public PolicePatrol Patrol => patrol;
        public float LightSpeedKmh => lightSpeedKmh;
        public float TimeLimit => timeLimitSeconds;
        public float TimeRemaining { get; private set; }
        public string ResultLabel { get; private set; }
        public bool HasWon { get; private set; }
        public bool RunOver => ResultLabel != null;

        void Awake()
        {
            TimeRemaining = timeLimitSeconds;
            if (motor != null) motor.PadImpulse += OnPadImpulse;
            SpeedPad.Collected += OnPadCollected;
            PauseMenu.Spawn(this, motor);
            if (patrolEnabled && motor != null)
            {
                patrol = PolicePatrol.Spawn(motor, patrolSpeedKmh, patrolRampKmhPerSecond,
                                            patrolRubberBandFactor, patrolCatchUpKmhPerSecond,
                                            patrolStartGap, patrolCatchDistance, patrolWarnDistance,
                                            patrolAlertLeadMeters);
                ChaseMinimap.Spawn(motor, patrol, minimapRangeMeters, patrolWarnDistance);
            }
        }

        void Update()
        {
            if (motor == null || RunOver) return;

            if (motor.CurrentSpeed * 3.6f >= lightSpeedKmh)
            {
                HasWon = true;
                EndRun("LIGHT SPEED — YOU ESCAPED!");
                return;
            }

            if (patrol != null && patrol.HasCaught)
            {
                EndRun("BUSTED — CAUGHT BY THE PATROL");
                HapticsSystem.Instance.Pulse(1f, 0.7f, 1.5f); // long busted rumble
                return;
            }

            if (motor.HasStopped)
            {
                EndRun("BUSTED — OUT OF SPEED");
                return;
            }

            // Time only pressures the player while the ship is flying.
            if (motor.Paused) return;

            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            if (TimeRemaining <= 0f)
                EndRun("BUSTED — TIME RAN OUT");
        }

        void EndRun(string label)
        {
            ResultLabel = label;
            motor.Paused = true; // freeze the sim; the hover keeps the ship floating

            // RPG message on both outcomes. Losing waits for the line to
            // disappear and only then resets the run.
            if (HasWon)
                RpgMessageSystem.Instance.ShowMessage(
                    "PILOT", winMessage, messageHoldSeconds, pilotMessageColor);
            else
                RpgMessageSystem.Instance.ShowMessage(
                    "PATROL", loseMessage, messageHoldSeconds, patrolMessageColor,
                    onFinished: Restart);
        }

        // Story beat: hype line every time the rare orb tier is grabbed.
        void OnPadCollected(SpeedPad pad, ShipMotor collector)
        {
            if (collector != motor || RunOver) return;
            if (!string.IsNullOrEmpty(messageOrbTierName) && pad.TierName == messageOrbTierName)
                RpgMessageSystem.Instance.ShowMessage(
                    "PILOT", purpleOrbMessage, messageHoldSeconds, pilotMessageColor);
        }

        // Haptics: a snappy buzz for boosts, a heavier thud for brakes.
        void OnPadImpulse(float rawMagnitude)
        {
            if (rawMagnitude > 0f) HapticsSystem.Instance.Pulse(0.15f, 0.55f, 0.15f);
            else HapticsSystem.Instance.Pulse(0.65f, 0.2f, 0.25f);
        }

        void OnDestroy()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
            SpeedPad.Collected -= OnPadCollected;
        }

        /// <summary>Resets the run; rebuilds the track (endless runs must — the stretch behind the start was culled).</summary>
        public void Restart()
        {
            // A manual restart from the result screen may race the game-over
            // message's auto-restart — dropping any pending message (and its
            // onFinished) keeps the new run from being reset a second time.
            RpgMessageSystem.Instance.ClearMessages();

            ResultLabel = null;
            HasWon = false;
            TimeRemaining = timeLimitSeconds;
            if (generator != null) generator.RegenerateForRun();
            motor.Paused = false; // EndRun froze the sim; the tuning screen re-pauses if present
            motor.Launch();
            if (patrol != null) patrol.Launch();

            // Reopen ship setup so points can be re-allocated; it re-launches on START.
            if (tuningScreen != null) tuningScreen.Show();
        }
    }
}
