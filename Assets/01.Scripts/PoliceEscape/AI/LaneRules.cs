using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// The derived-lane traffic model shared by both AI drivers and both
    /// spawn managers. There is no per-lane graph — roads are one cell wide
    /// with a single undirected node — so a car's lane is DERIVED from its
    /// direction of travel: the steer target is the waypoint pushed to the
    /// right of the travel segment (right-hand traffic). A car that follows
    /// its steer targets is structurally incapable of driving in the wrong
    /// lane, which reduces "never go the wrong way" to two rules enforced
    /// elsewhere: never flip travel direction outside a dead end (the
    /// drivers' ExtendWander) and enter traffic oriented with a legal
    /// direction (the managers' spawns and the drivers' HardRecover).
    /// Direction indices are 0..3 = N,E,S,W, matching EdgeMaskUtility.
    /// </summary>
    public static class LaneRules
    {
        /// <summary>The grid direction nearest a flat heading (0..3 = N,E,S,W).</summary>
        public static int HeadingToDirection(Vector3 forward) =>
            Mathf.Abs(forward.x) >= Mathf.Abs(forward.z)
                ? (forward.x >= 0f ? 1 : 3)
                : (forward.z >= 0f ? 0 : 2);

        /// <summary>Dominant-axis direction of a flat segment; -1 when degenerate (under half a metre — fork seam twins can share a world centre).</summary>
        public static int SegmentDirection(Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            segment.y = 0f;
            return segment.sqrMagnitude <= 0.25f ? -1 : HeadingToDirection(segment);
        }

        /// <summary>The opposite direction — the one a lane-abiding car may never turn into outside a dead end.</summary>
        public static int ReverseOf(int direction) => (direction + 2) & 3;

        /// <summary>World forward of a direction index.</summary>
        public static Vector3 DirectionVector(int direction)
        {
            Vector2Int offset = EdgeMaskUtility.Offset(direction);
            return new Vector3(offset.x, 0f, offset.y);
        }

        /// <summary>Right-hand unit vector of a direction index — the side of the road centre a car travelling that way keeps to.</summary>
        public static Vector3 RightOf(int direction) => Vector3.Cross(Vector3.up, DirectionVector(direction));

        /// <summary>
        /// The connected direction closest to the car's own heading — how
        /// recovery re-enters traffic legally: a recovered car resumes the
        /// direction it was already travelling (or the nearest legal one)
        /// instead of a random socket that could point it into oncoming
        /// traffic.
        /// </summary>
        public static int NearestConnectedDirection(EdgeMask connections, Vector3 forward)
        {
            forward.y = 0f;
            int best = 0;
            float bestDot = float.MinValue;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((connections & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                float dot = Vector3.Dot(forward, DirectionVector(dir));
                if (dot <= bestDot) continue;
                bestDot = dot;
                best = dir;
            }
            return best;
        }

        /// <summary>
        /// The lane point beside a waypoint. Without a next waypoint this is
        /// the plain right-hand offset of the previous → current segment;
        /// with one it is a miter join — the intersection of the incoming and
        /// outgoing lane lines — so a corner arrival lands in the lane of the
        /// OUTGOING leg (right turns cut tight, left turns swing wide)
        /// instead of flipping sides mid-node. Segments are flattened first,
        /// so on ramps the offset stays perpendicular to the climb. A
        /// near-U-turn join falls back to the incoming offset so the miter
        /// denominator can never explode.
        /// </summary>
        public static Vector3 LaneTarget(Vector3 previous, Vector3 current, Vector3? next, float offset)
        {
            Vector3 incoming = current - previous;
            incoming.y = 0f;
            if (incoming.sqrMagnitude <= 0.25f) return current;
            Vector3 rIn = Vector3.Cross(Vector3.up, incoming.normalized);
            if (next.HasValue)
            {
                Vector3 outgoing = next.Value - current;
                outgoing.y = 0f;
                if (outgoing.sqrMagnitude > 0.25f)
                {
                    Vector3 rOut = Vector3.Cross(Vector3.up, outgoing.normalized);
                    float dot = Vector3.Dot(rIn, rOut);
                    if (dot > -0.8f) return current + (rIn + rOut) * (offset / (1f + dot));
                }
            }
            return current + rIn * offset;
        }
    }
}
