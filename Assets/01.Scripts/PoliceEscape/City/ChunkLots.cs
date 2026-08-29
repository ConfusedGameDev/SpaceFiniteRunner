using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Shared lot finder: groups a chunk's cells into 4-connected components
    /// under a caller-supplied membership predicate, in deterministic scan
    /// order. One implementation serves every consumer — the building
    /// populator (buildable, non-park cells), the park-lot marker (all
    /// buildable cells) and the nature placer (park-flagged cells) — so "what
    /// is a lot" can never drift apart between the pass that claims lots and
    /// the passes that fill them.
    /// </summary>
    public static class ChunkLots
    {
        /// <summary>Group member cells (4-connected) into lots, each sorted (y, x) for a stable fill order.</summary>
        public static List<List<Vector2Int>> FindLots(ChunkData data, System.Func<int, int, bool> isMember)
        {
            var lots = new List<List<Vector2Int>>();
            var visited = new bool[data.SizeInCells * data.SizeInCells];
            var stack = new Stack<Vector2Int>();

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                int index = y * data.SizeInCells + x;
                if (visited[index] || !isMember(x, y)) continue;

                var lot = new List<Vector2Int>();
                stack.Push(new Vector2Int(x, y));
                visited[index] = true;
                while (stack.Count > 0)
                {
                    Vector2Int cell = stack.Pop();
                    lot.Add(cell);
                    for (int dir = 0; dir < 4; dir++)
                    {
                        Vector2Int n = cell + EdgeMaskUtility.Offset(dir);
                        if (!data.InBounds(n.x, n.y)) continue;
                        int ni = n.y * data.SizeInCells + n.x;
                        if (visited[ni] || !isMember(n.x, n.y)) continue;
                        visited[ni] = true;
                        stack.Push(n);
                    }
                }
                // Flood-fill order depends on stack pops — sort for a stable fill order.
                lot.Sort((a, b) => a.y != b.y ? a.y - b.y : a.x - b.x);
                lots.Add(lot);
            }
            return lots;
        }
    }
}
