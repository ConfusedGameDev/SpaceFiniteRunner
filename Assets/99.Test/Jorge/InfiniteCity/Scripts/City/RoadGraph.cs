using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Runtime waypoint graph derived from the generated roads — the AI's
    /// navigation source, per the plan's "RoadGraph, not NavMesh" rule: the
    /// generator already knows exactly where roads are. Nodes are world cell
    /// coordinates (cell units, not meters); edges follow each cell's socket
    /// mask, so paths can never cut through connections the road data says
    /// don't exist. Chunks register their cells after generation; A* answers
    /// route queries. Rebuilt from scratch on every Recalculate.
    /// </summary>
    public class RoadGraph
    {
        readonly Dictionary<Vector2Int, EdgeMask> cells = new();
        readonly float cellSize;

        public RoadGraph(float cellSize) => this.cellSize = cellSize;

        public int Count => cells.Count;

        /// <summary>All registered road cells with their connection masks.</summary>
        public IEnumerable<KeyValuePair<Vector2Int, EdgeMask>> Cells => cells;

        public void RegisterChunk(ChunkData data)
        {
            Vector2Int origin = data.WorldCellOrigin;
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (!data.IsRoad(x, y)) continue;
                cells[new Vector2Int(origin.x + x, origin.y + y)] = data.GetConnections(x, y);
            }
        }

        public bool IsRoad(Vector2Int cell) => cells.ContainsKey(cell);

        public EdgeMask Connections(Vector2Int cell) =>
            cells.TryGetValue(cell, out EdgeMask mask) ? mask : EdgeMask.None;

        public Vector2Int WorldToCell(Vector3 position) =>
            new(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));

        public Vector3 CellCenter(Vector2Int cell) =>
            new((cell.x + 0.5f) * cellSize, 0f, (cell.y + 0.5f) * cellSize);

        /// <summary>Nearest registered road cell to a world position. O(n) scan — fine at v1 sizes, revisit with spatial buckets if streaming makes it hot.</summary>
        public bool TryGetNearestCell(Vector3 position, out Vector2Int nearest)
        {
            nearest = default;
            float bestSqr = float.MaxValue;
            foreach (var pair in cells)
            {
                Vector3 center = CellCenter(pair.Key);
                float sqr = (center - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                nearest = pair.Key;
            }
            return bestSqr < float.MaxValue;
        }

        /// <summary>
        /// A* route over the road network, start and goal included in the
        /// result. Neighbours come from the connection masks, Manhattan
        /// heuristic (uniform cell cost). The open list uses linear min
        /// extraction — city-sized searches are a few hundred nodes, so
        /// clarity wins over a heap until profiling says otherwise.
        /// </summary>
        public bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> path, int maxExpansions = 8192)
        {
            path.Clear();
            if (!IsRoad(start) || !IsRoad(goal)) return false;
            if (start == goal)
            {
                path.Add(start);
                return true;
            }

            var open = new List<Vector2Int> { start };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { [start] = 0 };

            int expansions = 0;
            while (open.Count > 0 && expansions++ < maxExpansions)
            {
                // Linear extract-min on f = g + h.
                int bestIndex = 0;
                int bestF = int.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    int f = gScore[open[i]] + Heuristic(open[i], goal);
                    if (f >= bestF) continue;
                    bestF = f;
                    bestIndex = i;
                }

                Vector2Int current = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (current == goal)
                {
                    Reconstruct(cameFrom, current, path);
                    return true;
                }

                EdgeMask mask = Connections(current);
                for (int dir = 0; dir < 4; dir++)
                {
                    if ((mask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                    Vector2Int neighbour = current + EdgeMaskUtility.Offset(dir);
                    if (!IsRoad(neighbour)) continue;

                    int tentative = gScore[current] + 1;
                    if (gScore.TryGetValue(neighbour, out int known) && tentative >= known) continue;
                    cameFrom[neighbour] = current;
                    gScore[neighbour] = tentative;
                    if (!open.Contains(neighbour)) open.Add(neighbour);
                }
            }
            return false;
        }

        static int Heuristic(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        static void Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current, List<Vector2Int> path)
        {
            path.Add(current);
            while (cameFrom.TryGetValue(current, out Vector2Int previous))
            {
                current = previous;
                path.Add(current);
            }
            path.Reverse();
        }
    }
}
