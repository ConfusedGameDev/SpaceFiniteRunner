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
        Vector2Int wanderFrom;
        float repathTimer, lostSightTimer, searchTimer, stuckTimer, reverseTimer, retargetTimer;
        float lastForwardSteer;

        public void Initialize(PursuitSettings pursuitSettings, CityManager cityManager)
        {
            settings = pursuitSettings;
            city = cityManager;
        }

        void Awake() => car = GetComponent<CarController>();

        void Update()
        {
            float dt = Time.deltaTime;
            if (settings == null || !EnsureCity())
            {
                Steer = 0f;
                Throttle = 0f;
                return;
            }

            RefreshPlayer(dt);
            bool seesPlayer = CanSeePlayer();
            if (seesPlayer) lastKnownPlayerPosition = player.transform.position;

            UpdateState(seesPlayer, dt);
            PlanRoute(seesPlayer, dt);
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
                    if (waypoints.Count == 0) ExtendWander();
                    break;

                case AiState.Patrol:
                    if (waypoints.Count < 2) ExtendWander();
                    break;
            }
        }

        // ------------------------------------------------------------- driving

        void Drive(float dt)
        {
            if (reverseTimer > 0f)
            {
                // Backing out of whatever we're wedged against, steering away
                // from the last forward direction; the cleared route forces a
                // fresh plan afterwards.
                reverseTimer -= dt;
                Throttle = -0.8f;
                Steer = -Mathf.Sign(lastForwardSteer) * 0.8f;
                return;
            }

            while (waypoints.Count > 0 && FlatDistance(transform.position, waypoints[0]) < settings.waypointReachDistance)
                waypoints.RemoveAt(0);

            if (waypoints.Count == 0)
            {
                Steer = 0f;
                Throttle = car.SpeedKmh > 5f ? -0.3f : 0f; // ease to a stop until the next plan
                return;
            }

            Vector3 local = transform.InverseTransformPoint(waypoints[0]);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float maxSteerAngle = car.config != null ? car.config.maxSteerAngle : 35f;
            Steer = Mathf.Clamp(angle / maxSteerAngle, -1f, 1f);
            lastForwardSteer = Steer;

            float cruise = State == AiState.Chase ? settings.chaseSpeedKmh : settings.patrolSpeedKmh;
            float desired = Mathf.Lerp(settings.cornerSpeedKmh, cruise, Mathf.Clamp01(1f - Mathf.Abs(angle) / 90f));
            if (ObstacleAhead()) desired = Mathf.Min(desired, settings.cornerSpeedKmh * 0.5f);
            Throttle = Mathf.Clamp((desired - car.SpeedKmh) * settings.throttleGain, -1f, 1f);

            bool wantsMotion = Mathf.Abs(Throttle) > 0.2f;
            stuckTimer = wantsMotion && car.SpeedKmh < 3f ? stuckTimer + dt : 0f;
            if (stuckTimer >= settings.stuckSeconds)
            {
                stuckTimer = 0f;
                reverseTimer = settings.reverseSeconds;
                waypoints.Clear();
            }
        }

        /// <summary>Another car dead ahead? Brake instead of shunting it — walls are the path planner's problem, so only rigidbodies count.</summary>
        bool ObstacleAhead()
        {
            Vector3 origin = transform.position + Vector3.up * 0.6f + transform.forward * 2.6f;
            if (!Physics.Raycast(origin, transform.forward, out RaycastHit hit,
                    settings.forwardBrakeDistance, ~0, QueryTriggerInteraction.Ignore))
                return false;
            return hit.rigidbody != null && hit.rigidbody != car.Body;
        }

        // ------------------------------------------------------------- routing

        void PathToPosition(Vector3 position)
        {
            RoadGraph graph = city.Graph;
            if (!TryGetCellOn(graph, transform.position, out Vector2Int start)) return;
            if (!TryGetCellOn(graph, position, out Vector2Int goal)) return;
            if (!graph.TryFindPath(start, goal, pathBuffer)) return;

            waypoints.Clear();
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
