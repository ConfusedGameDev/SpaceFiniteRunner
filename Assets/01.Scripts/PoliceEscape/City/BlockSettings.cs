using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Per-block overrides for the INTERIOR of one city block. The rule this
    /// asset enforces: anything that could touch a block border lives on
    /// <see cref="CityGenerationSettings"/> and is identical city-wide —
    /// arterials are the only roads that reach borders, they are computed from
    /// the city seed alone, and that is the entire guarantee that adjacent
    /// blocks always connect (and that the pacman wrap seam lines up). A block
    /// may therefore only vary what happens strictly inside its margin:
    /// connector carving, feature chances, and which building/decoration sets
    /// fill its lots. Null sets fall back to the city-wide ones.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockSettings", menuName = "PoliceEscape/Block Settings")]
    public class BlockSettings : ScriptableObject
    {
        [TitleGroup("Layout (interior only)")]
        [Tooltip("Chance that a lot between arterials gets carved with a secondary connector road.")]
        [PropertyRange(0f, 1f)]
        public float connectorDensity = 0.6f;

        [TitleGroup("Layout (interior only)")]
        [Tooltip("Chance a connector is L-shaped (adds corners) instead of a straight span.")]
        [PropertyRange(0f, 1f)]
        public float turnProbability = 0.35f;

        [TitleGroup("Layout (interior only)")]
        [Tooltip("Keep dead-end stubs instead of repairing them away. Needs a single-socket piece in the city piece list.")]
        public bool allowDeadEnds;

        [TitleGroup("Features")]
        [Tooltip("Master switch for this block's feature pass: overpasses, forks and multi-cell templates.")]
        public bool placeFeatures = true;

        [TitleGroup("Features")]
        [Tooltip("Chance that an eligible arterial crossing becomes a flyover in this block.")]
        [PropertyRange(0f, 1f), EnableIf(nameof(placeFeatures))]
        public float overpassChance = 0.5f;

        [TitleGroup("Features")]
        [Tooltip("Chance that a straight side street forks after leaving its arterial in this block.")]
        [PropertyRange(0f, 1f), EnableIf(nameof(placeFeatures))]
        public float forkChance = 0.5f;

        [TitleGroup("Content")]
        [Tooltip("Building set for this block's lots. Empty = the city-wide set.")]
        public Population.BuildingSet buildingSet;

        [TitleGroup("Content")]
        [Tooltip("Street prop set for this block's sidewalks. Empty = the city-wide set.")]
        public Decoration.DecorationSet decorationSet;

        [TitleGroup("Content")]
        [Tooltip("Multiplier on the building set's density for this block — below 1 thins it into a sparse district, above 1 packs it.")]
        [PropertyRange(0f, 2f)]
        public float buildingDensityMultiplier = 1f;

        [TitleGroup("Content")]
        [Tooltip("Multiplier on the decoration set's density for this block.")]
        [PropertyRange(0f, 2f)]
        public float decorationDensityMultiplier = 1f;
    }
}
