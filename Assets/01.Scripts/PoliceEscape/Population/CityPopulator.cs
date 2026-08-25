using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Population
{
    /// <summary>
    /// Fills a block's non-road cells with buildings from its effective
    /// <see cref="BuildingSet"/>. Contiguous empty cells are grouped into lots
    /// (blocks bounded by roads), each lot is filled greedily — largest fitting
    /// footprint first, weighted random among same-size candidates — and every
    /// building is yawed to face the road side with the most frontage.
    /// Deterministic: its RNG derives from the BLOCK seed (own salt), so
    /// rebaking a block reproduces identical results and rerolling one block
    /// never moves another block's buildings. Frontage beyond the block's own
    /// grid is answered by the caller's road predicate, which knows the whole
    /// city (including connector blocks and the wrap seam).
    /// </summary>
    public static class CityPopulator
    {
        const int SaltPopulate = 505;

        /// <summary>Fill the block's lots. All RNG draws happen in deterministic scan order off the block seed.</summary>
        public static void Populate(CityGenerationSettings settings, BuildingSet set, float densityMultiplier,
            int blockSeed, System.Func<int, int, bool> isRoadOutside, ChunkData data, Transform buildingsRoot)
        {
            if (set == null || set.buildings == null || set.buildings.Count == 0) return;

            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltPopulate, data.Coord.x, data.Coord.y));
            var occupied = new bool[data.SizeInCells * data.SizeInCells];
            float density = Mathf.Clamp01(set.density * densityMultiplier);

            foreach (List<Vector2Int> lot in FindLots(data))
                FillLot(settings, set, density, isRoadOutside, data, lot, occupied, rng, buildingsRoot);
        }

        // ----------------------------------------------------------------- lots

        /// <summary>Group contiguous empty cells (4-connected) into lots, in deterministic scan order.</summary>
        static List<List<Vector2Int>> FindLots(ChunkData data)
        {
            var lots = new List<List<Vector2Int>>();
            var visited = new bool[data.SizeInCells * data.SizeInCells];
            var stack = new Stack<Vector2Int>();

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                int index = y * data.SizeInCells + x;
                if (visited[index] || !data.IsBuildable(x, y)) continue; // roads AND Reserved feature cells are off limits

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
                        if (visited[ni] || !data.IsBuildable(n.x, n.y)) continue;
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

        // ------------------------------------------------------------ placement

        static void FillLot(CityGenerationSettings settings, BuildingSet set, float density,
            System.Func<int, int, bool> isRoadOutside, ChunkData data,
            List<Vector2Int> lot, bool[] occupied, System.Random rng, Transform buildingsRoot)
        {
            foreach (Vector2Int cell in lot)
            {
                if (occupied[cell.y * data.SizeInCells + cell.x]) continue;

                List<(BuildingDefinition def, int w, int h)> candidates = CollectCandidates(set, data, occupied, cell);
                if (candidates.Count == 0) continue;

                // Density: a skipped spot stays empty for good (marked occupied)
                // so later smaller footprints don't quietly fill the plaza.
                if (rng.NextDouble() >= density)
                {
                    occupied[cell.y * data.SizeInCells + cell.x] = true;
                    continue;
                }

                // Greedy: only the largest fitting area competes; weights decide within it.
                int maxArea = 0;
                foreach (var c in candidates) maxArea = Mathf.Max(maxArea, c.w * c.h);
                float totalWeight = 0f;
                (BuildingDefinition def, int w, int h) picked = default;
                foreach (var c in candidates)
                {
                    if (c.w * c.h != maxArea) continue;
                    totalWeight += c.def.weight;
                    if ((float)rng.NextDouble() * totalWeight <= c.def.weight) picked = c;
                }

                Occupy(data, occupied, cell, picked.w, picked.h, picked.def.minSpacing);
                Spawn(settings, set, isRoadOutside, data, cell, picked, rng, buildingsRoot);
            }
        }

        static List<(BuildingDefinition, int, int)> CollectCandidates(BuildingSet set, ChunkData data, bool[] occupied, Vector2Int cell)
        {
            var candidates = new List<(BuildingDefinition, int, int)>();
            foreach (BuildingDefinition def in set.buildings)
            {
                if (def?.prefab == null) continue;
                int w = Mathf.Max(1, def.footprintInCells.x);
                int h = Mathf.Max(1, def.footprintInCells.y);
                if (Fits(data, occupied, cell, w, h)) candidates.Add((def, w, h));
                if (def.allowRotation && w != h && Fits(data, occupied, cell, h, w)) candidates.Add((def, h, w));
            }
            return candidates;
        }

        static bool Fits(ChunkData data, bool[] occupied, Vector2Int cell, int w, int h)
        {
            for (int y = cell.y; y < cell.y + h; y++)
            for (int x = cell.x; x < cell.x + w; x++)
            {
                if (!data.InBounds(x, y) || !data.IsBuildable(x, y) || occupied[y * data.SizeInCells + x])
                    return false;
            }
            return true;
        }

        static void Occupy(ChunkData data, bool[] occupied, Vector2Int cell, int w, int h, int spacing)
        {
            for (int y = cell.y - spacing; y < cell.y + h + spacing; y++)
            for (int x = cell.x - spacing; x < cell.x + w + spacing; x++)
            {
                if (data.InBounds(x, y)) occupied[y * data.SizeInCells + x] = true;
            }
        }

        // ------------------------------------------------------------- spawning

        static void Spawn(CityGenerationSettings settings, BuildingSet set,
            System.Func<int, int, bool> isRoadOutside, ChunkData data,
            Vector2Int cell, (BuildingDefinition def, int w, int h) placed, System.Random rng, Transform buildingsRoot)
        {
            (BuildingDefinition def, int w, int h) = placed;
            int facing = PickFacing(isRoadOutside, data, cell, w, h, def.footprintInCells.x != def.footprintInCells.y && w != def.footprintInCells.x);

            // All RNG draws happen here, in the same order as instant builds —
            // only the Instantiate below may be deferred to the spawn budget.
            float cellSize = settings.cellSize;
            float jitterX = ((float)rng.NextDouble() * 2f - 1f) * def.positionJitter * cellSize;
            float jitterZ = ((float)rng.NextDouble() * 2f - 1f) * def.positionJitter * cellSize;
            // Buildings are authored with their base on the same terrain plane
            // as the road tiles' base plate, so they sink with the roads (see
            // CityManager.RoadSurfaceHeight) — the whole stamped city drops
            // together and the road/lot relationship stays exactly as authored.
            var localPosition = new Vector3(
                (cell.x + w * 0.5f) * cellSize + jitterX,
                -settings.RoadSurfaceNativeHeight * settings.PieceScale,
                (cell.y + h * 0.5f) * cellSize + jitterZ);

            var localRotation = Quaternion.Euler(0f, facing * 90f + def.rotationOffset, 0f);

            float baseScale = set.nativeCellSize > 0.0001f ? cellSize / set.nativeCellSize : 1f;
            float uniform = baseScale * (1f + ((float)rng.NextDouble() * 2f - 1f) * def.scaleJitter);
            float height = uniform * (1f + (float)rng.NextDouble() * def.heightJitter);
            var localScale = new Vector3(uniform, height, uniform);

            var instance = CityBlockBuilder.Instantiate(def.prefab, buildingsRoot);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;
            if (settings.generateColliders) AddMeshColliders(instance);
        }

        /// <summary>
        /// A mesh collider on every child mesh, so collision follows the actual
        /// model — buildings like building-b/building-n have drive-through
        /// archways a fitted bounding box would seal off. Buildings never move,
        /// so the colliders stay non-convex (legal for static geometry), and
        /// the kit meshes are low-poly so per-instance cooking is cheap.
        /// Skipped when the prefab already ships colliders of its own.
        /// </summary>
        static void AddMeshColliders(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>() != null) return;

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
            }
        }

        /// <summary>
        /// Direction index (0..3 = N,E,S,W) the building front should face:
        /// the road side with the most frontage. Non-square footprints may only
        /// face along their depth axis (the model can't yaw 90° without
        /// breaking cell alignment), square ones may face any side. Neighbours
        /// beyond the block border count via the caller's city-wide road
        /// predicate, same as the road generator.
        /// </summary>
        static int PickFacing(System.Func<int, int, bool> isRoadOutside, ChunkData data, Vector2Int cell, int w, int h, bool rotated)
        {
            bool square = w == h;
            bool northSouthAllowed = square || !rotated;
            bool eastWestAllowed = square || rotated;

            int best = -1, bestCount = -1;
            for (int dir = 0; dir < 4; dir++)
            {
                bool allowed = (dir == 0 || dir == 2) ? northSouthAllowed : eastWestAllowed;
                if (!allowed) continue;
                int count = CountRoadOnSide(isRoadOutside, data, cell, w, h, dir);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = dir;
                }
            }
            return best < 0 ? 0 : best;
        }

        static int CountRoadOnSide(System.Func<int, int, bool> isRoadOutside, ChunkData data, Vector2Int cell, int w, int h, int dir)
        {
            Vector2Int origin = data.WorldCellOrigin;
            int count = 0;
            // Walk the row/column of cells just outside the footprint on that side.
            int x0 = dir == 1 ? cell.x + w : dir == 3 ? cell.x - 1 : cell.x;
            int y0 = dir == 0 ? cell.y + h : dir == 2 ? cell.y - 1 : cell.y;
            int steps = (dir == 0 || dir == 2) ? w : h;
            for (int i = 0; i < steps; i++)
            {
                int x = x0 + ((dir == 0 || dir == 2) ? i : 0);
                int y = y0 + ((dir == 1 || dir == 3) ? i : 0);
                bool road = data.InBounds(x, y)
                    ? data.IsRoad(x, y)
                    : isRoadOutside != null && isRoadOutside(origin.x + x, origin.y + y);
                if (road) count++;
            }
            return count;
        }
    }
}
