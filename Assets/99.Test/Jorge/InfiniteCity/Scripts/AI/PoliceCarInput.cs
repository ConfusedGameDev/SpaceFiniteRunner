using System.Collections.Generic;
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
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PoliceCarInput : MonoBehaviour, ICarInput
    {
        public enum AiState { Patrol, Chase, Search }

        [Required, InlineEditor]
        [Tooltip("All pursuit tunables live on this asset — assigned by the PatrolManager at spawn.")]
        public PursuitSettings settings;

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public AiState State { get; private set; } = AiState.Patrol;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake => false;
        public bool RespawnPressed => false;

        CarController car;
        CityManager city;
        CarController player;

        readonly List<Vector3> waypoints = new();
        readonly List<Vector2Int> pathBuffer = new();
        Vector3 lastKnownPlayerPosition;
        Vector3 previousWaypoint;
        Vector2Int wanderFrom;
        bool offRoad;
        float repathTimer, lostSightTimer, searchTimer, stuckTimer, reverseTimer, retargetTimer, noProgressTime;
        Vector3 progressAnchor;
        float lastForwardSteer;
        float reverseSteer;
        float queuedPatienceFactor = 3f;
        float recentContactTimer;
        float obstacleHitSide;
        ObstacleKind lastObstacle;

        static readonly Collider[] OverlapBuffer = new Collider[8];

        float CellSize => city != null && city.settings != null ? city.settings.cellSize : 20f;

        public void Initialize(PursuitSettings pursuitSettings, CityManager cityManager)
        {
            settings = pursuitSettings;
            city = cityManager;
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
            offRoad = !city.Graph.IsRoad(city.Graph.WorldToCell(transform.position));
            if (offRoad && State != AiState.Chase)
            {
                // Replan unless we're already creeping to that exact cell —
                // a stale single-waypoint plan must not block recovery.
                if (city.Graph.TryGetNearestCell(transform.position, out Vector2Int nearest)
                    && (waypoints.Count != 1 || city.Graph.WorldToCell(waypoints[0]) != nearest))
                {
                    waypoints.Clear();
                    waypoints.Add(city.Graph.CellCenter(nearest));
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
                        searchTimer = settings.searchDuration;
                        waypoints.Clear();
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
                        waypoints.Clear();
                    }
                    break;
            }
        }

        void EnterChase()
        {
            State = AiState.Chase;
            lostSightTimer = 0f;
            repathTimer = 0f;
            waypoints.Clear();
            previousWaypoint = transform.position;
        }

        void PlanRoute(bool seesPlayer, float dt)
        {
            switch (State)
            {
                case AiState.Chase:
                    if (player == null) break;
                    Vector3 predicted = player.transform.position + player.Velocity * settings.predictionLead;

                    // Close and visible: skip the graph and hunt directly.
                    if (seesPlayer && FlatDistance(transform.position, player.transform.position) < city.settings.cellSize * 1.5f)
                    {
                        waypoints.Clear();
                        waypoints.Add(predicted);
                        previousWaypoint = transform.position;
                        break;
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

            // Tight tracking: pop when genuinely close OR just passed — a
            // missed waypoint must never become an orbit center. Measured
            // against the SAME lane-offset point we steer at: popping against
            // the raw cell center while aiming beside it is exactly how a
            // never-satisfied waypoint becomes an orbit center.
            float reach = Mathf.Min(settings.waypointReachDistance, CellSize * 0.4f);
            while (waypoints.Count > 0)
            {
                Vector3 to = SteerTarget(waypoints[0], previousWaypoint) - transform.position;
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
                return;
            }

            Vector3 target = SteerTarget(waypoints[0], previousWaypoint);
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float maxSteerAngle = car.config != null ? car.config.maxSteerAngle : 35f;
            Steer = Mathf.Clamp(angle / maxSteerAngle, -1f, 1f);
            lastForwardSteer = Steer;

            // Slow BEFORE the corner, not just in it: when the next leg bends,
            // the closer we get to the corner cell the harder we brake.
            float steerFactor = Mathf.Clamp01(1f - Mathf.Abs(angle) / 90f);
            float approachFactor = 1f;
            if (waypoints.Count >= 2)
            {
                float turnAhead = Vector3.Angle(waypoints[1] - waypoints[0], waypoints[0] - transform.position);
                if (turnAhead > 30f)
                    approachFactor = Mathf.Clamp01(FlatDistance(transform.position, waypoints[0]) / (CellSize * 1.2f));
            }
            float cruise = State == AiState.Chase ? settings.chaseSpeedKmh : settings.patrolSpeedKmh;
            float desired = Mathf.Lerp(settings.cornerSpeedKmh, cruise, Mathf.Min(steerFactor, approachFactor));
            if (offRoad && State != AiState.Chase) desired = Mathf.Min(desired, settings.cornerSpeedKmh); // creep back onto the road
            ObstacleKind obstacle = ObstacleAhead();
            lastObstacle = obstacle;
            if (obstacle == ObstacleKind.Wall) desired = 0f;
            else if (obstacle == ObstacleKind.Vehicle) desired = Mathf.Min(desired, settings.cornerSpeedKmh * 0.5f);
            Throttle = Mathf.Clamp((desired - car.SpeedKmh) * settings.throttleGain, -1f, 1f);

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
                waypoints.Clear();
                previousWaypoint = transform.position;
                wanderFrom = city.Graph.WorldToCell(transform.position); // stale value could allow an instant U-turn into the wreck
            }
        }

        /// <summary>
        /// The point we actually steer at: outside Chase, the waypoint pushed
        /// into the right-hand lane, anchored to the SEGMENT direction so the
        /// target is fixed in space (live-approach offsets rotate with the car
        /// and create merry-go-rounds). A chasing cop takes the center line.
        /// </summary>
        Vector3 SteerTarget(Vector3 waypoint, Vector3 previous)
        {
            if (State == AiState.Chase) return waypoint;
            Vector3 segment = waypoint - previous;
            segment.y = 0f;
            if (segment.sqrMagnitude <= 0.25f) return waypoint;
            float laneOffset = Mathf.Min(CellSize * settings.laneOffsetFraction, 2.2f);
            return waypoint + Vector3.Cross(Vector3.up, segment.normalized) * laneOffset;
        }

        void OnCollisionEnter(Collision collision)
        {
            // A car we just hit is a wreck to back out of, not a queue to
            // wait in — short patience for a while after any vehicle contact.
            if (collision.rigidbody != null) recentContactTimer = 3f;
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
            if (!graph.TryGetNearestCell(transform.position, out Vector2Int cell)) return false;
            Vector3 center = graph.CellCenter(cell);
            if (!CellClearOfOtherCars(center)) return false;

            noProgressTime = 0f;
            progressAnchor = center;
            stuckTimer = 0f;
            reverseTimer = 0f;
            waypoints.Clear();
            float yaw = CityManager.RandomConnectedYaw(graph.Connections(cell));
            car.Body.linearVelocity = Vector3.zero;
            car.Body.angularVelocity = Vector3.zero;
            CarFactory.Teleport(car.Body, center + Vector3.up * 0.5f, Quaternion.Euler(0f, yaw, 0f));
            previousWaypoint = transform.position;
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

        enum ObstacleKind { None, Vehicle, Wall }

        /// <summary>Another car dead ahead (brake, don't shunt) — or a wall close ahead, meaning we've left the road line.</summary>
        ObstacleKind ObstacleAhead()
        {
            obstacleHitSide = 0f;
            Vector3 origin = transform.position + Vector3.up * 0.6f + transform.forward * 2.6f;

            // Three forward rays — center plus both fenders. A single center
            // ray lets a building corner slip past and catch the fender, which
            // is exactly how cars wedge nose-first on junction corners.
            for (int i = 0; i < 3; i++)
            {
                Vector3 rayOrigin = origin + transform.right * ((i - 1) * 0.85f);
                if (!Physics.Raycast(rayOrigin, transform.forward, out RaycastHit hit,
                        settings.forwardBrakeDistance, ~0, QueryTriggerInteraction.Ignore)
                    || hit.transform.IsChildOf(transform))
                    continue;
                obstacleHitSide = Mathf.Sign(transform.InverseTransformPoint(hit.point).x);
                if (hit.rigidbody != null && hit.rigidbody != car.Body) return ObstacleKind.Vehicle;
                // Static geometry: a genuinely-close hit is an imminent wedge
                // regardless of steering; farther hits only count when head-on
                // and not mid-turn — a hard-steering cruiser's ray sweeps off a
                // corner facade by itself, and grazing hits are normal there.
                if (hit.rigidbody == null)
                {
                    if (hit.distance < 1.2f) return ObstacleKind.Wall;
                    bool headOn = Vector3.Dot(hit.normal, transform.forward) < -0.5f;
                    if (hit.distance < settings.wallBrakeDistance && headOn && Mathf.Abs(Steer) < 0.5f)
                        return ObstacleKind.Wall;
                }
                obstacleHitSide = 0f;
            }

            return ObstacleKind.None;
        }

        // ------------------------------------------------------------- routing

        void PathToPosition(Vector3 position)
        {
            RoadGraph graph = city.Graph;
            if (!TryGetCellOn(graph, transform.position, out Vector2Int start)) return;
            if (!TryGetCellOn(graph, position, out Vector2Int goal)) return;
            if (!graph.TryFindPath(start, goal, pathBuffer)) return;

            waypoints.Clear();
            previousWaypoint = transform.position;
            foreach (Vector2Int cell in pathBuffer)
                if (cell != start)
                    waypoints.Add(graph.CellCenter(cell));
            if (waypoints.Count == 0) waypoints.Add(graph.CellCenter(goal));
            wanderFrom = start;
        }

        /// <summary>Append one more wander cell: a random connected neighbour, biased against turning straight back (dead ends may).</summary>
        void ExtendWander()
        {
            RoadGraph graph = city.Graph;
            Vector2Int from;
            if (waypoints.Count > 0) from = graph.WorldToCell(waypoints[^1]);
            else if (!TryGetCellOn(graph, transform.position, out from)) return;

            EdgeMask mask = graph.Connections(from);
            Vector2Int pick = default;
            int seen = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((mask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                Vector2Int neighbour = from + EdgeMaskUtility.Offset(dir);
                if (!graph.IsRoad(neighbour) || neighbour == wanderFrom) continue;
                seen++;
                if (Random.Range(0, seen) == 0) pick = neighbour;
            }
            if (seen == 0)
            {
                if (!graph.IsRoad(wanderFrom)) return;
                pick = wanderFrom; // dead end — U-turn is the only option
            }

            wanderFrom = from;
            waypoints.Add(graph.CellCenter(pick));
        }

        static bool TryGetCellOn(RoadGraph graph, Vector3 position, out Vector2Int cell)
        {
            cell = graph.WorldToCell(position);
            return graph.IsRoad(cell) || graph.TryGetNearestCell(position, out cell);
        }

        // ---------------------------------------------------------- perception

        bool CanSeePlayer()
        {
            if (player == null) return false;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 aim = player.transform.position + Vector3.up * 0.8f;
            Vector3 delta = aim - eye;
            float distance = delta.magnitude;
            if (distance > settings.detectionRange) return false;

            // Anything solid between eye and player (that is neither of us) blocks the view.
            var hits = Physics.RaycastAll(eye, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.transform.IsChildOf(player.transform)) continue;
                return false;
            }
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
