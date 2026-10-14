using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Simulation
{
    /// <summary>
    /// The handling numbers an AI driver plans with — the subset of its
    /// definition the shared line rules read. A plain struct so the patrol
    /// (<c>PatrolDefinition</c>) and the race rivals (<c>RaceDefinition</c>)
    /// feed the very same rules from their own assets.
    /// </summary>
    public struct LineTuning
    {
        /// <summary>Lateral speed full steer settles at, m/s — what the planner counts on for a sideways trip.</summary>
        public float lateralSpeed;
        /// <summary>How far ahead a boost orb is considered, seconds of travel.</summary>
        public float orbLookaheadSeconds;
        /// <summary>How far ahead a ramp is planned for, seconds of travel.</summary>
        public float rampLookaheadSeconds;
        /// <summary>How far ahead a flat sweep is braked for, seconds of travel.</summary>
        public float curveLookaheadSeconds;
        /// <summary>Grip at rest on a flat sweep, m/s² (<see cref="BodyParams.gripBase"/>).</summary>
        public float gripBase;
        /// <summary>Extra grip per m/s (<see cref="BodyParams.gripPerSpeed"/>).</summary>
        public float gripPerSpeed;
        /// <summary>Deceleration at full brake, m/s².</summary>
        public float brakeDecel;
    }

    /// <summary>
    /// The line rules every AI driver on the runner track shares, split out of
    /// the patrol's driver (RaceModePRD R2.1) so the race rivals drive by the
    /// same judgement: <b>orbs</b> it can still reach pull the line toward
    /// themselves, a <b>ramp</b> in the line is rounded when the sideways trip
    /// fits in the time left and lined up and jumped otherwise, an <b>open
    /// edge</b> is kept a margin away, and a <b>flat sweep</b> is braked for at
    /// the speed the driver's own grip holds its tightest point at. Pure
    /// functions of the track and the body: no state, so any number of drivers
    /// can share them.
    /// </summary>
    public static class LineDriving
    {
        /// <summary>Metres of lateral error answered with full steer.</summary>
        public const float SteerReach = 4f;
        const float EdgeMargin = 6f;     // kept clear of an edge with no wall, metres
        const float RampClearance = 1.5f; // kept clear of a ramp's side, metres
        public const float CurveSafety = 0.9f;  // share of the grip-limit speed it aims for
        const float LateralUse = 0.8f;   // share of its top lateral speed the planner counts on
        const int CurveSamples = 16;

        /// <summary>Steer toward <paramref name="line"/>: full lock at <see cref="SteerReach"/> metres of error.</summary>
        public static float SteerFor(TrackBody body, float line) => Mathf.Clamp((line - body.Lateral) / SteerReach, -1f, 1f);

        /// <summary>
        /// The best boost orb the body can still get to, blended into
        /// <paramref name="line"/> by <paramref name="weight"/> (0 = ignore
        /// orbs). The score is the orb's worth against the sideways trip it
        /// costs. <paramref name="accept"/>, when given, may turn an orb down
        /// (a rival's appetite).
        /// </summary>
        public static float SeekOrb(TrackBody body, in LineTuning tuning, float weight, float line,
                                    System.Func<SpeedPad, bool> accept = null)
        {
            if (weight <= 0f) return line;
            float d = body.Distance;
            float speed = Mathf.Max(body.ForwardSpeed, 1f);
            float reach = speed * tuning.orbLookaheadSeconds;
            float bestScore = 0f;
            float bestLateral = line;
            var pickups = PickupRegistry.All;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (pickups[i] is not SpeedPad pad || !pad.Available || !pad.IsBoostOrb) continue;
                float ahead = pad.TrackDistance - d;
                if (ahead <= 0f || ahead > reach || pad.TrackHeight > 1f) continue;

                float across = Mathf.Abs(pad.TrackLateral - body.Lateral);
                if (across > tuning.lateralSpeed * LateralUse * (ahead / speed) + pad.TrackHalfExtents.x) continue; // out of reach in the time left
                if (accept != null && !accept(pad)) continue;

                float score = pad.SpeedDelta / (1f + across);
                if (score <= bestScore) continue;
                bestScore = score;
                bestLateral = pad.TrackLateral;
            }
            return bestScore > 0f ? Mathf.Lerp(line, bestLateral, weight) : line;
        }

        /// <summary>
        /// A ramp in the line: round it if the sideways travel fits in the
        /// time left, else straight up the middle (the body jumps it by
        /// itself). Beside a ramp the line is held clear of its side wall. End
        /// ramps are never planned round — the end of the track is not an
        /// obstacle.
        /// </summary>
        public static float PlanRamps(TrackBody body, in LineTuning tuning, float bandMin, float bandMax, float line)
        {
            float d = body.Distance;
            float speed = Mathf.Max(body.ForwardSpeed, 1f);
            float reach = speed * tuning.rampLookaheadSeconds;
            foreach (var ramp in JumpRamp.Active)
            {
                if (ramp == null || ramp.Definition == null) continue;
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
                bool fits = !bodyBlocked || travel <= tuning.lateralSpeed * LateralUse * seconds;
                return fits ? around : ramp.Lateral;
            }
            return line;
        }

        /// <summary>An edge with no wall — here or where it will be in half a second — pulls the line a margin inside the road.</summary>
        public static float KeepOffOpenEdges(TrackManager track, float d, float speed, float bandMin, float bandMax, float line,
                                             float margin = EdgeMargin)
        {
            float soon = d + speed * 0.5f;
            if (track.IsEdgeOpen(d, -1) || track.IsEdgeOpen(soon, -1)) line = Mathf.Max(line, bandMin + margin);
            if (track.IsEdgeOpen(d, 1) || track.IsEdgeOpen(soon, 1)) line = Mathf.Min(line, bandMax - margin);
            return Mathf.Clamp(line, bandMin, bandMax);
        }

        /// <summary>
        /// The next flat sweep inside the look-ahead: the speed the driver's
        /// grip holds its tightest point at (× <paramref name="safety"/>), and
        /// the brake it takes to be there in time. <paramref name="speedCap"/>
        /// comes back as that speed while it matters, else
        /// <see cref="float.MaxValue"/>.
        /// </summary>
        public static float BrakeForSweep(TrackBody body, TrackManager track, in LineTuning tuning, out float speedCap,
                                          float safety = CurveSafety)
        {
            speedCap = float.MaxValue;
            float d = body.Distance;
            float speed = Mathf.Max(body.ForwardSpeed, 1f);
            var sweep = track.FlatSweepWithin(d, speed * tuning.curveLookaheadSeconds);
            if (sweep == null) return 0f;

            float curvature = TightestAhead(track, sweep, d);
            if (curvature < 1e-5f) return 0f;

            // v²κ = g0 + g1·v  →  the positive root.
            float g0 = tuning.gripBase, g1 = tuning.gripPerSpeed;
            float safe = (g1 + Mathf.Sqrt(g1 * g1 + 4f * curvature * g0)) / (2f * curvature) * safety;
            if (speed <= safe) { if (d >= sweep.Start) speedCap = safe; return 0f; }

            float room = sweep.Start - d;
            if (room <= 0f)
            {
                speedCap = safe;
                return Mathf.Clamp01((speed - safe) / 20f);
            }

            float needed = (speed * speed - safe * safe) / (2f * room);
            if (needed < 0.3f * tuning.brakeDecel) return 0f; // still time: keep going
            speedCap = safe;
            return Mathf.Clamp01(needed / Mathf.Max(tuning.brakeDecel, 0.01f));
        }

        // ---- the flat sweep's curvature profile, sampled once per sweep ----
        //
        // A curvature sample is three spline pose lookups (~0.02 ms), and the
        // brake used to take nine of them on every substep of every driver
        // with a sweep in its look-ahead — 0.3 ms a call, which is most of a
        // race's tick with five rivals. The road under a sweep never changes
        // once laid (only its End grows while an endless track streams), so
        // each sweep is sampled ONCE into a suffix-max table: the tightest
        // point from any sample to the sweep's end. The cache is dropped when
        // the track is rebuilt (its Revision) and a sweep is resampled if its
        // End has moved.
        sealed class SweepProfile
        {
            public float start, end, step;
            public float[] suffixMax; // suffixMax[i] = the tightest |curvature| from sample i to the end
        }

        static readonly System.Collections.Generic.Dictionary<TrackManager.FlatSweep, SweepProfile> Profiles = new();
        static TrackManager profiledTrack;
        static int profiledRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() // domain reload is off
        {
            Profiles.Clear();
            profiledTrack = null;
            profiledRevision = -1;
        }

        /// <summary>The tightest |curvature| of <paramref name="sweep"/> from <paramref name="d"/> (or its start, if before it) to its end.</summary>
        static float TightestAhead(TrackManager track, TrackManager.FlatSweep sweep, float d)
        {
            if (track != profiledTrack || track.Revision != profiledRevision)
            {
                Profiles.Clear();
                profiledTrack = track;
                profiledRevision = track.Revision;
            }
            if (!Profiles.TryGetValue(sweep, out SweepProfile profile) || !Mathf.Approximately(profile.end, sweep.End))
            {
                profile = Sample(track, sweep);
                Profiles[sweep] = profile;
            }
            // The sample at or just behind d onward: inside the sweep only what is left of it counts.
            int i = d <= profile.start ? 0 : Mathf.Clamp(Mathf.FloorToInt((d - profile.start) / profile.step), 0, profile.suffixMax.Length - 1);
            return profile.suffixMax[i];
        }

        static SweepProfile Sample(TrackManager track, TrackManager.FlatSweep sweep)
        {
            float length = Mathf.Max(0f, sweep.End - sweep.Start);
            float step = Mathf.Max(1f, length / CurveSamples);
            int count = Mathf.FloorToInt(length / step) + 1;
            var suffix = new float[count];
            for (int i = 0; i < count; i++)
                suffix[i] = Mathf.Abs(track.GetCurvatureAtDistance(sweep.Start + i * step));
            for (int i = count - 2; i >= 0; i--)
                suffix[i] = Mathf.Max(suffix[i], suffix[i + 1]);
            return new SweepProfile { start = sweep.Start, end = sweep.End, step = step, suffixMax = suffix };
        }
    }
}
