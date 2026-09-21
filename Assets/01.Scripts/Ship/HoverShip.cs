using System;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Simulation;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The standalone ship: drop the prefab on any level whose surfaces sit on
    /// the <see cref="ShipLayers.Ground"/> layer and it flies — no track, no
    /// manager, no scene wiring. It is the ship's IDENTITY (what the camera,
    /// HUD and game rules read) over a <see cref="HoverBody"/>, which does the
    /// moving: this component reads the input, refills the body's feel from
    /// its <see cref="ShipDefinition"/> every tick, ticks it in
    /// <c>FixedUpdate</c> and poses the transform along the tick's substep
    /// path in <c>Update</c> — the track-space ship's render rule, so nothing
    /// that follows the transform ever sees the 36 m stride of a physics step.
    /// The rigidbody is kinematic: it is there so the hull collider is a body
    /// the world can sense, never to integrate the motion.
    ///
    /// Both assets run as runtime clones taken in <c>Awake</c> — a game may
    /// push its rules into them, and the debug menu edits them, without ever
    /// touching the asset. Everything cosmetic (the bank into a turn, the
    /// hover bob and pitch wobble) lives on the <see cref="visual"/> child,
    /// ported as-is from the runner's motor; the root is the physical pose.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class HoverShip : MonoBehaviour, ICameraTarget
    {
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        ShipDefinition definition;

        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        ShipSettings settings;

        [Tooltip("The model child that banks, bobs and pitches. The root stays the physical pose.")]
        [SerializeField, Required] Transform visual;

        [Tooltip("Launch with the definition's initial impulse on Start. Off = a game launches it.")]
        [SerializeField] bool launchOnStart = true;

        ISteeringInput steering;
        IThrottleInput throttleInput;
        BoxCollider hull;
        readonly HoverBody body = new();
        float lastTickTime;
        float bankAngle;

        public ShipDefinition Definition => definition;
        public ShipSettings Settings => settings;
        public HoverBody Body => body;
        public Transform Visual => visual;
        public float CurrentSpeed => body.ForwardSpeed;
        public ShipState State => body.State;
        public float AirTime => body.AirTime;
        public bool IsSliding => body.IsSliding;
        /// <summary>Freezes the simulation (menus, a game's countdown).</summary>
        public bool Paused { get; set; }
        /// <summary>A game's say over the camera's view cycle (a menu is open).</summary>
        public bool ViewCycleLocked { get; set; }
        /// <summary>When set, these controls drive the ship instead of the player's input (an autopilot, a scripted test).</summary>
        public BodyControls? ControlOverride { get; set; }
        /// <summary>Cost of the last simulation tick, milliseconds — the number the substep budget is judged by.</summary>
        public double LastTickMilliseconds { get; private set; }

        public event Action Launched;
        public event Action<ShipState> StateChanged;
        public event Action TookOff;
        public event Action Landed;
        public event Action<float> WallHit;
        public event Action<float> Sliding;

        // ------------------------------------------------------ ICameraTarget
        Transform ICameraTarget.Transform => transform;
        float ICameraTarget.SpeedKmh => body.ForwardSpeed * 3.6f;
        bool ICameraTarget.TryGetChassisBox(out Vector3 localCentre, out float localTop)
        {
            localCentre = hull != null ? hull.center : Vector3.zero;
            localTop = hull != null ? hull.center.y + hull.size.y * 0.5f : 0f;
            return hull != null;
        }
        bool ICameraTarget.BlockPanInput => false;
        bool ICameraTarget.BlockModeCycle =>
            ViewCycleLocked || State == ShipState.Airborne || State == ShipState.Falling || State == ShipState.OffTrack;

        void Awake()
        {
            steering = GetComponent<ISteeringInput>();
            throttleInput = GetComponent<IThrottleInput>();
            hull = GetComponent<BoxCollider>();

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None; // the ship interpolates itself, along the substep path

            // Play never touches the assets.
            if (definition != null) SetDefinition(Instantiate(definition), definition.name);
            if (settings != null)
            {
                string source = settings.name;
                settings = Instantiate(settings);
                settings.name = source + " (run)";
            }
            body.Settings = settings;
        }

        void OnEnable()
        {
            body.StateChanged += OnStateChanged;
            body.TookOff += OnTookOff;
            body.Landed += OnLanded;
            body.WallHit += OnWallHit;
            body.Sliding += OnSliding;
        }

        void OnDisable()
        {
            body.StateChanged -= OnStateChanged;
            body.TookOff -= OnTookOff;
            body.Landed -= OnLanded;
            body.WallHit -= OnWallHit;
            body.Sliding -= OnSliding;
        }

        void Start()
        {
            if (launchOnStart) Launch();
        }

        /// <summary>Swaps in a run's definition (a store / debug clone). The ship flies on it as given — it is the caller's clone.</summary>
        public void SetDefinition(ShipDefinition runDefinition) => SetDefinition(runDefinition, null);

        void SetDefinition(ShipDefinition runDefinition, string sourceName)
        {
            if (runDefinition == null) return;
            definition = runDefinition;
            if (sourceName != null) definition.name = sourceName + " (run)";
        }

        /// <summary>Starts a run from where the ship stands, facing where it faces, at the definition's initial impulse.</summary>
        public void Launch() => Launch(transform.position, transform.rotation);

        public void Launch(Vector3 position, Quaternion rotation)
        {
            if (definition == null || settings == null) return;
            FillParams();
            body.Reset(position, rotation, definition.initialImpulse);
            bankAngle = 0f;
            lastTickTime = Time.fixedTime;
            ApplyPose(1f);
            Launched?.Invoke();
        }

        /// <summary>A pad, an orb, a boost: a speed change scaled by the ship's weight, blended in by the body.</summary>
        public void AddSpeedImpulse(float rawMagnitude) => body.AddSpeedChange(definition.ScalePadEffect(rawMagnitude));

        void FixedUpdate()
        {
            if (Paused || definition == null || settings == null) return;
            if (throttleInput != null) throttleInput.DigitalRampSeconds = definition.digitalThrottleRampSeconds;
            FillParams();
            BodyControls controls = ControlOverride ?? new BodyControls
            {
                steer = steering != null ? steering.SteerAxis : 0f,
                throttle = throttleInput != null ? throttleInput.Throttle : 0f,
                brake = throttleInput != null ? throttleInput.Brake : 0f,
            };
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            body.Tick(Time.fixedDeltaTime, controls);
            LastTickMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lastTickTime = Time.fixedTime;
        }

        void FillParams()
        {
            body.Settings = settings;
            body.Params = new HoverParams
            {
                impulseBlendRate = definition.acceleration,
                cruiseSpeed = definition.cruiseSpeed,
                thrust = definition.thrust,
                brakeDecel = definition.brakeDecel,
                coastDrag = definition.coastDrag,
                passiveDeceleration = definition.passiveDeceleration,
                lateralSpeed = definition.lateralSpeed,
                handlingResponse = definition.handlingResponse,
                gripBase = definition.gripBase,
                gripPerSpeed = definition.gripPerSpeed,
                slideThreshold = definition.slideThreshold,
                slideSpeedLoss = definition.slideSpeedLoss,
                jumpStrength = definition.jumpStrength,
                rideHeight = definition.hoverHeight,
            };
        }

        void Update()
        {
            if (definition == null || settings == null) return;
            float dt = Time.deltaTime;

            // Between two ticks the pose runs along the last tick's substep
            // path; with no fresh tick (paused) it rests at the end of it.
            float alpha = Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f));
            ApplyPose(alpha);
            if (!Paused) UpdateBank(dt);
            ApplyHover();
        }

        void ApplyPose(float alpha)
        {
            Pose pose = body.PoseAt(alpha);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        // Bank into the push: roll with the lateral demand the body applied
        // (full steer = full bank; a dash or a slide leans a little past it).
        void UpdateBank(float dt)
        {
            float demand = Mathf.Clamp(body.BankDemand, -1.25f, 1.25f);
            float targetBank = -demand * definition.maxBankAngle;
            bankAngle = Mathf.Lerp(bankAngle, targetBank, 1f - Mathf.Exp(-definition.bankResponse * dt));
        }

        // Visual-only float: organic bob and pitch wobble on the model. The
        // hover HEIGHT is physical here (the root rides it), so the model only
        // adds the settings' cosmetic lift.
        void ApplyHover()
        {
            if (visual == null) return;
            float t = Time.time * definition.bobFrequency;
            float bob = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * definition.bobAmplitude;
            float pitch = (Mathf.PerlinNoise(0.71f, t * 0.8f) - 0.5f) * 2f * definition.hoverPitchDegrees;
            visual.localPosition = new Vector3(0f, settings.visualLift + bob, 0f);
            visual.localRotation = Quaternion.Euler(pitch, 0f, bankAngle);
        }

        void OnStateChanged(ShipState next) => StateChanged?.Invoke(next);
        void OnTookOff() => TookOff?.Invoke();
        void OnLanded() => Landed?.Invoke();
        void OnWallHit(float speed) => WallHit?.Invoke(speed);
        void OnSliding(float excess) => Sliding?.Invoke(excess);
    }
}
