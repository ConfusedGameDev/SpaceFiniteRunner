using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Population
{
    /// <summary>
    /// Fills a chunk's non-road cells with buildings from the settings'
    /// <see cref="BuildingSet"/>. Contiguous empty cells are grouped into lots
    /// (blocks bounded by roads), each lot is filled greedily — largest fitting
    /// footprint first, weighted random among same-size candidates — and every
    /// building is yawed to face the road side with the most frontage.
    /// Deterministic: its RNG derives from the same global seed as the road
    /// layout (different salt), so recalculating reproduces identical results
    /// and Repopulate never has to touch the roads.
    /// </summary>
    public static class CityPopulator
    {
        const int SaltPopulate = 505;

        public static void Populate(CityGenerationSettings settings, ChunkData data, Transform buildingsRoot)
        {
            BuildingSet set = settings.buildingSet;
            if (set == null || set.buildings == null || set.buildings.Count == 0) return;

            var rng = new System.Random(DeterministicHash.Combine(settings.globalSeed, SaltPopulate, data.Coord.x, data.Coord.y));
            var occupied = new bool[data.SizeInCells * data.SizeInCells];

            foreach (List<Vector2Int> lot in FindLots(data))
                FillLot(settings, set, data, lot, occupied, rng, buildingsRoot);
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
                if (visited[index] || data.IsRoad(x, y)) continue;

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
                        if (visited[ni] || data.IsRoad(n.x, n.y)) continue;
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

        static void FillLot(CityGenerationSettings settings, BuildingSet set, ChunkData data,
            List<Vector2Int> lot, bool[] occupied, System.Random rng, Transform buildingsRoot)
        {
            foreach (Vector2Int cell in lot)
            {
                if (occupied[cell.y * data.SizeInCells + cell.x]) continue;

                List<(BuildingDefinition def, int w, int h)> candidates = CollectCandidates(set, data, occupied, cell);
                if (candidates.Count == 0) continue;

                // Density: a skipped spot stays empty for good (marked occupied)
                // so later smaller footprints don't quietly fill the plaza.
                if (rng.NextDouble() >= set.density)
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
                Spawn(settings, set, data, cell, picked, rng, buildingsRoot);
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
                if (!data.InBounds(x, y) || data.IsRoad(x, y) || occupied[y * data.SizeInCells + x])
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

        static void Spawn(CityGenerationSettings settings, BuildingSet set, ChunkData data,
            Vector2Int cell, (BuildingDefinition def, int w, int h) placed, System.Random rng, Transform buildingsRoot)
        {
            (BuildingDefinition def, int w, int h) = placed;
            int facing = PickFacing(settings, data, cell, w, h, def.footprintInCells.x != def.footprintInCells.y && w != def.footprintInCells.x);

            float cellSize = settings.cellSize;
            var instance = Object.Instantiate(def.prefab, buildingsRoot);
            CityManager.ApplyGeneratedFlags(instance);

            float jitterX = ((float)rng.NextDouble() * 2f - 1f) * def.positionJitter * cellSize;
            float jitterZ = ((float)rng.NextDouble() * 2f - 1f) * def.positionJitter * cellSize;
            instance.transform.localPosition = new Vector3(
                (cell.x + w * 0.5f) * cellSize + jitterX,
                0f,
                (cell.y + h * 0.5f) * cellSize + jitterZ);

            instance.transform.localRotation = Quaternion.Euler(0f, facing * 90f + def.rotationOffset, 0f);

            float baseScale = set.nativeCellSize > 0.0001f ? cellSize / set.nativeCellSize : 1f;
            float uniform = baseScale * (1f + ((float)rng.NextDouble() * 2f - 1f) * def.scaleJitter);
            float height = uniform * (1f + (float)rng.NextDouble() * def.heightJitter);
            instance.transform.localScale = new Vector3(uniform, height, uniform);

            if (settings.generateColliders) AddFittedCollider(instance);
        }

        /// <summary>
        /// One box collider on the building root, fitted to the combined mesh
        /// bounds in root-local space — it inherits the instance's rotation and
        /// scale for free, so a single box serves every jittered variant.
        /// Skipped when the prefab already ships colliders of its own.
        /// </summary>
        static void AddFittedCollider(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>() != null) return;

            Transform root = instance.transform;
            bool hasBounds = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Bounds meshBounds = mesh.bounds;
                // Walk all 8 corners through child → world → root-local so
                // rotated child meshes still land inside the box.
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = root.InverseTransformPoint(filter.transform.TransformPoint(local));
                    if (!hasBounds)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else bounds.Encapsulate(point);
                }
            }
            if (!hasBounds) return;

            var box = instance.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }

        /// <summary>
        /// Direction index (0..3 = N,E,S,W) the building front should face:
        /// the road side with the most frontage. Non-square footprints may only
        /// face along their depth axis (the model can't yaw 90° without
        /// breaking cell alignment), square ones may face any side. Neighbours
        /// beyond the chunk border count via the world arterial function, same
        /// as the road generator.
        /// </summary>
        static int PickFacing(CityGenerationSettings settings, ChunkData data, Vector2Int cell, int w, int h, bool rotated)
        {
            bool square = w == h;
            bool northSouthAllowed = square || !rotated;
            bool eastWestAllowed = square || rotated;

            int best = -1, bestCount = -1;
            for (int dir = 0; dir < 4; dir++)
            {
                bool allowed = (dir == 0 || dir == 2) ? northSouthAllowed : eastWestAllowed;
                if (!allowed) continue;
                int count = CountRoadOnSide(settings, data, cell, w, h, dir);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = dir;
                }
            }
            return best < 0 ? 0 : best;
        }

        static int CountRoadOnSide(CityGenerationSettings settings, ChunkData data, Vector2Int cell, int w, int h, int dir)
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
                    : RoadNetworkGenerator.IsArterialWorldCell(settings, origin.x + x, origin.y + y);
                if (road) count++;
            }
            return count;
        }
    }
}
