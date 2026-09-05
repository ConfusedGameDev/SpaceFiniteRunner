using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.Debugging;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// The police driver: an ICarInput implementation, so patrol cars run the
    /// exact same CarController physics as the player — same config, different
    /// driver (the plan's "keep AI cars honest" rule). Navigates the city's
    /// RoadGraph with a three-state machine:
    /// Patrol — wander the graph, random turns at junctions, biased against
    /// U-turns. Chase — entered on sight (distance + line-of-sight ray, so
    /// buildings hide the player); repaths to the player's predicted position
    /// at an interval. Search — after losing sight, drives to the last known
    /// position and sweeps nearby until the search window runs out, which is
    /// what makes shaking a pursuit feel earned. Steering aims at the next
    /// waypoint, throttle chases a target speed that drops for corners, a
    /// stuck timer backs the car out of walls, and a forward ray brakes
    /// behind other cars (v1 anti-pileup). All knobs live on PursuitSettings.
    ///
    /// Close and visible, Chase becomes a RAM: the player is a target the
    /// cruiser drives INTO, never a waypoint it can "reach" (a waypoint pops a
    /// few metres short and would park the car behind a slow or stopped
    /// player), the anti-pileup brake ignores the player, and a charge that
    /// stops closing (nose against the bumper, shoving a crawling player) is
    /// spent — the cruiser reverses for a run-up with its nose swinging back
    /// onto the player, then charges again. Hit, back off, hit: the rhythm the
    /// chase is built on, whatever speed the player is doing.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PoliceCarInput : MonoBehaviour, ICarInput, IAiDebugDriver
    {
        public enum AiState { Patrol, Chase, Search }

        /// <summary>Chase sub-phase: Charge drives flat out into the player, Backoff reverses for a run-up after a spent hit.</summary>
        public enum RamPhase { None, Charge, Backoff }

        [Required, InlineEditor]
        [Tooltip("All pursuit tunables live on this asset — assigned by the PatrolManager at spawn.")]
        public PursuitSettings settings;

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public AiState State { get; private set; } = AiState.Patrol;

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public RamPhase Ram { get; private set; } = RamPhase.None;

        /// <summary>Distance this cruiser can spot the player from — what the radar draws as its search disc.</summary>
        public float DetectionRange => settings != null ? settings.detectionRange : 0f;

        /// <summary>
        /// 0..1 progress of a chasing cruiser toward giving the player up:
        /// how long its line of sight has been broken, as a fraction of
        /// <see cref="PursuitSettings.loseSightSeconds"/>. 0 while it can
        /// see the player, 1 the moment it drops to Search.
        /// </summary>
        public float LoseSightProgress => State == AiState.Chase && settings != null && settings.loseSightSeconds > 0f
            ? Mathf.Clamp01(lostSightTimer / settings.loseSightSeconds)
            : 0f;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake => health != null && health.IsDead;
        public bool Burnout => false; // player gesture only — a cruiser never line-locks
        public bool RespawnPressed => false;

        /// <summary>What kind of car this cruiser is and what colour it is — the CarController's identity, surfaced on the NPC.</summary>
        public VehicleIdentity Identity => car != null ? car.identity : default;

        CarController car;
        CarHealth health;
        CityManager city;
        CarController player;

        readonly List<Vector3> waypoints = new();
        readonly List<RoadNode> pathBuffer = new();
        Vector3 lastKnownPlayerPosition;
        Vector3 previousWaypoint;
        RoadNode wanderFrom;
        // Graph node of the LAST queued waypoint. Waypoints are plain positions
        // and an overpass deck shares its XZ with the street below, so the level
        // has to travel with the plan instead of being re-derived from it.
        RoadNode? planHead;
        // Grid direction the plan is travelling (0..3 = N,E,S,W, -1 unknown).
        // The wander rule re-derives it from the plan (or the cruiser's nose on
        // a fresh plan), so this is the debug/recovery record, not the authority.
        int travelDirection = -1;
        bool offRoad;
        float repathTimer, lostSightTimer, searchTimer, stuckTimer, reverseTimer, retargetTimer, noProgressTime;
        // Ram bookkeeping: where the charge is aimed this frame, how long the
        // charge has been stalled against the player, how long the current
        // back-off has run, and a short window after touching the player that
        // lets a stalled charge give up at once instead of waiting out the fuse.
        Vector3 ramTarget;
        float ramStallTimer, ramBackoffTimer, playerContactTimer;
        Vector3 progressAnchor;
        float lastForwardSteer;
        float reverseSteer;
        float queuedPatienceFactor = 3f;
        float recentContactTimer;
        float obstacleHitSide;
        ObstacleKind lastObstacle;

        // Debug mirror: what the driver actually decided this frame, kept so
        // the overlay can show the real decision instead of re-deriving one.
        readonly AiProbeLog probeLog = new();
        Vector3 steerAim;
        Vector3 sightFrom, sightTo;
        bool sightValid, sightClear;

        static readonly Collider[] OverlapBuffer = new Collider[8];

        float CellSize => city != null && city.settings != null ? city.settings.cellSize : 20f;

        public void Initialize(PursuitSettings pursuitSettings, CityManager cityManager)
        {
            settings = pursuitSettings;
            city = cityManager;
            health = GetComponent<CarHealth>(); // attached by the manager before Initialize
        }

        void Awake()
        {
            car = GetComponent<CarController>();
            previousWaypoint = transform.position;
            progressAnchor = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (settings == null || !EnsureCity())
            {
                Steer = 0f;
                Throttle = 0f;
                return;
            }

            if (recentContactTimer > 0f) recentContactTimer -= dt;
            if (playerContactTimer > 0f) playerContactTimer -= dt;

            // A dead cruiser is a wreck burning its fuse: brake to a stop and
            // hold. Before the self-heal on purpose — a wreck must never be
            // teleported back onto the road, and must not reverse out either.
            if (health != null && health.IsDead)
            {
                Steer = 0f;
                Throttle = car.SpeedKmh > 3f ? -0.5f : 0f;
                return; // Handbrake holds the wreck
            }

            // Last-resort self-heal outside Chase: NO NET PROGRESS for too
            // long → snap onto the nearest road cell. Displacement, not speed:
            // a reverse-crash-reverse loop keeps momentary speed (each reverse
            // resets a low-speed timer) while going nowhere. A failed recover
            // (target cell occupied) retries next frame.
            if (State == AiState.Chase || FlatDistance(transform.position, progressAnchor) > CellSize * 0.6f)
            {
                progressAnchor = transform.position;
                noProgressTime = 0f;
            }
            // Politely queued behind another car (no collision involved) is
            // patience, not stuckness — don't teleport a cruiser out of a queue.
            else if (lastObstacle != ObstacleKind.Vehicle || recentContactTimer > 0f)
                noProgressTime += dt;
            if (noProgressTime >= settings.hardRecoverSeconds && HardRecover())
                return;

            RefreshPlayer(dt);
            bool seesPlayer = CanSeePlayer();
            if (seesPlayer) lastKnownPlayerPosition = player.transform.position;

            UpdateState(seesPlayer, dt);

            // Roads only (except mid-Chase, where cutting a lot toward a
            // visible player is fair game): off the grid, drop the plan and
            // creep straight back to the nearest road cell.
            offRoad = !city.Graph.TryGetNodeAt(transform.position, out _);
            if (offRoad && State != AiState.Chase)
            {
                // Replan unless we're already creeping to that exact node —
                // a stale single-waypoint plan must not block recovery.
                if (city.Graph.TryGetNearestNode(transform.position, out RoadNode nearest)
                    && (waypoints.Count != 1 || planHead != nearest))
                {
                    ClearPlan();
                    waypoints.Add(city.Graph.Center(nearest));
                    planHead = nearest;
                    previousWaypoint = transform.position;
                }
            }
            else
            {
                PlanRoute(seesPlayer, dt);
            }
            Drive(dt);
        }

        // -------------------------------------------------------------- states

        void UpdateState(bool seesPlayer, float dt)
        {
            switch (State)
            {
                case AiState.Patrol:
                    if (seesPlayer) EnterChase();
                    break;

                case AiState.Chase:
                    if (seesPlayer)
                    {
                        lostSightTimer = 0f;
                        break;
                    }
                    lostSightTimer += dt;
                    if (lostSightTimer >= settings.loseSightSeconds)
                    {
                        State = AiState.Search;
                        Ram = RamPhase.None;
                        searchTimer = settings.searchDuration;
                        ClearPlan();
                        PathToPosition(lastKnownPlayerPosition);
                    }
                    break;

                case AiState.Search:
                    if (seesPlayer)
                    {
                        EnterChase();
                        break;
                    }
                    searchTimer -= dt;
                    if (searchTimer <= 0f)
                    {
                        State = AiState.Patrol;
                        ClearPlan();
                    }
                    break;
            }
        }

        /// <summary>Drop the current plan — waypoints and the node they ended on go together.</summary>
        void ClearPlan()
        {
            waypoints.Clear();
            planHead = null;
        }

        void EnterChase()
        {
            State = AiState.Chase;
            Ram = RamPhase.None;
            lostSightTimer = 0f;
            repathTimer = 0f;
            ClearPlan();
            previousWaypoint = transform.position;
        }

        void PlanRoute(bool seesPlayer, float dt)
        {
            switch (State)
            {
                case AiState.Chase:
                    if (player == null)
                    {
                        Ram = RamPhase.None;
                        break;
                    }
                    Vector3 predicted = player.transform.position + player.Velocity * settings.predictionLead;

                    // Close and visible: skip the graph and ram — but not across
                    // levels; a player on an overpass right above us is still a
                    // routing problem, not a ramming target. The player is held
                    // as a RAM TARGET, never queued as a waypoint: a waypoint is
                    // "reached" inside the pop radius, and that is exactly how
                    // the cruiser used to ease to a stop a car-length behind a
                    // slow or parked player instead of hitting it.
                    if (seesPlayer && FlatDistance(transform.position, player.transform.position) < city.settings.cellSize * 1.5f
                        && Mathf.Abs(player.transform.position.y - transform.position.y) < 3f)
                    {
                        if (waypoints.Count > 0)
                        {
                            ClearPlan();
                            previousWaypoint = transform.position;
                        }
                        ramTarget = predicted;
                        if (Ram == RamPhase.None) BeginCharge();
                        break;
                    }

                    // Out of ram range (the player pulled away, or climbed a
                    // level): back to routing, whatever the ram was doing —
                    // with a route THIS frame, not after the repath interval.
                    if (Ram != RamPhase.None)
                    {
                        Ram = RamPhase.None;
                        repathTimer = 0f;
                    }
                    repathTimer -= dt;
                    if (repathTimer <= 0f)
                    {
                        repathTimer = settings.repathInterval;
                        PathToPosition(predicted);
                    }
                    break;

                case AiState.Search:
                    // Reached (or failed to path to) the last known spot — sweep the nearby junctions.
                    if (waypoints.Count == 0)
                        for (int i = 0; waypoints.Count < 3 && i < 4; i++) ExtendWander();
                    break;

                case AiState.Patrol:
                    // Keep a few cells queued so the corner slowdown can look ahead.
                    for (int i = 0; waypoints.Count < 3 && i < 4; i++) ExtendWander();
                    break;
            }
        }

        // ------------------------------------------------------------- driving

        void Drive(float dt)
        {
            if (reverseTimer > 0f)
            {
                // Backing out of whatever we're wedged against, nose swinging
                // away from the recorded obstacle; the cleared route forces a
                // fresh plan afterwards.
                reverseTimer -= dt;
                Throttle = -0.8f;
                Steer = reverseSteer;
                return;
            }

            // The ram needs a player to aim at; a respawn gap drops it back to
            // the ordinary plan until the next PlanRoute re-arms it.
            if (Ram != RamPhase.None && player == null) Ram = RamPhase.None;
            if (Ram == RamPhase.Backoff)
            {
                DriveBackoff(dt);
                return;
            }

            Vector3 target;
            if (Ram == RamPhase.Charge)
            {
                // No pop radius on a ram target — the car drives THROUGH it.
                target = ramTarget;
            }
            else
            {
                // Tight tracking: pop when genuinely close OR just passed — a
                // missed waypoint must never become an orbit center. Measured
                // against the SAME lane-offset point we steer at: popping against
                // the raw cell center while aiming beside it is exactly how a
                // never-satisfied waypoint becomes an orbit center.
                float reach = Mathf.Min(settings.waypointReachDistance, CellSize * 0.4f);
                while (waypoints.Count > 0)
                {
                    Vector3? next = waypoints.Count >= 2 ? waypoints[1] : (Vector3?)null;
                    Vector3 to = SteerTarget(waypoints[0], previousWaypoint, next) - transform.position;
                    to.y = 0f;
                    bool reached = to.magnitude < reach;
                    bool passed = Vector3.Dot(transform.forward, to) < 0f && to.magnitude < reach * 2.5f;
                    if (!reached && !passed) break;
                    previousWaypoint = waypoints[0];
                    waypoints.RemoveAt(0);
                }

                if (waypoints.Count == 0)
                {
                    Steer = 0f;
                    Throttle = car.SpeedKmh > 5f ? -0.3f : 0f; // ease to a stop until the next plan
                    steerAim = transform.position;
                    return;
                }

                target = SteerTarget(waypoints[0], previousWaypoint, waypoints.Count >= 2 ? waypoints[1] : (Vector3?)null);
            }
            steerAim = target;
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float maxSteerAngle = car.config != null ? car.config.maxSteerAngle : 35f;
            Steer = Mathf.Clamp(angle / maxSteerAngle, -1f, 1f);
            lastForwardSteer = Steer;

            // Slow BEFORE the corner, not just in it: when the next leg bends,
            // the closer we get to the corner cell the harder we brake.
            float steerFactor = Mathf.Clamp01(1f - Mathf.Abs(angle) / 90f);
            float approachFactor = 1f;
            if (Ram == RamPhase.None && waypoints.Count >= 2)
            {
                float turnAhead = Vector3.Angle(Flat(waypoints[1] - waypoints[0]), Flat(waypoints[0] - transform.position)); // XZ only — a ramp is a climb, not a corner
                if (turnAhead > 30f)
                    approachFactor = Mathf.Clamp01(FlatDistance(transform.position, waypoints[0]) / (CellSize * 1.2f));
            }
            float cruise = State == AiState.Chase ? settings.chaseSpeedKmh : settings.patrolSpeedKmh;
            float desired = Mathf.Lerp(settings.cornerSpeedKmh, cruise, Mathf.Min(steerFactor, approachFactor));
            // A charge is never a crawl: with the player in the front arc the
            // corner slowdown is overruled by the ram floor, so a parked player
            // still takes a real hit. Beside or behind us the tight turn keeps
            // its slow speed — that is what brings the nose back around.
            if (Ram == RamPhase.Charge && Mathf.Abs(angle) < 75f) desired = Mathf.Max(desired, settings.ramMinSpeedKmh);
            if (offRoad && State != AiState.Chase) desired = Mathf.Min(desired, settings.cornerSpeedKmh); // creep back onto the road
            ObstacleKind obstacle = ObstacleAhead();
            lastObstacle = obstacle;
            if (obstacle == ObstacleKind.Wall) desired = 0f;
            else if (obstacle == ObstacleKind.Vehicle) desired = Mathf.Min(desired, settings.cornerSpeedKmh * 0.5f);
            if (health != null) desired *= health.SpeedFactor; // a wounded engine can't hold cruise speed
            Throttle = Mathf.Clamp((desired - car.SpeedKmh) * settings.throttleGain, -1f, 1f);

            // A charge that has stopped closing is spent — back off for the
            // next one. Checked before the stuck rule so nose-to-bumper never
            // reads as "wedged": the player is the one thing a cruiser pushes
            // against on purpose.
            if (Ram == RamPhase.Charge && ChargeSpent(dt))
            {
                stuckTimer = 0f;
                BeginBackoff();
                return;
            }

            // Stuck escalation while standing still: walls escalate fast,
            // vehicles patiently (Chase keeps its short fuse — back up and
            // charge again is exactly the ramming rhythm we want). Patience is
            // rolled per episode and cut after a real collision — two crashed
            // cars on identical timers reverse in lockstep and re-collide.
            bool standing = car.SpeedKmh < 3f;
            bool wedged = obstacle == ObstacleKind.Wall || (obstacle == ObstacleKind.None && Mathf.Abs(Throttle) > 0.2f);
            bool queued = obstacle == ObstacleKind.Vehicle;
            bool wasStuck = stuckTimer > 0f;
            stuckTimer = standing && (wedged || queued) ? stuckTimer + dt : 0f;
            if (stuckTimer > 0f && !wasStuck) queuedPatienceFactor = Random.Range(1.5f, 3f);
            float patience = recentContactTimer > 0f ? 1f : queuedPatienceFactor;
            float escalation = queued && State != AiState.Chase ? settings.stuckSeconds * patience : settings.stuckSeconds;
            if (stuckTimer >= escalation)
            {
                stuckTimer = 0f;
                reverseTimer = settings.reverseSeconds * Random.Range(0.7f, 1.8f);
                // Reverse steering TOWARD the obstacle side swings the nose
                // away from it (front-steer kinematics reverse the yaw).
                reverseSteer = obstacleHitSide != 0f
                    ? obstacleHitSide * 0.8f
                    : -Mathf.Sign(lastForwardSteer) * 0.8f;
                ClearPlan();
                previousWaypoint = transform.position;
                if (city.Graph.TryGetNodeAt(transform.position, out RoadNode here)) wanderFrom = here; // stale value could allow an instant U-turn into the wreck
            }
        }

        // ------------------------------------------------------------- ramming

        void BeginCharge()
        {
            Ram = RamPhase.Charge;
            ramStallTimer = 0f;
            ramBackoffTimer = 0f;
        }

        void BeginBackoff()
        {
            Ram = RamPhase.Backoff;
            ramStallTimer = 0f;
            ramBackoffTimer = 0f;
            playerContactTimer = 0f;
        }

        /// <summary>
        /// Is the charge spent? Inside contact distance AND the gap has stopped
        /// shrinking AND the cruiser itself is under the ram floor. Closing
        /// speed, not own speed, is the tell: a cruiser shoving a crawling
        /// player along closes at ~0 (spent), one being out-run closes negative
        /// (keep chasing — reversing now would hand the player the gap), one
        /// still at full tilt has simply not arrived yet. A short fuse filters
        /// the physics jitter of the impact frame; a fresh touch of the player
        /// skips it, so the reverse starts the moment the hit lands.
        /// </summary>
        bool ChargeSpent(float dt)
        {
            Vector3 toPlayer = Flat(player.transform.position - transform.position);
            float distance = toPlayer.magnitude;
            if (distance >= settings.RamContactDistance)
            {
                ramStallTimer = 0f;
                return false;
            }
            Vector3 direction = distance > 0.01f ? toPlayer / distance : Flat(transform.forward).normalized;
            float closingKmh = Vector3.Dot(car.Velocity - player.Velocity, direction) * 3.6f;
            bool spent = Mathf.Abs(closingKmh) < settings.ramStallSpeedKmh && car.SpeedKmh < settings.ramMinSpeedKmh;
            ramStallTimer = spent ? ramStallTimer + dt : 0f;
            float fuse = playerContactTimer > 0f ? 0f : 0.35f;
            return spent && ramStallTimer >= fuse;
        }

        /// <summary>
        /// The run-up: reverse until the player is the back-off distance away,
        /// with the nose swinging back ONTO the player (front-steer kinematics
        /// flip the yaw in reverse, so steering away from the player's side
        /// turns the nose toward it), and the next charge starts lined up.
        /// Ends early when time runs out or the reverse itself is blocked — a
        /// cruiser pinned against a wall behind charges from where it stands
        /// rather than grinding its bumper. Leaving ram range mid-back-off is
        /// PlanRoute's call (it drops the ram) — the player driving away is the
        /// distance exit here, not a reason to keep reversing.
        /// </summary>
        void DriveBackoff(float dt)
        {
            ramBackoffTimer += dt;
            float distance = FlatDistance(transform.position, player.transform.position);
            bool blocked = ramBackoffTimer > 0.6f && car.SpeedKmh < 2f;
            if (distance >= settings.RamBackoffDistance || ramBackoffTimer >= settings.ramBackoffMaxSeconds || blocked)
            {
                BeginCharge();
                return;
            }

            Vector3 local = transform.InverseTransformPoint(player.transform.position);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            Steer = Mathf.Clamp(-angle / 45f, -1f, 1f);
            Throttle = -0.9f;
            steerAim = player.transform.position;
            lastObstacle = ObstacleKind.None;
        }

        /// <summary>Lane offset in metres: the designer fraction of a cell under an absolute cap, so wide cells can't push the lane onto the sidewalk.</summary>
        float LaneOffset => Mathf.Min(CellSize * settings.laneOffsetFraction, settings.laneOffsetMaxMeters);

        /// <summary>
        /// The point we actually steer at: outside Chase, the waypoint pushed
        /// into the right-hand lane (a miter join when the next waypoint is
        /// known, so corner arrivals land in the OUTGOING leg's lane — see
        /// <see cref="LaneRules.LaneTarget"/>), anchored to the SEGMENT
        /// direction so the target is fixed in space (live-approach offsets
        /// rotate with the car and create merry-go-rounds). Fork seams and
        /// roundabout footprints collapse to the centre line. A chasing cop
        /// takes the center line everywhere — the shortest path to the player
        /// outranks every traffic rule.
        /// </summary>
        Vector3 SteerTarget(Vector3 waypoint, Vector3 previous, Vector3? next)
        {
            if (State == AiState.Chase) return waypoint;
            float lane = city.Graph.IsCenterLineOnlyAt(waypoint) ? 0f : LaneOffset;
            return LaneRules.LaneTarget(previous, waypoint, next, lane);
        }

        void OnCollisionEnter(Collision collision)
        {
            // A car we just hit is a wreck to back out of, not a queue to
            // wait in — short patience for a while after any vehicle contact.
            if (collision.rigidbody != null) recentContactTimer = 3f;
            // Touching the player is the hit landing: lets a spent charge back
            // off at once instead of waiting out the stall fuse.
            if (player != null && collision.rigidbody == player.Body) playerContactTimer = 0.6f;
        }

        /// <summary>
        /// Snap onto the nearest road cell, aligned with the road — the answer
        /// to a hopeless wedge outside Chase. Refuses (returns false, caller
        /// retries next frame) while another car occupies the cell, so recovery
        /// never materializes one wreck inside another.
        /// </summary>
        bool HardRecover()
        {
            RoadGraph graph = city.Graph;
            if (!graph.TryGetNearestNode(transform.position, out RoadNode node)) return false;
            Vector3 center = graph.Center(node);
            if (!CellClearOfOtherCars(center)) return false;

            noProgressTime = 0f;
            stuckTimer = 0f;
            reverseTimer = 0f;
            ClearPlan();
            // Re-enter traffic legally: resume the nearest connected direction
            // to the old heading, standing in that direction's lane — a random
            // yaw here points a recovered cruiser into oncoming traffic half
            // the time.
            int dir = LaneRules.NearestConnectedDirection(graph.Connections(node), transform.forward);
            float lane = graph.IsCenterLineOnly(node) ? 0f : LaneOffset;
            Vector3 pose = center + LaneRules.RightOf(dir) * lane;
            progressAnchor = pose;
            car.Body.linearVelocity = Vector3.zero;
            car.Body.angularVelocity = Vector3.zero;
            CarFactory.Teleport(car.Body, pose + Vector3.up * 0.5f, Quaternion.Euler(0f, dir * 90f, 0f));
            previousWaypoint = transform.position;
            travelDirection = dir;
            return true;
        }

        /// <summary>No FOREIGN rigidbody in the cell — own colliders don't count, so a cruiser can always recover onto the cell it already stands on.</summary>
        bool CellClearOfOtherCars(Vector3 center)
        {
            Vector3 halfExtents = new(CellSize * 0.45f, 1f, CellSize * 0.45f);
            int count = Physics.OverlapBoxNonAlloc(center + Vector3.up, halfExtents, OverlapBuffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody other = OverlapBuffer[i].attachedRigidbody;
                if (other != null && other != car.Body) return false;
            }
            return true;
        }

        /// <summary>
        /// Another car dead ahead (brake, don't shunt) — or a wall close ahead,
        /// meaning we've left the road line. Every cast is recorded into
        /// <see cref="probeLog"/> while the debug overlay is on, verdict
        /// included: the visualizer must show the decision that was made, not
        /// one it re-derives a frame later.
        /// </summary>
        ObstacleKind ObstacleAhead()
        {
            obstacleHitSide = 0f;
            bool log = DebugManager.ShowCollisionProbes;
            if (log) probeLog.Begin();
            Vector3 origin = transform.position + Vector3.up * 0.6f + transform.forward * 2.6f;

            // Three forward rays — center plus both fenders. A single center
            // ray lets a building corner slip past and catch the fender, which
            // is exactly how cars wedge nose-first on junction corners.
            for (int i = 0; i < 3; i++)
            {
                Vector3 rayOrigin = origin + transform.right * ((i - 1) * 0.85f);
                bool blocked = Physics.Raycast(rayOrigin, transform.forward, out RaycastHit hit,
                                   settings.forwardBrakeDistance, ~0, QueryTriggerInteraction.Ignore)
                               && !hit.transform.IsChildOf(transform);
                ObstacleKind verdict = ObstacleKind.None;
                if (blocked)
                {
                    obstacleHitSide = Mathf.Sign(transform.InverseTransformPoint(hit.point).x);
                    // The player is the one car a chasing cruiser never brakes
                    // for — the anti-pileup rule would otherwise park it a
                    // brake-length behind a slow player instead of hitting it.
                    bool isPlayer = State == AiState.Chase && player != null && hit.rigidbody == player.Body;
                    if (hit.rigidbody != null && hit.rigidbody != car.Body && !isPlayer) verdict = ObstacleKind.Vehicle;
                    // Static geometry: a genuinely-close hit is an imminent wedge
                    // regardless of steering; farther hits only count when head-on
                    // and not mid-turn — a hard-steering cruiser's ray sweeps off a
                    // corner facade by itself, and grazing hits are normal there.
                    else if (hit.rigidbody == null)
                    {
                        // A drivable slope — a ramp surface, or the flat street seen
                        // from the top of a down-ramp — is not a wall to stop for.
                        bool headOn = Vector3.Dot(hit.normal, transform.forward) < -0.5f;
                        if (hit.normal.y > 0.35f) verdict = ObstacleKind.None;
                        else if (hit.distance < 1.2f) verdict = ObstacleKind.Wall;
                        else if (hit.distance < settings.wallBrakeDistance && headOn && Mathf.Abs(Steer) < 0.5f)
                            verdict = ObstacleKind.Wall;
                    }
                    if (verdict == ObstacleKind.None) obstacleHitSide = 0f;
                }
                if (log)
                    probeLog.Add(i == 1 ? AiProbeRole.Forward : AiProbeRole.Fender, rayOrigin, transform.forward,
                        settings.forwardBrakeDistance, blocked, hit.point, verdict);
                if (verdict != ObstacleKind.None) return verdict;
            }

            return ObstacleKind.None;
        }

        // ------------------------------------------------------------- routing

        void PathToPosition(Vector3 position)
        {
            RoadGraph graph = city.Graph;
            if (!TryGetNodeOn(graph, transform.position, out RoadNode start)) return;
            if (!TryGetNodeOn(graph, position, out RoadNode goal)) return;
            // A chasing cruiser may cut across a roundabout's island and run
            // its ring either way; patrol and search keep to the traffic rule.
            if (!graph.TryFindPath(start, goal, pathBuffer, cutThrough: State == AiState.Chase)) return;

            ClearPlan();
            previousWaypoint = transform.position;
            foreach (RoadNode node in pathBuffer)
                if (node != start)
                    waypoints.Add(graph.Center(node));
            if (waypoints.Count == 0) waypoints.Add(graph.Center(goal));
            planHead = goal;
            wanderFrom = start;
        }

        /// <summary>
        /// Append one more wander cell under the traffic rules: never the
        /// reverse of the direction of travel (a U-turn puts the cruiser in
        /// the oncoming lane — dead ends stay the one legal flip), a straight
        /// bias so patrols read as through-traffic, otherwise a random
        /// connected neighbour. The direction of travel is re-derived from
        /// the plan itself — or from the cruiser's own nose on a fresh plan,
        /// so a crash-spun car legally resumes in whichever direction it now
        /// faces. Chase never wanders, so the rule only ever shapes Patrol
        /// and Search.
        /// </summary>
        void ExtendWander()
        {
            RoadGraph graph = city.Graph;
            RoadNode from;
            if (waypoints.Count > 0 && planHead.HasValue) from = planHead.Value;
            else if (waypoints.Count > 0 && graph.TryGetNodeAt(waypoints[^1], out from)) { }
            else if (!TryGetNodeOn(graph, transform.position, out from)) return;

            // The direction we arrive at 'from' with: the plan's last segment
            // when there is one (seam twins can share a centre — fall through),
            // else the car's own heading.
            int incoming = waypoints.Count > 0
                ? LaneRules.SegmentDirection(waypoints.Count >= 2 ? waypoints[^2] : previousWaypoint, waypoints[^1])
                : -1;
            if (incoming < 0) incoming = LaneRules.HeadingToDirection(transform.forward);
            int banned = LaneRules.ReverseOf(incoming);

            RoadNode pick = default;
            RoadNode straightPick = default;
            bool straightSeen = false;
            int seen = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if (dir == banned) continue; // wrong-way turn — never, outside a dead end
                if (!graph.TryGetNeighbour(from, dir, out RoadNode neighbour) || neighbour == wanderFrom) continue;
                // A patrolling car keeps to the allowed blocks — it may only
                // roam into a neighbour block while the player is near that
                // edge. Chase and Search ignore the gate: a car converging on
                // the player is by definition heading toward allowed space.
                if (State == AiState.Patrol && !city.IsNpcPositionAllowed(graph.Center(neighbour))) continue;
                seen++;
                if (dir == incoming)
                {
                    straightSeen = true;
                    straightPick = neighbour;
                }
                if (Random.Range(0, seen) == 0) pick = neighbour;
            }
            // No straight bias on a roundabout: "straight" there is "keep
            // circling", and a fleet that prefers it laps the ring forever.
            if (straightSeen && graph.Roundabout(from) == RoundaboutRole.None && Random.value < settings.straightBias) pick = straightPick;
            if (seen == 0)
            {
                // Dead end (or everything else filtered): the U-turn is the
                // one legal direction flip left.
                if (!graph.TryGetNeighbour(from, banned, out pick))
                {
                    if (!graph.Contains(wanderFrom)) return;
                    pick = wanderFrom;
                }
            }

            wanderFrom = from;
            waypoints.Add(graph.Center(pick));
            planHead = pick;
            travelDirection = LaneRules.SegmentDirection(graph.Center(from), graph.Center(pick));
        }

        /// <summary>The node a position stands on (level chosen by height), else the nearest one.</summary>
        static bool TryGetNodeOn(RoadGraph graph, Vector3 position, out RoadNode node) =>
            graph.TryGetNodeAt(position, out node) || graph.TryGetNearestNode(position, out node);

        // ---------------------------------------------------------- perception

        bool CanSeePlayer()
        {
            if (player == null) return false;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 aim = player.transform.position + Vector3.up * 0.8f;
            Vector3 delta = aim - eye;
            float distance = delta.magnitude;
            sightFrom = eye;
            sightTo = aim;
            sightValid = true;
            sightClear = false;
            if (distance > settings.detectionRange) return false;

            // Anything solid between eye and player (that is neither of us) blocks the view.
            var hits = Physics.RaycastAll(eye, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.transform.IsChildOf(player.transform)) continue;
                return false;
            }
            sightClear = true;
            return true;
        }

        void RefreshPlayer(float dt)
        {
            retargetTimer -= dt;
            if (player != null && retargetTimer > 0f) return;
            retargetTimer = 1f;
            player = PatrolManager.FindPlayerCar();
        }

        bool EnsureCity()
        {
            if (city == null) city = FindAnyObjectByType<CityManager>();
            return city != null && city.Graph != null && city.Graph.Count > 0;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // --------------------------------------------------------- debug view

        string IAiDebugDriver.StateLabel => State == AiState.Chase && Ram != RamPhase.None
            ? $"CHASE·{Ram.ToString().ToUpperInvariant()}"
            : State.ToString().ToUpperInvariant();

        Color IAiDebugDriver.StateColor => State switch
        {
            AiState.Chase => new Color(1f, 0.25f, 0.2f),
            AiState.Search => new Color(1f, 0.8f, 0.15f),
            _ => new Color(0.35f, 0.7f, 1f),
        };

        IReadOnlyList<Vector3> IAiDebugDriver.Waypoints => waypoints;
        Vector3 IAiDebugDriver.PreviousWaypoint => previousWaypoint;
        Vector3 IAiDebugDriver.SteerAim => steerAim;

        /// <summary>The current leg's direction while driving one; the last recorded one otherwise.</summary>
        int IAiDebugDriver.TravelDirection => waypoints.Count > 0
            ? LaneRules.SegmentDirection(previousWaypoint, waypoints[0])
            : travelDirection;
        bool IAiDebugDriver.OffRoad => offRoad;
        bool IAiDebugDriver.Reversing => reverseTimer > 0f || Ram == RamPhase.Backoff;
        float IAiDebugDriver.StuckTime => stuckTimer;
        ObstacleKind IAiDebugDriver.Obstacle => lastObstacle;
        IReadOnlyList<AiProbe> IAiDebugDriver.Probes => probeLog.Probes;

        bool IAiDebugDriver.TryGetSightLine(out Vector3 from, out Vector3 to, out bool clear)
        {
            from = sightFrom;
            to = sightTo;
            clear = sightClear;
            return sightValid && player != null;
        }

        // -------------------------------------------------------------- gizmos

        void OnDrawGizmosSelected()
        {
            Gizmos.color = State switch
            {
                AiState.Chase => Color.red,
                AiState.Search => Color.yellow,
                _ => Color.cyan,
            };
            Vector3 previous = transform.position;
            foreach (Vector3 waypoint in waypoints)
            {
                Gizmos.DrawLine(previous, waypoint + Vector3.up * 0.5f);
                Gizmos.DrawWireSphere(waypoint + Vector3.up * 0.5f, 0.6f);
                previous = waypoint + Vector3.up * 0.5f;
            }
        }
    }
}
