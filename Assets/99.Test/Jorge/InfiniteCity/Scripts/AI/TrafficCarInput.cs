using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// The civilian driver: an ICarInput like the police, so traffic runs
    /// the same CarController physics — but it only ever wanders the
    /// RoadGraph at its personal cruise speed, brakes to a queue behind
    /// whatever car is ahead, and (for work vehicles like the garbage
    /// truck) randomly pulls to a stop for a few seconds before rolling on.
    /// No perception, no pathfinding — that keeps a whole fleet cheap; the
    /// wander/drive core intentionally mirrors PoliceCarInput's Patrol
    /// behavior. All knobs live on TrafficSettings.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class TrafficCarInput : MonoBehaviour, ICarInput
    {
        [Required, InlineEditor]
        [Tooltip("All traffic tunables live on this asset — assigned by the TrafficManager at spawn.")]
        public TrafficSettings settings;

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public bool Stopped { get; private set; }

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake => Stopped;
        public bool RespawnPressed => false;

        CarController car;
        CityManager city;
        bool stopsRandomly;
        bool offRoad;
        float cruiseSpeedKmh = 30f;
        readonly List<Vector3> waypoints = new();
        Vector2Int wanderFrom;
        Vector3 previousWaypoint;
        float stopTimer, stoppedTimer, stuckTimer, reverseTimer, noProgressTime;
        Vector3 progressAnchor;
        float lastForwardSteer;
        float reverseSteer;
        float queuedPatienceFactor = 3f;
        float recentContactTimer;
        float obstacleHitSide;
        ObstacleKind lastObstacle;

        static readonly Collider[] OverlapBuffer = new Collider[8];

        float CellSize => city != null && city.settings != null ? city.settings.cellSize : 20f;

        public void Initialize(TrafficSettings trafficSettings, CityManager cityManager, bool stops)
        {
            settings = trafficSettings;
            city = cityManager;
            stopsRandomly = stops;
            cruiseSpeedKmh = Random.Range(settings.CruiseMin, settings.CruiseMax);
            stopTimer = Random.Range(settings.StopEveryMin, settings.StopEveryMax);
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
            if (settings == null || city == null || city.Graph == null || city.Graph.Count == 0)
            {
                Steer = 0f;
                Throttle = 0f;
                return;
            }

            if (recentContactTimer > 0f) recentContactTimer -= dt;

            // Last-resort self-heal: NO NET PROGRESS for too long → snap onto
            // the road. Displacement, not speed: a reverse-crash-reverse loop
            // keeps momentary speed (each reverse resets a low-speed timer)
            // while going nowhere. Deliberate stops excluded; a failed recover
            // (target cell occupied) retries next frame.
            if (Stopped || FlatDistance(transform.position, progressAnchor) > CellSize * 0.6f)
            {
                progressAnchor = transform.position;
                noProgressTime = 0f;
            }
            // Politely queued behind another car (no collision involved) is
            // patience, not stuckness — don't teleport a car out of a queue.
            else if (lastObstacle != ObstacleKind.Vehicle || recentContactTimer > 0f)
                noProgressTime += dt;
            if (noProgressTime >= settings.hardRecoverSeconds && HardRecover())
                return;

            if (stopsRandomly) UpdateStopCycle(dt);
            if (Stopped)
            {
                Steer = 0f;
                Throttle = 0f;
                return; // Handbrake holds the vehicle
            }

            // Roads only: if we're off the grid (clipped a corner, got shoved
            // into a lot), drop the plan and creep straight back to the
            // nearest road cell instead of wandering onward through buildings.
            RoadGraph graph = city.Graph;
            offRoad = !graph.IsRoad(graph.WorldToCell(transform.position));
            if (offRoad)
            {
                // Replan unless we're already creeping to that exact cell —
                // a stale single-waypoint plan must not block recovery.
                if (graph.TryGetNearestCell(transform.position, out Vector2Int nearest)
                    && (waypoints.Count != 1 || graph.WorldToCell(waypoints[0]) != nearest))
                {
                    waypoints.Clear();
                    waypoints.Add(graph.CellCenter(nearest));
                    previousWaypoint = transform.position;
                }
            }
            else
            {
                // Keep a few cells queued so the corner slowdown can look ahead.
                for (int i = 0; waypoints.Count < 3 && i < 4; i++) ExtendWander();
            }

            Drive(dt);
        }

        /// <summary>Work-vehicle rhythm: drive a while, pull to a stop for a few seconds, repeat — each interval rolled fresh.</summary>
        void UpdateStopCycle(float dt)
        {
            if (Stopped)
            {
                stoppedTimer -= dt;
                if (stoppedTimer > 0f) return;
                Stopped = false;
                stopTimer = Random.Range(settings.StopEveryMin, settings.StopEveryMax);
                return;
            }

            stopTimer -= dt;
            if (stopTimer > 0f) return;
            Stopped = true;
            stoppedTimer = Random.Range(settings.StopDurationMin, settings.StopDurationMax);
        }

        void Drive(float dt)
        {
            if (reverseTimer > 0f)
            {
                reverseTimer -= dt;
                Throttle = -0.6f;
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
                Throttle = car.SpeedKmh > 5f ? -0.3f : 0f;
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
            float desired = Mathf.Lerp(settings.cornerSpeedKmh, cruiseSpeedKmh, Mathf.Min(steerFactor, approachFactor));
            if (offRoad) desired = Mathf.Min(desired, settings.cornerSpeedKmh); // creep back onto the road
            ObstacleKind obstacle = ObstacleAhead();
            lastObstacle = obstacle;
            if (obstacle != ObstacleKind.None) desired = 0f; // queue politely / don't wedge into the wall
            Throttle = Mathf.Clamp((desired - car.SpeedKmh) * settings.throttleGain, -1f, 1f);

            // Stuck escalation while standing still: walls escalate fast (we're
            // wedged, back out), a vehicle ahead is waited on patiently (it's
            // probably a queue), and plain wheelspin counts as wedged too.
            // Patience is rolled per episode and cut after a real collision —
            // two crashed cars on identical timers reverse in lockstep and
            // re-collide forever.
            bool standing = car.SpeedKmh < 3f;
            bool wedged = obstacle == ObstacleKind.Wall || (obstacle == ObstacleKind.None && Mathf.Abs(Throttle) > 0.2f && desired > 1f);
            bool queued = obstacle == ObstacleKind.Vehicle;
            bool wasStuck = stuckTimer > 0f;
            stuckTimer = standing && (wedged || queued) ? stuckTimer + dt : 0f;
            if (stuckTimer > 0f && !wasStuck) queuedPatienceFactor = Random.Range(1.5f, 3f);
            float patience = recentContactTimer > 0f ? 1f : queuedPatienceFactor;
            float escalation = queued ? settings.stuckSeconds * patience : settings.stuckSeconds;
            if (stuckTimer >= escalation)
            {
                if (settings.logTrafficEvents)
                    Debug.Log($"[Traffic] stuck escalation ({(queued ? "queued" : "wedged")}) at {transform.position}", this);
                stuckTimer = 0f;
                reverseTimer = settings.reverseSeconds * Random.Range(0.7f, 1.8f);
                // Reverse steering TOWARD the obstacle side swings the nose
                // away from it (front-steer kinematics reverse the yaw).
                reverseSteer = obstacleHitSide != 0f
                    ? obstacleHitSide * 0.7f
                    : -Mathf.Sign(lastForwardSteer) * 0.7f;
                waypoints.Clear();
                previousWaypoint = transform.position;
                wanderFrom = city.Graph.WorldToCell(transform.position); // stale value could allow an instant U-turn into the wreck
            }
        }

        /// <summary>
        /// The point we actually steer at: the waypoint pushed into the
        /// right-hand lane. Anchored to the SEGMENT direction (previous →
        /// current waypoint) so the target is a fixed point in space —
        /// offsetting by the live approach vector rotates the target with the
        /// car and creates merry-go-rounds. Absolute cap keeps wide roads
        /// from pushing the lane too far out.
        /// </summary>
        Vector3 SteerTarget(Vector3 waypoint, Vector3 previous)
        {
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
            if (collision.rigidbody == null) return;
            recentContactTimer = 3f;
            if (settings != null && settings.logTrafficEvents)
                Debug.Log($"[Traffic] vehicle contact with {collision.gameObject.name}", this);
        }

        /// <summary>
        /// Snap onto the nearest road cell, aligned with the road — the
        /// ambient-traffic answer to a hopeless wedge. Refuses (returns false,
        /// caller retries next frame) while another car occupies the cell, so
        /// recovery never materializes one wreck inside another.
        /// </summary>
        bool HardRecover()
        {
            RoadGraph graph = city.Graph;
            if (!graph.TryGetNearestCell(transform.position, out Vector2Int cell)) return false;
            Vector3 center = graph.CellCenter(cell);
            if (!CellClearOfOtherCars(center)) return false;

            if (settings.logTrafficEvents)
                Debug.Log($"[Traffic] HardRecover to {cell}", this);
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

        /// <summary>No FOREIGN rigidbody in the cell — own colliders don't count, so a car can always recover onto the cell it already stands on.</summary>
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
                // and not mid-turn — a hard-steering car's ray sweeps off a
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

            // Junction yield: a right-hand whisker sees crossing traffic the
            // forward ray can't. Vehicles only — buildings on the whisker are
            // normal at corners. Right-only is the tiebreak (priority to the
            // right): of two converging cars, exactly one yields.
            Vector3 whisker = Quaternion.AngleAxis(settings.yieldWhiskerAngle, Vector3.up) * transform.forward;
            if (Physics.Raycast(origin, whisker, out RaycastHit side,
                    settings.yieldWhiskerDistance, ~0, QueryTriggerInteraction.Ignore)
                && !side.transform.IsChildOf(transform)
                && side.rigidbody != null && side.rigidbody != car.Body)
            {
                obstacleHitSide = 1f;
                return ObstacleKind.Vehicle;
            }

            return ObstacleKind.None;
        }

        /// <summary>Append one more wander cell: a random connected neighbour, biased against turning straight back.</summary>
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

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
