using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Every handling knob of a WheelCollider car in one designer-facing
    /// asset: chassis, drivetrain, brakes, steering, suspension, grip and
    /// recovery. Shared by the player and (later) the patrol AI so every car
    /// obeys identical physics — only the driver differs. CarController draws
    /// this inline in its inspector and re-applies the wheel values every
    /// physics step, so most sliders tune the handling live while driving
    /// (mass / center of mass / substeps need its Apply Config button).
    /// </summary>
    [CreateAssetMenu(fileName = "CarConfig", menuName = "PoliceEscape/Car Config")]
    public class CarConfig : ScriptableObject
    {
        public enum Drivetrain { RearWheelDrive, FrontWheelDrive, AllWheelDrive }

        // ------------------------------------------------------------- chassis
        [TitleGroup("Chassis")]
        [Tooltip("Rigidbody mass. Heavier = steadier and harder to shove around, but slower to accelerate and stop.")]
        [PropertyRange(400f, 3000f), SuffixLabel("kg", true)]
        public float mass = 1200f;

        [TitleGroup("Chassis")]
        [Tooltip("How far the center of mass is pushed below the auto-computed one. The number-one anti-rollover knob.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("m", true)]
        public float centerOfMassDrop = 0.6f;

        [TitleGroup("Chassis")]
        [Tooltip("Extra downward force per m/s of speed — glues the car to the road at pace without changing low-speed feel.")]
        [PropertyRange(0f, 200f), SuffixLabel("N per m/s", true)]
        public float downforce = 40f;

        [TitleGroup("Chassis")]
        [Tooltip("Anti-roll bar strength per axle: shifts load between the two wheels to keep the body flat in corners. 0 = off.")]
        [PropertyRange(0f, 20000f), SuffixLabel("N", true)]
        public float antiRollForce = 8000f;

        // ---------------------------------------------------------- drivetrain
        [TitleGroup("Drivetrain")]
        [Tooltip("Which axle receives motor torque. All-wheel drive is the most forgiving for the arcade chase feel.")]
        [EnumToggleButtons]
        public Drivetrain drivetrain = Drivetrain.AllWheelDrive;

        [TitleGroup("Drivetrain")]
        [Tooltip("Total motor torque at full throttle, split evenly among the driven wheels.")]
        [PropertyRange(200f, 8000f), SuffixLabel("N·m", true)]
        public float maxMotorTorque = 3000f;

        [TitleGroup("Drivetrain")]
        [Tooltip("Torque multiplier over speed: x = current speed ÷ top speed, y = fraction of max torque. A falling curve gives a soft top speed — strong launch, gentle taper.")]
        public AnimationCurve torqueBySpeed = AnimationCurve.Linear(0f, 1f, 1f, 0.15f);

        [TitleGroup("Drivetrain")]
        [Tooltip("Speed that maps to the right edge of the torque and steering curves. A soft cap, not a hard limit — torque just fades to the curve's end value.")]
        [PropertyRange(40f, 300f), SuffixLabel("km/h", true)]
        public float topSpeedKmh = 140f;

        [TitleGroup("Drivetrain")]
        [Tooltip("Fraction of max torque available in reverse.")]
        [PropertyRange(0.1f, 1f)]
        public float reverseTorqueFactor = 0.5f;

        // -------------------------------------------------------------- brakes
        [TitleGroup("Brakes")]
        [Tooltip("Brake torque per wheel when throttling against the current travel direction (the two-pedal arcade brake).")]
        [PropertyRange(500f, 12000f), SuffixLabel("N·m", true)]
        public float brakeTorque = 4000f;

        [TitleGroup("Brakes")]
        [Tooltip("Engine-brake torque per wheel whenever the throttle is released — bleeds speed so a driverless car (fresh spawn, player away from the keys) settles on the road instead of coasting into a block. Keep well below the brake torque.")]
        [PropertyRange(0f, 2000f), SuffixLabel("N·m", true)]
        public float coastBrakeTorque = 250f;

        [TitleGroup("Brakes")]
        [Tooltip("Brake torque on the rear wheels while the handbrake is held.")]
        [PropertyRange(500f, 12000f), SuffixLabel("N·m", true)]
        public float handbrakeTorque = 6000f;

        [TitleGroup("Brakes")]
        [Tooltip("Rear sideways grip multiplier while the handbrake is held — lower = looser tail = easier drift turns.")]
        [PropertyRange(0.1f, 1f)]
        public float handbrakeGrip = 0.5f;

        [TitleGroup("Brakes")]
        [Tooltip("Road gradient above which throttling against a slow gravity rollback drives instead of braking — the anti-stall rule for climbs. 0 turns it off, so throttle against travel always brakes.")]
        [PropertyRange(0f, 15f), SuffixLabel("°", true)]
        public float hillRollbackSlope = 3f;

        // ------------------------------------------------------------ steering
        [TitleGroup("Steering")]
        [Tooltip("Front wheel steer angle at standstill.")]
        [PropertyRange(10f, 60f), SuffixLabel("°", true)]
        public float maxSteerAngle = 35f;

        [TitleGroup("Steering")]
        [Tooltip("Steer angle multiplier over speed: x = current speed ÷ top speed, y = fraction of max angle. Falling curve = stable at speed, agile when slow.")]
        public AnimationCurve steerBySpeed = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);

        [TitleGroup("Steering")]
        [Tooltip("How fast the wheels swing toward the requested angle. Higher = snappier, lower = smoother.")]
        [PropertyRange(60f, 720f), SuffixLabel("°/s", true)]
        public float steerResponse = 360f;

        // ---------------------------------------------------------- suspension
        [TitleGroup("Suspension")]
        [Tooltip("Suspension travel below the wheel's attach point.")]
        [PropertyRange(0.05f, 0.6f), SuffixLabel("m", true)]
        public float suspensionDistance = 0.25f;

        [TitleGroup("Suspension")]
        [Tooltip("Spring force holding the car up. Too low bottoms out; too high skips over bumps.")]
        [PropertyRange(5000f, 100000f), SuffixLabel("N/m", true)]
        public float springForce = 35000f;

        [TitleGroup("Suspension")]
        [Tooltip("Damping of the spring. Low = bouncy boat, high = stiff kart.")]
        [PropertyRange(500f, 10000f), SuffixLabel("N·s/m", true)]
        public float damperForce = 4500f;

        // ---------------------------------------------------------------- grip
        [TitleGroup("Grip")]
        [Tooltip("Longitudinal tire stiffness — acceleration and braking traction. Below 1 spins the wheels, above bites hard.")]
        [PropertyRange(0.25f, 4f)]
        public float forwardStiffness = 1.5f;

        [TitleGroup("Grip")]
        [Tooltip("Lateral tire stiffness — cornering grip. Higher corners on rails; lower slides. Keep a touch above forward stiffness for a forgiving arcade feel.")]
        [PropertyRange(0.25f, 4f)]
        public float sideStiffness = 2f;

        // ------------------------------------------------------------ stability
        [TitleGroup("Stability")]
        [Tooltip("Speed under which the extra wheel substeps apply — low-speed jitter is WheelCollider's classic weakness.")]
        [PropertyRange(1f, 50f), SuffixLabel("m/s", true)]
        public float substepSpeedThreshold = 10f;

        [TitleGroup("Stability")]
        [Tooltip("Physics substeps per wheel below the threshold. More = smoother low-speed behavior, slightly more CPU.")]
        [PropertyRange(1, 20)]
        public int substepsBelowThreshold = 12;

        [TitleGroup("Stability")]
        [Tooltip("Physics substeps per wheel above the threshold.")]
        [PropertyRange(1, 20)]
        public int substepsAboveThreshold = 6;

        // --------------------------------------------------------------- spawn
        [TitleGroup("Spawn")]
        [Tooltip("Rolling-start speed: the car is already doing this when it spawns, so the player takes the wheel mid-motion. 0 = spawn parked.")]
        [PropertyRange(0f, 150f), SuffixLabel("km/h", true)]
        public float spawnSpeedKmh = 40f;

        [TitleGroup("Spawn")]
        [Tooltip("Straight road cells required ahead of the spawn cell, so the rolling start begins on a runway instead of a corner or junction.")]
        [PropertyRange(0, 10), SuffixLabel("cells", true)]
        public int spawnRunwayCells = 4;

        // ------------------------------------------------------- EVP comparison
        // The Edy's Vehicle Physics backend thinks in forces and ratios, not
        // wheel torques, so it gets its own small knob set. Shared knobs (mass,
        // max steer angle, top speed, center-of-mass drop, drivetrain,
        // suspension) are mapped across from the sections above — only the
        // quantities with no built-in equivalent live here.
        [TitleGroup("EVP (comparison backend)")]
        [Tooltip("EVP total drive force at full throttle. Roughly mass × desired acceleration in m/s².")]
        [PropertyRange(500f, 12000f), SuffixLabel("N", true)]
        public float evpDriveForce = 4000f;

        [TitleGroup("EVP (comparison backend)")]
        [Tooltip("EVP total brake force.")]
        [PropertyRange(1000f, 20000f), SuffixLabel("N", true)]
        public float evpBrakeForce = 6000f;

        [TitleGroup("EVP (comparison backend)")]
        [Tooltip("EVP tire friction coefficient — overall grip. 1 = street tires, 2+ = arcade glue.")]
        [PropertyRange(0f, 3f)]
        public float evpTireFriction = 1.6f;

        [TitleGroup("EVP (comparison backend)")]
        [Tooltip("EVP anti-roll ratio — how much the chassis resists body roll in corners.")]
        [PropertyRange(0f, 1f)]
        public float evpAntiRoll = 0.4f;

        [TitleGroup("EVP (comparison backend)")]
        [Tooltip("EVP aerodynamic downforce factor — high-speed road glue, the counterpart of the built-in downforce knob.")]
        [PropertyRange(0f, 2f)]
        public float evpAeroDownforce = 1f;

        [TitleGroup("EVP (comparison backend)")]
        [Tooltip("EVP rolling resistance — how quickly a coasting car bleeds speed (the counterpart of the coast-brake torque).")]
        [PropertyRange(0f, 1f)]
        public float evpRollingResistance = 0.1f;

        // ------------------------------------------------------------- recovery
        [TitleGroup("Recovery")]
        [Tooltip("Seconds the car may sit flipped (and nearly stopped) before it auto-respawns onto the nearest road.")]
        [PropertyRange(0.5f, 5f), SuffixLabel("s", true)]
        public float flipTimeout = 1.5f;

        [TitleGroup("Recovery")]
        [Tooltip("Drop height above the road surface when respawning, so the suspension settles instead of clipping in.")]
        [PropertyRange(0.1f, 2f), SuffixLabel("m", true)]
        public float respawnHeight = 0.6f;
    }
}
