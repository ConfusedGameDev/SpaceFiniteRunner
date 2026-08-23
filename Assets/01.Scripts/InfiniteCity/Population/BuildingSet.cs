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

        [TableList(AlwaysExpanded = true)]
        public List<BuildingDefinition> buildings = new();
    }
}
