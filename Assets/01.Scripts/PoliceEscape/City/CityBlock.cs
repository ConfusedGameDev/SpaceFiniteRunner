using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// One baked block of the city prefab: the root of its stamped geometry
    /// (Roads/Buildings/Decorations children) plus everything needed to reason
    /// about it without regenerating — grid coordinate, the seed and settings
    /// it was baked with, and its serialized <see cref="BlockLayoutData"/>.
    /// Unlike the old streaming CityChunk, the grid model IS serialized: the
    /// prefab must survive domain reloads, editor restarts and plain scene
    /// loads with zero generator involvement, so the data rides in the asset
    /// and <see cref="Data"/> merely rehydrates it on demand. Gizmos draw the
    /// grid overlay per block, in the scene and in Prefab Mode alike.
    /// </summary>
    public class CityBlock : MonoBehaviour
    {
        [ReadOnly, Tooltip("Grid coordinate of this block, (0,0) at the south-west corner.")]
        public Vector2Int coord;

        [ReadOnly, Tooltip("Seed this block's interior was baked with. Reroll via the City Designer window, not here.")]
        public int seed;

        [ReadOnly, Tooltip("Interior settings this block was baked with (null = the city default at bake time).")]
        public BlockSettings settingsOverride;

        [ReadOnly, Tooltip("Baked as a connector-only bridge block.")]
        public bool connectorOnly;

        [ReadOnly, ShowIf(nameof(connectorOnly)), Tooltip("0 = bridge runs East–West, 1 = North–South.")]
        public int connectorAxis;

        [HideInInspector]
        public BlockLayoutData layout;

        ChunkData cached;
        CityRoot root;

        /// <summary>Grid model this block was baked from, rehydrated from the serialized layout. Null only for a component that was never baked.</summary>
        public ChunkData Data => cached ??= layout?.ToChunkData();

        /// <summary>Adopt a freshly generated model: cache it and serialize it into the prefab-safe layout.</summary>
        public void SetData(ChunkData data)
        {
            cached = data;
            layout = data != null ? BlockLayoutData.From(data) : null;
        }

        CityRoot Root => root != null ? root : root = GetComponentInParent<CityRoot>();

        // -------------------------------------------------------------- gizmos

        void OnDrawGizmos()
        {
            CityRoot city = Root;
            if (city == null || !city.drawGizmos) return;
            ChunkData data = Data;
            if (data == null) return;

            float cell = city.cellSize;
            float deckHeight = city.deckWorldHeight;
            Vector3 origin = transform.position;
            float sideMeters = data.SizeInCells * cell;

            Gizmos.color = connectorOnly ? new Color(1f, 0.55f, 0.1f) : Color.green;
            Vector3 center = origin + new Vector3(sideMeters * 0.5f, 0f, sideMeters * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(sideMeters, 0.1f, sideMeters));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                origin + new Vector3(0.5f, 0f, 0.5f) * cell,
                $"Block {coord}  seed {seed}{(connectorOnly ? "  [bridge]" : string.Empty)}");
#endif

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                Vector3 cellCenter = origin + new Vector3((x + 0.5f) * cell, 0.05f, (y + 0.5f) * cell);
                var slab = new Vector3(cell * 0.85f, 0.05f, cell * 0.85f);

                if (data.HasDeck(x, y))
                {
                    // Upper level at deck height; the street underneath (if any) keeps its own slab below.
                    Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
                    Gizmos.DrawCube(cellCenter + Vector3.up * deckHeight, slab);
                }
                if (data.IsReserved(x, y))
                {
                    Gizmos.color = new Color(0.35f, 0.35f, 0.35f, 0.6f);  // feature-owned, no road, no building
                    Gizmos.DrawCube(cellCenter, slab);
                    continue;
                }
                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                Gizmos.color = data.IsRamp(x, y)
                    ? new Color(1f, 0.55f, 0.1f)                         // ramp
                    : mask.ConnectionCount() switch
                    {
                        1 => Color.red,                                  // dead end
                        2 => mask.RotateCw(2) == mask
                            ? new Color(0.3f, 0.9f, 1f)                  // straight
                            : Color.yellow,                              // corner
                        3 => Color.magenta,                              // T-junction
                        4 => Color.white,                                // crossroad
                        _ => Color.grey,
                    };
                if (data.IsRamp(x, y)) cellCenter.y += deckHeight * data.RampHeight01(x, y);
                Vector2 shift = data.GetCenterOffset(x, y) * cell; // fork seam roads draw where their node is
                cellCenter += new Vector3(shift.x, 0f, shift.y);
                Gizmos.DrawCube(cellCenter, slab);
            }
        }
    }
}
