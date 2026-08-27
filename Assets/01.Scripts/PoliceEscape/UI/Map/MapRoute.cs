using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The route the player asked for — and how much of it is left: the cells
    /// it still runs through (what the map paints) and the world points along
    /// it (what the minimap projects).
    ///
    /// Two rules are baked in here. First, <b>the map screen and the minimap
    /// must not know about each other</b>. The minimap is spawned by
    /// CityManager and lives its whole life in the corner of the HUD; the map
    /// screen comes and goes with Tab. Hanging the route off a shared static
    /// here means the minimap reads <see cref="Current"/> without a reference
    /// to the map, and the route survives the map screen being closed — which
    /// is the entire point, since you close the map in order to drive it.
    ///
    /// Second, <b>a route only ever holds what is still to be driven</b>.
    /// <see cref="Advance"/> slides the head of the line onto the car and
    /// throws away everything behind it, so neither view has to work out which
    /// part of the path is history — they draw all of it and are correct. That
    /// also makes progress monotonic: the search window only looks forward, so
    /// a route that loops back near itself can never snap the car's progress
    /// onto the far leg.
    ///
    /// Cells and points are index-aligned (both come from the same A* node
    /// list), and trimming keeps them that way.
    /// </summary>
    public class MapRoute
    {
        // How far ahead Advance looks, in nodes (one node per city cell). Wide
        // enough that no plausible frame of driving outruns it, narrow enough
        // that a parallel leg of the same route is out of scope. Leaving the
        // window reads as off-route, and re-pathing is the right answer then.
        const int ProgressWindowNodes = 32;

        readonly List<Vector2Int> cells = new();
        readonly List<Vector3> points = new();
        readonly bool shared;

        /// <summary>
        /// A shared route (the default) publishes itself to <see cref="Current"/>
        /// on Set — the marker-route contract. Pass false for a private route
        /// (the objective GPS) that other views find through its own owner
        /// instead, so it can never hijack the marker slot.
        /// </summary>
        public MapRoute(bool shared = true) => this.shared = shared;

        /// <summary>The route currently being followed, or null when none is set.</summary>
        public static MapRoute Current { get; private set; }

        /// <summary>Cells the remaining route passes through, nearest first.</summary>
        public IReadOnlyList<Vector2Int> Cells => cells;

        /// <summary>
        /// World positions along the remaining route, nearest first.
        /// <c>Points[0]</c> is the car's own projection onto the line once
        /// <see cref="Advance"/> has run, so the drawn line starts at the car.
        /// </summary>
        public IReadOnlyList<Vector3> Points => points;

        public bool HasRoute => cells.Count > 0;

        /// <summary>Ground distance still to drive, in metres.</summary>
        public float RemainingMeters { get; private set; }

        /// <summary>
        /// How far the car sat from the line at the last <see cref="Advance"/>,
        /// in metres — what the map screen watches to decide it has to re-path.
        /// </summary>
        public float OffRouteMeters { get; private set; }

        /// <summary>The end of the route. Only meaningful while <see cref="HasRoute"/>.</summary>
        public Vector3 Destination => points.Count > 0 ? points[^1] : Vector3.zero;

        public void Set(List<Vector2Int> routeCells, List<Vector3> routePoints)
        {
            cells.Clear();
            points.Clear();
            cells.AddRange(routeCells);
            points.AddRange(routePoints);

            OffRouteMeters = 0f;
            RecomputeRemaining();
            if (shared) Current = this;
        }

        /// <summary>
        /// Move the car's progress along the route: find the closest point on
        /// the line ahead, drop everything behind it, and pull the head of the
        /// line onto it. Returns true when whole nodes were consumed — the map
        /// paints cells, so only that needs to trigger a repaint.
        /// </summary>
        public bool Advance(Vector3 position)
        {
            if (points.Count == 0)
            {
                OffRouteMeters = 0f;
                return false;
            }
            if (points.Count == 1)
            {
                // Nothing left but the destination: no segment to slide along,
                // so "off route" is simply how far away it is.
                OffRouteMeters = Flat(points[0] - position).magnitude;
                RemainingMeters = OffRouteMeters;
                return false;
            }

            int lastSegment = Mathf.Min(points.Count - 1, ProgressWindowNodes);
            int bestSegment = 0;
            Vector3 bestPoint = points[0];
            float bestSqr = float.MaxValue;

            for (int i = 0; i < lastSegment; i++)
            {
                Vector3 projected = ClosestPointOnSegment(points[i], points[i + 1], position);
                float sqr = Flat(projected - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                bestSegment = i;
                bestPoint = projected;
            }

            OffRouteMeters = Mathf.Sqrt(bestSqr);

            bool trimmed = bestSegment > 0;
            if (trimmed)
            {
                points.RemoveRange(0, bestSegment);
                // Never empty the cell list — the destination cell has to stay
                // painted right up until the marker itself is cleared.
                int dropped = Mathf.Min(bestSegment, cells.Count - 1);
                if (dropped > 0) cells.RemoveRange(0, dropped);
            }
            points[0] = bestPoint;
            RecomputeRemaining();
            return trimmed;
        }

        public void Clear()
        {
            cells.Clear();
            points.Clear();
            RemainingMeters = 0f;
            OffRouteMeters = 0f;
            if (Current == this) Current = null;
        }

        /// <summary>
        /// Forget any route globally. Domain reload is disabled in this
        /// project, so <see cref="Current"/> outlives a play session and has to
        /// be cleared explicitly rather than trusted to reset itself.
        /// </summary>
        public static void ClearCurrent() => Current = null;

        void RecomputeRemaining()
        {
            RemainingMeters = 0f;
            for (int i = 1; i < points.Count; i++)
                RemainingMeters += Flat(points[i] - points[i - 1]).magnitude;
        }

        // Routes are driven on the ground: heights come from decks and ramps
        // and would otherwise pad every distance the HUD prints.
        static Vector2 Flat(Vector3 delta) => new(delta.x, delta.z);

        static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 position)
        {
            Vector2 segment = Flat(b - a);
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr < 0.0001f) return a;
            float t = Mathf.Clamp01(Vector2.Dot(Flat(position - a), segment) / lengthSqr);
            return a + (b - a) * t;
        }
    }
}
