using System.Collections.Generic;
using UnityEngine;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track.Features;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The lateral spans of road that something has to steer ROUND on a
    /// stretch of track: every built jump ramp (end ramps aside — nothing
    /// drives the run-up but the ship) and every laser gate, beams plus
    /// emitters. Reads the BUILT objects (<see cref="JumpRamp.Active"/>, the
    /// gates in <see cref="PickupRegistry"/>), so it answers for generated and
    /// saved tracks alike, but only inside the stream window — ask about road
    /// the generator has settled. Plain static queries, no state: the oncoming
    /// traffic steers by it and checks a spawn's whole path with it
    /// (OncomingTrafficPRD.md R3.1).
    /// </summary>
    public static class TrackObstacles
    {
        /// <summary>One blocked span: where along the track it starts and ends, and the lateral band it closes.</summary>
        public struct Obstacle
        {
            public float start, end;  // track distance, metres
            public float min, max;    // lateral, metres
        }

        /// <summary>
        /// Adds to <paramref name="results"/> every ramp and laser gate touching
        /// the track distances <paramref name="from"/>..<paramref name="to"/>,
        /// its lateral band widened by <paramref name="pad"/> each side.
        /// </summary>
        public static void Collect(float from, float to, float pad, List<Obstacle> results)
        {
            foreach (var ramp in JumpRamp.Active)
            {
                if (ramp == null || ramp.IsEndRamp) continue;
                if (ramp.StartDistance > to || ramp.EndDistance < from) continue;
                results.Add(new Obstacle
                {
                    start = ramp.StartDistance,
                    end = ramp.EndDistance,
                    min = ramp.Lateral - ramp.HalfWidth - pad,
                    max = ramp.Lateral + ramp.HalfWidth + pad,
                });
            }

            var pickups = PickupRegistry.All;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (!(pickups[i] is LaserGate gate) || !gate.Available) continue;
                float d = gate.TrackDistance, depth = gate.HalfDepth;
                if (d - depth > to || d + depth < from) continue;
                // A vertical beam stands on the road with no emitter beside it.
                float across = gate.TrackHalfExtents.x + (gate.Variant == LaserGateVariant.Vertical ? 0f : gate.EmitterReach) + pad;
                results.Add(new Obstacle
                {
                    start = d - depth,
                    end = d + depth,
                    min = gate.TrackLateral - across,
                    max = gate.TrackLateral + across,
                });
            }
        }

        /// <summary>
        /// The parts of <paramref name="min"/>..<paramref name="max"/> no
        /// obstacle in <paramref name="blocked"/> closes, as (x = from, y = to)
        /// intervals in increasing order, into <paramref name="free"/>
        /// (cleared first). Empty when nothing is left.
        /// </summary>
        public static void FreeIntervals(float min, float max, List<Obstacle> blocked, List<Vector2> free)
        {
            free.Clear();
            if (max < min) return;
            free.Add(new Vector2(min, max));
            foreach (var o in blocked)
            {
                for (int i = free.Count - 1; i >= 0; i--)
                {
                    Vector2 f = free[i];
                    if (o.max <= f.x || o.min >= f.y) continue;
                    free.RemoveAt(i);
                    if (o.max < f.y) free.Insert(i, new Vector2(o.max, f.y));
                    if (o.min > f.x) free.Insert(i, new Vector2(f.x, o.min));
                }
            }
        }

        /// <summary>The point of <paramref name="free"/> closest to <paramref name="lateral"/>; false when there is none.</summary>
        public static bool Nearest(List<Vector2> free, float lateral, out float nearest)
        {
            nearest = lateral;
            float best = float.PositiveInfinity;
            foreach (var f in free)
            {
                float x = Mathf.Clamp(lateral, f.x, f.y);
                float gap = Mathf.Abs(x - lateral);
                if (gap < best) { best = gap; nearest = x; }
            }
            return best < float.PositiveInfinity;
        }
    }
}
