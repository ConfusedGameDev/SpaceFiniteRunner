using UnityEngine;

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using ConfusedGameDev.FiniteRunner.CameraFX;
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
    /// gates' tint and labels, and the Light Speed state: while the ship flies
    /// at Light Speed the hyperspace sky is held in and the lens distortion
    /// and motion blur are pushed up; below it (past a small exit margin, so
    /// it never flickers on the line) all three blend back to their authored
    /// values. Split out of the <see cref="GameManager"/>
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
        HyperspaceSky hyperspace; // null when the hyperspace sky is off on GameSettings

        // Light Speed warp: true while at Light Speed (with the exit margin),
        // and the lens / blur blend that follows it.
        bool atLightSpeed;
        bool falling; // FellOff → Respawned: over an open edge, the look lets go at once
        float warpBlend;
        LensDistortionController lens; // cached on the first warp, so teardown never creates one
        MotionBlur motionBlur;   // the global volume's runtime-profile override, found lazily
        float baseBlurIntensity; // its authored values, restored when the warp lets go
        float baseBlurClamp;

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

            // Hyperspace sky: the scene's hand-placed HyperspaceSky beside the
            // speed lines. It follows the ship's heading; Update holds it in
            // while the ship is at Light Speed.
            hyperspace = HyperspaceSky.Apply(settings.hyperspaceSkyEnabled);
            if (hyperspace != null && motor != null)
                hyperspace.SetTarget(motor.transform, () => motor.CurrentSpeed * 3.6f, run.LightSpeedKmh);

            SpeedPad.Collected += OnPadCollected;
            RepairOrb.Collected += OnRepairOrb;
            if (motor != null)
            {
                motor.PadImpulse += OnPadImpulse;
                motor.WallHit += OnWallHit;
                motor.Sliding += OnSliding;
                motor.FellOff += OnFellOff;
                motor.Respawned += OnRespawned;
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
                motor.Respawned -= OnRespawned;
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

        void OnDestroy()
        {
            Unsubscribe();
            ClearWarp();
        }

        /// <summary>A retry: no speed-line burst carried over (the speed term follows the relaunch on its own).</summary>
        public void ResetForRun()
        {
            if (speedLines != null) speedLines.ClearPulse();
            if (hyperspace != null) hyperspace.ResetForRun();
            ClearWarp();
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

            UpdateLightSpeed();
        }

        bool Quiet => run == null || run.RunOver || run.IsEnding;

        /// <summary>
        /// The Light Speed state, from the ship's CURRENT speed: in at Light
        /// Speed, out once it falls <see cref="GameSettings.lightSpeedExitMargin"/>
        /// under it — and never while the ship is falling (over an open edge
        /// until it relaunches, or off the end of the track without the win):
        /// the fall's speed would keep the warp up on a tumbling camera, so a
        /// fall starts the blend back on its first frame. Holds the hyperspace
        /// sky in (it fades itself) and blends
        /// the lens distortion and motion blur toward their Light Speed values
        /// over <see cref="GameSettings.lightSpeedWarpBlendSeconds"/>.
        /// </summary>
        void UpdateLightSpeed()
        {
            if (motor == null) return;
            float kmh = motor.CurrentSpeed * 3.6f;
            float light = run.LightSpeedKmh;
            bool fallingNow = falling || (motor.HasLeftTrackEnd && !motor.IsEscaping);
            atLightSpeed = !fallingNow && (atLightSpeed
                ? kmh >= light * (1f - settings.lightSpeedExitMargin)
                : kmh >= light);

            if (hyperspace != null) hyperspace.SetEngaged(atLightSpeed);

            if (!settings.lightSpeedWarpEnabled)
            {
                if (warpBlend > 0f) ClearWarp();
                return;
            }
            float seconds = settings.lightSpeedWarpBlendSeconds;
            warpBlend = Mathf.MoveTowards(warpBlend, atLightSpeed ? 1f : 0f,
                                          seconds > 0f ? Time.deltaTime / seconds : 1f);
            ApplyWarp();
        }

        // Lens through its controller's held layer (its boost kicks ride on
        // top); blur straight on the global volume's runtime profile — the
        // copy the lens controller already made, so the asset is never written.
        void ApplyWarp()
        {
            if (lens == null) lens = LensDistortionController.Instance;
            lens.SetHeld(warpBlend, settings.lightSpeedLensIntensity);

            if (motionBlur == null)
            {
                if (warpBlend <= 0f || lens.volume == null) return;
                VolumeProfile profile = lens.volume.profile; // the runtime copy
                if (!profile.TryGet(out motionBlur))
                {
                    motionBlur = profile.Add<MotionBlur>();
                    motionBlur.intensity.value = 0f;
                    motionBlur.clamp.value = 0.05f;
                }
                motionBlur.active = true;
                motionBlur.intensity.overrideState = true;
                motionBlur.clamp.overrideState = true;
                baseBlurIntensity = motionBlur.intensity.value;
                baseBlurClamp = motionBlur.clamp.value;
            }
            motionBlur.intensity.value = Mathf.Lerp(baseBlurIntensity, settings.lightSpeedMotionBlurIntensity, warpBlend);
            motionBlur.clamp.value = Mathf.Lerp(baseBlurClamp, settings.lightSpeedMotionBlurClamp, warpBlend);
        }

        // Back to the authored lens and blur at once (a retry, or teardown).
        void ClearWarp()
        {
            atLightSpeed = false;
            falling = false;
            warpBlend = 0f;
            if (lens != null) lens.SetHeld(0f, 0f);
            if (motionBlur != null)
            {
                motionBlur.intensity.value = baseBlurIntensity;
                motionBlur.clamp.value = baseBlurClamp;
            }
        }

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
            falling = true; // the Light Speed look starts fading this frame (UpdateLightSpeed)
            HapticsSystem.Instance.Pulse(settings.fallRumble);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.fallGlitchStrength);
        }

        // Back on the road after a fall: the Light Speed look may come back with the speed.
        void OnRespawned() => falling = false;

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
