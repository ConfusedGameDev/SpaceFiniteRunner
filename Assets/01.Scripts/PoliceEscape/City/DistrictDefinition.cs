using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// One district flavour of the city — downtown, suburb, park, beachfront…
    /// A block's district is resolved purely from the authored
    /// <see cref="CityDefinition"/> and the city seed (see
    /// <c>CityDefinition.DistrictFor</c>), so both sides of every block border
    /// always agree on it. The rule this asset enforces: the ONLY
    /// border-relevant field is <see cref="useSecondaryArterials"/>, and it is
    /// consumed exclusively by <see cref="CityLayout"/>'s periodic road field;
    /// everything else flows into block interiors through the
    /// <see cref="BlockKnobs"/> fallback chain (a block's own settings
    /// override still wins) and can never move a border road.
    /// </summary>
    [CreateAssetMenu(fileName = "DistrictDefinition", menuName = "PoliceEscape/District Definition")]
    public class DistrictDefinition : ScriptableObject
    {
        [Tooltip("Name shown on the City Designer grid and its legend.")]
        public string displayName = "District";

        [Tooltip("This district's tint on the City Designer grid (and future map shading).")]
        public Color mapColor = new(0.55f, 0.55f, 0.55f);

        [TitleGroup("Roads")]
        [Tooltip("Blocks of this district also materialize the city's secondary arterial field — a denser street grid for downtowns. Consumed only by CityLayout's city-seeded field, so the block-border contract holds.")]
        public bool useSecondaryArterials;

        [TitleGroup("Interior")]
        [InlineEditor]
        [Tooltip("Interior knobs (connectors, features, content sets) for this district's blocks. A block's own settingsOverride still wins; empty falls back to the definition's default block settings.")]
        public BlockSettings interiorSettings;

        [TitleGroup("Parks")]
        [Tooltip("Whole blocks of this district are parks: every lot gets the nature pass instead of buildings.")]
        public bool isPark;

        [TitleGroup("Parks")]
        [Tooltip("Chance an individual lot in a normal block of this district becomes a small park instead of buildings.")]
        [PropertyRange(0f, 1f), HideIf(nameof(isPark))]
        public float parkLotChance;

        [TitleGroup("Parks")]
        [Tooltip("Nature set used by this district's park lots. Empty = no nature pass even when park rolls succeed.")]
        public Decoration.NatureSet natureSet;

        [TitleGroup("Curved avenues")]
        [Tooltip("Chance per attempt that a block of this district carves a spline-curved avenue between two of its arterials.")]
        [PropertyRange(0f, 1f)]
        public float curveChance;

        [TitleGroup("Curved avenues")]
        [Tooltip("Curved-avenue attempts per block.")]
        [PropertyRange(0, 2), EnableIf("@curveChance > 0f")]
        public int maxCurvedAvenues = 1;
    }
}
