using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Population
{
    /// <summary>
    /// A themed pool of buildings the populator draws from — the "easy to
    /// replace, easy to tune" requirement lives here as an Odin table. The
    /// generation settings pick one set for now; later, districts can select
    /// different sets by noise (downtown vs. suburbs) without touching code.
    /// Two knobs decide how full the lots read: <see cref="lotSubdivision"/>
    /// splits every cell into sub-lots so several small models pack into one
    /// cell (footprints then count sub-lots), and <see cref="lotFill"/> scales
    /// each building per axis to fill its lot (needs the definitions'
    /// measured <see cref="BuildingDefinition.nativeSize"/>). A kit authored
    /// at exactly one cell per model, like Kenney's, leaves both at their
    /// defaults and bakes exactly as before.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingSet", menuName = "PoliceEscape/Building Set")]
    public class BuildingSet : ScriptableObject
    {
        [Tooltip("Meters one grid cell measures in the models' own space — building instances are scaled by cellSize ÷ this. 1 for the Kenney kit.")]
        [PropertyRange(0.1f, 60f), SuffixLabel("m", true)]
        public float nativeCellSize = 1f;

        [Tooltip("Chance a free spot actually gets a building — below 1 leaves empty lots and plazas.")]
        [PropertyRange(0f, 1f)]
        public float density = 1f;

        [Tooltip("Sub-lots per cell side: 2 turns every cell into a 2×2 of lots, so four small models pack where one sat. Footprints in the table count SUB-LOTS, not cells. 1 for kits authored at one cell per model.")]
        [PropertyRange(1, 4)]
        public int lotSubdivision = 1;

        [Tooltip("Scale each building per axis so its measured size fills this share of its lot (0 = never rescale, keep the kit's own proportions). Only definitions with a measured nativeSize are fitted.")]
        [PropertyRange(0f, 1f)]
        public float lotFill;

        [Tooltip("Cap on the per-axis fit factor, both ways (1.75 = at most 75% bigger or 43% smaller than authored). Bounds the texture stretch on thin models.")]
        [PropertyRange(1f, 4f)]
        public float maxStretch = 1.75f;

        [Tooltip("How much of the XZ fit carries into height: 1 = height grows with the geometric mean of the two factors (a lot that doubles a shack's width also makes it taller), 0 = heights stay as authored.")]
        [PropertyRange(0f, 1f)]
        public float heightFitShare = 1f;

        [TableList(AlwaysExpanded = true)]
        public List<BuildingDefinition> buildings = new();
    }
}
