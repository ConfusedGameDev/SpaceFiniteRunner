using ConfusedGameDev.FiniteRunner.PoliceEscape.FX;
using Sirenix.OdinInspector;
using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Owns the win/lose conditions of the chase. Win: reach Light Speed
    /// before the countdown ends. Lose: the police patrol catches up, the
    /// timer runs out, or the ship bleeds down to a standstill.
    /// Also spawns the PolicePatrol at runtime and restarts it with the run.
    /// The timer only ticks while the ship is actually flying (not while
    /// the tuning screen has the simulation paused).
    /// Holds no tunables itself — every balance knob lives on the
    /// <see cref="GameSettings"/> asset, drawn inline here for the designers.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Title("Scene references")]
        [SerializeField, Required] ShipMotor motor;
        [SerializeField, Required] TrackGenerator generator;
        [SerializeField] TuningScreen tuningScreen;

        [Title("Flow")]
        [Tooltip("Overlay the main menu (attract screen) over this scene on boot — an in-scene testing shortcut. The shipping flow keeps this off: the menu is its own scene (MainMenu.unity, build index 0) and this scene is reached from the city chase.")]
        [SerializeField] bool mainMenuOnBoot;

        [Title("Balance")]
        [Tooltip("Every tunable of the run. Edit it right here — it is the same asset the whole project shares.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        GameSettings settings;

        PolicePatrol patrol;
        DashPromptController dashPrompt;

        public float BoostTextLeadMeters => settings.boostTextLeadMeters;

        /// <summary>Base speed gain of a power-up orb in m/s; orb tiers multiply this.</summary>
        public float PowerUpSpeedBoost => settings.powerUpSpeedBoost;

        public PolicePatrol Patrol => patrol;
        public float LightSpeedKmh => settings.lightSpeedKmh;
        public float TimeLimit => settings.timeLimitSeconds;
        public float TimeRemaining { get; private set; }
        public string ResultLabel { get; private set; }
        public bool HasWon { get; private set; }
        public bool RunOver => ResultLabel != null;

        void Awake()
        {
            // Never run without balance data: a throwaway instance keeps the
            // scene playable (on defaults) instead of throwing every frame.
            if (settings == null)
            {
                Debug.LogError($"{nameof(GameManager)} has no {nameof(GameSettings)} asset assigned — falling back to defaults.", this);
                settings = ScriptableObject.CreateInstance<GameSettings>();
            }

            TimeRemaining = settings.timeLimitSeconds;
            if (motor != null) motor.PadImpulse += OnPadImpulse;
            SpeedPad.Collected += OnPadCollected;
            PauseMenu.Spawn(this, motor);

            if (motor != null)
            {
                motor.ConfigureDash(settings);
                motor.WallHit += OnWallHit;
                motor.DashPerformed += OnDashPerformed;

                // The DashMeterUI is a scene child of the Ship — it configures
                // itself off the motor in Start, after ConfigureDash above.
                if (settings.dashEnabled)
                {
                    motor.gameObject.AddComponent<DashGhostTrail>().Init(motor, settings);
                    dashPrompt = DashPromptController.Spawn(motor, settings);
                }
            }

            // Above the pause menu's canvas and holding timeScale at 0, so the
            // scene boots to the attract screen with nothing running behind it.
            if (mainMenuOnBoot) MainMenuController.Spawn(motor, tuningScreen);

            if (settings.patrolEnabled && motor != null)
            {
                patrol = PolicePatrol.Spawn(motor, settings.patrolSpeedKmh, settings.patrolRampKmhPerSecond,
                                            settings.patrolRubberBandFactor, settings.patrolCatchUpKmhPerSecond,
                                            settings.patrolStartGap, settings.PatrolCatchDistance,
                                            settings.PatrolWarnDistance, settings.patrolAlertLeadMeters);
                patrol.SetRedeployRule(settings.PatrolRedeployDistance, settings.PatrolRedeployGap,
                                       settings.patrolRedeploySpeedFactor);
                ChaseMinimap.Spawn(motor, patrol, settings.minimapRangeMeters, settings.PatrolWarnDistance);
            }
        }

        void Update()
        {
            if (motor == null || RunOver) return;

            if (motor.CurrentSpeed * 3.6f >= settings.lightSpeedKmh)
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
                    "PILOT", settings.winMessage, settings.messageHoldSeconds, settings.pilotMessageColor);
            else
                RpgMessageSystem.Instance.ShowMessage(
                    "PATROL", settings.loseMessage, settings.messageHoldSeconds, settings.patrolMessageColor,
                    onFinished: Restart);
        }

        // Story beat: hype line every time the rare orb tier is grabbed.
        void OnPadCollected(SpeedPad pad, ShipMotor collector)
        {
            if (collector != motor || RunOver) return;
            if (!string.IsNullOrEmpty(settings.messageOrbTierName) && pad.TierName == settings.messageOrbTierName)
                RpgMessageSystem.Instance.ShowMessage(
                    "PILOT", settings.purpleOrbMessage, settings.messageHoldSeconds, settings.pilotMessageColor);
        }

        // Haptics: a snappy buzz for boosts, a heavier thud for brakes.
        void OnPadImpulse(float rawMagnitude)
        {
            if (rawMagnitude > 0f) HapticsSystem.Instance.Pulse(0.15f, 0.55f, 0.15f);
            else HapticsSystem.Instance.Pulse(0.65f, 0.2f, 0.25f);
        }

        // Dash feel: a short kick in the hands.
        void OnDashPerformed(int direction)
        {
            HapticsSystem.Instance.Pulse(0.3f, 0.6f, 0.12f);
        }

        // Dash into the wall: a thud in the hands and a burst of signal corruption.
        void OnWallHit(float impactSpeed)
        {
            HapticsSystem.Instance.Pulse(0.8f, 0.4f, 0.2f);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.dashWallGlitchStrength);
        }

        void OnDestroy()
        {
            if (motor != null)
            {
                motor.PadImpulse -= OnPadImpulse;
                motor.WallHit -= OnWallHit;
                motor.DashPerformed -= OnDashPerformed;
            }
            SpeedPad.Collected -= OnPadCollected;
        }

        /// <summary>Resets the run; rebuilds the track (endless runs must — the stretch behind the start was culled).</summary>
        public void Restart()
        {
            // A manual restart from the result screen may race the game-over
            // message's auto-restart — dropping any pending message (and its
            // onFinished) keeps the new run from being reset a second time.
            RpgMessageSystem.Instance.ClearMessages();
            if (dashPrompt != null) dashPrompt.ResetForRun();

            ResultLabel = null;
            HasWon = false;
            TimeRemaining = settings.timeLimitSeconds;
            if (generator != null) generator.RegenerateForRun();
            motor.Paused = false; // EndRun froze the sim; the tuning screen re-pauses if present
            motor.Launch();
            if (patrol != null) patrol.Launch();

            // Reopen ship setup so points can be re-allocated; it re-launches on START.
            if (tuningScreen != null) tuningScreen.Show();
        }
    }
}
