using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Population
{
    /// <summary>
    /// One building the populator may place, described by its footprint in grid
    /// cells. Swapping the prefab or dragging the weight slider is the whole
    /// tuning workflow — no code. Models are assumed to face +Z at zero yaw
    /// (fix stragglers with <see cref="rotationOffset"/>); the populator yaws
    /// them toward the nearest road.
    /// </summary>
    [Serializable]
    public class BuildingDefinition
    {
        [Required, AssetsOnly]
        [Tooltip("Building model/prefab. FBX assets can be assigned directly.")]
        public GameObject prefab;

        [Tooltip("Relative pick chance among candidates of the same footprint area.")]
        [PropertyRange(0.01f, 10f)]
        public float weight = 1f;

        [Tooltip("Footprint in grid cells (width × depth). Multi-cell buildings are placed only where every cell is free.")]
        [MinValue(1)]
        public Vector2Int footprintInCells = Vector2Int.one;

        [Tooltip("Allow placing rotated 90° so a 2×1 can also fill a 1×2 gap.")]
        public bool allowRotation = true;

        [Tooltip("Extra yaw if the model's front isn't +Z.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float rotationOffset;

        [Tooltip("Random XZ offset inside the footprint, as a fraction of a cell — breaks up perfect grid alignment.")]
        [PropertyRange(0f, 0.4f)]
        public float positionJitter = 0.05f;

        [Tooltip("Uniform random scale variation (0.2 = ±20%).")]
        [PropertyRange(0f, 0.5f)]
        public float scaleJitter = 0.1f;

        [Tooltip("Extra height-only random variation on top of the uniform jitter — cheap skyline variety.")]
        [PropertyRange(0f, 0.5f)]
        public float heightJitter = 0.15f;

        [Tooltip("Keep this many cells around the building free of other buildings (0 = pack tight, suburban sets want 1).")]
        [PropertyRange(0, 2)]
        public int minSpacing;
    }
}
