using Sirenix.OdinInspector;
using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Drives the ship along the track spline. Core speed rule: an initial
    /// impulse at launch followed by constant decay — pads are the only way
    /// to regain speed, and there is no upper cap (the win condition is
    /// reaching Light Speed). The track streams endlessly ahead (see
    /// TrackGenerator); the run ends when speed hits 0 or the GameManager's
    /// timer expires. All tunables come from the assigned <see cref="ShipDefinition"/>.
    /// </summary>
    public class ShipMotor : MonoBehaviour
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

        /// <summary>Raised when a dash slams the track edge. Argument: lateral impact speed in m/s.</summary>
        public event System.Action<float> WallHit;

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
                ApplyPose(dt);

                if (CurrentSpeed <= 0f)
                {
                    CurrentSpeed = 0f;
                    HasStopped = true;
                }
            }

            // Hover runs even after the run ends, so the ship keeps floating.
            ApplyHover();
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

            float targetVelocity = steer * definition.lateralSpeed;
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
                                             * Mathf.Clamp01(dashTimeLeft / duration);
                dashTimeLeft -= dt;
            }
            dashVelocityThisFrame = dashVelocity;

            // The clamp is what guarantees a dash can never leave the track.
            lateralOffset = Mathf.Clamp(lateralOffset + (lateralVelocity + dashVelocity) * dt,
                                        -halfWidth, halfWidth);
            if (Mathf.Abs(lateralOffset) >= halfWidth)
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

        void ApplyPose(float dt)
        {
            track.GetPose(splineT, lateralOffset, out Vector3 position, out Quaternion rotation);
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
        // The root (and its trigger collider) stays on the flight line, so pads still work.
        void ApplyHover()
        {
            float t = Time.time * definition.bobFrequency;
            float bob = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * definition.bobAmplitude;
            float pitch = (Mathf.PerlinNoise(0.71f, t * 0.8f) - 0.5f) * 2f * definition.hoverPitchDegrees;

            visual.localPosition = new Vector3(0f, definition.hoverHeight + bob, 0f);
            visual.localRotation = Quaternion.Euler(pitch, 0f, bankAngle);
        }
    }
}
