using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Drives the ship along the track spline. Core speed rule: an initial
    /// impulse at launch followed by constant decay — pads are the only way
    /// to regain speed. Speed 0 before the goal ends the run.
    /// All tunables come from the assigned <see cref="ShipDefinition"/>.
    /// </summary>
    public class ShipMotor : MonoBehaviour
    {
        [SerializeField] ShipDefinition definition;
        [SerializeField] TrackManager track;

        [Tooltip("Visual child that banks when steering (the ship model). Falls back to this transform.")]
        [SerializeField] Transform visual;

        public ShipDefinition Definition => definition;
        public float CurrentSpeed { get; private set; }
        public float DistanceTravelled { get; private set; }
        public float DistanceToGoal => track != null ? Mathf.Max(0f, track.Length - DistanceTravelled) : 0f;
        public bool HasStopped { get; private set; }
        public bool HasFinished { get; private set; }
        public float FinalSpeed { get; private set; }

        /// <summary>Raised when a pad impulse is applied. Argument is the raw magnitude (positive = boost).</summary>
        public event System.Action<float> PadImpulse;

        /// <summary>While true the simulation is frozen (setup screen); the hover keeps running.</summary>
        public bool Paused { get; set; }

        ISteeringInput steering;
        float splineT;
        float lateralOffset;
        float lateralVelocity;
        float bankAngle;
        float pendingSpeedChange; // pad effects blend in via ShipDefinition.acceleration

        void Awake()
        {
            steering = GetComponent<ISteeringInput>();
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

        /// <summary>Resets the run to the track start and applies the initial impulse.</summary>
        public void Launch()
        {
            CurrentSpeed = Mathf.Min(definition.initialImpulse, definition.maxSpeed);
            DistanceTravelled = 0f;
            splineT = 0f;
            lateralOffset = 0f;
            lateralVelocity = 0f;
            pendingSpeedChange = 0f;
            HasStopped = false;
            HasFinished = false;
            FinalSpeed = 0f;
            ApplyPose(0f);
        }

        /// <summary>
        /// Called by pads and card effects. Positive boosts, negative brakes.
        /// The change is scaled by weight and blended in over time by acceleration.
        /// </summary>
        public void AddSpeedImpulse(float rawMagnitude)
        {
            if (HasStopped || HasFinished) return;
            pendingSpeedChange += definition.ScalePadEffect(rawMagnitude);
            PadImpulse?.Invoke(rawMagnitude);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (!Paused && !HasStopped && !HasFinished)
            {
                UpdateSpeed(dt);
                UpdateLateral(dt);
                AdvanceAlongTrack(dt);
                ApplyPose(dt);

                if (!HasFinished && CurrentSpeed <= 0f)
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

            // The core rule: speed always bleeds away.
            CurrentSpeed -= definition.passiveDeceleration * dt;
            CurrentSpeed = Mathf.Clamp(CurrentSpeed, 0f, definition.maxSpeed);
        }

        void UpdateLateral(float dt)
        {
            float steer = steering?.SteerAxis ?? 0f;
            float halfWidth = track.HalfWidth;

            float targetVelocity = steer * definition.lateralSpeed;
            lateralVelocity = Mathf.MoveTowards(
                lateralVelocity, targetVelocity,
                definition.lateralSpeed * definition.handlingResponse * dt);

            lateralOffset = Mathf.Clamp(lateralOffset + lateralVelocity * dt, -halfWidth, halfWidth);
            if (Mathf.Abs(lateralOffset) >= halfWidth) lateralVelocity = 0f;
        }

        void AdvanceAlongTrack(float dt)
        {
            float step = CurrentSpeed * dt;
            DistanceTravelled += step;
            splineT = track.AdvanceT(splineT, step);

            if (splineT >= 1f)
            {
                splineT = 1f;
                HasFinished = true;
                FinalSpeed = CurrentSpeed;
            }
        }

        void ApplyPose(float dt)
        {
            track.GetPose(splineT, lateralOffset, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);

            // Bank into the movement: roll opposite to lateral velocity.
            float targetBank = -(lateralVelocity / Mathf.Max(definition.lateralSpeed, 0.01f)) *
                               definition.maxBankAngle;
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
