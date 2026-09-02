using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// Base of every track feature kind the generator can place (jumps, loops,
    /// tubes). A feature is a stretch of track that does something to the
    /// ship: it occupies a footprint — <see cref="FootprintLength"/>, or the
    /// length of the <see cref="TrackSection"/> it builds — that pads keep
    /// off when <see cref="ClaimsFootprint"/> (a tube wants its orbs), and an
    /// <see cref="ExclusionAhead"/> no other feature may start in — for a
    /// jump, the longest arc it can throw the ship, so nothing waits under a
    /// landing. A feature that owns its own pose over its footprint builds a
    /// section through <see cref="CreateSection"/>, registered the moment the
    /// generator decides the spot, before anything is placed beyond it.
    /// Definitions are assets, and the generator plays a runtime clone (never
    /// the asset on disk), which is what the debug menu edits.
    /// </summary>
    public abstract class TrackFeatureDefinition : ScriptableObject
    {
        [Tooltip("Name used in spawned object names and HUD text.")]
        public string displayName = "Feature";

        /// <summary>Metres of track this feature occupies from its start distance, when it builds no section (a section's Length wins).</summary>
        public abstract float FootprintLength { get; }

        /// <summary>Metres past the footprint where no other feature may start.</summary>
        public abstract float ExclusionAhead { get; }

        /// <summary>True when pads must keep off the footprint (ramps, loops). A tube wants its orbs.</summary>
        public virtual bool ClaimsFootprint => true;

        /// <summary>
        /// The section a feature that owns its pose routes the track through,
        /// or null. <paramref name="roll01"/> is a draw off the layout rng for
        /// per-instance variation (a tube's length), so seeded runs repeat.
        /// </summary>
        public virtual TrackSection CreateSection(TrackManager track, float startDistance, float roll01) => null;
    }
}
