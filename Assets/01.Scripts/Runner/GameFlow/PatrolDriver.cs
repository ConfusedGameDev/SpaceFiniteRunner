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
        /// <summary>
        /// One tick of driving. <paramref name="speedCap"/> comes back as the
        /// most the patrol should be doing right now (a flat sweep's safe
        /// speed), or <see cref="float.MaxValue"/> when nothing limits it.
        ///
        /// <paramref name="lineOverride"/> replaces the ship's lateral as the
        /// line to drive — an attack run steers for a FLANK, not the ship's
        /// own lane. It also switches orb seeking off: a committed patrol does
        /// not break off for a pickup. Everything else still applies, so the
        /// patrol keeps itself off the open edge it is pushing the ship toward.
        /// The line rules themselves are <see cref="LineDriving"/>'s, shared
        /// with the race rivals.
        /// </summary>
        public BodyControls Drive(TrackBody body, float shipLateral, TrackManager track, PatrolDefinition def,
                                  float gap, out float speedCap, float? lineOverride = null)
        {
            float d = body.Distance;
            float speed = Mathf.Max(body.ForwardSpeed, 1f);
            track.GetLateralBand(d, out float bandMin, out float bandMax);
            LineTuning tuning = Tuning(def);

            // Round a full tube the ship's lateral may be whole turns away:
            // chase the nearest equivalent.
            float line = lineOverride ?? shipLateral;
            if (track.SectionAt(d) is TubeSection { Unbounded: true } tube)
                line += Mathf.Round((body.Lateral - line) / tube.Circumference) * tube.Circumference;

            // Orbs pull less and less as the gap closes: up close it wants the ship.
            if (!lineOverride.HasValue)
                line = LineDriving.SeekOrb(body, tuning, def.orbSeekWeight * Mathf.Clamp01(gap / Mathf.Max(def.warnDistance, 1f)), line);
            line = LineDriving.PlanRamps(body, tuning, bandMin, bandMax, line);
            line = LineDriving.KeepOffOpenEdges(track, d, speed, bandMin, bandMax, line);

            var controls = new BodyControls
            {
                steer = LineDriving.SteerFor(body, line),
                throttle = 1f,
                brake = LineDriving.BrakeForSweep(body, track, tuning, out speedCap),
            };
            return controls;
        }

        static LineTuning Tuning(PatrolDefinition def) => new()
        {
            lateralSpeed = def.lateralSpeed,
            orbLookaheadSeconds = def.orbLookaheadSeconds,
            rampLookaheadSeconds = def.rampLookaheadSeconds,
            curveLookaheadSeconds = def.curveLookaheadSeconds,
            gripBase = def.gripBase,
            gripPerSpeed = def.gripPerSpeed,
            brakeDecel = def.brakeDecel,
        };
    }
}
