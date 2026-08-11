using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Smooth chase camera with velocity look-ahead, per the plan's player
    /// car brief: position damped behind a follow direction that blends the
    /// car's facing toward its actual travel direction (so drifts swing the
    /// camera out), aim led by velocity, FOV widened with speed. All feel
    /// knobs live on the ChaseCameraSettings asset. Runs in LateUpdate; the
    /// car's rigidbody interpolates, so the read position is already smooth.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ChaseCamera : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("All camera-feel tunables live on this asset — add new knobs there, not here.")]
        public ChaseCameraSettings settings;

        [Required]
        [Tooltip("The car to chase.")]
        public CarController target;

        Camera cam;
        Vector3 positionVelocity;
        Vector3 smoothedLookPoint;

        void Awake() => cam = GetComponent<Camera>();

        void OnEnable()
        {
            if (target != null) SnapBehindTarget();
        }

        void LateUpdate()
        {
            if (settings == null || target == null) return;

            Transform car = target.transform;
            Vector3 velocity = target.Velocity;

            // Follow direction: the car's facing, bent toward the travel
            // direction while moving forward. Reversing keeps the camera on
            // the nose side so backing up doesn't spin it around.
            Vector3 forward = car.forward;
            if (velocity.sqrMagnitude > 1f && Vector3.Dot(velocity, car.forward) > 0f)
                forward = Vector3.Slerp(forward, velocity.normalized, settings.velocityAlignment);
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            Vector3 desired = car.position - forward * settings.followDistance + Vector3.up * settings.followHeight;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref positionVelocity, settings.positionSmoothTime);

            Vector3 lookPoint = car.position + Vector3.up * settings.lookHeight + velocity * settings.lookAhead;
            smoothedLookPoint = Vector3.Lerp(smoothedLookPoint, lookPoint, 1f - Mathf.Exp(-settings.lookSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(smoothedLookPoint - transform.position, Vector3.up);

            cam.fieldOfView = settings.baseFov + Mathf.Min(settings.maxFovBoost, target.SpeedKmh * settings.fovPerKmh);
        }

        /// <summary>Teleport straight to the follow pose — used on enable and after respawns so the camera never swoops across the city.</summary>
        public void SnapBehindTarget()
        {
            if (settings == null || target == null) return;
            Transform car = target.transform;
            Vector3 forward = car.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            transform.position = car.position - forward * settings.followDistance + Vector3.up * settings.followHeight;
            smoothedLookPoint = car.position + Vector3.up * settings.lookHeight;
            positionVelocity = Vector3.zero;
            transform.rotation = Quaternion.LookRotation(smoothedLookPoint - transform.position, Vector3.up);
        }
    }
}
