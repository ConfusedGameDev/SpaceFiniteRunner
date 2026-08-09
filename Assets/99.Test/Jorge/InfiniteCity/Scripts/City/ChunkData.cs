using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Pure C# grid model of one city chunk — which cells are road, what kind,
    /// and which edges connect. Deliberately holds no GameObjects: keeping the
    /// model separate from instantiation is what makes recalculate, time-sliced
    /// building and deterministic infinite streaming cheap to support. The
    /// populator later reads the Empty cells; the road graph reads connections.
    /// </summary>
    public class ChunkData
    {
        /// <summary>What occupies a cell. Arterials are world-continuous and may cross chunk borders; connectors never do.</summary>
        public enum CellKind : byte { Empty, Arterial, Connector }

        public readonly Vector2Int Coord;
        public readonly int SizeInCells;

        readonly CellKind[] kinds;
        readonly EdgeMask[] connections;

        public ChunkData(Vector2Int coord, int sizeInCells)
        {
            Coord = coord;
            SizeInCells = sizeInCells;
            kinds = new CellKind[sizeInCells * sizeInCells];
            connections = new EdgeMask[sizeInCells * sizeInCells];
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < SizeInCells && y < SizeInCells;

        public CellKind GetKind(int x, int y) => kinds[y * SizeInCells + x];
        public void SetKind(int x, int y, CellKind kind) => kinds[y * SizeInCells + x] = kind;

        public bool IsRoad(int x, int y) => kinds[y * SizeInCells + x] != CellKind.Empty;

        public EdgeMask GetConnections(int x, int y) => connections[y * SizeInCells + x];
        public void SetConnections(int x, int y, EdgeMask mask) => connections[y * SizeInCells + x] = mask;

        /// <summary>World-grid coordinate of this chunk's cell (0,0) — cell units, not meters.</summary>
        public Vector2Int WorldCellOrigin => new(Coord.x * SizeInCells, Coord.y * SizeInCells);
    }
}
