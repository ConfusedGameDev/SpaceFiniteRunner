using EVP;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The Edy's Vehicle Physics half of the backend toggle
    /// (<see cref="VehiclePhysicsSettings"/>): installed by CarController.Start
    /// on the finished rig, it adds an EVP5 VehicleController over the SAME
    /// rigidbody, WheelColliders and wheel pivots the built-in sim uses, so
    /// the two backends drive identical cars and differ only in tire model.
    /// CarController stays on the object as the identity every other system
    /// keys on (FindPlayerCar, camera, HUD, AI perception, health) and keeps
    /// serving its read-only surface — it just stops simulating. This bridge
    /// then does the two jobs the built-in sim did itself: it re-applies the
    /// CarConfig mapping every physics step (so the debug sliders stay live),
    /// and it translates whatever ICarInput sits on the car — player or AI,
    /// with the -1..+1 two-pedal throttle contract — into EVP's separate
    /// throttle/brake inputs, using the same speed-aware forward/reverse rule
    /// as EVP's own VehicleStandardInput.
    /// </summary>
    [DefaultExecutionOrder(-10)] // write the frame's inputs before VehicleController's FixedUpdate consumes them
    public class EvpCarBackend : MonoBehaviour
    {
        CarController car;
        VehicleController vehicle;
        ICarInput input;

        // EVP zeroes the WheelColliders' friction curves wholesale to run its
        // own tire model; these snapshots are what hands the wheels back
        // intact if the toggle flips to built-in mid-run (the built-in sim
        // only re-writes stiffness, never the slip curve underneath it).
        WheelCollider[] wheels;
        WheelFrictionCurve[] savedForward;
        WheelFrictionCurve[] savedSideways;

        public VehicleController Vehicle => vehicle;

        /// <summary>
        /// Add and wire the EVP controller on a rig whose CarController already
        /// owns four WheelColliders and visual pivots. The object is briefly
        /// deactivated so VehicleController's OnEnable runs only once the wheel
        /// list is filled (on an empty list it logs and disables itself);
        /// deactivating a rigidbody drops its motion, so the rolling-start
        /// velocity is carried across the cycle by hand.
        /// </summary>
        public static EvpCarBackend Install(CarController car)
        {
            GameObject go = car.gameObject;
            var body = go.GetComponent<Rigidbody>();
            Vector3 velocity = body.linearVelocity;
            Vector3 angular = body.angularVelocity;

            bool wasActive = go.activeSelf;
            go.SetActive(false);

            var vehicle = go.AddComponent<VehicleController>();
            vehicle.wheels = BuildWheels(car);
            ApplySuspension(car);

            var backend = go.AddComponent<EvpCarBackend>();
            backend.car = car;
            backend.vehicle = vehicle;
            backend.SnapshotFriction();
            backend.ApplyChassis();
            backend.ApplyLiveConfig();

            go.SetActive(wasActive);
            body.linearVelocity = velocity;
            body.angularVelocity = angular;
            return backend;
        }

        /// <summary>
        /// Tear EVP down and hand the wheels back to the built-in sim: restore
        /// the friction curves EVP zeroed, then drop both EVP components. The
        /// caller re-runs ApplyConfig so chassis and substeps follow.
        /// </summary>
        public void Uninstall()
        {
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i] == null) continue;
                    wheels[i].forwardFriction = savedForward[i];
                    wheels[i].sidewaysFriction = savedSideways[i];
                    wheels[i].motorTorque = 0f;
                    wheels[i].steerAngle = 0f;
                }

            if (vehicle != null) Destroy(vehicle);
            Destroy(this);
        }

        void SnapshotFriction()
        {
            wheels = new[] { car.frontLeft, car.frontRight, car.rearLeft, car.rearRight };
            savedForward = new WheelFrictionCurve[wheels.Length];
            savedSideways = new WheelFrictionCurve[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                savedForward[i] = wheels[i].forwardFriction;
                savedSideways[i] = wheels[i].sidewaysFriction;
            }
        }

        void Start()
        {
            // Same binding rule as CarController: the driver component lands on
            // the rig before the first FixedUpdate, whoever built it.
            input = GetComponent<ICarInput>();
        }

        void FixedUpdate()
        {
            if (vehicle == null || car == null || car.config == null) return;

            ApplyLiveConfig();

            float steer = input != null ? Mathf.Clamp(input.Steer, -1f, 1f) : 0f;
            float throttle = input != null ? Mathf.Clamp(input.Throttle, -1f, 1f) : 0f;
            bool handbrake = input != null && input.Handbrake;

            // The project's ICarInput is a single two-pedal axis; EVP wants
            // separate throttle and brake. This is the "continuous forward and
            // reverse" translation from EVP's VehicleStandardInput: opposing
            // the travel direction brakes first, then reverses near standstill.
            float forwardInput = Mathf.Clamp01(throttle);
            float reverseInput = Mathf.Clamp01(-throttle);
            const float minSpeed = 0.1f;
            const float minInput = 0.1f;

            float throttleInput = 0f;
            float brakeInput = 0f;
            if (vehicle.speed > minSpeed)
            {
                throttleInput = forwardInput;
                brakeInput = reverseInput;
            }
            else if (reverseInput > minInput)
            {
                throttleInput = -reverseInput;
            }
            else if (forwardInput > minInput)
            {
                if (vehicle.speed < -minSpeed) brakeInput = forwardInput;
                else throttleInput = forwardInput;
            }

            vehicle.steerInput = steer;
            vehicle.throttleInput = throttleInput;
            vehicle.brakeInput = brakeInput;
            vehicle.handbrakeInput = handbrake ? 1f : 0f;
        }

        /// <summary>
        /// The chassis half of the CarConfig mapping — mass and center of mass.
        /// EVP reads its parametric center of mass in OnEnable only, so a
        /// mid-run chassis change (the debug menu's Apply Config path) cycles
        /// the component to make it re-run that setup.
        /// </summary>
        public void ApplyChassis()
        {
            CarConfig config = car != null ? car.config : null;
            if (config == null || vehicle == null) return;

            var body = GetComponent<Rigidbody>();
            body.mass = config.mass;

            vehicle.centerOfMassMode = VehicleController.CenterOfMassMode.Parametric;
            vehicle.centerOfMassPosition = 0.5f;
            vehicle.centerOfMassHeightOffset = -Mathf.Clamp(config.centerOfMassDrop, 0f, 1f);

            if (vehicle.enabled && gameObject.activeInHierarchy)
            {
                vehicle.enabled = false;
                vehicle.enabled = true;
            }
        }

        /// <summary>
        /// The per-step half of the mapping: shared knobs (steering, top speed)
        /// come straight off the built-in sections, grip and forces off the
        /// config's EVP section. Re-applied every FixedUpdate for the same
        /// reason CarController re-applies wheel setup — live debug sliders.
        /// </summary>
        void ApplyLiveConfig()
        {
            CarConfig config = car != null ? car.config : null;
            if (config == null || vehicle == null) return;
            vehicle.maxSteerAngle = config.maxSteerAngle;
            vehicle.maxSpeedForward = config.topSpeedKmh / 3.6f;
            vehicle.maxSpeedReverse = config.topSpeedKmh / 3.6f * config.reverseTorqueFactor;
            vehicle.maxDriveForce = config.evpDriveForce;
            vehicle.maxBrakeForce = config.evpBrakeForce;
            vehicle.tireFriction = config.evpTireFriction;
            vehicle.antiRoll = config.evpAntiRoll;
            vehicle.aeroDownforce = config.evpAeroDownforce;
            vehicle.rollingResistance = config.evpRollingResistance;
        }

        /// <summary>
        /// EVP drives the same four WheelColliders and steers the same visual
        /// pivots the built-in sim used. Steer on the front axle, handbrake on
        /// the rear, drive flags from the config's drivetrain.
        /// </summary>
        static Wheel[] BuildWheels(CarController car)
        {
            CarConfig config = car.config;
            bool frontDriven = config == null || config.drivetrain != CarConfig.Drivetrain.RearWheelDrive;
            bool rearDriven = config == null || config.drivetrain != CarConfig.Drivetrain.FrontWheelDrive;

            Wheel Make(WheelCollider collider, Transform visual, bool front) => new Wheel
            {
                wheelCollider = collider,
                wheelTransform = visual,
                steer = front,
                drive = front ? frontDriven : rearDriven,
                brake = true,
                handbrake = !front,
            };

            return new[]
            {
                Make(car.frontLeft, car.frontLeftVisual, true),
                Make(car.frontRight, car.frontRightVisual, true),
                Make(car.rearLeft, car.rearLeftVisual, false),
                Make(car.rearRight, car.rearRightVisual, false),
            };
        }

        /// <summary>
        /// With the built-in sim's per-step wheel setup gone, the config's
        /// suspension lands on the WheelColliders once here — EVP keeps
        /// whatever suspension the colliders carry (only raising the spring to
        /// its computed minimum) and zeroes their friction to run its own.
        /// </summary>
        static void ApplySuspension(CarController car)
        {
            CarConfig config = car.config;
            if (config == null) return;
            foreach (var wheel in new[] { car.frontLeft, car.frontRight, car.rearLeft, car.rearRight })
            {
                if (wheel == null) continue;
                wheel.suspensionDistance = config.suspensionDistance;
                JointSpring spring = wheel.suspensionSpring;
                spring.spring = config.springForce;
                spring.damper = config.damperForce;
                wheel.suspensionSpring = spring;
            }
        }
    }
}
