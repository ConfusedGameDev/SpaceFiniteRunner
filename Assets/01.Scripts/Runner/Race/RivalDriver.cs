using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Race
{
    /// <summary>
    /// A rival's hands (RaceModePRD R2): every tick it reads the road ahead and
    /// answers with the same <see cref="BodyControls"/> the ship's input
    /// produces, so a rival is held to the ship's physics. It shares the
    /// patrol's line judgement (<see cref="LineDriving"/>: orbs, ramps, open
    /// edges, the flat-sweep brake) and drops the chase: instead of the ship's
    /// lateral it holds a <b>racing line</b> — its grid lane, wandering by its
    /// aggression — steers round laser gates, and on the final run-up lines up
    /// with the nearest end ramp. Orbs it can reach are taken by appetite,
    /// rolled once per orb by a hash (no state, no allocation). Plain C#, one
    /// per rival; it only remembers the rival's lane and seed.
    /// </summary>
    public sealed class RivalDriver
    {
        const float GateClearance = 1.5f; // kept clear of a beam's span, metres beyond the hull (plus RaceDefinition.gateMargin)
        const float WanderWavelength = 900f; // metres of road per swing of the line

        readonly List<TrackObstacles.Obstacle> gates = new();
        readonly List<Vector2> free = new();
        readonly List<TrackObstacles.Obstacle> single = new(); // the next gate alone
        System.Func<SpeedPad, bool> acceptOrb;
        RivalEntry entry;
        int seed;

        /// <summary>The lane the rival started in: the centre its line wanders round.</summary>
        public float HomeLateral { get; set; }

        /// <summary>The line it steered for on the last tick (debug readout).</summary>
        public float LastLine { get; private set; }

        /// <summary>Rolled per run by the system: salts the mistake rolls so a rival misjudges different hazards each race.</summary>
        public int RunSalt { get; set; }

        /// <summary>The hazard it is misjudging right now, for the debug readout: "" when none.</summary>
        public string Mistake { get; private set; } = string.Empty;

        public void Configure(RivalEntry entry, int seed)
        {
            this.entry = entry;
            this.seed = seed;
            acceptOrb = AcceptOrb;
        }

        /// <summary>
        /// One tick of driving. <paramref name="speedCap"/> comes back as the
        /// most the rival should be doing right now (a flat sweep's safe
        /// speed), or <see cref="float.MaxValue"/>. <paramref name="endRamps"/>
        /// are the end ramps' laterals (null on an endless track).
        /// </summary>
        public BodyControls Drive(TrackBody body, TrackManager track, RaceDefinition def, float[] endRamps,
                                  in ShipSpot ship, IReadOnlyList<TrackObstacles.Obstacle> knownGates, out float speedCap)
        {
            float d = body.Distance;
            float speed = Mathf.Max(body.ForwardSpeed, 1f);
            track.GetLateralBand(d, out float bandMin, out float bandMax);
            var tuning = new LineTuning
            {
                lateralSpeed = def.lateralSpeed,
                orbLookaheadSeconds = def.orbLookaheadSeconds,
                rampLookaheadSeconds = def.hazardLookaheadSeconds,
                curveLookaheadSeconds = def.curveLookaheadSeconds,
                gripBase = def.gripBase,
                gripPerSpeed = def.gripPerSpeed,
                brakeDecel = def.brakeDecel,
            };

            float line;
            bool runUp = track.HasEnd && track.EndZoneStart >= 0f && d >= track.EndZoneStart;
            Mistake = string.Empty;
            if (runUp && endRamps != null && endRamps.Length > 0)
            {
                // The final run-up: the nearest end ramp, and nothing else
                // matters — unless it misjudges the end and commits to a gap.
                line = Misjudges(EndHazardKey) && endRamps.Length > 1 ? NearestGap(endRamps, body.Lateral) : NearestRamp(endRamps, body.Lateral);
                if (Misjudges(EndHazardKey) && endRamps.Length > 1) Mistake = "gap";
            }
            else
            {
                // Approaching the run-up, the racing line becomes the
                // nearest end ramp: the run-up is ~1 s at race speed, too
                // short to line up from across the road. Everything below
                // (gates, edges, the ship) still applies until the run-up.
                bool lineUp = track.HasEnd && track.EndZoneStart >= 0f && endRamps != null && endRamps.Length > 0
                              && d >= track.EndZoneStart - speed * def.hazardLookaheadSeconds;
                line = lineUp ? NearestRamp(endRamps, body.Lateral) : RacingLine(d, bandMin, bandMax);
                if (track.SectionAt(d) is TubeSection { Unbounded: true } tube)
                    line += Mathf.Round((body.Lateral - line) / tube.Circumference) * tube.Circumference;
                line = LineDriving.SeekOrb(body, tuning, entry != null && entry.orbAppetite > 0f ? 1f : 0f, line, acceptOrb);
                line = LineDriving.PlanRamps(body, tuning, bandMin, bandMax, line);
                line = PassShip(body, def, bandMin, bandMax, line, ship);
                line = LineDriving.KeepOffOpenEdges(track, d, speed, bandMin, bandMax, line,
                                                    (entry != null ? entry.halfWidth : 4.5f) + 2f);
                line = ShaveEdge(track, def, d, speed, bandMin, bandMax, line);
                // Gates last: a beam is a wreck, so nothing after this may
                // pull the line back into one — not even a misjudged edge,
                // whose run for the edge is held off until the beam is behind.
                line = AvoidGates(body, def, speed, bandMin, bandMax, line, knownGates);
            }
            LastLine = line;

            // A flat sweep it misjudges: it arrives over its grip limit and slides.
            float safety = LineDriving.CurveSafety;
            var sweep = track.FlatSweepWithin(d, speed * tuning.curveLookaheadSeconds);
            if (sweep != null && Misjudges(SweepHazardKey(sweep.Start)))
            {
                safety *= def.mistakeOverspeed;
                Mistake = "sweep";
            }

            return new BodyControls
            {
                steer = LineDriving.SteerFor(body, line),
                throttle = 1f,
                brake = LineDriving.BrakeForSweep(body, track, tuning, out speedCap, safety),
            };
        }

        // ---- mistakes (R2.3) ----
        //
        // One roll of the rival's mistakeRate per HAZARD — a flat sweep, a
        // stretch of open-edged road, the end — hashed from the hazard, the
        // rival and the run, so the same hazard gets the same answer every
        // tick with nothing stored. Only a rival in full sim drives, so a
        // rival can only wreck itself round the ship, where it can be seen;
        // a ghost never crashes (D10).

        const int EndHazardKey = -1;
        static int SweepHazardKey(float start) => 0x5000000 ^ Mathf.RoundToInt(start);
        int EdgeHazardKey(float d, RaceDefinition def) => 0x2000000 ^ Mathf.FloorToInt(d / Mathf.Max(def.edgeHazardLength, 1f));

        bool Misjudges(int hazard)
        {
            float rate = entry != null ? entry.mistakeRate : 0f;
            if (rate <= 0f) return false;
            uint h = (uint)(hazard * 83492791) ^ (uint)(seed * 19349663) ^ (uint)(RunSalt * 73856093);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffff) / 65535f < rate;
        }

        // An open edge it misjudges: it holds its line past the edge on the
        // side it is nearer, and after the edge's grace it falls (a DNF).
        float ShaveEdge(TrackManager track, RaceDefinition def, float d, float speed, float bandMin, float bandMax, float line)
        {
            float soon = d + speed * 0.5f;
            bool left = track.IsEdgeOpen(d, -1) || track.IsEdgeOpen(soon, -1);
            bool right = track.IsEdgeOpen(d, 1) || track.IsEdgeOpen(soon, 1);
            if (!left && !right) return line;
            if (!Misjudges(EdgeHazardKey(d, def))) return line;
            Mistake = "edge";
            bool goLeft = left && (!right || line - bandMin < bandMax - line);
            return goLeft ? bandMin - def.mistakeEdgeShave : bandMax + def.mistakeEdgeShave;
        }

        static float NearestRamp(float[] ramps, float lateral)
        {
            float best = ramps[0];
            foreach (float ramp in ramps)
                if (Mathf.Abs(ramp - lateral) < Mathf.Abs(best - lateral)) best = ramp;
            return best;
        }

        // The middle of the gap between two neighbouring end ramps nearest the lateral.
        static float NearestGap(float[] ramps, float lateral)
        {
            float best = float.NaN;
            for (int i = 0; i < ramps.Length; i++)
                for (int j = 0; j < ramps.Length; j++)
                {
                    if (ramps[j] <= ramps[i]) continue;
                    bool neighbours = true;
                    foreach (float k in ramps) if (k > ramps[i] && k < ramps[j]) { neighbours = false; break; }
                    if (!neighbours) continue;
                    float gap = (ramps[i] + ramps[j]) * 0.5f;
                    if (float.IsNaN(best) || Mathf.Abs(gap - lateral) < Mathf.Abs(best - lateral)) best = gap;
                }
            return float.IsNaN(best) ? NearestRamp(ramps, lateral) : best;
        }

        // Its lane, wandering across the road by its aggression: a slow swing
        // keyed on distance (and its seed), so the pack spreads and crosses
        // without any shared state.
        float RacingLine(float d, float bandMin, float bandMax)
        {
            float aggression = entry != null ? entry.aggression : 0f;
            float halfBand = (bandMax - bandMin) * 0.5f;
            float wander = (Mathf.PerlinNoise(d / WanderWavelength + seed * 0.137f, seed * 0.071f) - 0.5f) * 2f;
            float line = HomeLateral + wander * aggression * halfBand * 0.8f;
            float margin = entry != null ? entry.halfWidth + 1f : 5f;
            return Mathf.Clamp(line, bandMin + margin, bandMax - margin);
        }

        // A laser gate whose beam covers the line within the look-ahead: the
        // nearest point of road it leaves free, hull and margin allowed for.
        //
        // The gates come from the run's DECIDED placements (knownGates, built
        // by the system), not from the built ones: a rival ahead of the ship
        // drives at the edge of the built road, and a gate is only built as
        // the ship nears it — read off the registry, a gate appeared a few
        // metres in front of a fast rival with no time to steer, and the
        // field was wiped out by beams. The registry is the fallback for a
        // run with no generator.
        float AvoidGates(TrackBody body, RaceDefinition def, float speed, float bandMin, float bandMax, float line,
                         IReadOnlyList<TrackObstacles.Obstacle> knownGates)
        {
            float d = body.Distance;
            float halfWidth = entry != null ? entry.halfWidth : 4.5f;
            float pad = halfWidth + GateClearance + def.gateMargin;
            float reach = d + speed * def.hazardLookaheadSeconds;
            gates.Clear();
            if (knownGates != null) CollectKnownGates(knownGates, d, reach, pad);
            else CollectBuiltGates(def, d, reach, pad);
            if (gates.Count == 0) return line;

            // The NEXT gate decides the side: the rival passes it on the side
            // it can reach without crossing it (the free road nearest where it
            // IS — never nearest the line, which passing the ship or the
            // wander may have moved across a beam a moment before the gate).
            // Within that side it prefers a lane that also clears the gates
            // after it. Planning one lateral for every gate in the look-ahead
            // sent a rival across the first beam to reach a gap that cleared
            // the second.
            var next = gates[0];
            foreach (var o in gates) if (o.start < next.start) next = o;

            float lateral = body.Lateral;
            float roadMin = bandMin + halfWidth + 1f, roadMax = bandMax - halfWidth - 1f;
            single.Clear();
            single.Add(next);
            TrackObstacles.FreeIntervals(roadMin, roadMax, single, free);
            if (!TrackObstacles.Nearest(free, lateral, out float side)) return line;
            Vector2 lane = new(side, side);
            foreach (var f in free) if (side >= f.x && side <= f.y) { lane = f; break; }

            // Blocked: the line is not on that side, or the way from here to
            // it crosses a beam's zone.
            float pathMin = Mathf.Min(lateral, line), pathMax = Mathf.Max(lateral, line);
            bool blocked = line < lane.x || line > lane.y;
            foreach (var o in gates)
                if (pathMax > o.min && pathMin < o.max) { blocked = true; break; }
            if (!blocked) return line;

            // The aim: the line, as far as the side allows — then the part of
            // the side that also clears the later gates, when any of it does.
            float aim = Mathf.Clamp(line, lane.x, lane.y);
            TrackObstacles.FreeIntervals(lane.x, lane.y, gates, free);
            if (TrackObstacles.Nearest(free, aim, out float clear)) return Inset(clear, def.gateMargin);
            free.Clear();
            free.Add(lane);
            return Inset(aim, def.gateMargin);
        }

        // A point inside its free interval rather than on the edge of the
        // beam's zone: the steering closes on its target asymptotically, so
        // aiming AT the edge arrives still inside it.
        float Inset(float point, float margin)
        {
            margin = Mathf.Max(0f, margin);
            foreach (var f in free)
            {
                if (point < f.x - 0.01f || point > f.y + 0.01f) continue;
                if (f.y - f.x <= 2f * margin) return (f.x + f.y) * 0.5f;
                return Mathf.Clamp(point, f.x + margin, f.y - margin);
            }
            return point;
        }

        void CollectKnownGates(IReadOnlyList<TrackObstacles.Obstacle> known, float d, float reach, float pad)
        {
            // Sorted by start: the first that can still reach d, then on to the look-ahead.
            int lo = 0, hi = known.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (known[mid].start < d - 100f) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < known.Count && known[i].start <= reach; i++)
            {
                var o = known[i];
                if (o.end < d) continue;
                gates.Add(new TrackObstacles.Obstacle { start = o.start, end = o.end, min = o.min - pad, max = o.max + pad });
            }
        }

        void CollectBuiltGates(RaceDefinition def, float d, float reach, float pad)
        {
            var pickups = PickupRegistry.All;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (pickups[i] is not LaserGate gate || !gate.Available) continue;
                float at = gate.TrackDistance, depth = gate.HalfDepth;
                if (at + depth < d || at - depth > reach) continue;
                float across = gate.TrackHalfExtents.x + (gate.Variant == LaserGateVariant.Vertical ? 0f : gate.EmitterReach) + pad;
                gates.Add(new TrackObstacles.Obstacle
                {
                    start = at - depth,
                    end = at + depth,
                    min = gate.TrackLateral - across,
                    max = gate.TrackLateral + across,
                });
            }
        }

        // The ship in its line ahead and being caught (or alongside): a line
        // past it, on the side the rival is already on unless that side has
        // no road. Without it a faster rival drives straight up the ship's
        // exhaust and the contact rule has to shunt it every tick.
        float PassShip(TrackBody body, RaceDefinition def, float bandMin, float bandMax, float line, in ShipSpot ship)
        {
            if (!ship.valid) return line;
            float halfLength = entry != null ? entry.halfLength : 9.5f;
            float halfWidth = entry != null ? entry.halfWidth : 4.5f;
            float reach = halfLength + def.shipHalfLength;
            float gap = ship.distance - body.Distance; // positive: the ship is ahead
            if (gap < -reach) return line;             // already past it
            float closing = body.ForwardSpeed - ship.speed;
            if (gap > reach && (closing <= 0f || gap > reach + closing * def.passLookaheadSeconds)) return line;

            float clear = halfWidth + def.shipHalfWidth + 2f;
            if (Mathf.Abs(line - ship.lateral) >= clear) return line;
            float margin = halfWidth + 1f;
            float side = body.Lateral >= ship.lateral ? 1f : -1f;
            float pass = ship.lateral + side * clear;
            if (pass < bandMin + margin || pass > bandMax - margin)
            {
                side = -side;
                pass = ship.lateral + side * clear;
            }
            return Mathf.Clamp(pass, bandMin + margin, bandMax - margin);
        }

        // Appetite, rolled once per orb: a hash of the orb and the rival, so
        // the same orb gets the same answer every tick with nothing stored.
        bool AcceptOrb(SpeedPad pad)
        {
            float appetite = entry != null ? entry.orbAppetite : 0f;
            if (appetite >= 1f) return true;
            if (appetite <= 0f) return false;
            uint h = (uint)(pad.GetHashCode() * 73856093) ^ (uint)(seed * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffff) / 65535f < appetite;
        }
    }

    /// <summary>Where the ship is, for a rival's driver: track distance, lateral and speed (m/s). <see cref="valid"/> false = ignore it.</summary>
    public struct ShipSpot
    {
        public bool valid;
        public float distance, lateral, speed;
    }
}
