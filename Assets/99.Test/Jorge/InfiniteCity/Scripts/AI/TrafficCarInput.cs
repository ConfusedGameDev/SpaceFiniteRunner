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
        float stopTimer, stoppedTimer, stuckTimer, reverseTimer, lowSpeedTime;
        float lastForwardSteer;

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

            // Last-resort self-heal: crawling for too long (reverse cycles
            // included, deliberate stops excluded) → snap onto the road.
            if (!Stopped && car.SpeedKmh < 2f) lowSpeedTime += dt;
            else lowSpeedTime = 0f;
            if (lowSpeedTime >= settings.hardRecoverSeconds)
            {
                HardRecover();
                return;
            }

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
                if (waypoints.Count != 1 && graph.TryGetNearestCell(transform.position, out Vector2Int nearest))
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
                Steer = -Mathf.Sign(lastForwardSteer) * 0.7f;
                return;
            }

            // Tight tracking: pop when genuinely close OR just passed — a
            // missed waypoint must never become an orbit center.
            float reach = Mathf.Min(settings.waypointReachDistance, CellSize * 0.4f);
            while (waypoints.Count > 0)
            {
                Vector3 to = waypoints[0] - transform.position;
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

            // Lane discipline: aim right of the cell center. Anchored to the
            // SEGMENT direction (previous → current waypoint) so the target is
            // a fixed point in space — offsetting by the live approach vector
            // rotates the target with the car and creates merry-go-rounds.
            // Absolute cap keeps wide roads from pushing the lane too far out.
            Vector3 target = waypoints[0];
            Vector3 segment = waypoints[0] - previousWaypoint;
            segment.y = 0f;
            if (segment.sqrMagnitude > 0.25f)
            {
                float laneOffset = Mathf.Min(CellSize * settings.laneOffsetFraction, 2.2f);
                target += Vector3.Cross(Vector3.up, segment.normalized) * laneOffset;
            }

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
            if (obstacle != ObstacleKind.None) desired = 0f; // queue politely / don't wedge into the wall
            Throttle = Mathf.Clamp((desired - car.SpeedKmh) * settings.throttleGain, -1f, 1f);

            // Stuck escalation while standing still: walls escalate fast (we're
            // wedged, back out), a vehicle ahead is waited on patiently (it's
            // probably a queue), and plain wheelspin counts as wedged too.
            bool standing = car.SpeedKmh < 3f;
            bool wedged = obstacle == ObstacleKind.Wall || (obstacle == ObstacleKind.None && Mathf.Abs(Throttle) > 0.2f && desired > 1f);
            bool queued = obstacle == ObstacleKind.Vehicle;
            stuckTimer = standing && (wedged || queued) ? stuckTimer + dt : 0f;
            float escalation = queued ? settings.stuckSeconds * 3f : settings.stuckSeconds;
            if (stuckTimer >= escalation)
            {
                stuckTimer = 0f;
                reverseTimer = settings.reverseSeconds;
                waypoints.Clear();
                previousWaypoint = transform.position;
            }
        }

        /// <summary>Snap onto the nearest road cell, aligned with the road — the ambient-traffic answer to a hopeless wedge.</summary>
        void HardRecover()
        {
            lowSpeedTime = 0f;
            stuckTimer = 0f;
            reverseTimer = 0f;
            waypoints.Clear();

            RoadGraph graph = city.Graph;
            if (!graph.TryGetNearestCell(transform.position, out Vector2Int cell)) return;
            float yaw = CityManager.RandomConnectedYaw(graph.Connections(cell));
            car.Body.linearVelocity = Vector3.zero;
            car.Body.angularVelocity = Vector3.zero;
            CarFactory.Teleport(car.Body, graph.CellCenter(cell) + Vector3.up * 0.5f, Quaternion.Euler(0f, yaw, 0f));
            previousWaypoint = transform.position;
        }

        enum ObstacleKind { None, Vehicle, Wall }

        ObstacleKind ObstacleAhead()
        {
            Vector3 origin = transform.position + Vector3.up * 0.6f + transform.forward * 2.6f;
            if (!Physics.Raycast(origin, transform.forward, out RaycastHit hit,
                    settings.forwardBrakeDistance, ~0, QueryTriggerInteraction.Ignore))
                return ObstacleKind.None;
            if (hit.transform.IsChildOf(transform)) return ObstacleKind.None;
            if (hit.rigidbody != null && hit.rigidbody != car.Body) return ObstacleKind.Vehicle;
            // Emergency wall brake: a static obstacle (building) close ahead
            // means we've left the road line — stop before wedging into it.
            return hit.rigidbody == null && hit.distance < settings.forwardBrakeDistance * 0.45f
                ? ObstacleKind.Wall
                : ObstacleKind.None;
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
