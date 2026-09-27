using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// How the run FEELS: the rumble, shake, glitch pulse, speed-line burst,
    /// sparkles and story line each ship and patrol event plays, plus the loop
    /// gates' tint and labels. Split out of the <see cref="GameManager"/>
    /// (refactor Step 8.5), which keeps only the rules — an event whose
    /// feedback depends on a rule's outcome (a laser hit the blink shielded, a
    /// hull hit, the endings) stays there. Hand-placed beside the GameManager
    /// in <c>PF_Systems</c> and bound by it in Awake; it reads the run only
    /// through <see cref="IRunState"/>, and every strength it plays lives on
    /// <see cref="GameSettings"/> (the Haptics group, the shake assets, the
    /// glitch strengths). Removing it leaves a silent, still run.
    /// </summary>
    public class RunFeedback : MonoBehaviour
    {
        ShipMotor motor;
        PolicePatrol patrol;
        GameSettings settings;
        IRunState run;
        RunCameraDirector director;
        SpeedLines speedLines; // null when the speed lines are off on GameSettings

        /// <summary>The scene's feedback (hand-placed beside the GameManager); added only when the scene has none.</summary>
        public static RunFeedback Ensure(Component host)
        {
            var feedback = host.GetComponent<RunFeedback>();
            return feedback != null ? feedback : host.gameObject.AddComponent<RunFeedback>();
        }

        /// <summary>
        /// Binds the feedback to the run: subscribes to the ship's, the
        /// patrol's and the pickups' events and brings up the scene's speed
        /// lines. Called once, from the GameManager's Awake, after the camera
        /// director is bound (the speed lines follow its mode). A null patrol
        /// or director simply plays less.
        /// </summary>
        public void Bind(ShipMotor ship, PolicePatrol chaser, GameSettings runSettings, IRunState runState,
                         RunCameraDirector cameraDirector)
        {
            Unsubscribe();
            motor = ship;
            patrol = chaser;
            settings = runSettings;
            run = runState;
            director = cameraDirector;

            // Speed lines: the scene's hand-placed SpeedLines object (next to
            // the fog and the rain — tuned before play, never spawned here).
            // Apply finds it and parks it when off. The driver cannot see the
            // camera rig or the ship (FX does not reference Cameras), so it
            // takes the ship root as its focus, a km/h reader and Light Speed
            // as the reference its band is a fraction of; Update pushes the
            // camera mode each frame.
            speedLines = SpeedLines.Apply(settings.speedLinesEnabled);
            if (speedLines != null && motor != null)
                speedLines.SetTarget(motor.transform, () => motor.CurrentSpeed * 3.6f, run.LightSpeedKmh);

            SpeedPad.Collected += OnPadCollected;
            RepairOrb.Collected += OnRepairOrb;
            if (motor != null)
            {
                motor.PadImpulse += OnPadImpulse;
                motor.WallHit += OnWallHit;
                motor.Sliding += OnSliding;
                motor.FellOff += OnFellOff;
                motor.DashPerformed += OnDashPerformed;
                motor.Landed += OnLanded;
                motor.LoopFailed += OnLoopFailed;
            }
            if (patrol != null)
            {
                patrol.Redeployed += OnPatrolRedeployed;
                patrol.Warned += OnPatrolWarned;
            }
        }

        void Unsubscribe()
        {
            SpeedPad.Collected -= OnPadCollected;
            RepairOrb.Collected -= OnRepairOrb;
            if (motor != null)
            {
                motor.PadImpulse -= OnPadImpulse;
                motor.WallHit -= OnWallHit;
                motor.Sliding -= OnSliding;
                motor.FellOff -= OnFellOff;
                motor.DashPerformed -= OnDashPerformed;
                motor.Landed -= OnLanded;
                motor.LoopFailed -= OnLoopFailed;
            }
            if (patrol != null)
            {
                patrol.Redeployed -= OnPatrolRedeployed;
                patrol.Warned -= OnPatrolWarned;
            }
        }

        void OnDestroy() => Unsubscribe();

        /// <summary>A retry: no speed-line burst carried over (the speed term follows the relaunch on its own).</summary>
        public void ResetForRun()
        {
            if (speedLines != null) speedLines.ClearPulse();
        }

        void Update()
        {
            if (run == null) return;

            // Even after the run: the lines stay up through the death glitch,
            // and the view can still be cycled on the result screen. The
            // cinematic shot is its own mode for the lines: their asset
            // multiplier for it is 0, so they are off for the side-on shot.
            OrbitCameraRig rig = director != null ? director.Rig : null;
            if (speedLines != null && rig != null)
                speedLines.SetCameraMode(rig.Cinematic ? SpeedLines.CinematicMode : (int)rig.Mode);

            if (motor != null && !run.RunOver) UpdateLoops();
        }

        bool Quiet => run == null || run.RunOver || run.IsEnding;

        // Story beat: hype line every time the rare orb tier is grabbed.
        void OnPadCollected(SpeedPad pad, IShip collector)
        {
            if (motor == null || !motor.Is(collector) || Quiet) return;
            if (!string.IsNullOrEmpty(settings.messageOrbTierName) && pad.TierName == settings.messageOrbTierName)
                RpgMessageSystem.Instance.ShowMessage(
                    "PILOT", settings.purpleOrbMessage, settings.messageHoldSeconds, settings.pilotMessageColor);
        }

        // A repair orb: a soft, even pulse in the hands — neither a boost's kick nor a hit's rumble.
        void OnRepairOrb(RepairOrb orb, IShip collector, float healed)
        {
            if (motor == null || !motor.Is(collector) || Quiet) return;
            HapticsSystem.Instance.Pulse(settings.repairRumble);
        }

        // Story beat: the fresh patrol announces itself — a dialogue line, not
        // a floating text, and only when GameSettings asks for it (the minimap
        // and the rumble already show it cutting in).
        void OnPatrolRedeployed(int patrolNumber)
        {
            if (Quiet || !settings.showPatrolAlert) return;
            RpgMessageSystem.Instance.ShowMessage(
                "PATROL", string.Format(settings.patrolInboundMessage, patrolNumber),
                settings.messageHoldSeconds, settings.patrolMessageColor);
        }

        // Story beat: the patrol taunts as it closes in — once per approach,
        // and never queued behind a line already up (a stale gap would lie).
        void OnPatrolWarned(float gap)
        {
            if (Quiet || !settings.showPatrolWarnings || RpgMessageSystem.Instance.IsBusy) return;
            RpgMessageSystem.Instance.ShowMessage(
                "PATROL", string.Format(settings.patrolWarningMessage, Mathf.RoundToInt(gap)),
                settings.messageHoldSeconds, settings.patrolMessageColor);
        }

        // Haptics: a snappy buzz for boosts, a heavier thud for brakes.
        void OnPadImpulse(float rawMagnitude)
        {
            // A boost rumbles from the base toward the perfect-press rumble by the boost QTE's grade (0 for a plain boost).
            if (rawMagnitude > 0f)
            {
                Vector3 rumble = Vector3.Lerp(settings.boostRumble, settings.boostQteRumbleAtPerfect, BoostQte.FeedbackScale);
                HapticsSystem.Instance.Pulse(rumble.x, rumble.y, rumble.z);
            }
            else HapticsSystem.Instance.Pulse(settings.brakeRumble);

            // A burst of speed lines per boost, scaled by the orb's tier
            // (rawMagnitude is tier × powerUpSpeedBoost; a ramp takeoff's
            // boost comes through the same event and gets one too).
            if (rawMagnitude > 0f && speedLines != null)
                speedLines.Pulse(settings.boostPulseStrength * rawMagnitude / Mathf.Max(0.01f, settings.powerUpSpeedBoost),
                                 settings.boostPulseSeconds);
        }

        // Dash feel: a short kick in the hands.
        void OnDashPerformed(int direction) => HapticsSystem.Instance.Pulse(settings.dashRumble);

        /// <summary>
        /// Loop gates and labels: every live loop's gate is tinted against the
        /// ship's speed, and its required-speed number (fixed above the mouth)
        /// is shown once the loop comes inside its definition's label lead and
        /// hidden again once the ship has gone through the gate.
        /// </summary>
        void UpdateLoops()
        {
            float speed = motor.CurrentSpeed;
            float d = motor.DistanceTravelled;
            foreach (var loop in LoopFeature.Active)
            {
                if (loop == null) continue;
                loop.SetGateColor(speed >= loop.RequiredSpeed);

                float gap = loop.StartDistance - d;
                float lead = loop.Definition != null ? loop.Definition.labelLeadMeters : 0f;
                loop.SetLabelVisible(gap >= 0f && gap <= lead);
            }
        }

        // Too slow for the loop: the drop off the top is a hit of corruption
        // and a long rumble; the landing below rides the ordinary Landed path.
        void OnLoopFailed()
        {
            HapticsSystem.Instance.Pulse(settings.loopFailRumble);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.loopFallGlitchStrength);
        }

        // Touchdown: a thump in the hands and on the picture, a spray of
        // sparkles at the touchdown point, no speed change.
        void OnLanded()
        {
            HapticsSystem.Instance.Pulse(settings.landingRumble);
            CameraShake.Shake(settings.landingShake);
            SparkleVfx.SpawnBurst(motor.transform.position, motor.transform.up,
                                  settings.landingSparkleColor, settings.landingSparkleScale,
                                  settings.landingSparkleCount);
        }

        // Over an open edge: the picture glitches and the pad rumbles (the
        // patrol's hold is the GameManager's rule, the fall shot the director's).
        void OnFellOff()
        {
            HapticsSystem.Instance.Pulse(settings.fallRumble);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.fallGlitchStrength);
        }

        // Grip lost on a flat sweep: the warning before the edge, so it is a
        // long low rumble rather than the wall's sharp knock.
        void OnSliding(float excess)
        {
            HapticsSystem.Instance.Pulse(settings.slideRumble);
            CameraShake.Shake(settings.slideShake);
        }

        // Dash into the wall, or a ramp hit from the side: a thud in the
        // hands, a burst of signal corruption and a kick on the picture.
        void OnWallHit(float impactSpeed)
        {
            HapticsSystem.Instance.Pulse(settings.wallHitRumble);
            CameraShake.Shake(settings.wallHitShake);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.dashWallGlitchStrength);
        }
    }
}
