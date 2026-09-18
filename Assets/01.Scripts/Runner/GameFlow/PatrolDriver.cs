using UnityEngine;

using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The patrol's driver: what the player's hands are to the ship. Every
    /// tick it reads the road ahead and answers with the same
    /// <see cref="BodyControls"/> the ship's input produces, so the patrol is
    /// held to the very same physics — it can slide, hit a ramp's side and
    /// fall off an open edge — and drives accordingly:
    /// <list type="bullet">
    /// <item><b>Chase</b>: steers for the ship's lateral.</item>
    /// <item><b>Orbs</b>: a boost orb it can still reach inside its look-ahead
    /// pulls the line toward itself (<see cref="PatrolDefinition.orbSeekWeight"/>),
    /// less and less as the gap closes — up close it wants the ship.</item>
    /// <item><b>Ramps</b>: a ramp in its line is steered round when the
    /// sideways travel still fits in the time left; otherwise it commits,
    /// lines up with the middle and jumps it like the ship would. It never
    /// clips the side on purpose.</item>
    /// <item><b>Open edges</b>: the line is kept a margin inside any edge
    /// with no wall.</item>
    /// <item><b>Flat sweeps</b>: it works out the speed its own grip holds
    /// the sweep at (v²κ = gripBase + gripPerSpeed·v) and brakes to arrive at
    /// it — which is why a patrol fall is rare (a mistimed ramp, mostly).</item>
    /// </list>
    /// Plain C#, stateless between ticks: every decision is re-derived from
    /// the track, so a redeploy or a hold needs no reset.
    /// </summary>
    public sealed class PatrolDriver
    {
        const float SteerReach = 4f;     // metres of lateral error answered with full steer
        const float EdgeMargin = 6f;     // kept clear of an edge with no wall, metres
        const float RampClearance = 1.5f; // kept clear of a ramp's side, metres
        const float CurveSafety = 0.9f;  // share of the grip-limit speed it aims for
        const float LateralUse = 0.8f;   // share of its top lateral speed the planner counts on
        const int CurveSamples = 8;

        /// <summary>
        /// One tick of driving. <paramref name="speedCap"/> comes back as the
        /// most the patrol should be doing right now (a flat sweep's safe
        /// speed), or <see cref="float.MaxValue"/> when nothing limits it.
        /// </summary>
        public BodyControls Drive(TrackBody body, TrackBody ship, TrackManager track, PatrolDefinition def,
                                  float gap, out float speedCap)
        {
            float d = body.Distance;
            float speed = Mathf.Max(body.ForwardSpeed, 1f);
            track.GetLateralBand(d, out float bandMin, out float bandMax);

            // Round a full tube the ship's lateral may be whole turns away:
            // chase the nearest equivalent.
            float line = ship.Lateral;
            if (track.SectionAt(d) is TubeSection { Unbounded: true } tube)
                line += Mathf.Round((body.Lateral - line) / tube.Circumference) * tube.Circumference;

            line = SeekOrb(body, def, d, speed, gap, line);
            line = PlanRamps(body, def, d, speed, bandMin, bandMax, line);
            line = KeepOffOpenEdges(track, d, speed, bandMin, bandMax, line);

            var controls = new BodyControls
            {
                steer = Mathf.Clamp((line - body.Lateral) / SteerReach, -1f, 1f),
                throttle = 1f,
                brake = BrakeForSweep(body, track, def, d, speed, out speedCap),
            };
            return controls;
        }

        // The best boost orb it can still get to, blended into the line. The
        // score is the orb's worth against the sideways trip it costs.
        static float SeekOrb(TrackBody body, PatrolDefinition def, float d, float speed, float gap, float line)
        {
            float weight = def.orbSeekWeight * Mathf.Clamp01(gap / Mathf.Max(def.warnDistance, 1f));
            if (weight <= 0f) return line;

            float reach = speed * def.orbLookaheadSeconds;
            float bestScore = 0f;
            float bestLateral = line;
            var pickups = PickupRegistry.All;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (pickups[i] is not SpeedPad pad || !pad.Available || !pad.IsBoostOrb) continue;
                float ahead = pad.TrackDistance - d;
                if (ahead <= 0f || ahead > reach || pad.TrackHeight > 1f) continue;

                float across = Mathf.Abs(pad.TrackLateral - body.Lateral);
                if (across > def.lateralSpeed * LateralUse * (ahead / speed) + pad.TrackHalfExtents.x) continue; // out of reach in the time left

                float score = pad.SpeedDelta / (1f + across);
                if (score <= bestScore) continue;
                bestScore = score;
                bestLateral = pad.TrackLateral;
            }
            return bestScore > 0f ? Mathf.Lerp(line, bestLateral, weight) : line;
        }

        // A ramp in the line: round it if the sideways travel fits in the time
        // left, else straight up the middle (the body jumps it by itself).
        // Beside a ramp the line is held clear of its side wall.
        static float PlanRamps(TrackBody body, PatrolDefinition def, float d, float speed,
                               float bandMin, float bandMax, float line)
        {
            float reach = speed * def.rampLookaheadSeconds;
            foreach (var ramp in JumpRamp.Active)
            {
                if (ramp == null || ramp.Definition == null) continue;
                // The end of the track is not an obstacle to plan round: there
                // is nothing beyond it, and the end takes the patrol either way.
                if (ramp.IsEndRamp) continue;
                if (ramp.EndDistance <= d || ramp.StartDistance - d > reach) continue;
                if (body.Ramp == ramp) return ramp.Lateral; // committed: the rails hold it anyway

                float clear = ramp.HalfWidth + RampClearance; // outside this the ramp is just scenery
                bool lineBlocked = Mathf.Abs(line - ramp.Lateral) < clear;
                bool bodyBlocked = Mathf.Abs(body.Lateral - ramp.Lateral) < clear;
                if (!lineBlocked && !bodyBlocked) continue;

                // The way round: the side the body is already on, unless the
                // road has no room there.
                float side = body.Lateral >= ramp.Lateral ? 1f : -1f;
                float around = ramp.Lateral + side * clear;
                if (around > bandMax - 1f || around < bandMin + 1f)
                {
                    side = -side;
                    around = ramp.Lateral + side * clear;
                }

                float seconds = Mathf.Max(0f, ramp.StartDistance - d) / speed;
                float travel = Mathf.Abs(around - body.Lateral);
                bool fits = !bodyBlocked || travel <= def.lateralSpeed * LateralUse * seconds;
                return fits ? around : ramp.Lateral;
            }
            return line;
        }

        // An edge with no wall — here or where it will be in half a second —
        // pulls the line a margin inside the road.
        static float KeepOffOpenEdges(TrackManager track, float d, float speed, float bandMin, float bandMax, float line)
        {
            float soon = d + speed * 0.5f;
            if (track.IsEdgeOpen(d, -1) || track.IsEdgeOpen(soon, -1)) line = Mathf.Max(line, bandMin + EdgeMargin);
            if (track.IsEdgeOpen(d, 1) || track.IsEdgeOpen(soon, 1)) line = Mathf.Min(line, bandMax - EdgeMargin);
            return Mathf.Clamp(line, bandMin, bandMax);
        }

        // The next flat sweep inside the look-ahead: the speed the patrol's
        // grip holds its tightest point at, and the brake it takes to be
        // there in time.
        static float BrakeForSweep(TrackBody body, TrackManager track, PatrolDefinition def, float d, float speed, out float speedCap)
        {
            speedCap = float.MaxValue;
            var sweep = track.FlatSweepWithin(d, speed * def.curveLookaheadSeconds);
            if (sweep == null) return 0f;

            float from = Mathf.Max(d, sweep.Start);
            float step = Mathf.Max(1f, (sweep.End - from) / CurveSamples);
            float curvature = 0f;
            for (float s = from; s <= sweep.End; s += step)
                curvature = Mathf.Max(curvature, Mathf.Abs(track.GetCurvatureAtDistance(s)));
            if (curvature < 1e-5f) return 0f;

            // v²κ = g0 + g1·v  →  the positive root.
            float g0 = def.gripBase, g1 = def.gripPerSpeed;
            float safe = (g1 + Mathf.Sqrt(g1 * g1 + 4f * curvature * g0)) / (2f * curvature) * CurveSafety;
            if (speed <= safe) { if (d >= sweep.Start) speedCap = safe; return 0f; }

            float room = sweep.Start - d;
            if (room <= 0f)
            {
                speedCap = safe;
                return Mathf.Clamp01((speed - safe) / 20f);
            }

            float needed = (speed * speed - safe * safe) / (2f * room);
            if (needed < 0.3f * def.brakeDecel) return 0f; // still time: keep chasing
            speedCap = safe;
            return Mathf.Clamp01(needed / Mathf.Max(def.brakeDecel, 0.01f));
        }
    }
}
