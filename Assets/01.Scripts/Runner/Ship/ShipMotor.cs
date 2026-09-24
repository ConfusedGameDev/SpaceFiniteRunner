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
    /// The runner's face of the ship. The flying is the standalone
    /// <see cref="HoverShip"/> on the same object — a kinematic, cast-based
    /// surface follower riding the track's streamed colliders with the
    /// <see cref="TrackGuide"/> as its line (see <c>ship-standalone.md</c>).
    /// What this component adds is the RUNNER: every tick it reads where that
    /// ship is on the track and mirrors it into its <see cref="TrackBody"/> —
    /// distance, lateral, height, speed, state — so everything written
    /// against track coordinates (the GameManager's win and lose, the
    /// generator's streaming, the HUD, the ghost trail, the patrol that
    /// chases the mirrored body) keeps working; it forwards the ship's events
    /// and passes the run's commands down (<see cref="Launch"/> from the
    /// start line, <see cref="SetDefinition"/>, <see cref="AddSpeedImpulse"/>,
    /// <see cref="Paused"/>, <see cref="Autopilot"/>); and it enforces the
    /// rules only a track can state, as rules over the physical flight
    /// (<c>ShipMotor.Physics.cs</c>): the loop gate and its drop, the ramp
    /// boost and side hit, the tube return, the track's end, laser gates.
    /// <see cref="DistanceTravelled"/>, <see cref="LateralOffset"/> and
    /// <see cref="AirHeight"/> are the RENDERED values (blended between
    /// ticks), which is what every Update-time reader wants; <see cref="Body"/>
    /// has the tick's own.
    ///
    /// It is also the chase camera's <see cref="ICameraTarget"/> and the
    /// component the city's <c>DialogueTrigger</c> looks for to know a ship
    /// is in the scene. The track-space simulation this component used to be
    /// (a <see cref="TrackBody"/> stepped by the player's controls) lives on
    /// only in the body itself, which the patrol still drives.
    /// </summary>
    [RequireComponent(typeof(HoverShip))]
    public partial class ShipMotor : MonoBehaviour, IRunnerShip, ICameraTarget, ICollector
    {
        // Inline so the ship's sliders are reachable without leaving the scene —
        // in play mode this field holds the run's runtime clone, so editing here
        // tweaks the live run and never the asset on disk.
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)] ShipDefinition definition;
        [SerializeField, Required] TrackManager track;

        [Tooltip("The ship model — banked, bobbed and rolled by the HoverShip. Falls back to this transform.")]
        [SerializeField] Transform visual;

        public ShipDefinition Definition => definition;
        public float CurrentSpeed => body != null ? body.ForwardSpeed : 0f;

        /// <summary>Metres from the track start, as RENDERED this frame (interpolated between simulation ticks). <see cref="Body"/> holds the tick's own.</summary>
        public float DistanceTravelled { get; private set; }

        /// <summary>Stalled out: the ship sat at speed 0 with the throttle released for the whole stall grace (the ship's own rule, read off it). Cleared by <see cref="Launch"/>.</summary>
        public bool HasStopped { get; private set; }

        /// <summary>The track-space body the ship is mirrored into every tick: what the patrol chases and the generator streams for.</summary>
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

        /// <summary>Raised when a dash slams a wall, or the ship hits a ramp from the side. Argument: lateral impact speed in m/s.</summary>
        public event System.Action<float> WallHit;

        /// <summary>True while the last step pushed the ship into a wall — steering, slide or dash. Polled by <see cref="ShipHealth"/>; <see cref="WallHit"/> stays the hard slam's event.</summary>
        public bool IsTouchingWall => physicsShip != null && physicsShip.Body.LateralBlocked;

        /// <summary>Raised when the ship starts sliding outward on a flat sweep taken too fast. Argument: lateral acceleration beyond its grip, m/s².</summary>
        public event System.Action<float> Sliding;

        /// <summary>True while the ship is sliding on a flat sweep (and scrubbing speed).</summary>
        public bool IsSliding => physicsShip != null && physicsShip.IsSliding;

        /// <summary>Raised the step the ship leaves the road over an open edge (it is <see cref="ShipState.OffTrack"/> and falling).</summary>
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
        /// is replaced by a pull to the lane's middle, the throttle is held,
        /// dash input is swallowed and the road always holds (no grip test).
        /// The win no longer uses it — the run is won by LEAVING the track
        /// (<see cref="BeginEscape"/>) — but the capability stays for set
        /// pieces. It never stops the end of the track. Cleared by
        /// <see cref="Launch"/>.
        /// </summary>
        public bool Autopilot
        {
            get => autopilot;
            set { autopilot = value; if (physicsShip != null) physicsShip.Autopilot = value; }
        }

        /// <summary>Raised on every <see cref="State"/> change, after the new state is set.</summary>
        public event System.Action<ShipState> StateChanged;

        /// <summary>Raised the frame the ship leaves a ramp's lip.</summary>
        public event System.Action TookOff;

        /// <summary>Raised the frame the ship comes back to the ground — or a loop fall lands on the exit.</summary>
        public event System.Action Landed;

        /// <summary>Raised on entering a loop: true = fast enough, the loop is a pass; false = it will drop the ship at the top.</summary>
        public event System.Action<bool> LoopEntered;

        /// <summary>Raised the frame the ship drops off the top of a loop it was too slow for.</summary>
        public event System.Action LoopFailed;

        /// <summary>While true the simulation is frozen (setup screen); the hover keeps running.</summary>
        public bool Paused
        {
            get => paused;
            set { paused = value; if (physicsShip != null) physicsShip.Paused = value; }
        }

        /// <summary>
        /// Soft steering help added to the player's own input, -1..1 — the
        /// duel's assist. Never an override, so the dash survives it.
        /// </summary>
        public float SteerAssist
        {
            get => physicsShip != null ? physicsShip.SteerAssist : 0f;
            set { if (physicsShip != null) physicsShip.SteerAssist = value; }
        }

        /// <summary>Dash power meter, 0..1. Starts each run full (HoverShip.Launch).</summary>
        public float DashMeter => physicsShip != null ? physicsShip.DashMeter : 0f;

        /// <summary>True for the dash's window after the shove (the ghost trail's span; no new dash inside it). A wall ends it early.</summary>
        public bool IsDashing => physicsShip != null && physicsShip.IsDashing;

        /// <summary>Seconds the current (or last) dash burst was spread over: the dash duration on the ground, the barrel roll's length in the air.</summary>
        public float DashBurstDuration => physicsShip != null ? physicsShip.DashBurstDuration : 0f;

        /// <summary>True while the visual is mid barrel roll (an airborne dash). Outlives the burst if a wall cut the dash short — the roll always completes.</summary>
        public bool IsBarrelRolling => physicsShip != null && physicsShip.IsBarrelRolling;

        /// <summary>Barrel-roll direction of the current roll: -1 left, +1 right, 0 when not rolling.</summary>
        public int BarrelRollDirection => physicsShip != null ? physicsShip.BarrelRollDirection : 0;

        /// <summary>The banking/hovering model child — for visual-only consumers (ghost trail).</summary>
        public Transform Visual => visual;

        /// <summary>Metres across the track from the centre line, right positive — the steering coordinate, as rendered this frame.</summary>
        public float LateralOffset { get; private set; }

        /// <summary>The track the ship rides — for consumers that need the flight-line pose (ghost trail).</summary>
        public TrackManager Track => track;

        /// <summary>The run-level rules pushed in by the GameManager; null while unconfigured.</summary>
        public GameSettings DashSettings => dashSettings;

        /// <summary>Where the ship is, in the runner's terms — see <see cref="ShipState"/>. Grounded is Looping inside a loop, OnTube on a tube.</summary>
        public ShipState State => body != null ? body.State : ShipState.Grounded;

        /// <summary>Seconds since takeoff while airborne; the last flight's length otherwise.</summary>
        public float AirTime => physicsShip != null ? physicsShip.AirTime : 0f;

        /// <summary>Height of the root above the flight line (ramp slope or arc), metres, as rendered this frame.</summary>
        public float AirHeight { get; private set; }

        /// <summary>The ramp the ship is committed to (riding its run-up), or null.</summary>
        public JumpRamp CurrentRamp => physicsRamp;

        /// <summary>The loop the ship is inside (or falling out of), or null.</summary>
        public LoopFeature CurrentLoop => loop;

        // ------------------------------------------------------ ICameraTarget
        Transform ICameraTarget.Transform => transform;
        float ICameraTarget.SpeedKmh => CurrentSpeed * 3.6f;
        // The ship's box is its pickup measure, not a hull — the eye is authored on the camera settings instead.
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

        GameSettings dashSettings; // null = the run's rules not pushed yet
        TrackBody body;
        float lastTickTime = float.NegativeInfinity; // Time.fixedTime of the last tick, for the render's blend

        // Loop state. Inside a loop the ship rides the ring's colliders; the
        // motor only remembers the verdict taken at the gate and, on a fail,
        // runs the drop: a straight fall from the top onto the exit. The
        // section and definition are held here, not read off the feature: the
        // feature is a spawned scene object the generator may cull while the
        // ship is still inside the loop, and the state must not end with it.
        LoopFeature loop;
        LoopSection loopSection;
        LoopDefinition loopDefinition;
        bool loopPassed;
        float fallDistance;     // metres fallen so far
        float prevFallDistance; // ...at the start of the tick
        float fallVelocity;
        float fallHeight;       // the drop: twice the radius
        Vector3 fallTopPosition;
        Quaternion fallTopRotation, fallExitRotation;
        Vector3 fallExitPosition;

        // The end of the track: the one stretch the motor flies itself — a
        // world position and velocity, the terminal fall or the escape.
        Vector3 offPosition, prevOffPosition, offVelocity;
        Quaternion offRotation;

        /// <summary>What an <see cref="ShipState.OffTrack"/> ship is doing.</summary>
        enum OffTrackMode
        {
            /// <summary>Over an open edge: the standalone ship's own fall and respawn (<see cref="ShipRecovery"/>).</summary>
            Fall,
            /// <summary>Off the end of the track without the win: falls and tumbles for good.</summary>
            TerminalFall,
            /// <summary>Off an end ramp with the win: flies on along the ramp's line, upright, for good.</summary>
            Escape
        }
        OffTrackMode offMode;
        int offSide;            // the side it left over: the tumble rolls that way
        float offTimer;         // seconds flown
        float speedAtFall;      // the speed it left with

        Vector2 pickupReach = new(2.5f, 2.3f); // half width / half height of the ship's pickup volume (the laser gates' test)

        // Metres of lateral offset the autopilot and the tube return answer with full steer.
        const float AutopilotReach = 6f;

        void Awake()
        {
            if (visual == null) visual = transform;
            if (track == null)
            {
                Debug.LogError("ShipMotor needs a TrackManager reference.", this);
                enabled = false;
                return;
            }

            // The body is only ever written by Mirror; its state changes are the runner's state events.
            body = new TrackBody(track);
            body.StateChanged += next => StateChanged?.Invoke(next);

            // The pickup volume is the one authored on the ship: its box, only a measure.
            var box = GetComponent<BoxCollider>();
            if (box != null) pickupReach = new Vector2(box.size.x * 0.5f, box.size.y * 0.5f);

            BindPhysicsShip();
        }

        // Whoever asks "which ship flies this scene" (ShipRegistry) gets the standalone ship, which registers itself.
        void OnEnable() => SubscribePhysics();
        void OnDisable() => UnsubscribePhysics();

        // Launch in Start so a TrackGenerator's Awake can rebuild the spline first.
        void Start() => Launch();

        /// <summary>Swap the active definition (the run's clone) — on the ship too, and the colliders' sink follows its hover height.</summary>
        public void SetDefinition(ShipDefinition newDefinition)
        {
            definition = newDefinition;
            if (physicsShip != null)
            {
                physicsShip.SetDefinition(newDefinition);
                MatchSurfaceToShip();
            }
        }

        /// <summary>
        /// Hands the motor the run's rules (GameManager pushes the shared
        /// GameSettings asset here — the motor holds no settings reference of
        /// its own); they reach the ship through <see cref="RunnerShipSettingsSync"/>.
        /// </summary>
        public void ConfigureDash(GameSettings settings)
        {
            dashSettings = settings;
            ConfigurePhysics(settings);
        }

        /// <summary>Resets the run to the track start and applies the initial impulse.</summary>
        public void Launch()
        {
            if (body == null || physicsShip == null) return;
            LaunchPhysics();
        }

        /// <summary>
        /// Called by pads and card effects. Positive boosts, negative brakes.
        /// The change is scaled by weight and blended in over time by acceleration.
        /// </summary>
        public void AddSpeedImpulse(float rawMagnitude)
        {
            if (HasStopped || physicsShip == null) return;
            physicsShip.AddSpeedImpulse(rawMagnitude); // its PadImpulse comes back through the forward
        }

        /// <summary>
        /// A sideways slam from outside the ship — the patrol's shove. Feeds
        /// the same <c>ShoveVelocity</c> channel a dash does, which is the
        /// whole point: a wall reads that channel to tell a slam from ordinary
        /// steering, so the shove produces a real wall hit on a walled stretch
        /// and a fall over an open edge without any new damage source.
        /// Positive is to the right, in m/s of lateral velocity.
        /// </summary>
        public void AddLateralShove(float velocity)
        {
            if (HasStopped || physicsShip == null || physicsShip.Body == null) return;
            physicsShip.Body.AddLateralImpulse(velocity);
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

        void Update()
        {
            if (physicsShip == null) return;
            RenderPhysics(Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f)));
        }

        void FixedUpdate()
        {
            if (Paused || HasStopped || physicsShip == null) return;
            TickPhysics(Time.fixedDeltaTime);
            lastTickTime = Time.fixedTime;
        }

        // The flight off the end of the track: the escape flies on along the lip's line, nose on the velocity;
        // the terminal fall drops and tumbles over the side it left by, and never comes back.
        void StepFall(float dt)
        {
            if (offMode == OffTrackMode.Escape)
            {
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
            offRotation *= Quaternion.Euler(tumble * 0.35f * dt, 0f, -offSide * tumble * dt);
            offTimer += dt;
        }

        void ClearLoop()
        {
            loop = null;
            loopSection = null;
            loopDefinition = null;
        }
    }
}
