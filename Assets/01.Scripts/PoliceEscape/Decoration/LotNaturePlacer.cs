using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// Turns the lots the model claimed as parks (cells flagged
    /// <see cref="ChunkData.CellFlags.ParkLot"/> by the generator) into green
    /// space: a ground tile per cell, an optional walking-path run across the
    /// lot, scattered interior props (trees, bushes, rocks) and perimeter
    /// props (fences, palms) facing outward. Stamping only — WHICH lots are
    /// parks was decided in the model pass, so the flags ride the serialized
    /// layout and a rebake reproduces the same parks. Deterministic like every
    /// content pass: own salt off the BLOCK seed. Every prop goes through
    /// <see cref="DecorationProp.Configure"/>, so nature obeys the same
    /// mass-based push contract as street furniture.
    /// </summary>
    public static class LotNaturePlacer
    {
        const int SaltNature = 1111;

        public static void Populate(CityGenerationSettings settings, NatureSet set, int blockSeed, ChunkData data, Transform natureRoot)
        {
            if (set == null) return;

            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltNature, data.Coord.x, data.Coord.y));
            float cell = settings.cellSize;
            float cellScale = set.nativeCellSize > 0.0001f ? cell / set.nativeCellSize : 1f;

            foreach (List<Vector2Int> lot in ChunkLots.FindLots(data, (x, y) => data.HasFlag(x, y, ChunkData.CellFlags.ParkLot)))
            {
                var lotCells = new HashSet<Vector2Int>(lot);
                HashSet<Vector2Int> pathCells = PickPathCells(set, lot, rng);
                Vector2 centroid = Centroid(lot);

                foreach (Vector2Int c in lot)
                {
                    // Ground first: the path tile IS a grass tile with a path
                    // on it, so path cells get it instead of the plain ground.
                    bool onPath = pathCells.Contains(c);
                    GameObject tile = onPath ? set.pathTilePrefab : set.groundTilePrefab;
                    if (tile != null)
                    {
                        float tileYaw = onPath && PathRunsEastWest(pathCells, c) ? 90f : 0f;
                        var tileGo = CityBlockBuilder.Instantiate(tile, natureRoot);
                        tileGo.transform.localPosition = new Vector3((c.x + 0.5f) * cell, 0f, (c.y + 0.5f) * cell);
                        tileGo.transform.localRotation = Quaternion.Euler(0f, tileYaw, 0f);
                        tileGo.transform.localScale = tile.transform.localScale * cellScale;
                    }
                    if (onPath) continue; // paths stay walkable — no props

                    int outwardDir = OutwardDirection(lotCells, c);
                    if (outwardDir >= 0)
                        TrySpawnProp(set, set.perimeterDensity, rng, DecorationPlacement.LotPerimeter, data, c, outwardDir, cell, cellScale, natureRoot);
                    else if (!InsideClearing(set, c, centroid, pathCells))
                        TrySpawnProp(set, set.interiorDensity, rng, DecorationPlacement.LotInterior, data, c, -1, cell, cellScale, natureRoot);
                }
            }
        }

        // ------------------------------------------------------------- layout

        /// <summary>A straight path run across the lot's longer axis, through its middle — empty when the lot is too small or the set has no path tile.</summary>
        static HashSet<Vector2Int> PickPathCells(NatureSet set, List<Vector2Int> lot, System.Random rng)
        {
            var path = new HashSet<Vector2Int>();
            if (set.pathTilePrefab == null) return path;

            Vector2Int min = lot[0], max = lot[0];
            foreach (Vector2Int c in lot)
            {
                min = Vector2Int.Min(min, c);
                max = Vector2Int.Max(max, c);
            }
            int spanX = max.x - min.x + 1;
            int spanY = max.y - min.y + 1;
            if (Mathf.Max(spanX, spanY) < 3) return path;

            int coin = rng.Next(2); // one draw always, so lot shape never shifts later draws
            bool alongX = spanX == spanY ? coin == 0 : spanX > spanY;
            if (alongX)
            {
                int y = (min.y + max.y) / 2;
                foreach (Vector2Int c in lot)
                    if (c.y == y) path.Add(c);
            }
            else
            {
                int x = (min.x + max.x) / 2;
                foreach (Vector2Int c in lot)
                    if (c.x == x) path.Add(c);
            }
            return path;
        }

        static bool PathRunsEastWest(HashSet<Vector2Int> pathCells, Vector2Int c) =>
            pathCells.Contains(c + Vector2Int.right) || pathCells.Contains(c + Vector2Int.left);

        static Vector2 Centroid(List<Vector2Int> lot)
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2Int c in lot) sum += new Vector2(c.x + 0.5f, c.y + 0.5f);
            return sum / lot.Count;
        }

        /// <summary>Direction index (0..3) of a neighbour outside the lot — the side a perimeter prop should face; -1 for interior cells.</summary>
        static int OutwardDirection(HashSet<Vector2Int> lotCells, Vector2Int c)
        {
            for (int dir = 0; dir < 4; dir++)
                if (!lotCells.Contains(c + EdgeMaskUtility.Offset(dir)))
                    return dir;
            return -1;
        }

        static bool InsideClearing(NatureSet set, Vector2Int c, Vector2 centroid, HashSet<Vector2Int> pathCells)
        {
            if (set.clearingRadius <= 0f) return false;
            var center = new Vector2(c.x + 0.5f, c.y + 0.5f);
            if (Vector2.Distance(center, centroid) < set.clearingRadius) return true;
            foreach (Vector2Int p in pathCells)
                if (Vector2.Distance(center, new Vector2(p.x + 0.5f, p.y + 0.5f)) < set.clearingRadius)
                    return true;
            return false;
        }

        // ------------------------------------------------------------- spawning

        /// <summary>
        /// Roll one cell: density gate, weighted pick among the props claiming
        /// this placement, then spawn — perimeter props at the edge inset
        /// facing outward, interior props jittered inside the cell at a random
        /// yaw. Every RNG draw happens here, in deterministic order.
        /// </summary>
        static void TrySpawnProp(NatureSet set, float density, System.Random rng, DecorationPlacement placement,
            ChunkData data, Vector2Int c, int outwardDir, float cell, float cellScale, Transform natureRoot)
        {
            if (rng.NextDouble() >= density) return;

            float totalWeight = 0f;
            DecorationDefinition picked = null;
            foreach (DecorationDefinition def in set.decorations)
            {
                if (def?.prefab == null || def.placement != placement) continue;
                totalWeight += def.weight;
                // Reservoir-style single pass: replace the pick with probability weight/total.
                if ((float)rng.NextDouble() * totalWeight <= def.weight) picked = def;
            }
            if (picked == null) return;

            var center = new Vector3((c.x + 0.5f) * cell, 0f, (c.y + 0.5f) * cell);
            Vector3 position;
            float yaw;
            if (outwardDir >= 0)
            {
                Vector2Int step = EdgeMaskUtility.Offset(outwardDir);
                position = center + new Vector3(step.x, 0f, step.y) * ((0.5f - set.edgeInset) * cell);
                yaw = Mathf.Atan2(step.x, step.y) * Mathf.Rad2Deg;
            }
            else
            {
                position = center + new Vector3(
                    ((float)rng.NextDouble() * 2f - 1f) * 0.3f * cell, 0f,
                    ((float)rng.NextDouble() * 2f - 1f) * 0.3f * cell);
                yaw = (float)rng.NextDouble() * 360f;
            }
            yaw += picked.rotationOffset + ((float)rng.NextDouble() * 2f - 1f) * picked.yawJitter;

            var instance = CityBlockBuilder.Instantiate(picked.prefab, natureRoot);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = picked.prefab.transform.localScale * (cellScale * picked.scaleMultiplier);
            DecorationProp.Configure(instance, picked, set);
        }
    }
}
