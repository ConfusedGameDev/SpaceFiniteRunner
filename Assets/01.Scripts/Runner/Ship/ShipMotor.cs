using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Drives the ship along the track spline. Core speed rule: an initial
    /// impulse at launch, then the player's throttle holds the ship up to
    /// its cruise speed and the brake slows it — boost orbs are the only way
    /// past cruise (a bleed pulls the speed back down to it), and there is no
    /// upper cap (the win condition is reaching Light Speed). The track
    /// streams endlessly ahead (see TrackGenerator); the run ends when the
    /// ship sits at a standstill for <see cref="GameSettings.stallGraceSeconds"/>
    /// (<see cref="HasStopped"/>) or the GameManager's timer expires. All
    /// tunables come from the assigned <see cref="ShipDefinition"/>.
    ///
    /// <b>The motor is a driver around a <see cref="TrackBody"/></b>: the
    /// body holds the track-space state (distance, lateral, height and their
    /// velocities) and the rules any body shares — speed, the lane, ramps and
    /// the jump arc — while the motor feeds it the player's controls and
    /// keeps what only the ship has: the dash meter, the barrel roll, the
    /// loop verdict and its fall, the visual. <b>The simulation ticks in
    /// FixedUpdate</b> (cut into <see cref="GameSettings.simSubsteps"/>
    /// substeps) and the pose is rendered in Update, interpolated in track
    /// space between the last two ticks — so <see cref="DistanceTravelled"/>,
    /// <see cref="LateralOffset"/> and <see cref="AirHeight"/> are the
    /// RENDERED values, which is what every Update-time reader (the patrol's
    /// gap, the ghost trail, the streamer) wants; <see cref="Body"/> has the
    /// tick's own.
    ///
    /// <b>Jumps</b> are resolved analytically, in the body: every step it
    /// scans the live <see cref="JumpRamp"/>s. Inside a ramp's run-up with
    /// the centre far enough inside its edge, the ship is committed — lateral
    /// pinned to the ramp (side rails), root riding up the slope — and
    /// launches at the lip into a parabola authored in track distance (see
    /// <see cref="JumpDefinition"/>); beside a ramp, its edge is a wall that
    /// costs speed and fires <see cref="WallHit"/> when hit. <see cref="State"/>
    /// is the one place anything reads "is the ship flying". The root itself
    /// rises above the flight line while on a ramp or airborne, so ground
    /// orbs and brake pads are physically missed rather than ignored.
    /// <b>The airborne dash is a barrel roll</b>: the sideways burst is
    /// spread over <see cref="ShipDefinition.barrelRollSeconds"/> while the
    /// visual turns a full 360° in the dash direction on top of its bank
    /// (<see cref="IsBarrelRolling"/>, <see cref="BarrelRollStarted"/>);
    /// <see cref="BarrelRollTrail"/> draws the wingtip ribbons.
    /// <b>Loops</b> are track sections (the pose function goes round by
    /// itself — see <see cref="LoopSection"/>); the motor only decides the
    /// gate: entering a <see cref="LoopFeature"/> at or above its required
    /// speed is a pass, below it the ship rides up to the top, drops off it
    /// (<see cref="ShipState.Falling"/>: off the track, straight down under
    /// the loop's fake gravity onto the exit, distance frozen at the exit so
    /// the patrol keeps gaining) and lands with the loop's speed penalty.
    /// <b>Falling off</b>: when the body leaves the track over an open edge
    /// (<see cref="ShipState.OffTrack"/>) the motor takes its velocity into
    /// world space and flies a plain ballistic fall — no spline — for
    /// <see cref="GameSettings.fallDurationSeconds"/> (<see cref="FellOff"/>),
    /// then seats the ship at the first safe stretch PAST the fall point
    /// (<see cref="FindRespawnDistance"/>: you lose time, never distance, and
    /// never respawn into the sweep that threw you), at a standstill and
    /// untouchable (<see cref="ShipState.Respawning"/>,
    /// <see cref="RespawnStarted"/>, <see cref="RespawnBlink"/>), and after
    /// <see cref="GameSettings.respawnWaitSeconds"/> relaunches it at the
    /// speed it fell with minus the penalty (<see cref="Respawned"/>). The
    /// run's clock never stops for any of it. <see cref="Autopilot"/> is the
    /// post-win lockdown: edges closed, grip untested, the ship steered home.
    /// It is also the chase camera's <see cref="ICameraTarget"/>: the rig
    /// follows this root (the pose above the flight line, never the bobbing
    /// visual) and the view cycle is locked while airborne.
    /// </summary>
    public class ShipMotor : MonoBehaviour, ICameraTarget, ICollector
    {
        // Inline so the ship's sliders are reachable without leaving the scene —
        // in play mode this field holds the tuning screen's runtime clone, so
        // editing here tweaks the live run and never the asset on disk.
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)] ShipDefinition definition;
        [SerializeField, Required] TrackManager track;

        [Tooltip("Visual child that banks when steering (the ship model). Falls back to this transform.")]
        [SerializeField] Transform visual;

        public ShipDefinition Definition => definition;
        public float CurrentSpeed => body != null ? body.ForwardSpeed : 0f;

        /// <summary>Metres from the track start, as RENDERED this frame (interpolated between simulation ticks). <see cref="Body"/> holds the tick's own.</summary>
        public float DistanceTravelled { get; private set; }

        /// <summary>Stalled out: the ship sat at speed 0 with the throttle released for the whole stall grace. Latched until the next <see cref="Launch"/>; braking to a stop alone never sets it.</summary>
        public bool HasStopped { get; private set; }

        /// <summary>The track-space body the motor drives: the simulation's own state, at most one tick ahead of the rendered pose.</summary>
        public TrackBody Body => body;

        /// <summary>Raised when a pad impulse is applied. Argument is the raw magnitude (positive = boost).</summary>
        public event System.Action<float> PadImpulse;

        /// <summary>Raised when a lateral dash fires. Argument: -1 left, +1 right.</summary>
        public event System.Action<int> DashPerformed;

        /// <summary>Raised when an airborne dash starts its barrel roll. Argument: -1 rolls left, +1 right.</summary>
        public event System.Action<int> BarrelRollStarted;

        /// <summary>Raised at the end of <see cref="Launch"/>: the ship has been teleported to the start line.</summary>
        public event System.Action Launched;

        /// <summary>Raised each time the dash meter reaches full (edge, not per frame).</summary>
        public event System.Action MeterFilled;

        /// <summary>Raised when a dash slams the track edge, or the ship hits a ramp from the side. Argument: lateral impact speed in m/s.</summary>
        public event System.Action<float> WallHit;

        /// <summary>Raised when the ship starts sliding outward on a flat sweep taken too fast. Argument: lateral acceleration beyond its grip, m/s².</summary>
        public event System.Action<float> Sliding;

        /// <summary>True while the ship is sliding on a flat sweep (and scrubbing speed).</summary>
        public bool IsSliding => body != null && body.IsSliding;

        /// <summary>Raised the step the ship leaves the track over an open edge (it is <see cref="ShipState.OffTrack"/> and falling).</summary>
        public event System.Action FellOff;

        /// <summary>Raised when the fall ends and the ship is seated back on the track to wait (<see cref="ShipState.Respawning"/>), its root already moved. Argument: the world-space teleport, for the camera's warp.</summary>
        public event System.Action<Vector3> RespawnStarted;

        /// <summary>Raised when the respawn wait ends and the ship relaunches.</summary>
        public event System.Action Respawned;

        /// <summary>
        /// Raised the step the ship runs out of road at the end of a finite
        /// track, already <see cref="ShipState.OffTrack"/> and dropping — a
        /// fall that never respawns, unless the listener turns it into the
        /// winning flight with <see cref="BeginEscape"/> right here. Argument:
        /// true when it left by the lip of an end ramp, false when it missed
        /// them. <see cref="FellOff"/> is NOT raised: nothing is put on hold
        /// for a respawn that will not come.
        /// </summary>
        public event System.Action<bool> ReachedTrackEnd;

        /// <summary>True from the step the ship left the END of the track (won or lost) until the next <see cref="Launch"/>. The patrol stops judging a ship that is no longer on the road.</summary>
        public bool HasLeftTrackEnd => offMode != OffTrackMode.Fall;

        /// <summary>True while the ship is flying on off an end ramp with the run won.</summary>
        public bool IsEscaping => offMode == OffTrackMode.Escape;

        /// <summary>
        /// A lockdown for a ship that must not be lost: the player's steering
        /// is replaced by a gentle pull to the centre line, the throttle is
        /// held, dash input is swallowed, every edge is a wall and grip is
        /// never tested. The win no longer uses it — the run is won by LEAVING
        /// the track (<see cref="BeginEscape"/>) — but the capability stays
        /// for set pieces. It never stops the end of the track. Cleared by
        /// <see cref="Launch"/>.
        /// </summary>
        public bool Autopilot { get; set; }

        /// <summary>Raised on every <see cref="State"/> change, after the new state is set.</summary>
        public event System.Action<ShipState> StateChanged;

        /// <summary>Raised the frame the ship leaves a ramp's lip.</summary>
        public event System.Action TookOff;

        /// <summary>Raised the frame the ship's arc returns to the flight line — or a loop fall lands on the exit.</summary>
        public event System.Action Landed;

        /// <summary>Raised on entering a loop: true = fast enough, the loop is a pass; false = it will drop the ship at the top.</summary>
        public event System.Action<bool> LoopEntered;

        /// <summary>Raised the frame the ship drops off the top of a loop it was too slow for.</summary>
        public event System.Action LoopFailed;

        /// <summary>While true the simulation is frozen (setup screen); the hover keeps running.</summary>
        public bool Paused { get; set; }

        /// <summary>Dash power meter, 0..1. Starts each run empty.</summary>
        public float DashMeter => dashMeter;

        /// <summary>True for the dash's window after the shove (the ghost trail's span; no new dash inside it). A wall ends it early.</summary>
        public bool IsDashing => dashTimeLeft > 0f;

        /// <summary>Seconds the current (or last) dash burst was spread over: the dash duration on the ground, the barrel roll's length in the air.</summary>
        public float DashBurstDuration => dashBurstDuration;

        /// <summary>True while the visual is mid barrel roll (an airborne dash). Outlives the burst if a wall cut the dash short — the roll always completes.</summary>
        public bool IsBarrelRolling => rollTimeLeft > 0f;

        /// <summary>Barrel-roll direction of the current roll: -1 left, +1 right, 0 when not rolling.</summary>
        public int BarrelRollDirection => IsBarrelRolling ? rollDirection : 0;

        /// <summary>The banking/hovering model child — for visual-only consumers (ghost trail).</summary>
        public Transform Visual => visual;

        /// <summary>Metres across the track from the centre line, right positive — the steering coordinate, as rendered this frame.</summary>
        public float LateralOffset { get; private set; }

        /// <summary>The track the ship rides — for consumers that need the flight-line pose (ghost trail).</summary>
        public TrackManager Track => track;

        /// <summary>The run-level dash rules pushed in by the GameManager; null while unconfigured.</summary>
        public GameSettings DashSettings => dashSettings;

        /// <summary>Grounded or Airborne — see <see cref="ShipState"/>.</summary>
        public ShipState State => body != null ? body.State : ShipState.Grounded;

        /// <summary>Seconds since takeoff while airborne; the last flight's length otherwise.</summary>
        public float AirTime => body != null ? body.AirTime : 0f;

        /// <summary>Height of the root above the flight line (ramp slope or arc), metres, as rendered this frame.</summary>
        public float AirHeight { get; private set; }

        /// <summary>The ramp the ship is committed to (riding its run-up), or null.</summary>
        public JumpRamp CurrentRamp => body?.Ramp;

        /// <summary>The loop the ship is inside (or falling out of), or null.</summary>
        public LoopFeature CurrentLoop => loop;

        // ------------------------------------------------------ ICameraTarget
        Transform ICameraTarget.Transform => transform;
        float ICameraTarget.SpeedKmh => CurrentSpeed * 3.6f;
        // The ship's box is a trigger volume, not a hull — the eye is authored on the camera settings instead.
        bool ICameraTarget.TryGetChassisBox(out Vector3 localCentre, out float localTop)
        {
            localCentre = Vector3.zero;
            localTop = 0f;
            return false;
        }
        bool ICameraTarget.BlockPanInput => false; // the ship steers with the left stick and the arrows; the right stick is the camera's
        // A jump forces the Far framing; the cycle is locked so it cannot be undone mid-arc.
        bool ICameraTarget.BlockModeCycle => MainMenuController.IsOpen || State == ShipState.Airborne || State == ShipState.Falling
                                             || State == ShipState.OffTrack;

        ISteeringInput steering;
        IDashInput dashInput;
        IThrottleInput throttleInput; // null = no throttle device: the ship holds full throttle
        float stallTimer;             // seconds at a standstill with the throttle released
        GameSettings dashSettings; // null = dash feature off; also carries the simulation's tick rules
        TrackBody body;
        float lastTickTime = float.NegativeInfinity; // Time.fixedTime of the last simulation tick, for the render's blend
        float bankAngle;
        float dashMeter;
        float dashTimeLeft;
        int dashDirection;
        float dashBurstDuration;     // the profile's span: dashDuration grounded, barrelRollSeconds airborne

        // Barrel roll (airborne dash). Visual-only, on the model child like the
        // bank: a full 360° in the dash direction eased over its own timer, so
        // a wall or a landing that ends the burst never leaves the ship on its
        // side. Adds to the bank, and adds exactly one turn, so it lands seamlessly.
        float rollTimeLeft;
        float rollDuration;
        int rollDirection;
        float rollAngle;
        bool meterWasFull;
        float shownPitch;       // the body's pitch (nose up on the slope and the climb, down on the descent), eased

        // Loop state. Inside a loop the track pose does the work; the motor
        // only remembers the verdict taken at the gate and, on a fail, runs
        // the drop: a straight fall from the top onto the exit. The section
        // and definition are held here, not read off the feature: the feature
        // is a spawned scene object the generator may cull while the ship is
        // still inside the loop, and the state must not end with it.
        LoopFeature loop;
        LoopSection loopSection;
        LoopDefinition loopDefinition;
        bool loopPassed;
        float fallDistance;     // metres fallen so far
        float prevFallDistance; // ...at the start of the tick, for the render's blend
        float fallVelocity;
        float fallHeight;       // the drop: twice the radius
        Vector3 fallTopPosition;
        Quaternion fallTopRotation, fallExitRotation;
        Vector3 fallExitPosition;

        // Off-track fall and respawn. The fall is the one stretch the ship is
        // NOT in track space: a world position and velocity under gravity.
        // Both it and the wait run on the simulation's clock (timers in the
        // tick, not a coroutine), so a pause freezes them with everything else.
        Vector3 offPosition, prevOffPosition, offVelocity;
        Quaternion offRotation;

        /// <summary>What an <see cref="ShipState.OffTrack"/> ship is doing: they are all the same world-space flight, flown by the motor.</summary>
        enum OffTrackMode
        {
            /// <summary>Over an open edge: falls, then respawns further down the track.</summary>
            Fall,
            /// <summary>Off the end of the track without the win: falls and tumbles for good.</summary>
            TerminalFall,
            /// <summary>Off an end ramp with the win: flies on along the ramp's line, upright, for good.</summary>
            Escape
        }
        OffTrackMode offMode;
        int offSide;            // the side it left over: the tumble rolls that way
        float offTimer;         // seconds fallen, then seconds waited
        float speedAtFall;      // what the relaunch is measured against

        Vector2 pickupReach = new(2.5f, 2.3f); // half width / half height of the ship's pickup volume

        // Metres of lateral offset the autopilot answers with full steer.
        const float AutopilotReach = 6f;

        void Awake()
        {
            steering = GetComponent<ISteeringInput>();
            dashInput = GetComponent<IDashInput>();
            throttleInput = GetComponent<IThrottleInput>();
            if (visual == null) visual = transform;

            if (track == null)
            {
                Debug.LogError("ShipMotor needs a TrackManager reference.", this);
                enabled = false;
                return;
            }

            // The body is this component's own object, so its events need no
            // unsubscribe: they die together (nothing static is involved).
            body = new TrackBody(track);
            body.StateChanged += next => StateChanged?.Invoke(next);
            body.Landed += () => Landed?.Invoke();
            body.WallHit += impactSpeed => WallHit?.Invoke(impactSpeed);
            body.Sliding += excess => Sliding?.Invoke(excess);
            body.LeftTrack += OnLeftTrack;
            body.ReachedEnd += OnReachedEnd;
            body.PickedUp += OnPickedUp;

            // The pickup volume is the one authored on the ship: its box
            // (once a physics trigger, now only a measure).
            var box = GetComponent<BoxCollider>();
            if (box != null) pickupReach = new Vector2(box.size.x * 0.5f, box.size.y * 0.5f);
            body.TookOff += boost =>
            {
                // The takeoff boost rides the pad path: "+N" text, shake and rumble come free.
                if (boost != 0f) AddSpeedImpulse(boost);
                TookOff?.Invoke();
            };
        }

        // Launch in Start so a TrackGenerator's Awake can rebuild the spline first.
        void Start() => Launch();

        /// <summary>Swap the active definition (used by the tuning screen with a runtime clone).</summary>
        public void SetDefinition(ShipDefinition newDefinition) => definition = newDefinition;

        /// <summary>
        /// Hands the motor the dash tunables (GameManager pushes the shared
        /// GameSettings asset here — the motor holds no settings reference of
        /// its own). Null or dashEnabled off leaves the feature inert. The
        /// same asset carries the simulation's run rules (substeps, the
        /// stall grace).
        /// </summary>
        public void ConfigureDash(GameSettings settings)
        {
            dashSettings = settings;
            if (dashInput != null && settings != null)
                dashInput.DoubleTapSeconds = settings.dashDoubleTapSeconds;
        }

        /// <summary>Resets the run to the track start and applies the initial impulse.</summary>
        public void Launch()
        {
            if (body == null) return;

            dashMeter = 0f; // the meter charges from empty every run
            dashTimeLeft = 0f;
            rollTimeLeft = 0f;
            rollAngle = 0f;
            meterWasFull = false;
            HasStopped = false;
            stallTimer = 0f;
            Autopilot = false;
            offMode = OffTrackMode.Fall;
            ClearLoop();
            fallDistance = prevFallDistance = 0f;

            // Back on the line, silently: a restart mid-arc is not a landing.
            body.Reset(0f, definition.initialImpulse);

            bankAngle = 0f;
            RenderPose(1f);
            Launched?.Invoke();
        }

        /// <summary>
        /// Called by pads and card effects. Positive boosts, negative brakes.
        /// The change is scaled by weight and blended in over time by acceleration.
        /// </summary>
        public void AddSpeedImpulse(float rawMagnitude)
        {
            if (HasStopped || body == null) return;
            if (State == ShipState.OffTrack || State == ShipState.Respawning) return; // nothing touches a ship that is not on the track
            body.AddSpeedChange(definition.ScalePadEffect(rawMagnitude));
            PadImpulse?.Invoke(rawMagnitude);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool running = !Paused && !HasStopped;

            if (running) UpdateRoll(dt); // visual only, so it turns at the frame rate, not the tick's

            // Between two ticks the pose is the track-space blend of them; with
            // no fresh tick (paused, stopped) the blend runs out at the current
            // state and rests there.
            float alpha = Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f));
            RenderPose(alpha);
            if (running) UpdateBank(dt);

            // Hover runs even after the run ends, so the ship keeps floating.
            ApplyHover(dt);
        }

        void FixedUpdate()
        {
            if (Paused || HasStopped) return;
            Simulate(Time.fixedDeltaTime, dashSettings != null ? dashSettings.simSubsteps : 1);
            lastTickTime = Time.fixedTime;
        }

        /// <summary>One simulation tick of <paramref name="dt"/> seconds, cut into equal substeps.</summary>
        void Simulate(float dt, int substeps)
        {
            body.BeginTick();
            prevFallDistance = fallDistance;
            prevOffPosition = offPosition;
            if (throttleInput != null) throttleInput.DigitalRampSeconds = definition.digitalThrottleRampSeconds;
            body.Params = new BodyParams
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
                wallHitCooldownSeconds = dashSettings != null ? dashSettings.dashWallHitCooldownSeconds : 0.5f,
                pickupReach = pickupReach,
                edgeOverhang = dashSettings != null ? dashSettings.edgeOverhang : 1f,
                edgeGraceSeconds = dashSettings != null ? dashSettings.edgeGraceSeconds : 0.25f,
            };
            body.HoldOnTrack = Autopilot;

            substeps = Mathf.Max(1, substeps);
            float h = dt / substeps;
            for (int i = 0; i < substeps && !HasStopped; i++) Step(h);
        }

        void Step(float dt)
        {
            if (State == ShipState.OffTrack) { StepFall(dt); return; }
            if (State == ShipState.Respawning) { StepRespawnWait(dt); return; }

            UpdateDash(dt);

            var controls = new BodyControls
            {
                steer = steering?.SteerAxis ?? 0f,
                throttle = throttleInput != null ? throttleInput.Throttle : 1f,
                brake = throttleInput != null ? throttleInput.Brake : 0f,
            };
            if (Autopilot)
            {
                controls.steer = Mathf.Clamp(-body.Lateral / AutopilotReach, -1f, 1f);
                controls.throttle = 1f;
                controls.brake = 0f;
            }
            body.Step(dt, controls);
            dashTimeLeft = body.LateralBlocked ? 0f : Mathf.Max(0f, dashTimeLeft - dt); // a wall (or a tube's return) ends the dash

            UpdateLoop(dt);
            UpdateStall(dt, controls.throttle);
        }

        // A standstill is no longer the end by itself — the brake can stop the
        // ship and the throttle pulls it away again. Stalling out is sitting
        // at 0 with the throttle released for the whole grace.
        void UpdateStall(float dt, float throttle)
        {
            bool stalled = body.ForwardSpeed <= 0.01f && throttle <= 0.01f;
            stallTimer = stalled ? stallTimer + dt : 0f;
            float grace = dashSettings != null ? dashSettings.stallGraceSeconds : 2f;
            if (stallTimer >= grace) HasStopped = true;
        }

        // Meter recharge and dash triggering. Runs before the body's step so a
        // request fires on the step it was consumed.
        void UpdateDash(float dt)
        {
            if (dashSettings == null || !dashSettings.dashEnabled) return;

            // Recharge rate is a ship stat — the tuning screen's clone carries it.
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
            if (Autopilot) request = 0; // swallowed: the win's fly-on is hands-off
            if (request != 0 && !IsDashing && dashMeter >= dashSettings.dashCost)
            {
                dashMeter -= dashSettings.dashCost;
                dashDirection = request;

                // In the air the dash is a barrel roll: the same sideways
                // shove (at the jump's reduced authority) under a full spin
                // of the model, and the dash window lasts the roll's length.
                bool airborne = State == ShipState.Airborne;
                dashBurstDuration = Mathf.Max(airborne ? definition.barrelRollSeconds : definition.dashDuration, 0.01f);
                dashTimeLeft = dashBurstDuration;
                // The dash is a shove: a burst of lateral velocity the body's
                // drag eats over dashDistance (at the air's reduced authority
                // off a jump) — not a scripted slide.
                body.AddLateralImpulse(request * definition.DashImpulse);
                DashPerformed?.Invoke(request);

                if (airborne && !IsBarrelRolling)
                {
                    rollDuration = dashBurstDuration;
                    rollTimeLeft = rollDuration;
                    rollDirection = request;
                    BarrelRollStarted?.Invoke(request);
                }
            }
        }

        // The barrel roll's own clock: 0 → 360° in the dash direction with a
        // smooth ease at both ends, so it blends out of and back into the
        // bank underneath it. Roll right (+1) is the bank's sign for right.
        void UpdateRoll(float dt)
        {
            if (rollTimeLeft <= 0f) { rollAngle = 0f; return; }
            rollTimeLeft -= dt;
            float progress = 1f - Mathf.Clamp01(rollTimeLeft / Mathf.Max(rollDuration, 0.01f));
            rollAngle = -rollDirection * 360f * Mathf.SmoothStep(0f, 1f, progress);
            if (rollTimeLeft <= 0f) rollAngle = 0f; // a full turn is where it started
        }

        /// <summary>
        /// The loop gate and the fall. Entering a loop section takes the
        /// verdict once, against the speed the loop was built to demand; a
        /// fail still rides the circle up to the top, so the drop is seen
        /// coming, then falls straight down onto the exit.
        /// </summary>
        void UpdateLoop(float dt)
        {
            float d = body.Distance;

            if (State == ShipState.Falling)
            {
                fallVelocity += loopDefinition.fallGravity * dt;
                fallDistance += fallVelocity * dt;
                if (fallDistance >= fallHeight) LandFromLoop();
                return;
            }

            if (State == ShipState.Looping)
            {
                // The section is ours from the gate to the exit whatever
                // happens to the feature object behind us.
                if (loopSection == null) { ClearLoop(); body.SetState(ShipState.Grounded); return; }
                if (!loopPassed && d - loopSection.StartDistance >= loopSection.FirstTopLocal) { DropFromLoop(); return; }
                if (d >= loopSection.EndDistance)
                {
                    ClearLoop();
                    body.SetState(ShipState.Grounded);
                }
                return;
            }

            // (Grounded / OnTube is the body's own call, already settled for
            // this step. Ramps and loops never sit in a tube.)
            if (State != ShipState.Grounded) return;
            foreach (var candidate in LoopFeature.Active)
            {
                if (candidate == null || candidate.Section == null || !candidate.Section.Contains(d)) continue;
                loop = candidate;
                loopSection = candidate.Section;
                loopDefinition = candidate.Definition;
                loopPassed = CurrentSpeed >= candidate.RequiredSpeed;
                body.SetState(ShipState.Looping);
                LoopEntered?.Invoke(loopPassed);
                break;
            }
        }

        void DropFromLoop()
        {
            LoopSection section = loopSection;
            // From where the ship is SEEN (the rendered pose), so the drop
            // starts without a pop.
            fallTopPosition = transform.position;
            fallTopRotation = transform.rotation;
            section.GetExitPose(body.Lateral, out fallExitPosition, out fallExitRotation);
            fallHeight = Mathf.Max(0.01f, Vector3.Distance(fallTopPosition, fallExitPosition));
            fallDistance = prevFallDistance = 0f;
            fallVelocity = 0f;
            // The track waits for the ship at the exit: the patrol, which never
            // slows, gains the whole fall.
            body.Distance = section.EndDistance;
            body.ForwardSpeed *= 1f - Mathf.Clamp01(loopDefinition.fallSpeedLoss);
            body.StopLateralMotion();
            body.SnapInterpolation();
            dashTimeLeft = 0f;
            body.SetState(ShipState.Falling);
            LoopFailed?.Invoke();
        }

        void LandFromLoop()
        {
            ClearLoop();
            fallDistance = prevFallDistance = 0f;
            body.SnapInterpolation();
            body.SetState(ShipState.Grounded);
            Landed?.Invoke();
        }

        // The body swept over something lying on the track. What taking it
        // means is the pickup's own business — the motor only says who came.
        void OnPickedUp(ITrackPickup pickup)
        {
            if (pickup is SpeedPad pad) pad.Collect(this);
            else if (pickup is Collectible collectible) collectible.Collect();
        }

        // ------------------------------------------------ off-track + respawn

        // The body just went over an open edge. Its track-space velocity
        // becomes a world velocity at the pose it left from, and from here
        // the fall is plain ballistics.
        void OnLeftTrack(int side)
        {
            offMode = OffTrackMode.Fall;
            BeginWorldFlight(side);
            FellOff?.Invoke();
        }

        // The body just ran out of road at the end of the track. It drops for
        // good unless the listener (the GameManager, with the objectives met
        // and a ramp taken) turns this very flight into the escape.
        void OnReachedEnd(bool tookRamp)
        {
            offMode = OffTrackMode.TerminalFall;
            BeginWorldFlight(body.Lateral >= 0f ? 1 : -1);
            ReachedTrackEnd?.Invoke(tookRamp);
        }

        /// <summary>
        /// The win: the flight off the end ramp stops being a fall. The ship
        /// flies on along the line it left the lip with — upright, no tumble,
        /// under <see cref="GameSettings.winEscapeGravity"/> (0 = dead
        /// straight) — until the run is restarted. Only meaningful from a
        /// <see cref="ReachedTrackEnd"/> handler.
        /// </summary>
        public void BeginEscape()
        {
            if (State != ShipState.OffTrack || offMode == OffTrackMode.Fall) return;
            offMode = OffTrackMode.Escape;
        }

        // Track space to world space, at the pose the body left from.
        void BeginWorldFlight(int side)
        {
            track.GetPoseAtDistance(body.Distance, body.Lateral, out Vector3 position, out Quaternion rotation);
            position += rotation * (Vector3.up * body.Height);

            offSide = side;
            offTimer = 0f;
            speedAtFall = body.ForwardSpeed;
            offVelocity = rotation * new Vector3(body.TotalLateralVelocity, body.VerticalVelocity, body.ForwardSpeed);
            offRotation = rotation;
            offPosition = position;
            prevOffPosition = transform.position; // from where the ship is SEEN, so the fall starts without a pop

            dashTimeLeft = 0f;
        }

        void StepFall(float dt)
        {
            if (offMode == OffTrackMode.Escape)
            {
                // The winning flight: on along the ramp's line, nose on the
                // velocity, never coming back.
                float escapeGravity = dashSettings != null ? dashSettings.winEscapeGravity : 0f;
                offVelocity += Vector3.down * (escapeGravity * dt);
                offPosition += offVelocity * dt;
                if (offVelocity.sqrMagnitude > 0.01f)
                    offRotation = Quaternion.LookRotation(offVelocity.normalized, offRotation * Vector3.up);
                offTimer += dt;
                return;
            }

            float gravity = dashSettings != null ? dashSettings.fallGravity : 30f;
            float tumble = dashSettings != null ? dashSettings.fallTumbleDegreesPerSecond : 120f;
            offVelocity += Vector3.down * (gravity * dt);
            offPosition += offVelocity * dt;
            // Rolls over the edge it left by, nose dropping as it goes.
            offRotation *= Quaternion.Euler(tumble * 0.35f * dt, 0f, -offSide * tumble * dt);

            offTimer += dt;
            // Off the end of the track there is nothing to come back to.
            if (offMode == OffTrackMode.TerminalFall) return;
            if (offTimer >= (dashSettings != null ? dashSettings.fallDurationSeconds : 1.5f)) BeginRespawnWait();
        }

        void BeginRespawnWait()
        {
            Vector3 from = transform.position;
            rollTimeLeft = 0f;
            rollAngle = 0f;
            bankAngle = 0f;
            offTimer = 0f;
            body.Reset(FindRespawnDistance(body.Distance), 0f, ShipState.Respawning);
            RenderPose(1f);
            RespawnStarted?.Invoke(transform.position - from);
        }

        void StepRespawnWait(float dt)
        {
            offTimer += dt;
            if (offTimer < (dashSettings != null ? dashSettings.respawnWaitSeconds : 3f)) return;

            float penalty = dashSettings != null ? dashSettings.respawnSpeedPenalty : 0.15f;
            body.ForwardSpeed = speedAtFall * (1f - Mathf.Clamp01(penalty));
            body.SetState(ShipState.Grounded);
            Respawned?.Invoke();
        }

        /// <summary>
        /// The first safe stretch at or past <paramref name="from"/>: plain
        /// road — no section (loop, tube), no flat sweep, no ramp — with none
        /// of them starting within <see cref="GameSettings.respawnClearance"/>
        /// ahead. Anything in the way pushes the spot past its end, so a
        /// stretch crowded with features is skipped as a whole and the ship
        /// never relaunches into the sweep that threw it.
        /// </summary>
        float FindRespawnDistance(float from)
        {
            float clearance = dashSettings != null ? dashSettings.respawnClearance : 150f;
            float d = from;
            for (int guard = 0; guard < 64; guard++)
            {
                float before = d;

                foreach (var section in track.Sections)
                    if (section.EndDistance > d && section.StartDistance - d < clearance) d = section.EndDistance;

                var sweep = track.FlatSweepWithin(d, clearance);
                if (sweep != null) d = sweep.End;

                // The end ramps are not ground to clear: past them is the void.
                foreach (var candidate in JumpRamp.Active)
                    if (candidate != null && !candidate.IsEndRamp && candidate.EndDistance > d && candidate.StartDistance - d < clearance)
                        d = candidate.EndDistance;

                if (Mathf.Approximately(d, before)) break;
            }
            d = Mathf.Min(d, Mathf.Max(from, track.Length - 1f));

            // A finite track's final run-up (straight, walled, nothing on it)
            // is the last place to come back on: never further down than its
            // start — unless the ship fell from inside it, which only a wall
            // could allow and none does.
            if (track.EndZoneStart >= 0f) d = Mathf.Min(d, Mathf.Max(from, track.EndZoneStart));
            return d;
        }

        void ClearLoop()
        {
            loop = null;
            loopSection = null;
            loopDefinition = null;
        }

        /// <summary>
        /// Seats the root on the simulation's state blended
        /// <paramref name="alpha"/> of the way from the previous tick to the
        /// current one. The blend is in track space (distance, lateral,
        /// height), so the pose stays exactly on the track whatever its shape.
        /// </summary>
        void RenderPose(float alpha)
        {
            DistanceTravelled = body.DistanceAt(alpha);
            LateralOffset = body.LateralAt(alpha);
            AirHeight = body.HeightAt(alpha);

            Vector3 position;
            Quaternion rotation;
            if (State == ShipState.OffTrack)
            {
                // Off the track altogether: the world-space fall.
                position = Vector3.Lerp(prevOffPosition, offPosition, alpha);
                rotation = offRotation;
            }
            else if (State == ShipState.Falling)
            {
                // Off the top of a loop: a straight drop onto its exit,
                // rolling upright on the way down.
                float f = Mathf.Clamp01(Mathf.Lerp(prevFallDistance, fallDistance, alpha) / fallHeight);
                position = Vector3.Lerp(fallTopPosition, fallExitPosition, f);
                rotation = Quaternion.Slerp(fallTopRotation, fallExitRotation, f);
            }
            else
            {
                track.GetPoseAtDistance(DistanceTravelled, LateralOffset, out position, out rotation);
                // The lift is along the track's up, so it survives roll (loops, tubes).
                if (AirHeight > 0f) position += rotation * (Vector3.up * AirHeight);
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        // Bank into the push: roll with the lateral acceleration the body
        // actually applied (steering force and any slip; full steer = full
        // bank). A dash or a slide pushes a little past full bank (clamped so
        // it reads as a hard lean, not a barrel roll).
        void UpdateBank(float dt)
        {
            float demand = Mathf.Clamp(body.BankDemand, -1.25f, 1.25f);
            float targetBank = -demand * definition.maxBankAngle;
            bankAngle = Mathf.Lerp(bankAngle, targetBank, 1f - Mathf.Exp(-definition.bankResponse * dt));
        }

        // Visual-only float: offset + organic bob and pitch wobble on the model.
        // The root (and its trigger collider) stays on the flight line — or on
        // the jump arc above it — so pads still work; the model only adds
        // hover and the nose-up/down of a slope or a flight.
        void ApplyHover(float dt)
        {
            float t = Time.time * definition.bobFrequency;
            float bob = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * definition.bobAmplitude;
            float pitch = (Mathf.PerlinNoise(0.71f, t * 0.8f) - 0.5f) * 2f * definition.hoverPitchDegrees;
            shownPitch = dt > 0f ? Mathf.Lerp(shownPitch, body.PitchDegrees, 1f - Mathf.Exp(-8f * dt)) : body.PitchDegrees;

            visual.localPosition = new Vector3(0f, definition.hoverHeight + bob, 0f);
            // The barrel roll rides on top of the bank: one full turn, so the
            // model comes back to exactly the bank it would have had anyway.
            visual.localRotation = Quaternion.Euler(pitch + shownPitch, 0f, bankAngle + rollAngle);
        }
    }
}
