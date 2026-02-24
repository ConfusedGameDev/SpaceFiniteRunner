using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Core vehicle simulation. Owns forward speed and deceleration.
    /// The player has NO direct control over acceleration or braking —
    /// the ship decelerates naturally from its initial speed until it stops.
    ///
    /// Track objects (SpeedBooster, SpeedDecreaser) call ModifySpeed() to influence speed.
    /// Steering input is fed from VehicleInputHandler via SetSteerInput().
    ///
    /// Rigidbody requirements (set in Inspector):
    ///   - Linear Drag  = 0
    ///   - Angular Drag = 0
    ///   - Interpolate  = Interpolate
    ///   - Collision Detection = Continuous
    ///   - Freeze Rotation: X = true, Z = true, Y = false
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        [SerializeField] VehicleStatsSO stats;

        [Tooltip("Optional child transform containing the visual mesh. " +
                 "Will be banked (rolled) left/right during steering for visual effect only.")]
        [SerializeField] Transform shipMesh;

        Rigidbody rb;
        float     currentSpeed;
        float     steerInput;    // [-1, 1] set each Update by VehicleInputHandler
        bool      isStopped;

        /// <summary>Current forward speed in m/s. Read-only from outside.</summary>
        public float CurrentSpeed => currentSpeed;

        /// <summary>True once the vehicle has coasted to a complete stop.</summary>
        public bool IsStopped => isStopped;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void Start()
        {
            currentSpeed = stats.initialSpeed;
            RaceEvents.RaiseSpeedChanged(currentSpeed);
        }

        void FixedUpdate()
        {
            if (isStopped) return;

            ApplyDeceleration();
            ApplyForwardVelocity();
            ApplySteering();

            RaceEvents.RaiseSpeedChanged(currentSpeed);

            if (currentSpeed <= 0f)
                HandleVehicleStopped();
        }

        void Update()
        {
            UpdateVisualBank();
        }

        // ── Movement ──────────────────────────────────────────────────────────────

        void ApplyDeceleration()
        {
            currentSpeed = Mathf.Max(0f, currentSpeed - stats.deceleration * Time.fixedDeltaTime);
        }

        void ApplyForwardVelocity()
        {
            // Direct velocity assignment along the forward axis.
            // The Y component is preserved so the hover spring forces are not overwritten.
            Vector3 forward = transform.forward * currentSpeed;
            rb.linearVelocity = new Vector3(forward.x, rb.linearVelocity.y, forward.z);
        }

        void ApplySteering()
        {
            // Apply yaw torque. The explicit damping term prevents infinite spin when
            // the player releases the stick, without relying on Rigidbody.angularDamping.
            float torque  = steerInput * stats.steerTorque;
            float damping = -rb.angularVelocity.y * stats.steerDamping;
            rb.AddTorque(transform.up * (torque + damping), ForceMode.Force);
        }

        // ── Visual banking ────────────────────────────────────────────────────────

        void UpdateVisualBank()
        {
            if (shipMesh == null) return;

            float targetBank = -steerInput * stats.maxBankAngle;
            float currentBank = shipMesh.localEulerAngles.z;

            // Convert from [0,360] to [-180,180] for smooth lerp
            if (currentBank > 180f) currentBank -= 360f;

            float newBank = Mathf.Lerp(currentBank, targetBank, stats.bankLerpSpeed * Time.deltaTime);
            shipMesh.localEulerAngles = new Vector3(
                shipMesh.localEulerAngles.x,
                shipMesh.localEulerAngles.y,
                newBank);
        }

        // ── Stop handling ─────────────────────────────────────────────────────────

        void HandleVehicleStopped()
        {
            isStopped = true;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            RaceEvents.RaiseVehicleStopped();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by VehicleInputHandler each Update frame.
        /// Value is read in the next FixedUpdate.
        /// </summary>
        public void SetSteerInput(float value)
        {
            steerInput = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>
        /// Single entry point for all speed modifications (boosters, decreasers).
        /// Clamps result to [0, maxSpeed].
        /// </summary>
        public void ModifySpeed(float delta)
        {
            currentSpeed = Mathf.Clamp(currentSpeed + delta, 0f, stats.maxSpeed);
            RaceEvents.RaiseSpeedChanged(currentSpeed);
        }

        /// <summary>World position of the vehicle — used by scoring to measure stop distance.</summary>
        public Vector3 WorldPosition => transform.position;
    }
}
