using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// GPS guidance to the ACTIVE level objective — the yellow twin of the
    /// map screen's marker guidance. While the current objective is Go To
    /// Target or Chase Car, it keeps a road route from the player to the
    /// objective's position and publishes it through <see cref="Route"/> for
    /// the Minimap to draw; every other objective kind (and a finished level)
    /// clears it. The route lives in a PRIVATE <see cref="MapRoute"/> (shared:
    /// false) so it can never claim <see cref="MapRoute.Current"/> — that slot
    /// belongs to the player's own marker, and the two lines may coexist.
    ///
    /// Within <see cref="MinimapSettings.objectiveGpsRange"/> of the target
    /// the GPS switches itself off (the target is in sight — the line is
    /// clutter), resuming with a small hysteresis margin so the boundary
    /// never flickers. Chase Car aims at a moving goal, so besides the map
    /// screen's off-route rule this also re-paths when the prey has dragged
    /// the destination far enough from where the route ends. Re-pathing is
    /// cooldown-bounded for the same reason the map screen's is — A* over the
    /// whole city is too heavy to spend on frames that reach the same answer.
    ///
    /// Spawned by the Minimap next to itself; no scene wiring.
    /// </summary>
    public class ObjectiveGps : MonoBehaviour
    {
        const float RebuildCooldown = 2f;
        const float TargetDriftRebuildMeters = 25f; // moving prey: re-path once the goal has moved this far off the route's end
        const float OffRouteDistance = 25f;
        const float OffRouteGraceSeconds = 2f;
        const float ResumeRangeFactor = 1.2f;       // hysteresis: suppressed at range, resumes at range × this
        const int MaxExpansions = 200_000;          // same A* budget as the map screen

        readonly MapRoute route = new(shared: false);
        readonly List<RoadNode> pathBuffer = new();
        readonly List<Vector2Int> routeCells = new();
        readonly List<Vector3> routePoints = new();
        readonly List<TrafficCarInput> escapees = new();

        LevelManager level;
        CityManager city;
        CarController player;
        float refreshTimer;
        float recalcTimer;
        float offRouteTimer;
        bool suppressed;

        /// <summary>Range within which the GPS disables, off the minimap's settings (15 m by default).</summary>
        public MinimapSettings settings;

        /// <summary>The objective route being followed, or null while no ObjectiveGps is alive.</summary>
        public static MapRoute Route { get; private set; }

        void OnEnable() => Route = route;

        void OnDisable()
        {
            route.Clear();
            Route = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (recalcTimer > 0f) recalcTimer -= dt;

            refreshTimer -= dt;
            if (refreshTimer <= 0f || player == null)
            {
                refreshTimer = 1f;
                if (level == null) level = FindFirstObjectByType<LevelManager>();
                if (city == null) city = FindAnyObjectByType<CityManager>();
                player = PatrolManager.FindPlayerCar();
            }

            if (player == null || !TryGetObjectiveGoal(out Vector3 goal))
            {
                Drop();
                suppressed = false;
                return;
            }

            // The disable range, with hysteresis on the way back out so the
            // line doesn't flicker while driving along the boundary.
            float range = settings != null ? settings.objectiveGpsRange : 15f;
            float distance = FlatDistance(player.transform.position, goal);
            if (distance <= (suppressed ? range * ResumeRangeFactor : range))
            {
                suppressed = true;
                Drop();
                return;
            }
            suppressed = false;

            if (!route.HasRoute)
            {
                if (recalcTimer <= 0f) Rebuild(goal);
                return;
            }

            route.Advance(player.transform.position);

            // A moving goal (Chase Car) outruns its own route; a parked one
            // (Go To) makes this a no-op since the route already ends there.
            if (FlatDistance(route.Destination, goal) > TargetDriftRebuildMeters && recalcTimer <= 0f)
            {
                Rebuild(goal);
                return;
            }

            // Off the line — same grace rule as the map screen's marker route:
            // swerves and corner cuts read as off-route for a moment, and
            // re-pathing on those would thrash.
            if (route.OffRouteMeters <= OffRouteDistance)
            {
                offRouteTimer = 0f;
                return;
            }
            offRouteTimer += dt;
            if (offRouteTimer >= OffRouteGraceSeconds && recalcTimer <= 0f) Rebuild(goal);
        }

        /// <summary>
        /// The world position the active objective points at: the registered
        /// TargetObject for Go To, the escaping car for Chase Car (by the
        /// objective's id first, any escapee as fallback). False for every
        /// other objective kind, an unregistered id, or a finished level.
        /// </summary>
        bool TryGetObjectiveGoal(out Vector3 goal)
        {
            goal = default;
            if (level == null || level.Level == null || level.Completed) return false;
            int index = level.CurrentIndex;
            if (index < 0 || index >= level.Level.Count) return false;

            LevelObjective objective = level.Level.objectives[index];
            switch (objective.type)
            {
                case ObjectiveType.GoToTarget:
                    if (!TargetObject.TryFind(objective.targetId, out TargetObject target)) return false;
                    goal = target.transform.position;
                    return true;

                case ObjectiveType.ChaseCar:
                    if (TrafficCarInput.TryFindEscaping(objective.targetId, out TrafficCarInput prey) && prey != null)
                    {
                        goal = prey.transform.position;
                        return true;
                    }
                    TrafficCarInput.GetEscaping(escapees);
                    foreach (TrafficCarInput escapee in escapees)
                    {
                        if (escapee == null) continue;
                        goal = escapee.transform.position;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        void Drop()
        {
            if (route.HasRoute) route.Clear();
            offRouteTimer = 0f;
        }

        /// <summary>A* from the car to the goal over the city graph — the same path the map screen's marker route takes.</summary>
        void Rebuild(Vector3 goal)
        {
            recalcTimer = RebuildCooldown;
            offRouteTimer = 0f;

            RoadGraph graph = city != null ? city.Graph : null;
            if (graph == null || graph.Count == 0)
            {
                route.Clear();
                return;
            }

            if (!TryGetNodeOn(graph, player.transform.position, out RoadNode start) ||
                !TryGetNodeOn(graph, goal, out RoadNode end) ||
                !graph.TryFindPath(start, end, pathBuffer, MaxExpansions))
            {
                route.Clear();
                return;
            }

            routeCells.Clear();
            routePoints.Clear();
            foreach (RoadNode node in pathBuffer)
            {
                routeCells.Add(node.Cell);
                routePoints.Add(graph.Center(node));
            }
            route.Set(routeCells, routePoints);
        }

        /// <summary>The node a world position sits on, falling back to the nearest — same rule as the map screen and the police.</summary>
        static bool TryGetNodeOn(RoadGraph graph, Vector3 position, out RoadNode node) =>
            graph.TryGetNodeAt(position, out node) || graph.TryGetNearestNode(position, out node);

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
