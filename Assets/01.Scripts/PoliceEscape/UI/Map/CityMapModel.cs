using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The map's view of the city — a thin adapter over the baked
    /// <see cref="CityRoot"/>. The city is a fixed prefab now: every block's
    /// grid model is serialized on its <see cref="CityBlock"/> and the road
    /// graph covers the whole city from the first frame, so the on-demand
    /// generation, LRU cache and pump budget the streaming era needed are
    /// gone. What remains is the coordinate mapping the map screen paints
    /// with, cell queries against the baked layouts, and the shared graph for
    /// routing. The streaming-era surface (EnsureArea/Pump/PendingCount) is
    /// kept as no-ops so the screen's paint loop reads unchanged.
    ///
    /// Cells are GLOBAL cell coordinates, matching <see cref="RoadNode.Cell"/>.
    /// World conversion mirrors <see cref="RoadGraph.WorldToCell"/> (which
    /// subtracts the city root's origin).
    /// </summary>
    public class CityMapModel
    {
        readonly CityRoot root;
        readonly Vector3 origin;
        readonly float cellSize;
        readonly int blockSize;
        readonly Dictionary<Vector2Int, ChunkData> blocks = new();

        public CityMapModel(CityRoot root)
        {
            this.root = root;
            origin = root != null ? root.transform.position : Vector3.zero;
            cellSize = root != null ? root.cellSize : 20f;
            blockSize = root != null ? Mathf.Max(1, root.blockSizeInCells) : 1;
            if (root != null)
            {
                foreach (CityBlock block in root.GetComponentsInChildren<CityBlock>())
                {
                    ChunkData data = block.Data;
                    if (data != null) blocks[block.coord] = data;
                }
            }
        }

        /// <summary>Road graph over the whole baked city — shared with the AI, never shrinks.</summary>
        public RoadGraph Graph => root != null ? root.Graph : emptyGraph ??= new RoadGraph(cellSize);
        RoadGraph emptyGraph;

        public float CellSize => cellSize;
        public int ChunkSizeInCells => blockSize;

        /// <summary>Always 0 — the whole city exists up front. Kept for the screen's status line.</summary>
        public int PendingCount => 0;

        // ------------------------------------------------------- coordinates

        /// <summary>World position to global cell. Mirrors <see cref="RoadGraph.WorldToCell"/> exactly.</summary>
        public Vector2Int WorldToCell(Vector3 position) =>
            new(Mathf.FloorToInt((position.x - origin.x) / cellSize),
                Mathf.FloorToInt((position.z - origin.z) / cellSize));

        /// <summary>Centre of a global cell in world space, on the ground plane.</summary>
        public Vector3 CellToWorld(Vector2Int cell) =>
            origin + new Vector3((cell.x + 0.5f) * cellSize, 0f, (cell.y + 0.5f) * cellSize);

        /// <summary>Which block a global cell belongs to. Floor division, so negative coordinates behave.</summary>
        public Vector2Int CellToChunk(Vector2Int cell) =>
            new(DeterministicHash.FloorDiv(cell.x, blockSize), DeterministicHash.FloorDiv(cell.y, blockSize));

        // ------------------------------------------------------------ queries

        /// <summary>
        /// What occupies a global cell. False outside the baked city — the
        /// caller paints those as background (the void beyond the wrap seam).
        /// </summary>
        public bool TryGetCell(Vector2Int cell, out ChunkData.CellKind kind, out bool isRoad)
        {
            kind = ChunkData.CellKind.Empty;
            isRoad = false;
            if (!blocks.TryGetValue(CellToChunk(cell), out ChunkData data)) return false;

            int lx = DeterministicHash.Mod(cell.x, blockSize);
            int ly = DeterministicHash.Mod(cell.y, blockSize);
            kind = data.GetKind(lx, ly);
            isRoad = data.IsRoad(lx, ly);
            return true;
        }

        public bool IsChunkReady(Vector2Int coord) => blocks.ContainsKey(coord);

        // ------------------------------------------- streaming-era no-ops

        /// <summary>No-op — every block already exists. Kept so the paint loop reads unchanged.</summary>
        public void EnsureArea(RectInt chunkWindow, int margin) { }

        /// <summary>No-op — nothing is ever pending. Returns false: the schematic repaints on window changes instead.</summary>
        public bool Pump(int budget, int cacheSize) => false;

        /// <summary>No-op — the baked city cannot change under us mid-session.</summary>
        public void Clear() { }
    }
}
