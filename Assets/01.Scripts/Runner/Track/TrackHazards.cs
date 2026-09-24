using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The one place that answers "may something happen on this stretch of
    /// road?" for the systems that need a clear run rather than a free spot.
    ///
    /// The rule is the laser gate's, and the data is the laser gate's: every
    /// feature registers its footprint plus what lies ahead of it (a ramp's
    /// landing) in the generator's keep-out list, and the final run-up
    /// registers itself as open-ended — so a ramp, its landing, a loop, a tube
    /// and the end zone all fall out of one lookup
    /// (<see cref="TrackGenerator.IsGroundClear"/>), with a section sweep on
    /// top as the belt-and-braces the placer itself keeps.
    ///
    /// What it deliberately does NOT reject is an open edge or a flat sweep:
    /// those are the ground the patrol's attack run WANTS, because a shove
    /// there throws the ship off the road instead of into a wall.
    /// </summary>
    public static class TrackHazards
    {
        // The placer walks its window at this spacing; a loop or a tube is
        // never shorter than a stride, so nothing slips between samples.
        const float SectionSampleStride = 25f;

        /// <summary>
        /// True when [<paramref name="from"/>, <paramref name="to"/>] carries
        /// no ramp, ramp landing, loop, tube or final run-up. A missing
        /// generator falls back to the section sweep alone; a missing track
        /// answers false, because ground we cannot read is ground we do not
        /// commit to.
        /// </summary>
        public static bool IsEncounterGroundClear(TrackManager track, TrackGenerator generator, float from, float to)
        {
            if (track == null) return false;
            if (to < from) (from, to) = (to, from);

            if (generator != null && !generator.IsGroundClear(from, to)) return false;

            // The end of a finite track takes everything: the run-up is a
            // keep-out above, but a track whose end zone was never registered
            // (or a generator we do not have) still must not be duelled on.
            if (track.EndZoneStart >= 0f && to >= track.EndZoneStart) return false;

            for (float d = from; d < to; d += SectionSampleStride)
                if (track.SectionAt(d) != null) return false;
            return track.SectionAt(to) == null;
        }
    }
}
