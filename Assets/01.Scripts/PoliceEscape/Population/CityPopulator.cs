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
    ///
    /// The placement grid is the CELL grid subdivided by the set's
    /// <see cref="BuildingSet.lotSubdivision"/>: a sub-lot is buildable when
    /// its parent cell is, footprints and spacing count sub-lots, and road
    /// frontage is read off the parent cells. With subdivision 1 the walk,
    /// the RNG draw order and every position are exactly what they were, so
    /// the Kenney bakes do not move. Real-scale kits (Cyberpunk Megapolis)
    /// use 2 — four shacks per cell — and <see cref="BuildingSet.lotFill"/>,
    /// which scales each placed model per axis so its measured
    /// <see cref="BuildingDefinition.nativeSize"/> fills its lot, capped by
    /// <see cref="BuildingSet.maxStretch"/>; that is what turns "one model
    /// floating in the middle of a 37 m lot" into a packed street.
    /// </summary>
    public static class CityPopulator
    {
        const int SaltPopulate = 505;

        /// <summary>The placement grid: cells × subdivision, with the cell-level queries every step needs.</summary>
        readonly struct LotGrid
        {
            public readonly ChunkData Data;
            public readonly int Sub;
            public readonly int Size;
            public readonly float LotSize;

            public LotGrid(ChunkData data, int sub, float cellSize)
            {
                Data = data;
                Sub = sub;
                Size = data.SizeInCells * sub;
                LotSize = cellSize / sub;
            }

            public int Index(int lx, int ly) => ly * Size + lx;
            public bool InBounds(int lx, int ly) => lx >= 0 && ly >= 0 && lx < Size && ly < Size;
            public int CellOf(int lot) => FloorDiv(lot, Sub);

            public bool IsBuildable(int lx, int ly) =>
                InBounds(lx, ly) && IsBuildableLotCell(Data, CellOf(lx), CellOf(ly));

            /// <summary>Is the parent cell of a lot a road — inside the block from the data, beyond it from the city-wide predicate.</summary>
            public bool IsRoad(int lx, int ly, System.Func<int, int, bool> isRoadOutside)
            {
                int cx = CellOf(lx), cy = CellOf(ly);
                if (Data.InBounds(cx, cy)) return Data.IsRoad(cx, cy);
                Vector2Int origin = Data.WorldCellOrigin;
                return isRoadOutside != null && isRoadOutside(origin.x + cx, origin.y + cy);
            }

            static int FloorDiv(int value, int divisor) =>
                value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
        }

        /// <summary>Fill the block's lots. All RNG draws happen in deterministic scan order off the block seed.</summary>
        public static void Populate(CityGenerationSettings settings, BuildingSet set, float densityMultiplier,
            int blockSeed, System.Func<int, int, bool> isRoadOutside, ChunkData data, Transform buildingsRoot)
        {
            if (set == null || set.buildings == null || set.buildings.Count == 0) return;

            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltPopulate, data.Coord.x, data.Coord.y));
            var grid = new LotGrid(data, Mathf.Clamp(set.lotSubdivision, 1, 4), settings.cellSize);
            var occupied = new bool[grid.Size * grid.Size];
            float density = Mathf.Clamp01(set.density * densityMultiplier);

            // Roads and Reserved feature cells are off limits, and so are the
            // lots the model's park pass claimed — the nature placer fills those.
            foreach (List<Vector2Int> lot in ChunkLots.FindLots(data, (x, y) => IsBuildableLotCell(data, x, y)))
                FillLot(settings, set, density, isRoadOutside, grid, lot, occupied, rng, buildingsRoot);
        }

        static bool IsBuildableLotCell(ChunkData data, int x, int y) =>
            data.IsBuildable(x, y) && !data.HasFlag(x, y, ChunkData.CellFlags.ParkLot);

        // ------------------------------------------------------------ placement

        static void FillLot(CityGenerationSettings settings, BuildingSet set, float density,
            System.Func<int, int, bool> isRoadOutside, LotGrid grid,
            List<Vector2Int> lot, bool[] occupied, System.Random rng, Transform buildingsRoot)
        {
            foreach (Vector2Int cell in lot)
            for (int sy = 0; sy < grid.Sub; sy++)
            for (int sx = 0; sx < grid.Sub; sx++)
            {
                var spot = new Vector2Int(cell.x * grid.Sub + sx, cell.y * grid.Sub + sy);
                if (occupied[grid.Index(spot.x, spot.y)]) continue;

                List<(BuildingDefinition def, int w, int h)> candidates = CollectCandidates(set, grid, occupied, spot);
                if (candidates.Count == 0) continue;

                // Density: a skipped spot stays empty for good (marked occupied)
                // so later smaller footprints don't quietly fill the plaza.
                if (rng.NextDouble() >= density)
                {
                    occupied[grid.Index(spot.x, spot.y)] = true;
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

                Occupy(grid, occupied, spot, picked.w, picked.h, picked.def.minSpacing);
                Spawn(settings, set, isRoadOutside, grid, spot, picked, rng, buildingsRoot);
            }
        }

        static List<(BuildingDefinition, int, int)> CollectCandidates(BuildingSet set, LotGrid grid, bool[] occupied, Vector2Int spot)
        {
            var candidates = new List<(BuildingDefinition, int, int)>();
            foreach (BuildingDefinition def in set.buildings)
            {
                if (def?.prefab == null) continue;
                int w = Mathf.Max(1, def.footprintInCells.x);
                int h = Mathf.Max(1, def.footprintInCells.y);
                if (Fits(grid, occupied, spot, w, h)) candidates.Add((def, w, h));
                if (def.allowRotation && w != h && Fits(grid, occupied, spot, h, w)) candidates.Add((def, h, w));
            }
            return candidates;
        }

        static bool Fits(LotGrid grid, bool[] occupied, Vector2Int spot, int w, int h)
        {
            for (int y = spot.y; y < spot.y + h; y++)
            for (int x = spot.x; x < spot.x + w; x++)
            {
                if (!grid.IsBuildable(x, y) || occupied[grid.Index(x, y)])
                    return false;
            }
            return true;
        }

        static void Occupy(LotGrid grid, bool[] occupied, Vector2Int spot, int w, int h, int spacing)
        {
            for (int y = spot.y - spacing; y < spot.y + h + spacing; y++)
            for (int x = spot.x - spacing; x < spot.x + w + spacing; x++)
            {
                if (grid.InBounds(x, y)) occupied[grid.Index(x, y)] = true;
            }
        }

        // ------------------------------------------------------------- spawning

        static void Spawn(CityGenerationSettings settings, BuildingSet set,
            System.Func<int, int, bool> isRoadOutside, LotGrid grid,
            Vector2Int spot, (BuildingDefinition def, int w, int h) placed, System.Random rng, Transform buildingsRoot)
        {
            (BuildingDefinition def, int w, int h) = placed;
            bool rotated = def.footprintInCells.x != def.footprintInCells.y && w != def.footprintInCells.x;
            int facing = PickFacing(isRoadOutside, grid, spot, w, h, rotated);

            // All RNG draws happen here, in the same order as instant builds —
            // only the Instantiate below may be deferred to the spawn budget.
            float cellSize = settings.cellSize;
            float lotSize = grid.LotSize;
            float jitterX = ((float)rng.NextDouble() * 2f - 1f) * def.positionJitter * lotSize;
            float jitterZ = ((float)rng.NextDouble() * 2f - 1f) * def.positionJitter * lotSize;
            // Buildings are authored with their base on the same terrain plane
            // as the road tiles' base plate, so they sink with the roads (see
            // CityManager.RoadSurfaceHeight) — the whole stamped city drops
            // together and the road/lot relationship stays exactly as authored.
            var localPosition = new Vector3(
                (spot.x + w * 0.5f) * lotSize + jitterX,
                -settings.RoadSurfaceNativeHeight * settings.PieceScale,
                (spot.y + h * 0.5f) * lotSize + jitterZ);

            var localRotation = Quaternion.Euler(0f, facing * 90f + def.rotationOffset, 0f);

            float baseScale = set.nativeCellSize > 0.0001f ? cellSize / set.nativeCellSize : 1f;
            float uniform = baseScale * (1f + ((float)rng.NextDouble() * 2f - 1f) * def.scaleJitter);
            float height = uniform * (1f + (float)rng.NextDouble() * def.heightJitter);

            // Lot fit: per model axis, how much the measured size must grow or
            // shrink to fill the lot it was given. A rotated placement (w/h
            // swapped, yawed 90°) has the model's X running along world Z, so
            // the targets follow the model's axes, not the world's. Square
            // footprints face any side, but then w == h and both targets agree.
            float fitX = 1f, fitZ = 1f;
            if (set.lotFill > 0f && def.nativeSize.x > 0.001f && def.nativeSize.y > 0.001f)
            {
                int lotsAlongModelX = rotated ? h : w;
                int lotsAlongModelZ = rotated ? w : h;
                float cap = Mathf.Max(1f, set.maxStretch);
                fitX = Mathf.Clamp(lotsAlongModelX * lotSize * set.lotFill / (def.nativeSize.x * baseScale), 1f / cap, cap);
                fitZ = Mathf.Clamp(lotsAlongModelZ * lotSize * set.lotFill / (def.nativeSize.y * baseScale), 1f / cap, cap);
            }
            float fitH = Mathf.Lerp(1f, Mathf.Sqrt(fitX * fitZ), set.heightFitShare);
            var localScale = new Vector3(uniform * fitX, height * fitH, uniform * fitZ);

            // Re-centre models whose pivot is not under their bounds centre
            // (measured into the definition): the offset turns with the yaw
            // and grows with the scale, like the model itself.
            var pivotShift = new Vector3(def.pivotToCenter.x * localScale.x, 0f, def.pivotToCenter.y * localScale.z);
            localPosition -= localRotation * pivotShift;

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
        /// breaking lot alignment), square ones may face any side. Neighbours
        /// beyond the block border count via the caller's city-wide road
        /// predicate, same as the road generator.
        /// </summary>
        static int PickFacing(System.Func<int, int, bool> isRoadOutside, LotGrid grid, Vector2Int spot, int w, int h, bool rotated)
        {
            bool square = w == h;
            bool northSouthAllowed = square || !rotated;
            bool eastWestAllowed = square || rotated;

            int best = -1, bestCount = -1;
            for (int dir = 0; dir < 4; dir++)
            {
                bool allowed = (dir == 0 || dir == 2) ? northSouthAllowed : eastWestAllowed;
                if (!allowed) continue;
                int count = CountRoadOnSide(isRoadOutside, grid, spot, w, h, dir);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = dir;
                }
            }
            return best < 0 ? 0 : best;
        }

        static int CountRoadOnSide(System.Func<int, int, bool> isRoadOutside, LotGrid grid, Vector2Int spot, int w, int h, int dir)
        {
            int count = 0;
            // Walk the row/column of lots just outside the footprint on that side.
            int x0 = dir == 1 ? spot.x + w : dir == 3 ? spot.x - 1 : spot.x;
            int y0 = dir == 0 ? spot.y + h : dir == 2 ? spot.y - 1 : spot.y;
            int steps = (dir == 0 || dir == 2) ? w : h;
            for (int i = 0; i < steps; i++)
            {
                int x = x0 + ((dir == 0 || dir == 2) ? i : 0);
                int y = y0 + ((dir == 1 || dir == 3) ? i : 0);
                if (grid.IsRoad(x, y, isRoadOutside)) count++;
            }
            return count;
        }
    }
}
