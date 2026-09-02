using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Drives the ship along the track spline. Core speed rule: an initial
    /// impulse at launch followed by constant decay — pads are the only way
    /// to regain speed, and there is no upper cap (the win condition is
    /// reaching Light Speed). The track streams endlessly ahead (see
    /// TrackGenerator); the run ends when speed hits 0 or the GameManager's
    /// timer expires. All tunables come from the assigned <see cref="ShipDefinition"/>.
    ///
    /// <b>Jumps</b> are resolved here, analytically: every frame the motor
    /// scans the live <see cref="JumpRamp"/>s. Inside a ramp's run-up with
    /// the centre far enough inside its edge, the ship is committed — lateral
    /// pinned to the ramp (side rails), root riding up the slope — and
    /// launches at the lip into a parabola authored in track distance (see
    /// <see cref="JumpDefinition"/>); beside a ramp, its edge is a wall that
    /// costs speed and fires <see cref="WallHit"/> when hit. <see cref="State"/>
    /// is the one place anything reads "is the ship flying". The root itself
    /// rises above the flight line while on a ramp or airborne, so ground
    /// orbs and brake pads are physically missed rather than ignored.
    /// It is also the chase camera's <see cref="ICameraTarget"/>: the rig
    /// follows this root (the pose above the flight line, never the bobbing
    /// visual) and the view cycle is locked while airborne.
    /// </summary>
    public class ShipMotor : MonoBehaviour, ICameraTarget
    {
        // Inline so the ship's sliders are reachable without leaving the scene —
        // in play mode this field holds the tuning screen's runtime clone, so
        // editing here tweaks the live run and never the asset on disk.
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)] ShipDefinition definition;
        [SerializeField, Required] TrackManager track;

        [Tooltip("Visual child that banks when steering (the ship model). Falls back to this transform.")]
        [SerializeField] Transform visual;

        public ShipDefinition Definition => definition;
        public float CurrentSpeed { get; private set; }
        public float DistanceTravelled { get; private set; }
        public bool HasStopped { get; private set; }

        /// <summary>Raised when a pad impulse is applied. Argument is the raw magnitude (positive = boost).</summary>
        public event System.Action<float> PadImpulse;

        /// <summary>Raised when a lateral dash fires. Argument: -1 left, +1 right.</summary>
        public event System.Action<int> DashPerformed;

        /// <summary>Raised each time the dash meter reaches full (edge, not per frame).</summary>
        public event System.Action MeterFilled;

        /// <summary>Raised when a dash slams the track edge, or the ship hits a ramp from the side. Argument: lateral impact speed in m/s.</summary>
        public event System.Action<float> WallHit;

        /// <summary>Raised on every <see cref="State"/> change, after the new state is set.</summary>
        public event System.Action<ShipState> StateChanged;

        /// <summary>Raised the frame the ship leaves a ramp's lip.</summary>
        public event System.Action TookOff;

        /// <summary>Raised the frame the ship's arc returns to the flight line.</summary>
        public event System.Action Landed;

        /// <summary>While true the simulation is frozen (setup screen); the hover keeps running.</summary>
        public bool Paused { get; set; }

        /// <summary>Dash power meter, 0..1. Starts each run empty.</summary>
        public float DashMeter => dashMeter;

        /// <summary>True during the short dash burst.</summary>
        public bool IsDashing => dashTimeLeft > 0f;

        /// <summary>The banking/hovering model child — for visual-only consumers (ghost trail).</summary>
        public Transform Visual => visual;

        /// <summary>The run-level dash rules pushed in by the GameManager; null while unconfigured.</summary>
        public GameSettings DashSettings => dashSettings;

        /// <summary>Grounded or Airborne — see <see cref="ShipState"/>.</summary>
        public ShipState State { get; private set; }

        /// <summary>Seconds since takeoff while airborne; the last flight's length otherwise.</summary>
        public float AirTime { get; private set; }

        /// <summary>Height of the root above the flight line (ramp slope or arc), metres.</summary>
        public float AirHeight => height;

        /// <summary>The ramp the ship is committed to (riding its run-up), or null.</summary>
        public JumpRamp CurrentRamp => ramp;

        // ------------------------------------------------------ ICameraTarget
        Transform ICameraTarget.Transform => transform;
        float ICameraTarget.SpeedKmh => CurrentSpeed * 3.6f;
        // The ship's box is a trigger volume, not a hull — the eye is authored on the camera settings instead.
        bool ICameraTarget.TryGetChassisBox(out Vector3 localCentre, out float localTop)
        {
            localCentre = Vector3.zero;
            localTop = 0f;
            return false;
        }
        bool ICameraTarget.BlockPanInput => false; // the ship steers with the left stick and the arrows; the right stick is the camera's
        // A jump forces the Far framing; the cycle is locked so it cannot be undone mid-arc.
        bool ICameraTarget.BlockModeCycle => MainMenuController.IsOpen || State == ShipState.Airborne;

        ISteeringInput steering;
        IDashInput dashInput;
        GameSettings dashSettings; // null = dash feature off
        float splineT;
        float lateralOffset;
        float lateralVelocity;
        float bankAngle;
        float pendingSpeedChange; // pad effects blend in via ShipDefinition.acceleration
        float dashMeter;
        float dashTimeLeft;
        int dashDirection;
        float dashVelocityThisFrame; // handed from UpdateLateral to ApplyPose for the bank
        float wallHitCooldown;
        bool meterWasFull;

        // Jump state. `height` is the root's lift above the flight line; the
        // arc is y(s) = arcH0 + arcSlope·s − arcA·s² over s = distance past the
        // lip, chosen so it leaves the lip tangent to the ramp and lands at
        // exactly airLength (see JumpDefinition).
        float height;
        JumpRamp ramp;          // committed: riding the run-up
        JumpRamp blockingRamp;  // beside a ramp: its edge is a wall this frame
        int blockSide;          // which side of the blocking ramp the ship is on (+1 right)
        JumpDefinition airDefinition; // the jump in flight (control authority)
        float airStart, airLength, arcH0, arcSlope, arcA;
        float visualPitch;      // nose up on the slope and the climb, down on the descent
        float shownPitch;       // visualPitch, eased

        void Awake()
        {
            steering = GetComponent<ISteeringInput>();
            dashInput = GetComponent<IDashInput>();
            if (visual == null) visual = transform;

            if (track == null)
            {
                Debug.LogError("ShipMotor needs a TrackManager reference.", this);
                enabled = false;
            }
        }

        // Launch in Start so a TrackGenerator's Awake can rebuild the spline first.
        void Start() => Launch();

        /// <summary>Swap the active definition (used by the tuning screen with a runtime clone).</summary>
        public void SetDefinition(ShipDefinition newDefinition) => definition = newDefinition;

        /// <summary>
        /// Hands the motor the dash tunables (GameManager pushes the shared
        /// GameSettings asset here — the motor holds no settings reference of
        /// its own). Null or dashEnabled off leaves the feature inert.
        /// </summary>
        public void ConfigureDash(GameSettings settings)
        {
            dashSettings = settings;
            if (dashInput != null && settings != null)
                dashInput.DoubleTapSeconds = settings.dashDoubleTapSeconds;
        }

        /// <summary>Resets the run to the track start and applies the initial impulse.</summary>
        public void Launch()
        {
            CurrentSpeed = definition.initialImpulse;
            DistanceTravelled = 0f;
            splineT = 0f;
            lateralOffset = 0f;
            lateralVelocity = 0f;
            pendingSpeedChange = 0f;
            dashMeter = 0f; // the meter charges from empty every run
            dashTimeLeft = 0f;
            dashVelocityThisFrame = 0f;
            wallHitCooldown = 0f;
            meterWasFull = false;
            HasStopped = false;

            // Back on the line, silently: a restart mid-arc is not a landing.
            height = 0f;
            ramp = null;
            blockingRamp = null;
            airDefinition = null;
            AirTime = 0f;
            visualPitch = 0f;
            SetState(ShipState.Grounded);

            ApplyPose(0f);
        }

        /// <summary>
        /// Called by pads and card effects. Positive boosts, negative brakes.
        /// The change is scaled by weight and blended in over time by acceleration.
        /// </summary>
        public void AddSpeedImpulse(float rawMagnitude)
        {
            if (HasStopped) return;
            pendingSpeedChange += definition.ScalePadEffect(rawMagnitude);
            PadImpulse?.Invoke(rawMagnitude);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (!Paused && !HasStopped)
            {
                UpdateSpeed(dt);
                UpdateDash(dt);
                UpdateLateral(dt);
                AdvanceAlongTrack(dt);
                UpdateJump(dt);
                ApplyPose(dt);

                if (CurrentSpeed <= 0f)
                {
                    CurrentSpeed = 0f;
                    HasStopped = true;
                }
            }

            // Hover runs even after the run ends, so the ship keeps floating.
            ApplyHover(dt);
        }

        void UpdateSpeed(float dt)
        {
            // Blend queued pad effects in at the ship's acceleration rate.
            if (!Mathf.Approximately(pendingSpeedChange, 0f))
            {
                float step = Mathf.Sign(pendingSpeedChange) *
                             Mathf.Min(Mathf.Abs(pendingSpeedChange), definition.acceleration * dt);
                CurrentSpeed += step;
                pendingSpeedChange -= step;
            }

            // The core rule: speed always bleeds away. No upper cap — the win
            // condition is climbing all the way to Light Speed.
            CurrentSpeed = Mathf.Max(0f, CurrentSpeed - definition.passiveDeceleration * dt);
        }

        // Meter recharge and dash triggering. Runs before UpdateLateral so a
        // request fires on the frame it was consumed.
        void UpdateDash(float dt)
        {
            if (dashSettings == null || !dashSettings.dashEnabled) return;

            // Recharge rate is a ship stat — the tuning screen's clone carries it.
            dashMeter = Mathf.MoveTowards(dashMeter, 1f, dt / Mathf.Max(definition.dashRechargeSeconds, 0.01f));
            if (dashMeter >= 1f)
            {
                if (!meterWasFull)
                {
                    meterWasFull = true;
                    MeterFilled?.Invoke();
                }
            }
            else meterWasFull = false;

            wallHitCooldown = Mathf.Max(0f, wallHitCooldown - dt);

            int request = dashInput?.ConsumeDashRequest() ?? 0;
            if (request != 0 && !IsDashing && dashMeter >= dashSettings.dashCost)
            {
                dashMeter -= dashSettings.dashCost;
                dashTimeLeft = definition.dashDuration;
                dashDirection = request;
                DashPerformed?.Invoke(request);
            }
        }

        void UpdateLateral(float dt)
        {
            float steer = steering?.SteerAxis ?? 0f;
            float halfWidth = track.HalfWidth;

            // In the air the stick and the dash both work at the jump's reduced authority.
            float control = State == ShipState.Airborne && airDefinition != null ? airDefinition.airControlFactor : 1f;

            float targetVelocity = steer * definition.lateralSpeed * control;
            lateralVelocity = Mathf.MoveTowards(
                lateralVelocity, targetVelocity,
                definition.lateralSpeed * definition.handlingResponse * dt);

            // The dash is an additive velocity with its own state, so the
            // steering MoveTowards above never sees (and never decays) it.
            // Triangular ease-out profile: starts strong, tapers to 0, and
            // integrates to exactly dashDistance over dashDuration.
            float dashVelocity = 0f;
            if (dashTimeLeft > 0f && dashSettings != null)
            {
                float duration = Mathf.Max(definition.dashDuration, 0.01f);
                dashVelocity = dashDirection * (2f * definition.dashDistance / duration)
                                             * Mathf.Clamp01(dashTimeLeft / duration) * control;
                dashTimeLeft -= dt;
            }
            dashVelocityThisFrame = dashVelocity;

            // The clamp is what guarantees a dash can never leave the track.
            lateralOffset = Mathf.Clamp(lateralOffset + (lateralVelocity + dashVelocity) * dt, -halfWidth, halfWidth);
            bool hitEdge = Mathf.Abs(lateralOffset) >= halfWidth;

            // Committed to a ramp: the side rails hold the ship on the slope.
            if (ramp != null)
            {
                float rail = Mathf.Max(0f, ramp.HalfWidth - ramp.Definition.entryMargin);
                lateralOffset = Mathf.Clamp(lateralOffset, ramp.Lateral - rail, ramp.Lateral + rail);
            }
            // Beside a ramp: its edge is a wall. Crossing into it is a side hit —
            // the ship is held outside, rumbles, glitches and loses a slice of
            // speed (WallHit is the shared feedback path with the dash slam).
            else if (blockingRamp != null && State == ShipState.Grounded && blockingRamp.Spans(DistanceTravelled))
            {
                float edge = blockingRamp.Lateral + blockSide * Mathf.Max(0f, blockingRamp.HalfWidth - blockingRamp.Definition.entryMargin);
                bool intoWall = blockSide > 0 ? lateralOffset < edge : lateralOffset > edge;
                if (intoWall)
                {
                    lateralOffset = edge;
                    if (wallHitCooldown <= 0f)
                    {
                        CurrentSpeed *= 1f - Mathf.Clamp01(blockingRamp.Definition.sideHitSpeedLoss);
                        WallHit?.Invoke(Mathf.Abs(lateralVelocity + dashVelocity));
                        wallHitCooldown = dashSettings != null ? dashSettings.dashWallHitCooldownSeconds : 0.5f;
                    }
                    dashTimeLeft = 0f;
                    dashVelocityThisFrame = 0f;
                    lateralVelocity = 0f;
                }
            }

            if (hitEdge)
            {
                // Only a dash carried into the wall counts as a slam — gentle
                // steering saturation stays silent, and a cooldown stops spam.
                if (dashVelocity != 0f && wallHitCooldown <= 0f)
                {
                    WallHit?.Invoke(Mathf.Abs(lateralVelocity + dashVelocity));
                    wallHitCooldown = dashSettings.dashWallHitCooldownSeconds;
                }
                dashTimeLeft = 0f; // the wall ends the dash
                dashVelocityThisFrame = 0f;
                lateralVelocity = 0f;
            }
        }

        void AdvanceAlongTrack(float dt)
        {
            DistanceTravelled += CurrentSpeed * dt;

            // Distance is authoritative: the endless streamer grows the spline
            // during the run, which shifts what any given normalized t means,
            // so remap from distance every frame instead of advancing t.
            splineT = track.DistanceToT(DistanceTravelled);
        }

        /// <summary>
        /// Jump state machine, off the distance just advanced: the arc while
        /// airborne, the slope while committed, otherwise a scan of the live
        /// ramps for a run-up the ship is on — inside the entry band it is
        /// committed, beside it the ramp becomes next frame's wall.
        /// </summary>
        void UpdateJump(float dt)
        {
            float d = DistanceTravelled;

            if (State == ShipState.Airborne)
            {
                AirTime += dt;
                float s = d - airStart;
                if (s >= airLength) { Land(); return; }
                height = Mathf.Max(0f, arcH0 + arcSlope * s - arcA * s * s);
                float slope = arcSlope - 2f * arcA * s;
                visualPitch = -Mathf.Atan(slope) * Mathf.Rad2Deg;
                return;
            }

            if (ramp != null)
            {
                if (d >= ramp.EndDistance) { TakeOff(); return; }
                height = ramp.HeightAt(d);
                visualPitch = -ramp.Definition.rampAngle;
                return;
            }

            height = 0f;
            visualPitch = 0f;
            blockingRamp = null;
            foreach (var candidate in JumpRamp.Active)
            {
                if (candidate == null || candidate.Definition == null || !candidate.Spans(d)) continue;
                float rel = lateralOffset - candidate.Lateral;
                float inner = Mathf.Max(0f, candidate.HalfWidth - candidate.Definition.entryMargin);
                if (Mathf.Abs(rel) <= inner)
                {
                    ramp = candidate; // committed: no abort window at these speeds
                    height = ramp.HeightAt(d);
                    visualPitch = -ramp.Definition.rampAngle;
                    break;
                }
                blockingRamp = candidate;
                blockSide = rel >= 0f ? 1 : -1;
            }
        }

        void TakeOff()
        {
            JumpDefinition def = ramp.Definition;
            airDefinition = def;
            airStart = ramp.EndDistance;
            airLength = def.AirDistanceFor(CurrentSpeed);
            arcH0 = def.LipHeight;
            arcSlope = def.Slope;
            arcA = (arcH0 + arcSlope * airLength) / Mathf.Max(airLength * airLength, 0.01f);
            height = arcH0;
            AirTime = 0f;
            float boost = ramp.Boost;
            ramp = null;
            blockingRamp = null;

            SetState(ShipState.Airborne);
            // The takeoff boost rides the pad path: "+N" text, shake and rumble come free.
            if (boost != 0f) AddSpeedImpulse(boost);
            TookOff?.Invoke();
        }

        void Land()
        {
            height = 0f;
            visualPitch = 0f;
            airDefinition = null;
            SetState(ShipState.Grounded);
            Landed?.Invoke();
        }

        void SetState(ShipState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }

        void ApplyPose(float dt)
        {
            track.GetPose(splineT, lateralOffset, out Vector3 position, out Quaternion rotation);
            // The lift is along the track's up, so it survives roll (loops, tubes).
            if (height > 0f) position += rotation * (Vector3.up * height);
            transform.SetPositionAndRotation(position, rotation);

            // Bank into the movement: roll opposite to lateral velocity. A dash
            // pushes a little past full bank (clamped so it reads as a hard
            // lean, not a barrel roll).
            float normalizedLateral = Mathf.Clamp(
                (lateralVelocity + dashVelocityThisFrame) / Mathf.Max(definition.lateralSpeed, 0.01f),
                -1.25f, 1.25f);
            float targetBank = -normalizedLateral * definition.maxBankAngle;
            bankAngle = dt > 0f
                ? Mathf.Lerp(bankAngle, targetBank, 1f - Mathf.Exp(-definition.bankResponse * dt))
                : 0f;
        }

        // Visual-only float: offset + organic bob and pitch wobble on the model.
        // The root (and its trigger collider) stays on the flight line — or on
        // the jump arc above it — so pads still work; the model only adds
        // hover and the nose-up/down of a slope or a flight.
        void ApplyHover(float dt)
        {
            float t = Time.time * definition.bobFrequency;
            float bob = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * definition.bobAmplitude;
            float pitch = (Mathf.PerlinNoise(0.71f, t * 0.8f) - 0.5f) * 2f * definition.hoverPitchDegrees;
            shownPitch = dt > 0f ? Mathf.Lerp(shownPitch, visualPitch, 1f - Mathf.Exp(-8f * dt)) : visualPitch;

            visual.localPosition = new Vector3(0f, definition.hoverHeight + bob, 0f);
            visual.localRotation = Quaternion.Euler(pitch + shownPitch, 0f, bankAngle);
        }
    }
}
