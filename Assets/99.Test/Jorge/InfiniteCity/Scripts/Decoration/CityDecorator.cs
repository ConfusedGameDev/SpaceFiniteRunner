using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// Places street props on a chunk's road tiles from the settings'
    /// <see cref="DecorationSet"/>: light posts on the corner quads of
    /// intersection tiles (the connecting points between pieces), cones and
    /// barriers on the sidewalk strip of socket-less tile edges. Only plain
    /// flat road cells qualify — ramps, deck/underpass cells, feature-covered
    /// cells and fork seam rows (offset centres) are skipped, so a prop never
    /// stands inside stamped feature geometry. Deterministic like the
    /// populator: its RNG derives from the global seed (own salt), and all
    /// draws happen at placement time so streamed chunks equal instant ones.
    /// </summary>
    public static class CityDecorator
    {
        const int SaltDecorate = 707;

        public static void Decorate(CityGenerationSettings settings, ChunkData data, Transform decorationsRoot,
            System.Action<System.Action> scheduler = null)
        {
            DecorationSet set = settings.decorationSet;
            if (set == null || set.decorations == null || set.decorations.Count == 0) return;

            var rng = new System.Random(DeterministicHash.Combine(settings.globalSeed, SaltDecorate, data.Coord.x, data.Coord.y));
            float cell = settings.cellSize;
            // Props stand on the curb, which the Kenney kit models one
            // lane-height above the lane — the same distance the whole city is
            // sunk by, so the lift IS RoadSurfaceHeight (0 for flat test kits).
            float curbLift = settings.RoadSurfaceHeight;
            float scale = set.nativeCellSize > 0.0001f ? cell / set.nativeCellSize : 1f;

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (!data.IsRoad(x, y) || data.IsCovered(x, y) || data.IsRamp(x, y) || data.HasDeck(x, y) || data.HasCenterOffset(x, y))
                    continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask == EdgeMask.None) continue;

                var center = new Vector3((x + 0.5f) * cell, curbLift, (y + 0.5f) * cell);

                // The four corner quads of an intersection tile — always
                // sidewalk, whatever the lanes do between them.
                if (mask.ConnectionCount() >= 3)
                {
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float sx = (corner & 1) == 0 ? -1f : 1f;
                        float sz = (corner & 2) == 0 ? -1f : 1f;
                        Vector3 offset = new Vector3(sx, 0f, sz) * ((0.5f - set.cornerInset) * cell);
                        TrySpawn(set, rng, DecorationPlacement.IntersectionCorner, center + offset, center, scale, decorationsRoot, scheduler);
                    }
                }

                // Socket-less edges are the sidewalk strip beside the lane —
                // both long sides of a straight, three sides of a dead end.
                for (int dir = 0; dir < 4; dir++)
                {
                    if ((mask & EdgeMaskUtility.DirectionBit(dir)) != 0) continue;
                    Vector2Int step = EdgeMaskUtility.Offset(dir);
                    Vector3 offset = new Vector3(step.x, 0f, step.y) * ((0.5f - set.edgeInset) * cell);
                    TrySpawn(set, rng, DecorationPlacement.RoadEdge, center + offset, center, scale, decorationsRoot, scheduler);
                }
            }
        }

        /// <summary>
        /// Roll one spot: density gate, weighted pick among the props that
        /// claim this placement, then spawn facing the road (the tile centre)
        /// plus the prop's own offset and jitter. Every RNG draw happens here,
        /// in order — only the Instantiate may be deferred to the spawn budget.
        /// </summary>
        static void TrySpawn(DecorationSet set, System.Random rng, DecorationPlacement placement,
            Vector3 localPosition, Vector3 faceTarget, float cellScale, Transform decorationsRoot,
            System.Action<System.Action> scheduler)
        {
            if (rng.NextDouble() >= set.density) return;

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

            Vector3 toRoad = faceTarget - localPosition;
            float yaw = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg + picked.rotationOffset
                + ((float)rng.NextDouble() * 2f - 1f) * picked.yawJitter;
            var localRotation = Quaternion.Euler(0f, yaw, 0f);
            float uniform = cellScale * picked.scaleMultiplier;

            void SpawnNow()
            {
                if (decorationsRoot == null) return; // chunk unloaded before its turn in the queue
                var instance = Object.Instantiate(picked.prefab, decorationsRoot);
                CityManager.ApplyGeneratedFlags(instance);
                instance.transform.localPosition = localPosition;
                instance.transform.localRotation = localRotation;
                instance.transform.localScale = picked.prefab.transform.localScale * uniform;
                DecorationProp.Configure(instance, picked, set);
            }

            if (scheduler != null) scheduler(SpawnNow);
            else SpawnNow();
        }
    }
}
