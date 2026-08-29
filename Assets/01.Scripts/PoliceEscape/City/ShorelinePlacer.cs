using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Stamps a water block's coastline from its <see cref="ShorelineSet"/>,
    /// at bake time, under the block's Shoreline child. The shore is decided
    /// from AUTHORED facts only — which of the block's four (wrapped)
    /// neighbours are land, read off the <see cref="CityLayout"/> — so it is
    /// deterministic per bake and both blocks meeting at the pacman seam
    /// agree on it. Each land-facing edge gets a strip of edge pieces along
    /// its whole length, top flush at y = 0, faces to the water; where two
    /// land-facing edges meet, the strip's end slot is an inner-corner
    /// piece; and a corner touching land only DIAGONALLY takes the outer
    /// corner cap that closes the convex land corner between the two
    /// neighbours' strips (that cube sits in this block, so this block is
    /// the one to stamp it — no double stamping, no gap). The slots under a
    /// road cell — a causeway's bridge line crossing the border — are left
    /// open so the road runs straight through the cliff line. Variant picks
    /// hash the block seed with salt 1212, distinct from every other stream.
    /// Pieces go through <see cref="CityBlockBuilder.Instantiate"/> so they
    /// stay prefab instances in the city prefab; the cliffs are static
    /// colliders (never DecorationProps), so <c>IsCellClear</c> keeps spawns
    /// off them.
    /// </summary>
    public static class ShorelinePlacer
    {
        public const int SaltShoreline = 1212;

        // Corner index convention shared with the set's orientation contract:
        // 0 = NE, 1 = SE, 2 = SW, 3 = NW; yaw = index × 90°. Corner c is the
        // meeting point of edges c and c+1 (NE = north + east, …).
        static readonly Vector2Int[] DiagonalOffsets = { new(1, 1), new(1, -1), new(-1, -1), new(-1, 1) };
        // The corner at the low end (i = 0) and the high end of each edge's slot run.
        static readonly int[] LowCorner = { 3, 1, 2, 2 };
        static readonly int[] HighCorner = { 0, 0, 1, 3 };

        public static void Place(CityLayout layout, Vector2Int coord, ChunkData data, Transform root)
        {
            CityGenerationSettings settings = layout.Settings;
            ShorelineSet set = settings.shorelineSet;
            if (set == null || set.edges.Count == 0) return;

            float cell = settings.cellSize;
            int size = data.SizeInCells;
            float side = size * cell;

            bool[] land = new bool[4];
            bool[] diagonalLand = new bool[4];
            for (int dir = 0; dir < 4; dir++)
            {
                land[dir] = IsLand(layout, coord + EdgeMaskUtility.Offset(dir));
                diagonalLand[dir] = IsLand(layout, coord + DiagonalOffsets[dir]);
            }

            var rng = new System.Random(DeterministicHash.Combine(layout.SpecFor(coord).Seed, SaltShoreline, coord.x, coord.y));

            // Slot pitch: as many pieces along an edge as fit at the target
            // height's footprint, stretched a hair so the run closes exactly.
            ShorelineSet.Piece reference = ShorelineSet.Pick(set.edges, rng);
            if (reference == null) return;
            float footprint = Footprint(reference, set.targetHeight);
            int count = Mathf.Max(1, Mathf.RoundToInt(side / Mathf.Max(0.01f, footprint)));
            float pitch = side / count;

            for (int dir = 0; dir < 4; dir++)
            {
                if (!land[dir]) continue;
                bool lowIsInner = IsInnerCorner(land, LowCorner[dir]);
                bool highIsInner = IsInnerCorner(land, HighCorner[dir]);
                for (int i = 0; i < count; i++)
                {
                    if ((i == 0 && lowIsInner) || (i == count - 1 && highIsInner)) continue; // the corner piece owns that slot
                    float along = (i + 0.5f) * pitch;
                    Vector2Int under = CellUnderSlot(dir, along, cell, size);
                    if (data.IsRoad(under.x, under.y)) continue;                              // a causeway's road crosses here

                    Vector3 center = dir switch
                    {
                        0 => new Vector3(along, 0f, side - pitch * 0.5f),
                        1 => new Vector3(side - pitch * 0.5f, 0f, along),
                        2 => new Vector3(along, 0f, pitch * 0.5f),
                        _ => new Vector3(pitch * 0.5f, 0f, along),
                    };
                    List<ShorelineSet.Piece> pool = set.waterfalls.Count > 0 && rng.NextDouble() < set.waterfallChance ? set.waterfalls : set.edges;
                    ShorelineSet.Piece piece = ShorelineSet.Pick(pool, rng) ?? ShorelineSet.Pick(set.edges, rng);
                    Stamp(set, root, piece, center, dir * 90f, pitch, dir == 0 || dir == 2);
                }
            }

            for (int corner = 0; corner < 4; corner++)
            {
                int edgeA = corner, edgeB = (corner + 1) & 3;
                List<ShorelineSet.Piece> pool;
                if (land[edgeA] && land[edgeB]) pool = set.innerCorners;
                else if (!land[edgeA] && !land[edgeB] && diagonalLand[corner]) pool = set.outerCorners;
                else continue;
                ShorelineSet.Piece piece = ShorelineSet.Pick(pool, rng);
                if (piece == null) continue;

                Vector3 center = corner switch
                {
                    0 => new Vector3(side - pitch * 0.5f, 0f, side - pitch * 0.5f),
                    1 => new Vector3(side - pitch * 0.5f, 0f, pitch * 0.5f),
                    2 => new Vector3(pitch * 0.5f, 0f, pitch * 0.5f),
                    _ => new Vector3(pitch * 0.5f, 0f, side - pitch * 0.5f),
                };
                Stamp(set, root, piece, center, corner * 90f, pitch, null);
            }
        }

        static bool IsLand(CityLayout layout, Vector2Int coord)
        {
            var wrapped = new Vector2Int(DeterministicHash.Mod(coord.x, layout.GridWidth), DeterministicHash.Mod(coord.y, layout.GridHeight));
            return !layout.SpecFor(wrapped).IsWater;
        }

        static bool IsInnerCorner(bool[] land, int corner) => land[corner] && land[(corner + 1) & 3];

        static Vector2Int CellUnderSlot(int dir, float along, float cell, int size)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(along / cell), 0, size - 1);
            return dir switch
            {
                0 => new Vector2Int(index, size - 1),
                1 => new Vector2Int(size - 1, index),
                2 => new Vector2Int(index, 0),
                _ => new Vector2Int(0, index),
            };
        }

        /// <summary>World footprint of a piece along the shore once it stands targetHeight tall.</summary>
        static float Footprint(ShorelineSet.Piece piece, float targetHeight)
        {
            Bounds bounds = piece.nativeBounds;
            float scale = targetHeight / Mathf.Max(0.001f, bounds.size.y);
            return Mathf.Max(bounds.size.x, bounds.size.z) * scale;
        }

        /// <summary>
        /// One piece: uniformly scaled to the target height, stretched to the
        /// slot pitch along the shore (<paramref name="alongX"/>: the edge
        /// runs along world X; null: a corner, stretched on both axes), its
        /// bounds centre put on the slot and its bounds top on y = 0.
        /// </summary>
        static void Stamp(ShorelineSet set, Transform parent, ShorelineSet.Piece piece, Vector3 slotCenter, float yaw, float pitch, bool? alongX)
        {
            if (piece?.prefab == null) return;
            Bounds bounds = piece.nativeBounds;
            float uniform = set.targetHeight / Mathf.Max(0.001f, bounds.size.y);
            float footprint = Mathf.Max(bounds.size.x, bounds.size.z) * uniform;
            float stretch = pitch / Mathf.Max(0.001f, footprint) * set.overlap;

            float totalYaw = yaw + piece.rotationOffset;
            // Which local axis ends up along the shore after the yaw — that is the one to stretch.
            Vector3 scale;
            if (alongX.HasValue)
            {
                Vector3 shoreLocal = Quaternion.Euler(0f, -totalYaw, 0f) * (alongX.Value ? Vector3.right : Vector3.forward);
                scale = new Vector3(
                    uniform * Mathf.Lerp(1f, stretch, Mathf.Abs(shoreLocal.x)),
                    uniform,
                    uniform * Mathf.Lerp(1f, stretch, Mathf.Abs(shoreLocal.z)));
            }
            else
            {
                scale = new Vector3(uniform * stretch, uniform, uniform * stretch);
            }

            GameObject instance = CityBlockBuilder.Instantiate(piece.prefab, parent);
            var rotation = Quaternion.Euler(0f, totalYaw, 0f);
            // The bounds centre (in the prefab's own frame) is what lands on the slot; the top lands on the road plane.
            Vector3 centerOffset = rotation * Vector3.Scale(bounds.center, scale);
            Vector3 position = slotCenter - new Vector3(centerOffset.x, 0f, centerOffset.z);
            position.y = -bounds.max.y * scale.y;
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = Vector3.Scale(piece.prefab.transform.localScale, scale);

            if (set.addColliders && instance.GetComponentInChildren<Collider>() == null)
            {
                // Fitted box in the root's own space: the measured bounds
                // already include the prefab root's scale, so divide it out.
                Vector3 rootScale = piece.prefab.transform.localScale;
                var box = instance.AddComponent<BoxCollider>();
                box.center = Divide(bounds.center, rootScale);
                box.size = Divide(bounds.size, rootScale);
            }
        }

        static Vector3 Divide(Vector3 v, Vector3 by) => new(
            v.x / (Mathf.Abs(by.x) > 0.0001f ? by.x : 1f),
            v.y / (Mathf.Abs(by.y) > 0.0001f ? by.y : 1f),
            v.z / (Mathf.Abs(by.z) > 0.0001f ? by.z : 1f));
    }
}
