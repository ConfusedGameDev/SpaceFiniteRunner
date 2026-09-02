using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// Base of every track feature kind the generator can place (jumps now;
    /// loops and tube sections later). A feature is a stretch of track that
    /// does something to the ship: it claims a <see cref="FootprintLength"/>
    /// no pad may spawn on, and an <see cref="ExclusionAhead"/> no other
    /// feature may start in — for a jump, the longest arc it can throw the
    /// ship, so nothing waits under a landing. Definitions are assets, and
    /// the generator plays a runtime clone (never the asset on disk), which
    /// is what the debug menu edits.
    /// </summary>
    public abstract class TrackFeatureDefinition : ScriptableObject
    {
        [Tooltip("Name used in spawned object names and HUD text.")]
        public string displayName = "Feature";

        /// <summary>Metres of track this feature occupies from its start distance — pads keep off it.</summary>
        public abstract float FootprintLength { get; }

        /// <summary>Metres past the footprint where no other feature may start.</summary>
        public abstract float ExclusionAhead { get; }
    }
}
