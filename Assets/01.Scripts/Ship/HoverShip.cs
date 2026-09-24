using System;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Simulation;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The standalone ship: drop the prefab on any level and it flies — no
    /// track, no manager, no scene wiring. It is the ship's IDENTITY (what the
    /// camera, HUD and game rules read, through <see cref="IShip"/>) over a
    /// <see cref="HoverBody"/>, which does the moving: this component reads
    /// the input, refills the body's feel from its <see cref="ShipDefinition"/>
    /// every tick, ticks it in <c>FixedUpdate</c> and poses the transform
    /// along the tick's substep path in <c>Update</c> — the track-space ship's
    /// render rule, so nothing that follows the transform ever sees the 36 m
    /// stride of a physics step. The rigidbody is kinematic: it is there so
    /// the hull collider is a body the world can sense, never to integrate
    /// the motion.
    ///
    /// What only the ship has lives here, ported one-to-one from the runner's
    /// motor so both ships play the same: the <b>dash</b> (a meter that
    /// recharges at the definition's rate; a request spends
    /// <see cref="ShipSettings.dashCost"/> and shoves the body sideways by
    /// exactly the definition's dash distance), its airborne form the
    /// <b>barrel roll</b> (the same shove at air authority under a full 360°
    /// of the model, on its own clock so a wall or a landing never leaves the
    /// ship on its side) and the <b>stall</b> (a standstill with the throttle
    /// released for the grace — braking to a stop alone is fine). Unlike the
    /// runner's motor, a stall never freezes this ship: <see cref="HasStopped"/>
    /// is a REPORT a game may end its run on (and pause the ship itself); in a
    /// level with no such game the ship simply flies on, and the report clears
    /// the moment it moves again.
    ///
    /// Both assets run as runtime clones taken in <c>Awake</c> — a game may
    /// push its rules into them, and the debug menu edits them, without ever
    /// touching the asset. Everything cosmetic (bank, roll, hover bob and
    /// pitch wobble) is on the <see cref="visual"/> child; the root is the
    /// physical pose.
    /// </summary>
    [DefaultExecutionOrder(-10)] // ticks before whatever reads it the same step (the runner's motor mirrors it, recovery and pickups follow it)
    [RequireComponent(typeof(Rigidbody))]
    public sealed class HoverShip : MonoBehaviour, IShip, ICameraTarget
    {
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        ShipDefinition definition;

        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        ShipSettings settings;

        [Tooltip("The model child that banks, rolls, bobs and pitches. The root stays the physical pose.")]
        [SerializeField, Required] Transform visual;

        [Tooltip("Launch with the definition's initial impulse on Start. Off = a game launches it.")]
        [SerializeField] bool launchOnStart = true;

        const float AutopilotReach = 10f; // metres off the lane's middle at which the autopilot steers at full lock

        ISteeringInput steering;
        IThrottleInput throttleInput;
        IDashInput dashInput;
        BoxCollider hull;
        readonly HoverBody body = new();
        float lastTickTime;
        float bankAngle;

        float dashMeter = 1f;
        float dashTimeLeft;
        float dashBurstDuration;
        bool meterWasFull = true;
        float rollTimeLeft, rollDuration, rollAngle;
        int rollDirection;
        float stallTimer;
        float guideSearchTimer;

        public ShipDefinition Definition => definition;
        public ShipSettings Settings => settings;
        public HoverBody Body => body;
        public Transform Visual => visual;
        public float CurrentSpeed => body.ForwardSpeed;
        public ShipState State => body.State;
        public float AirTime => body.AirTime;
        public bool IsSliding => body.IsSliding;
        public bool HasStopped { get; private set; }
        public bool Paused { get; set; }
        public float DashMeter => dashMeter;
        public bool IsDashing => dashTimeLeft > 0f;
        public float DashBurstDuration => dashBurstDuration;
        public bool IsBarrelRolling => rollTimeLeft > 0f;
        public int BarrelRollDirection => IsBarrelRolling ? rollDirection : 0;
        /// <summary>The guide spline the ship is currently helped by, or null in free flight.</summary>
        public IShipGuide Guide => body.HasGuideSample ? body.Guide : null;
        /// <summary>Where the ship sits on its guide — only meaningful while <see cref="Guide"/> is not null.</summary>
        public GuideSample GuideSample => body.Guided;
        /// <summary>A game's say over the camera's view cycle (a menu is open).</summary>
        public bool ViewCycleLocked { get; set; }
        /// <summary>
        /// The hands-off lockdown a game switches on when the run is decided
        /// (the runner's post-win fly-on): throttle held, the stick replaced
        /// by a pull to the middle of the guide's lane, dash requests
        /// swallowed, and the road always holds — a win never ends in a slide
        /// or a fall. With no guide in reach it just holds the throttle and
        /// flies straight. Cleared by <see cref="Launch()"/>.
        /// </summary>
        public bool Autopilot { get; set; }

        /// <summary>Launch by itself on Start. A game that launches the ship from its own start line switches this off in Awake.</summary>
        public bool LaunchOnStart { get => launchOnStart; set => launchOnStart = value; }

        /// <summary>When set, the stick alone is replaced (throttle and brake stay the player's) and dash requests are swallowed — a level taking the steering for a stretch, like the end of the runner's tubes.</summary>
        public float? SteerOverride { get; set; }

        /// <summary>When set, these controls drive the ship instead of the player's input (an autopilot, a scripted test). Dash requests from the input are swallowed while it is.</summary>
        public BodyControls? ControlOverride { get; set; }

        /// <summary>
        /// A steering contribution ADDED to the player's own, -1..1, set by
        /// whoever is helping (the patrol duel's soft assist). Unlike the three
        /// flags above it leaves the dash alone, so a helped ship can still
        /// dash — which is the whole reason it exists. 0 = no help, and the
        /// owner is responsible for clearing it.
        /// </summary>
        public float SteerAssist { get; set; }
        /// <summary>Cost of the last simulation tick, milliseconds — the number the substep budget is judged by.</summary>
        public double LastTickMilliseconds { get; private set; }

        public event Action<float> PadImpulse;
        public event Action<int> DashPerformed;
        public event Action<int> BarrelRollStarted;
        public event Action Launched;
        public event Action MeterFilled;
        public event Action<float> WallHit;
        public event Action<float> Sliding;
        public event Action<ShipState> StateChanged;
        public event Action TookOff;
        public event Action Landed;
        public event Action FellOff;
        public event Action<Vector3> RespawnStarted;
        public event Action Respawned;

        // The fall and the respawn are flown by ShipRecovery; the ship stays the one voice the game listens to.
        public void RaiseFellOff() => FellOff?.Invoke();
        public void RaiseRespawnStarted(Vector3 teleport) => RespawnStarted?.Invoke(teleport);
        public void RaiseRespawned() => Respawned?.Invoke();

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
            dashInput = GetComponent<IDashInput>();
            hull = GetComponent<BoxCollider>();

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None; // the ship interpolates itself, along the substep path

            // Play never touches the assets.
            if (definition != null)
            {
                string source = definition.name;
                definition = Instantiate(definition);
                definition.name = source + " (run)";
            }
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
            ShipRegistry.Register(this);
            body.StateChanged += OnStateChanged;
            body.TookOff += OnTookOff;
            body.Landed += OnLanded;
            body.WallHit += OnWallHit;
            body.Sliding += OnSliding;
        }

        void OnDisable()
        {
            ShipRegistry.Unregister(this);
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
        public void SetDefinition(ShipDefinition runDefinition)
        {
            if (runDefinition != null) definition = runDefinition;
        }

        /// <summary>Starts a run from where the ship stands, facing where it faces, at the definition's initial impulse.</summary>
        public void Launch() => Launch(transform.position, transform.rotation);

        public void Launch(Vector3 position, Quaternion rotation)
        {
            if (definition == null || settings == null) return;
            dashMeter = 1f;
            meterWasFull = true;
            dashTimeLeft = 0f;
            rollTimeLeft = 0f;
            rollAngle = 0f;
            stallTimer = 0f;
            HasStopped = false;
            Autopilot = false;
            bankAngle = 0f;
            dashInput?.ConsumeDashRequest(); // a press from before the launch is not a dash
            guideSearchTimer = 0f;           // a teleport: look for the level's guide at once
            body.Guide = null;

            FillParams();
            body.Reset(position, rotation, definition.initialImpulse);
            lastTickTime = Time.fixedTime;
            ApplyPose(1f);
            Launched?.Invoke();
        }

        /// <summary>A pad, an orb, a boost: a speed change scaled by the ship's weight, blended in by the body. Ignored while the ship is out of play.</summary>
        public void AddSpeedImpulse(float rawMagnitude)
        {
            if (State == ShipState.OffTrack || State == ShipState.Respawning) return;
            body.AddSpeedChange(definition.ScalePadEffect(rawMagnitude));
            PadImpulse?.Invoke(rawMagnitude);
        }

        // --------------------------------------------------------------- tick
        void FixedUpdate()
        {
            if (Paused || definition == null || settings == null) return;
            float dt = Time.fixedDeltaTime;

            // Rules read live off the clones, like every tunable.
            if (throttleInput != null) throttleInput.DigitalRampSeconds = definition.digitalThrottleRampSeconds;
            if (dashInput != null)
            {
                dashInput.SinglePress = settings.dashSinglePress;
                dashInput.DoubleTapSeconds = settings.dashDoubleTapSeconds;
            }
            FillParams();
            UpdateGuide(dt);
            body.MagneticOverride = FindMagnetVolume();

            UpdateDash(dt); // before the body's tick, so a request fires on the tick it was consumed

            BodyControls controls = ControlOverride ?? new BodyControls
            {
                steer = steering != null ? steering.SteerAxis : 0f,
                throttle = throttleInput != null ? throttleInput.Throttle : 1f, // no throttle input = held
                brake = throttleInput != null ? throttleInput.Brake : 0f,
            };
            if (SteerOverride.HasValue && !ControlOverride.HasValue) controls.steer = Mathf.Clamp(SteerOverride.Value, -1f, 1f);
            // Guidance that ADDS to the player's steering rather than
            // replacing it. Deliberately not an override: the three override
            // flags above all swallow the dash (see UpdateDash), and the
            // patrol duel's finisher IS a dash. Hands never leave the ship.
            if (SteerAssist != 0f && !ControlOverride.HasValue)
                controls.steer = Mathf.Clamp(controls.steer + SteerAssist, -1f, 1f);
            body.HoldOnRoad = Autopilot;
            if (Autopilot)
                controls = new BodyControls
                {
                    // Guided, the stick strafes: a pull to the middle of the lane. (The body's guide sample is last tick's — a tick stale is nothing here.)
                    steer = body.HasGuideSample ? Mathf.Clamp(-body.Guided.lateral / AutopilotReach, -1f, 1f) : 0f,
                    throttle = 1f,
                };
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            body.Tick(dt, controls);
            LastTickMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lastTickTime = Time.fixedTime;

            dashTimeLeft = body.LateralBlocked ? 0f : dashTimeLeft - dt; // a wall ends the dash
            UpdateStall(dt, controls.throttle);
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

        static readonly Collider[] Volumes = new Collider[4];

        // A MagnetVolume the ship is inside of has the say over the hover rule; none = the settings'.
        bool? FindMagnetVolume()
        {
            int count = Physics.OverlapSphereNonAlloc(body.Position, 0.5f, Volumes, ShipLayers.VolumeMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (Volumes[i].TryGetComponent(out MagnetVolume volume)) return volume.Magnetic;
            return null;
        }

        // Detecting the level's guide: nothing is wired — a guide in reach is
        // found through the registry, on a timer (the search walks the whole
        // line), and only while the ship has none. No guide = free flight.
        void UpdateGuide(float dt)
        {
            body.GuideAssist = settings.useGuide ? settings.guideAssist : 0f;
            if (!settings.useGuide) { body.Guide = null; return; }
            if (body.Guide as UnityEngine.Object != null && body.HasGuideSample) return;

            guideSearchTimer -= dt;
            if (guideSearchTimer > 0f) return;
            guideSearchTimer = settings.guideSearchSeconds;
            body.Guide = ShipGuideRegistry.FindInReach(gameObject.scene, body.Position, out _);
        }

        // Meter recharge and dash triggering.
        void UpdateDash(float dt)
        {
            if (!settings.dashEnabled) return;

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

            int request = dashInput?.ConsumeDashRequest() ?? 0;
            if (ControlOverride.HasValue || Autopilot || SteerOverride.HasValue) request = 0; // an autopilot is hands-off
            if (request != 0) TryDash(request);
        }

        /// <summary>
        /// A dash to the left (−1) or right (+1), if the meter affords it and
        /// no dash window is open. On the ground it is a sideways shove the
        /// lateral drag stops after exactly the definition's dash distance; in
        /// the air the same shove (at air authority) rides under a barrel
        /// roll and the window lasts the roll.
        /// </summary>
        public bool TryDash(int direction)
        {
            if (direction == 0 || !settings.dashEnabled || IsDashing || dashMeter < settings.dashCost) return false;
            if (State == ShipState.OffTrack || State == ShipState.Respawning) return false;
            direction = direction > 0 ? 1 : -1;
            dashMeter -= settings.dashCost;

            bool airborne = State == ShipState.Airborne;
            dashBurstDuration = Mathf.Max(airborne ? definition.barrelRollSeconds : definition.dashDuration, 0.01f);
            dashTimeLeft = dashBurstDuration;
            body.AddLateralImpulse(direction * definition.DashImpulse);
            DashPerformed?.Invoke(direction);

            if (airborne && !IsBarrelRolling)
            {
                rollDuration = dashBurstDuration;
                rollTimeLeft = rollDuration;
                rollDirection = direction;
                BarrelRollStarted?.Invoke(direction);
            }
            return true;
        }

        // A standstill is not the end by itself — the brake can stop the ship
        // and the throttle pulls it away again. Stalling out is sitting at 0
        // with the throttle released for the whole grace. It is only reported:
        // a ship that froze on it could never leave a level with no game to
        // restart it (it sat dead in the city the first time it stopped).
        void UpdateStall(float dt, float throttle)
        {
            bool still = body.ForwardSpeed <= 0.01f && body.ReverseSpeed <= 0.01f;
            // Only a ship in play can stall: one waiting out its respawn stands still by rule, and a player who let go of
            // the throttle for those seconds lost the run to it.
            bool stalled = still && throttle <= 0.01f && State == ShipState.Grounded;
            stallTimer = stalled ? stallTimer + dt : 0f;
            if (stallTimer >= settings.stallGraceSeconds) HasStopped = true;
            else if (!still) HasStopped = false;
        }

        // ------------------------------------------------------------- render
        void Update()
        {
            if (definition == null || settings == null) return;
            float dt = Time.deltaTime;
            bool running = !Paused;

            if (running) UpdateRoll(dt); // visual only, so it turns at the frame rate, not the tick's

            // Between two ticks the pose runs along the last tick's substep
            // path; with no fresh tick (paused, stopped) it rests at the end of it.
            float alpha = Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f));
            ApplyPose(alpha);
            if (running) UpdateBank(dt);
            ApplyHover(); // runs even after the run ends, so the ship keeps floating
        }

        void ApplyPose(float alpha)
        {
            Pose pose = body.PoseAt(alpha);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        // The barrel roll's own clock: 0 → 360° in the dash direction with a
        // smooth ease at both ends, so it blends out of and back into the bank
        // underneath it.
        void UpdateRoll(float dt)
        {
            if (rollTimeLeft <= 0f) { rollAngle = 0f; return; }
            rollTimeLeft -= dt;
            float progress = 1f - Mathf.Clamp01(rollTimeLeft / Mathf.Max(rollDuration, 0.01f));
            rollAngle = -rollDirection * 360f * Mathf.SmoothStep(0f, 1f, progress);
            if (rollTimeLeft <= 0f) rollAngle = 0f; // a full turn is where it started
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
        // adds the settings' cosmetic lift. The barrel roll rides on top of
        // the bank: one full turn, back to exactly the bank it would have had.
        void ApplyHover()
        {
            if (visual == null) return;
            float t = Time.time * definition.bobFrequency;
            float bob = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * definition.bobAmplitude;
            float pitch = (Mathf.PerlinNoise(0.71f, t * 0.8f) - 0.5f) * 2f * definition.hoverPitchDegrees;
            visual.localPosition = new Vector3(0f, settings.visualLift + bob, 0f);
            visual.localRotation = Quaternion.Euler(pitch, 0f, bankAngle + rollAngle);
        }

        void OnStateChanged(ShipState next) => StateChanged?.Invoke(next);
        void OnTookOff() => TookOff?.Invoke();
        void OnLanded() => Landed?.Invoke();
        void OnWallHit(float speed) => WallHit?.Invoke(speed);
        void OnSliding(float excess) => Sliding?.Invoke(excess);
    }
}
