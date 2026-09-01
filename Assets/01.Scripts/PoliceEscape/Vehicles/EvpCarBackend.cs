using ConfusedGameDev.FiniteRunner.UI;
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
    ///
    /// <b>Two install modes.</b> A car built by the game gets a VehicleController
    /// ADDED here and tuned from the L200 baseline plus the CarConfig mapping.
    /// A prefab that already ships its own VehicleController — the EVP demo
    /// cars converted into traffic NPCs — is <i>authored</i>: the existing
    /// controller is reused as-is (its wheel list, mass, forces, curves are
    /// the whole point of putting that car on the road) and the CarConfig
    /// mapping is reduced to the one number the AI drivers depend on, the
    /// steer angle they normalise against. Toggling to built-in parks an
    /// authored controller instead of destroying it, so a toggle back finds
    /// its tuning intact.
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

        // Player-car effects (EVP demo audio + skid marks), torn down with the backend.
        VehicleAudio audioModule;
        VehicleTireEffects tireEffects;
        GameObject audioRoot;

        // Cosmetic body damage (every car, both install modes), torn down with the backend.
        CarDeformation deformation;

        // True when the VehicleController came with the prefab (see the
        // class summary): its tuning is kept, not mapped from the CarConfig.
        bool authored;

        public VehicleController Vehicle => vehicle;

        /// <summary>The body-damage owner while the config's evpDamage is on; null otherwise (and always in built-in mode).</summary>
        public CarDeformation Deformation => deformation;

        /// <summary>True when this car drives on the VehicleController its prefab authored, not one mapped from the CarConfig.</summary>
        public bool Authored => authored;

        /// <summary>
        /// Switch off a prefab-authored VehicleController on <paramref name="car"/>
        /// (nothing happens on cars without one). CarController calls this on
        /// the built-in side of the backend toggle so an EVP demo prefab in
        /// built-in mode does not run two sims over one rigidbody.
        /// </summary>
        public static void ParkAuthoredController(CarController car)
        {
            if (car == null || car.GetComponent<EvpCarBackend>() != null) return; // the backend owns it while installed
            var vehicle = car.GetComponent<VehicleController>();
            if (vehicle != null && vehicle.enabled) vehicle.enabled = false;
        }

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
            // The ground-material catch-all must exist before any
            // VehicleController's OnEnable — that is where EVP caches its
            // GroundMaterialManager lookup.
            EnsureGroundEffects();

            GameObject go = car.gameObject;
            var body = go.GetComponent<Rigidbody>();
            Vector3 velocity = body.linearVelocity;
            Vector3 angular = body.angularVelocity;

            bool wasActive = go.activeSelf;
            go.SetActive(false);

            // Authored mode: the prefab brought its own controller, wheel
            // list and tuning — wake it and leave every number alone.
            var vehicle = go.GetComponent<VehicleController>();
            bool authored = vehicle != null;
            if (authored)
            {
                vehicle.enabled = true;
            }
            else
            {
                vehicle = go.AddComponent<VehicleController>();
                vehicle.wheels = BuildWheels(car);
                ApplyL200Baseline(vehicle);
                ApplySuspension(car);
            }

            var backend = go.AddComponent<EvpCarBackend>();
            backend.car = car;
            backend.vehicle = vehicle;
            backend.authored = authored;
            backend.SnapshotFriction();
            backend.ApplyChassis();
            backend.ApplyLiveConfig();

            // Body damage for EVERY car, authored included — a dented traffic
            // bus is the same visual language as a dented player. Installed
            // while the object is still inactive, so VehicleDamage snapshots
            // the meshes exactly once, on activation.
            if (car.config == null || car.config.evpDamage)
                backend.deformation = CarDeformation.Install(car, vehicle);

            // Demo-grade juice for the car the player is actually in: engine /
            // skid / impact audio and skid marks. AI cars stay silent — a
            // twenty-car fleet of engine loops is noise, not comparison data.
            if (car.GetComponent<CarInput>() != null)
                backend.InstallEffects();

            go.SetActive(wasActive);
            body.linearVelocity = velocity;
            body.angularVelocity = angular;
            return backend;
        }

        /// <summary>
        /// The parts of the L200 demo tuning with no CarConfig knob: curve
        /// shapes, slip limits, balance and the driving aids. Copied verbatim
        /// from Assets/00.Plugins/EVP5/Prefabs/L200-Red.prefab so EVP mode
        /// reproduces that vehicle's character; the quantities a designer
        /// actually dials live on the config's EVP section instead.
        /// </summary>
        static void ApplyL200Baseline(VehicleController vehicle)
        {
            vehicle.maxSpeedReverse = 14f;
            vehicle.aeroDrag = 2f;

            vehicle.driveBalance = 0.5f;
            vehicle.brakeBalance = 0.7f;
            vehicle.tireFrictionBalance = 0.5f;
            vehicle.aeroBalance = 0.5f;
            vehicle.handlingBias = 0.5f;

            vehicle.forceCurveShape = 0.8f;
            vehicle.maxDriveSlip = 4f;
            vehicle.driveForceToMaxSlip = 500f;

            vehicle.brakeForceToMaxSlip = 1000f;
            vehicle.brakeMode = VehicleController.BrakeMode.Ratio;
            vehicle.maxBrakeSlip = 2f;
            vehicle.maxBrakeRatio = 1f;
            vehicle.handbrakeMode = VehicleController.BrakeMode.Slip;
            vehicle.maxHandbrakeSlip = 4f;
            vehicle.maxHandbrakeRatio = 0.5f;

            vehicle.tractionControl = true;
            vehicle.tractionControlRatio = 0.8f;
            vehicle.brakeAssist = true;
            vehicle.brakeAssistRatio = 1f;
            vehicle.steeringLimit = true;
            vehicle.steeringLimitRatio = 0.8f;
            vehicle.steeringAssist = true;
            vehicle.steeringAssistRatio = 0.5f;
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

            if (audioModule != null) Destroy(audioModule);
            if (tireEffects != null) Destroy(tireEffects);
            if (audioRoot != null) Destroy(audioRoot);
            // Before the controller: VehicleDamage [RequireComponent]s it, and
            // the body goes back to its authored shape with the backend.
            if (deformation != null)
            {
                deformation.Detach(keepDents: false);
                deformation = null;
            }
            if (vehicle != null)
            {
                // An authored controller is the prefab's tuning — park it for
                // the next toggle back rather than throwing it away.
                if (authored) vehicle.enabled = false;
                else Destroy(vehicle);
            }
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

            // The rear-lights flag — CarController's drive step is idle in
            // EVP mode, so this backend keeps it honest: brake, handbrake,
            // reverse gear (negative EVP throttle) or rolling backwards.
            car.RearLightsOn = brakeInput > 0f || handbrake || throttleInput < 0f
                               || vehicle.speed < -CarController.ReverseLightSpeed;
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
            // Authored: the prefab's mass and centre of mass ARE the car.
            if (authored) return;

            var body = GetComponent<Rigidbody>();
            body.mass = config.mass;

            // Parametric center of mass: the L200's fore-aft weight
            // distribution, but the HEIGHT is the config's own EVP knob — the
            // demo truck's -0.116 flips at this game's cornering speeds, and
            // CoM height is the number-one rollover dial. (The debug CENTER OF
            // MASS slider is a different scale and only moves the built-in sim.)
            vehicle.centerOfMassMode = VehicleController.CenterOfMassMode.Parametric;
            vehicle.centerOfMassPosition = 0.569f;
            vehicle.centerOfMassHeightOffset = Mathf.Clamp(config.evpCenterOfMassHeight, -1f, 0f);

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
            // The AI drivers turn a wanted wheel angle into a -1..+1 steer by
            // dividing by config.maxSteerAngle — so this one number must
            // match on an authored car too, or its steering over/undershoots.
            vehicle.maxSteerAngle = config.maxSteerAngle;
            ApplyDamageConfig(config);
            if (authored) return;
            vehicle.maxSpeedForward = config.topSpeedKmh / 3.6f;
            // maxSpeedReverse stays at the L200 baseline's 14 m/s.
            vehicle.maxDriveForce = config.evpDriveForce;
            vehicle.maxBrakeForce = config.evpBrakeForce;
            vehicle.tireFriction = config.evpTireFriction;
            vehicle.antiRoll = config.evpAntiRoll;
            vehicle.aeroDownforce = config.evpAeroDownforce;
            vehicle.rollingResistance = config.evpRollingResistance;
        }

        /// <summary>
        /// The body-damage half of the live mapping, authored cars included
        /// (dents are not physics tuning). The master toggle installs or
        /// detaches the component; the wheel toggle re-installs it, because
        /// VehicleDamage sizes its node list when it enables — both reset the
        /// current dents, which a debug toggle may. A re-install waits for the
        /// NEXT step: Destroy is deferred, so an Install in the same step would
        /// find the dying component through GetComponent and adopt it.
        /// Everything else is pushed straight through.
        /// </summary>
        void ApplyDamageConfig(CarConfig config)
        {
            bool wanted = config.evpDamage;
            if (deformation != null && (!wanted || deformation.WheelsEnabled != config.evpDamageWheels))
            {
                deformation.Detach(keepDents: false);
                deformation = null;
                return;
            }
            if (wanted && deformation == null)
                deformation = CarDeformation.Install(car, vehicle);
            else if (deformation != null)
                deformation.Apply(config);
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
        /// With the built-in sim's per-step wheel setup gone, the L200 demo's
        /// wheel rig lands on the WheelColliders once here — EVP keeps
        /// whatever suspension the colliders carry (only raising the spring to
        /// its computed minimum) and zeroes their friction to run its own.
        /// L200 values, not the config's: its 1500 N·s/m damper against the
        /// config's 4500 is most of the difference between the demo's lively
        /// body motion and an overdamped kart.
        /// </summary>
        static void ApplySuspension(CarController car)
        {
            foreach (var wheel in new[] { car.frontLeft, car.frontRight, car.rearLeft, car.rearRight })
            {
                if (wheel == null) continue;
                wheel.suspensionDistance = 0.3f;
                wheel.mass = 20f;
                wheel.wheelDampingRate = 0.25f;
                JointSpring spring = wheel.suspensionSpring;
                spring.spring = 35000f;
                spring.damper = 1500f;
                wheel.suspensionSpring = spring;
            }
        }

        // ------------------------------------------------------------- effects

        /// <summary>
        /// The L200 demo's audio rig and tire effects, rebuilt in code on the
        /// player's car: engine loop over simulated RPM and gears, tire skid
        /// on braking and hard cornering, wind, body drags, one-shot impacts —
        /// every number copied from the L200-Red prefab (only its values that
        /// differ from the component defaults are set here). Clips come off
        /// the settings asset; anything left unassigned is silently skipped
        /// (VehicleAudio null-checks every source).
        /// </summary>
        void InstallEffects()
        {
            var settings = VehiclePhysicsSettings.Current;

            tireEffects = gameObject.AddComponent<VehicleTireEffects>();
            tireEffects.tireWidth = 0.2f;
            tireEffects.minSlip = 1.5f;
            tireEffects.maxSlip = 6f;
            tireEffects.intensity = 1f;
            tireEffects.updateInterval = 0.02f;
            tireEffects.minIntensityTime = 1f;
            tireEffects.maxIntensityTime = 7f;
            tireEffects.limitIntensityTime = 8f;

            audioRoot = new GameObject("EvpAudio");
            audioRoot.transform.SetParent(transform, false);

            audioModule = gameObject.AddComponent<VehicleAudio>();

            var template = MakeSource("OneShotTemplate", null);
            template.loop = false;
            audioModule.audioClipTemplate = template;

            audioModule.engine.audioSource = MakeSource("Engine", settings.engineClip);
            audioModule.engine.idlePitch = 0.6f;
            audioModule.engine.maxRpm = 5000f;
            audioModule.engine.maxPitch = 3.5f;
            audioModule.engine.maxVolume = 0.55f;
            audioModule.engine.wheelsToEngineRatio = 13.6f;
            audioModule.engine.gears = 4;
            audioModule.engine.gearDownRpm = 1800f;
            audioModule.engine.gearUpRpm = 4000f;
            audioModule.engine.gearDownRpmRate = 20f;

            audioModule.wheels.skidAudioSource = MakeSource("Skid", settings.skidClip);
            audioModule.wheels.skidMinSlip = 1f;
            audioModule.wheels.offroadAudioSource = MakeSource("Offroad", settings.offroadClip);
            audioModule.wheels.bumpAudioClip = settings.bumpClip;

            audioModule.impacts.hardImpactAudioClip = settings.hardImpactClip;
            audioModule.impacts.softImpactAudioClip = settings.softImpactClip;
            audioModule.impacts.minPitch = 0.4f;

            audioModule.drags.hardDragAudioSource = MakeSource("HardDrag", settings.hardDragClip);
            audioModule.drags.softDragAudioSource = MakeSource("SoftDrag", settings.softDragClip);
            audioModule.drags.scratchAudioClip = settings.scratchClip;

            audioModule.wind.windAudioSource = MakeSource("Wind", settings.windClip);
            audioModule.wind.maxVolume = 0.4f;
        }

        /// <summary>
        /// A 3D looping source under EvpAudio, spatialized like the L200's
        /// (1–110 m, half volume). Routed through the mixer's FX bus so the
        /// whole car — engine, skid, wind, drags, and the one-shots
        /// VehicleAudio clones off the template — ducks with the pause
        /// snapshot and follows the SFX volume slider.
        /// </summary>
        AudioSource MakeSource(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(audioRoot.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 110f;
            source.volume = 0.55f;
            source.outputAudioMixerGroup = GameAudio.Fx;
            return source;
        }

        /// <summary>
        /// The scene-side half of skid marks, built once: a
        /// GroundMaterialManager whose single catch-all entry (no physic
        /// material — which is exactly what every city collider reports)
        /// mirrors the demo's default ground, pointing at a TireMarksRenderer
        /// with the demo's skidmarks decal settings. It stays through backend
        /// switches; built-in cars simply never query it.
        /// </summary>
        static void EnsureGroundEffects()
        {
            if (FindAnyObjectByType<GroundMaterialManager>() != null) return;

            var root = new GameObject("EVP Ground Effects");
            SceneHierarchy.Adopt(root, SceneHierarchy.Systems(root.scene));
            var manager = root.AddComponent<GroundMaterialManager>();

            TireMarksRenderer marks = null;
            Material marksMaterial = UsableMarksMaterial(VehiclePhysicsSettings.Current.tireMarksMaterial);
            if (marksMaterial != null)
            {
                // Configured while INACTIVE: TireMarksRenderer.OnEnable is
                // where it creates its MeshRenderer (taking the material) and
                // sizes its mark arrays (taking maxMarks) — on an active
                // object AddComponent would fire OnEnable before any of these
                // fields land, leaving a materialless (pink) mesh.
                var marksGo = new GameObject("Skidmarks Renderer");
                marksGo.transform.SetParent(root.transform, false);
                marksGo.SetActive(false);
                marks = marksGo.AddComponent<TireMarksRenderer>();
                marks.mode = TireMarksRenderer.Mode.PressureAndSkid;
                marks.maxMarks = 4000;
                marks.minDistance = 0.1f;
                marks.groundOffset = 0.01f;
                marks.textureOffsetY = 0.05f;
                marks.fadeOutRange = 0.5f;
                marks.material = marksMaterial;
                marksGo.SetActive(true);
            }

            manager.groundMaterials = new[]
            {
                new GroundMaterial
                {
                    physicMaterial = null,
                    grip = 1f,
                    drag = 0.1f,
                    marksRenderer = marks,
                    particleEmitter = null,
                    surfaceType = GroundMaterial.SurfaceType.Hard,
                },
            };
        }

        /// <summary>
        /// The authored skidmarks material, unless its shader failed to
        /// compile in this pipeline (the marks render as error-pink) — then an
        /// equivalent is built on Sprites/Default, which is always available,
        /// alpha-blends, and multiplies the same vertex color the marks
        /// renderer fades segments with. Same texture, same black tint; it is
        /// only unlit, which black tread marks can't visibly tell.
        /// </summary>
        static Material UsableMarksMaterial(Material authored)
        {
            if (authored == null) return null;
            if (authored.shader != null && authored.shader.isSupported) return authored;

            Shader fallback = Shader.Find("Sprites/Default");
            if (fallback == null) return null; // nothing sane to draw with — better no marks than pink ones
            Debug.LogWarning($"EvpCarBackend: skid-mark material '{authored.name}' uses an unsupported shader " +
                             $"('{(authored.shader != null ? authored.shader.name : "none")}') — using a Sprites/Default fallback.");

            var material = new Material(fallback) { name = authored.name + " (fallback)" };
            material.mainTexture = authored.mainTexture;
            material.color = authored.HasProperty("_Color") ? authored.color : Color.black;
            return material;
        }
    }
}
