using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The route the player asked for: the cells it runs through (what the map
    /// paints) and the world points along it (what the minimap projects).
    ///
    /// The rule this enforces: <b>the map screen and the minimap must not know
    /// about each other</b>. The minimap is spawned by CityManager and lives
    /// its whole life in the corner of the HUD; the map screen comes and goes
    /// with Tab. Hanging the route off a shared static here means the minimap
    /// reads <see cref="Current"/> without a reference to the map, and the
    /// route survives the map screen being closed — which is the entire point,
    /// since you close the map in order to drive the route.
    ///
    /// Populated in M4; the map and minimap already read it.
    /// </summary>
    public class MapRoute
    {
        readonly List<Vector2Int> cells = new();
        readonly List<Vector3> points = new();

        /// <summary>The route currently being followed, or null when none is set.</summary>
        public static MapRoute Current { get; private set; }

        /// <summary>Cells the route passes through, start first.</summary>
        public IReadOnlyList<Vector2Int> Cells => cells;

        /// <summary>World positions along the route, start first.</summary>
        public IReadOnlyList<Vector3> Points => points;

        public bool HasRoute => cells.Count > 0;

        /// <summary>Total ground distance along the route, in metres.</summary>
        public float LengthMeters { get; private set; }

        public void Set(List<Vector2Int> routeCells, List<Vector3> routePoints)
        {
            cells.Clear();
            points.Clear();
            cells.AddRange(routeCells);
            points.AddRange(routePoints);

            LengthMeters = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a = points[i - 1];
                Vector3 b = points[i];
                LengthMeters += new Vector2(b.x - a.x, b.z - a.z).magnitude;
            }
            Current = this;
        }

        public void Clear()
        {
            cells.Clear();
            points.Clear();
            LengthMeters = 0f;
            if (Current == this) Current = null;
        }

        /// <summary>
        /// Forget any route globally. Domain reload is disabled in this
        /// project, so <see cref="Current"/> outlives a play session and has to
        /// be cleared explicitly rather than trusted to reset itself.
        /// </summary>
        public static void ClearCurrent() => Current = null;
    }
}
