using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// WheelCollider vehicle sim shared by player and (later) AI cars: it
    /// reads whichever ICarInput sits beside it and drives the four wheels.
    /// All tunables live on the CarConfig asset — add knobs there, not here.
    /// Suspension and tire friction are re-applied every physics step so the
    /// config sliders work live while driving; one-time setup (mass, center
    /// of mass, substeps) reruns via the Apply Config button. Throttle
    /// opposite to the current travel direction brakes first, then reverses —
    /// the standard two-pedal arcade feel. Stability essentials per the plan:
    /// dropped center of mass, vehicle substeps, anti-roll bars, downforce.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("All handling tunables live on this asset — add new knobs there, not here.")]
        public CarConfig config;

        [TitleGroup("Wheels")] public WheelCollider frontLeft;
        [TitleGroup("Wheels")] public WheelCollider frontRight;
        [TitleGroup("Wheels")] public WheelCollider rearLeft;
        [TitleGroup("Wheels")] public WheelCollider rearRight;

        [TitleGroup("Wheel visuals")]
        [Tooltip("Optional mesh pivots synced to each wheel's world pose (position, steer and spin).")]
        public Transform frontLeftVisual;
        [TitleGroup("Wheel visuals")] public Transform frontRightVisual;
        [TitleGroup("Wheel visuals")] public Transform rearLeftVisual;
        [TitleGroup("Wheel visuals")] public Transform rearRightVisual;

        Rigidbody body;
        ICarInput input;
        float steerAngle;

        public Rigidbody Body => body;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public float SpeedKmh => Velocity.magnitude * 3.6f;

        /// <summary>Signed speed along the car's facing — negative while reversing.</summary>
        public float ForwardSpeed => Vector3.Dot(Velocity, transform.forward);

        public bool IsGrounded =>
            frontLeft.isGrounded || frontRight.isGrounded || rearLeft.isGrounded || rearRight.isGrounded;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        void Start()
        {
            // Bound in Start, not Awake: runtime-rigged cars (VehicleRigBuilder)
            // add the driver after this component, and Start runs once all
            // same-frame AddComponent calls are done but before any FixedUpdate.
            input = GetComponent<ICarInput>();
            ApplyConfig();
        }

        /// <summary>
        /// One-time chassis setup from the config: mass, dropped center of
        /// mass and wheel substeps. Press after tuning those sliders mid-run;
        /// everything else on the config applies live each physics step.
        /// </summary>
        [TitleGroup("Actions")]
        [Button("Apply Config", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
        public void ApplyConfig()
        {
            if (config == null || body == null || !HasWheels) return;

            body.mass = config.mass;
            body.ResetCenterOfMass();
            Vector3 com = body.centerOfMass;
            com.y -= config.centerOfMassDrop;
            body.centerOfMass = com;

            foreach (var wheel in Wheels())
                wheel.ConfigureVehicleSubsteps(
                    config.substepSpeedThreshold, config.substepsBelowThreshold, config.substepsAboveThreshold);
        }

        void FixedUpdate()
        {
            if (config == null || !HasWheels) return;

            float steer = input?.Steer ?? 0f;
            float throttle = input != null ? Mathf.Clamp(input.Throttle, -1f, 1f) : 0f;
            bool handbrake = input?.Handbrake ?? false;

            float speed01 = config.topSpeedKmh > 1f ? Mathf.Clamp01(SpeedKmh / config.topSpeedKmh) : 1f;

            ApplyWheelSetup(handbrake);
            ApplySteering(steer, speed01);
            ApplyDrive(throttle, handbrake, speed01);
            ApplyAntiRoll(frontLeft, frontRight);
            ApplyAntiRoll(rearLeft, rearRight);

            // Downforce grows with speed so the car never floats off crests at pace.
            body.AddForce(-transform.up * (config.downforce * Velocity.magnitude));
        }

        void LateUpdate()
        {
            SyncVisual(frontLeft, frontLeftVisual);
            SyncVisual(frontRight, frontRightVisual);
            SyncVisual(rearLeft, rearLeftVisual);
            SyncVisual(rearRight, rearRightVisual);
        }

        // ------------------------------------------------------------- physics

        /// <summary>
        /// Suspension and friction pushed from config to every wheel each
        /// step — cheap for four wheels, and it's what makes the inline config
        /// sliders live-tunable while driving. The handbrake loosens rear
        /// lateral grip here for drift turns.
        /// </summary>
        void ApplyWheelSetup(bool handbrake)
        {
            foreach (var wheel in Wheels())
            {
                wheel.suspensionDistance = config.suspensionDistance;
                JointSpring spring = wheel.suspensionSpring;
                spring.spring = config.springForce;
                spring.damper = config.damperForce;
                wheel.suspensionSpring = spring;

                bool rear = wheel == rearLeft || wheel == rearRight;
                WheelFrictionCurve forward = wheel.forwardFriction;
                forward.stiffness = config.forwardStiffness;
                wheel.forwardFriction = forward;

                WheelFrictionCurve side = wheel.sidewaysFriction;
                side.stiffness = config.sideStiffness * (rear && handbrake ? config.handbrakeGrip : 1f);
                wheel.sidewaysFriction = side;
            }
        }

        void ApplySteering(float steerInput, float speed01)
        {
            float maxAngle = config.maxSteerAngle * config.steerBySpeed.Evaluate(speed01);
            float target = steerInput * maxAngle;
            steerAngle = Mathf.MoveTowards(steerAngle, target, config.steerResponse * Time.fixedDeltaTime);
            frontLeft.steerAngle = steerAngle;
            frontRight.steerAngle = steerAngle;
        }

        void ApplyDrive(float throttle, bool handbrake, float speed01)
        {
            float forwardSpeed = ForwardSpeed;

            // Two-pedal arcade rule: throttle against the travel direction is a
            // brake until (nearly) stopped, then becomes drive the other way.
            bool opposing = Mathf.Abs(forwardSpeed) > 0.5f
                && throttle != 0f
                && Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed);

            float motor = 0f;
            float brake = 0f;
            if (opposing)
            {
                brake = config.brakeTorque * Mathf.Abs(throttle);
            }
            else if (throttle != 0f)
            {
                motor = config.maxMotorTorque * config.torqueBySpeed.Evaluate(speed01) * throttle;
                if (throttle < 0f) motor *= config.reverseTorqueFactor;
            }
            else if (!handbrake)
            {
                // Engine braking: a driverless car (fresh spawn, player away
                // from the keys) bleeds speed and settles on the road instead
                // of coasting blocks away; near standstill a firmer hold stops
                // downhill creep.
                brake = config.coastBrakeTorque;
                if (Mathf.Abs(forwardSpeed) < 1f)
                    brake = Mathf.Max(brake, config.brakeTorque * 0.1f);
            }

            bool frontDriven = config.drivetrain != CarConfig.Drivetrain.RearWheelDrive;
            bool rearDriven = config.drivetrain != CarConfig.Drivetrain.FrontWheelDrive;
            float perWheel = motor / ((frontDriven ? 2 : 0) + (rearDriven ? 2 : 0));

            frontLeft.motorTorque = frontRight.motorTorque = frontDriven ? perWheel : 0f;
            rearLeft.motorTorque = rearRight.motorTorque = rearDriven ? perWheel : 0f;

            frontLeft.brakeTorque = frontRight.brakeTorque = brake;
            float rearBrake = handbrake ? Mathf.Max(brake, config.handbrakeTorque) : brake;
            rearLeft.brakeTorque = rearRight.brakeTorque = rearBrake;
        }

        /// <summary>
        /// Classic anti-roll bar: the axle's compression difference becomes an
        /// opposing force pair, keeping the body flat through corners without
        /// stiffening the whole suspension.
        /// </summary>
        void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            if (config.antiRollForce <= 0f) return;

            float travelLeft = 1f, travelRight = 1f;
            bool groundedLeft = left.GetGroundHit(out WheelHit hitLeft);
            if (groundedLeft)
                travelLeft = (-left.transform.InverseTransformPoint(hitLeft.point).y - left.radius) / left.suspensionDistance;
            bool groundedRight = right.GetGroundHit(out WheelHit hitRight);
            if (groundedRight)
                travelRight = (-right.transform.InverseTransformPoint(hitRight.point).y - right.radius) / right.suspensionDistance;

            float force = (travelLeft - travelRight) * config.antiRollForce;
            if (groundedLeft) body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedRight) body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        // ------------------------------------------------------------- helpers

        static void SyncVisual(WheelCollider wheel, Transform visual)
        {
            if (wheel == null || visual == null) return;
            wheel.GetWorldPose(out Vector3 position, out Quaternion rotation);
            visual.SetPositionAndRotation(position, rotation);
        }

        bool HasWheels => frontLeft != null && frontRight != null && rearLeft != null && rearRight != null;

        IEnumerable<WheelCollider> Wheels()
        {
            yield return frontLeft;
            yield return frontRight;
            yield return rearLeft;
            yield return rearRight;
        }
    }
}
