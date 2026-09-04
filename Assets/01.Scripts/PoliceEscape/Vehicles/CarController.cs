using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Screens;
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
    public class CarController : MonoBehaviour, ICameraTarget
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

        [TitleGroup("Identity")]
        [Tooltip("What this car is and what colour it is. Authored on prefab cars; stamped at spawn from the traffic definition on model-rigged ones.")]
        [HideLabel, InlineProperty]
        public VehicleIdentity identity;

        Rigidbody body;
        ICarInput input;
        float steerAngle;
        EvpCarBackend evpBackend;

        /// <summary>True when the EVP5 comparison backend drives this car instead of the built-in sim.</summary>
        public bool UsingEvp => evpBackend != null;

        public VehicleKind Kind => identity.kind;
        public VehiclePaint Paint => identity.paint;
        public Color PaintColor => identity.color;

        public Rigidbody Body => body;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public float SpeedKmh => Velocity.magnitude * 3.6f;

        /// <summary>Signed speed along the car's facing — negative while reversing.</summary>
        public float ForwardSpeed => Vector3.Dot(Velocity, transform.forward);

        // ------------------------------------------------------ ICameraTarget
        Transform ICameraTarget.Transform => transform;
        // The root box is the chassis everything reads; its top is the roofline the first-person eye clears.
        bool ICameraTarget.TryGetChassisBox(out Vector3 localCentre, out float localTop)
        {
            if (TryGetComponent(out BoxCollider box))
            {
                localCentre = box.center;
                localTop = box.center.y + box.size.y * 0.5f;
                return true;
            }
            localCentre = Vector3.zero;
            localTop = 0f;
            return false;
        }
        // Mid-jump the stick and the arrows belong to the car (AirTimeSlowMo's air control).
        bool ICameraTarget.BlockPanInput => AirTimeSlowMo.IsActive;
        bool ICameraTarget.BlockModeCycle => MainMenuController.IsOpen;

        /// <summary>
        /// True while the rear lights should be lit — what <see cref="BrakeLights"/>
        /// shows: braking (throttle against the travel direction), the
        /// handbrake, or reverse (reverse throttle, or actually rolling
        /// backwards past <see cref="ReverseLightSpeed"/>). Engine braking on
        /// a released throttle does not count. Written by whichever backend
        /// simulates this car: the built-in drive step here, or
        /// <see cref="EvpCarBackend"/> in EVP mode.
        /// </summary>
        public bool RearLightsOn { get; internal set; }

        /// <summary>Backward speed (m/s) past which a car counts as reversing for the rear lights, whatever the pedal says.</summary>
        public const float ReverseLightSpeed = 0.5f;

        /// <summary>
        /// True while the car is burning out — fronts clamped, rears spinning.
        /// Written by whichever backend simulates this car (the
        /// <see cref="RearLightsOn"/> pattern); <see cref="BurnoutEffects"/>
        /// reads it for smoke and the built-in rev loop.
        /// </summary>
        public bool BurnoutActive { get; internal set; }

        /// <summary>
        /// The shared burnout gate both backends evaluate every step: the
        /// player's gas+brake gesture, no handbrake (it owns the drift feel),
        /// a rear wheel on the ground (airborne, the same two pedals are
        /// AirTimeSlowMo's clock) and near standstill — past the config's
        /// speed cap the pedals fall back to their normal meaning.
        /// (The WheelColliders keep simulating suspension under EVP, so
        /// isGrounded is valid in both modes.)
        /// </summary>
        internal bool WantsBurnout(ICarInput driver) =>
            driver != null && driver.Burnout && !driver.Handbrake
            && (rearLeft.isGrounded || rearRight.isGrounded)
            && Mathf.Abs(ForwardSpeed) * 3.6f < config.burnoutMaxSpeedKmh;

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

            // Backend toggle: with EVP selected, an EVP5 VehicleController is
            // installed over this same rigidbody and wheels, and this component
            // stops simulating — it stays as the car's identity and read-only
            // API (speed, body, config) for everything else in the game.
            SetBackend(VehiclePhysicsSettings.UseEvp);

            ApplyConfig();

            // Same coverage rule as the backend: every car built through any
            // path gets its brake lights here, no spawn-site wiring.
            BrakeLights.Ensure(this);

            // Player only: the air-time slow-mo owns the global clock, and the
            // project's player marker is the CarInput driver — an AI car off a
            // ramp must never slow the game. Burnout juice is player-only for
            // the same reason (only CarInput ever raises the gesture).
            if (input is CarInput)
            {
                AirTimeSlowMo.Ensure(this);
                BurnoutEffects.Ensure(this);
            }
        }

        /// <summary>
        /// Install or remove the EVP5 comparison sim on this live car — the
        /// runtime half of the <see cref="VehiclePhysicsSettings"/> toggle.
        /// Idempotent, so the debug menu can sweep every car on a flip and
        /// newly spawned cars can call it from Start unconditionally.
        /// </summary>
        public void SetBackend(bool evp)
        {
            // A prefab that ships its own EVP VehicleController (the EVP demo
            // cars) would otherwise keep simulating beside the built-in sim:
            // park it before the idempotence check, which is what returns
            // early on a fresh instance in built-in mode.
            if (!evp) EvpCarBackend.ParkAuthoredController(this);
            if (evp == UsingEvp || !HasWheels) return;
            if (evp)
            {
                evpBackend = EvpCarBackend.Install(this);
            }
            else
            {
                evpBackend.Uninstall();
                evpBackend = null;
                ApplyConfig(); // hand the chassis and wheel substeps back to the built-in sim
            }
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

            // EVP owns the chassis in EVP mode (parametric center of mass,
            // its own substep policy) — hand the re-apply over instead of
            // fighting it.
            if (evpBackend != null)
            {
                evpBackend.ApplyChassis();
                return;
            }

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
            if (evpBackend != null) return; // EVP simulates; this is a bystander.
            if (config == null || !HasWheels) return;

            float steer = input?.Steer ?? 0f;
            float throttle = input != null ? Mathf.Clamp(input.Throttle, -1f, 1f) : 0f;
            bool handbrake = input?.Handbrake ?? false;

            float speed01 = config.topSpeedKmh > 1f ? Mathf.Clamp01(SpeedKmh / config.topSpeedKmh) : 1f;

            bool burnout = WantsBurnout(input);
            BurnoutActive = burnout;

            ApplyWheelSetup(handbrake, burnout);
            ApplySteering(steer, speed01);
            ApplyDrive(throttle, handbrake, speed01, burnout);
            ApplyAntiRoll(frontLeft, frontRight);
            ApplyAntiRoll(rearLeft, rearRight);

            // Downforce grows with speed so the car never floats off crests at pace.
            body.AddForce(-transform.up * (config.downforce * Velocity.magnitude));
        }

        void LateUpdate()
        {
            if (evpBackend != null) return; // EVP places the wheel pivots itself.
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
        /// lateral grip here for drift turns; a burnout loosens rear
        /// longitudinal grip the same way so the rears spin in place.
        /// </summary>
        void ApplyWheelSetup(bool handbrake, bool burnout)
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
                forward.stiffness = config.forwardStiffness * (rear && burnout ? config.burnoutRearGrip : 1f);
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

        /// <summary>Rollback speed under which a gravity-driven drift counts as a stalled climb rather than a brake request.</summary>
        const float RollbackDriveSpeed = 3f;

        void ApplyDrive(float throttle, bool handbrake, float speed01, bool burnout)
        {
            // Burnout: fronts clamp with the full brake, rears get boosted
            // torque with no brake at all — with their grip dropped in
            // ApplyWheelSetup they spin in place (and the visuals spin with
            // them, since SyncVisual reads the WheelCollider's own rpm).
            if (burnout)
            {
                RearLightsOn = true; // the fronts ARE braking
                frontLeft.motorTorque = frontRight.motorTorque = 0f;
                rearLeft.motorTorque = rearRight.motorTorque =
                    config.maxMotorTorque * config.burnoutTorqueFactor * 0.5f;
                frontLeft.brakeTorque = frontRight.brakeTorque = config.brakeTorque;
                rearLeft.brakeTorque = rearRight.brakeTorque = 0f;
                return;
            }

            float forwardSpeed = ForwardSpeed;

            // A gradient drags the car along its own forward axis. Drifting
            // slowly in THAT direction while the driver asks for the other way
            // is a stalled climb, not a brake request — braking it locks the
            // wheels, gravity keeps winning, the car dips back under the
            // threshold, drive returns, and it stutters in place instead of
            // ever setting off. Only real gradients qualify, so the two-pedal
            // brake is untouched on level road, and only crawling speeds, so a
            // deliberate brake stays authoritative downhill at pace.
            float noseGrade = -transform.forward.y; // > 0 nose-down, < 0 nose-up
            bool rollingWithGravity =
                config.hillRollbackSlope > 0f
                && Mathf.Abs(noseGrade) > Mathf.Sin(config.hillRollbackSlope * Mathf.Deg2Rad)
                && Mathf.Abs(forwardSpeed) < RollbackDriveSpeed
                && Mathf.Sign(noseGrade) == Mathf.Sign(forwardSpeed);

            // Two-pedal arcade rule: throttle against the travel direction is a
            // brake until (nearly) stopped, then becomes drive the other way.
            bool opposing = Mathf.Abs(forwardSpeed) > 0.5f
                && throttle != 0f
                && Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed)
                && !rollingWithGravity;

            // Rear lights: brake, handbrake, or reverse — reverse throttle that
            // isn't a brake request is reverse gear, and rolling backwards
            // counts even with the pedal released.
            RearLightsOn = opposing || handbrake || throttle < 0f || forwardSpeed < -ReverseLightSpeed;

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
