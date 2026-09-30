using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A saved track (TrackAuthoringPRD M2): a <see cref="TrackLayout"/> the
    /// editor generated, plus the shape and spawn set it was generated with —
    /// what builds its ramps, orbs and gates, and what a regenerate starts
    /// from — and the run's countdown. A <c>RunnerLevelDefinition</c> names
    /// one in its <c>track</c> field; the level then plays exactly this track
    /// instead of generating one. Gameplay never writes it: the generator
    /// loads a copy of the records, and the hyperspace jump cuts that copy.
    /// Made and saved from the TrackGenerator's inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "Track_", menuName = "FiniteRunner/Track Layout")]
    public class TrackLayoutAsset : ScriptableObject
    {
        [TitleGroup("Run")]
        [Tooltip("Seconds on the run's countdown for this track. 0 = GameSettings.timeLimitSeconds.")]
        [PropertyRange(0f, 600f), SuffixLabel("s", true)]
        public float timeLimitSeconds;

        [TitleGroup("Generated with")]
        [Tooltip("The shape asset the track was generated with: its feature table builds the ramps (records point into it by index), and Generate starts from it.")]
        public TrackShapeSettings shape;

        [TitleGroup("Generated with")]
        [Tooltip("The spawn set the track was generated with: its spawners build the orbs, repair orbs and gates.")]
        public TrackSpawnSet spawnSet;

        [TitleGroup("Track")]
        [HideLabel, InlineProperty, ReadOnly]
        [ShowInInspector] string Summary => layout == null || !layout.IsValid
            ? "empty"
            : $"{layout.endDistance / 1000f:0.0} km · {layout.knots.Count} knots · {layout.placements.Count} placements · seed {layout.seed}";

        [HideInInspector]
        [SerializeField] TrackLayout layout = new();

        public TrackLayout Layout => layout;
        public bool IsValid => layout != null && layout.IsValid;

        /// <summary>Replaces the saved track (the editor's Save).</summary>
        public void SetLayout(TrackLayout newLayout, TrackShapeSettings shapeUsed, TrackSpawnSet spawnSetUsed)
        {
            layout = newLayout;
            shape = shapeUsed;
            spawnSet = spawnSetUsed;
        }
    }
}
