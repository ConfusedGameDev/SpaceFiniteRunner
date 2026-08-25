using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Stamps one block's <see cref="ChunkData"/> into GameObjects: the
    /// Block_{x}_{y} root (with its <see cref="CityBlock"/> record and ground
    /// slab), socket-matched road pieces, ramp runs, decks, pillars, fork
    /// parts and multi-cell templates under Roads, then buildings and street
    /// props. Extracted from the old streaming CityManager so the CITY BAKER
    /// can build blocks offline into a prefab — the only caller left; play
    /// mode never stamps anything. Every RNG stream is keyed on the BLOCK
    /// seed, so rebaking one block reproduces it exactly and rerolling it
    /// never changes a neighbour.
    ///
    /// <see cref="Instantiate"/> is pluggable: the baker swaps it for
    /// PrefabUtility.InstantiatePrefab so baked pieces stay prefab instances
    /// (smaller prefab file, live asset links) — something a runtime assembly
    /// cannot reference directly.
    /// </summary>
    public static class CityBlockBuilder
    {
        const int SaltPiecePick = 404;

        /// <summary>
        /// How pieces are instantiated. Defaults to Object.Instantiate; the
        /// editor baker temporarily swaps in PrefabUtility.InstantiatePrefab.
        /// </summary>
        public static System.Func<GameObject, Transform, GameObject> Instantiate = DefaultInstantiate;

        public static GameObject DefaultInstantiate(GameObject prefab, Transform parent) =>
            Object.Instantiate(prefab, parent);

        /// <summary>
        /// Build one block under <paramref name="parent"/> (the city root).
        /// The block's local position derives from its 0-based grid coordinate;
        /// connector-only blocks get roads (the bridge) but no buildings or
        /// decorations.
        /// </summary>
        public static GameObject BuildBlock(CityLayout layout, Vector2Int coord, ChunkData data, Transform parent)
        {
            CityGenerationSettings settings = layout.Settings;
            CityLayout.BlockSpec spec = layout.SpecFor(coord);
            float cellSize = settings.cellSize;
            float side = layout.BlockSize * cellSize;

            var blockGo = new GameObject($"Block_{coord.x}_{coord.y}");
            blockGo.transform.SetParent(parent, false);
            blockGo.transform.localPosition = new Vector3(coord.x * side, 0f, coord.y * side);

            var block = blockGo.AddComponent<CityBlock>();
            block.coord = coord;
            block.seed = spec.Seed;
            block.settingsOverride = spec.Settings;
            block.connectorOnly = spec.ConnectorOnly;
            block.connectorAxis = spec.ConnectorAxis;
            block.SetData(data);

            if (settings.generateColliders)
            {
                // One flat slab per block, top at road level (y = 0) — roads and
                // lots alike are drivable; buildings block with their own boxes.
                // Every stamped piece is sunk by RoadSurfaceHeight so its
                // asphalt lands exactly here, which is what keeps the ramps
                // (they carry real colliders) flush with the flat tiles (they
                // carry none and ride this slab).
                var ground = blockGo.AddComponent<BoxCollider>();
                ground.center = new Vector3(side * 0.5f, -0.5f, side * 0.5f);
                ground.size = new Vector3(side, 1f, side);
            }

            var roadsGo = new GameObject("Roads");
            roadsGo.transform.SetParent(blockGo.transform, false);

            StampRoads(settings, spec.Seed, data, roadsGo.transform);

            if (!spec.ConnectorOnly)
            {
                BlockKnobs knobs = layout.KnobsFor(coord);
                System.Func<int, int, bool> roadAt = layout.IsRoadCell;
                if (knobs.BuildingSet != null)
                {
                    var buildingsGo = new GameObject("Buildings");
                    buildingsGo.transform.SetParent(blockGo.transform, false);
                    Population.CityPopulator.Populate(settings, knobs.BuildingSet, knobs.BuildingDensityMultiplier,
                        spec.Seed, roadAt, data, buildingsGo.transform);
                }
                if (knobs.DecorationSet != null)
                {
                    var decorationsGo = new GameObject("Decorations");
                    decorationsGo.transform.SetParent(blockGo.transform, false);
                    Decoration.CityDecorator.Decorate(settings, knobs.DecorationSet, knobs.DecorationDensityMultiplier,
                        spec.Seed, data, decorationsGo.transform);
                }
            }

            return blockGo;
        }

        // ------------------------------------------------------------ stamping

        static void StampRoads(CityGenerationSettings settings, int blockSeed, ChunkData data, Transform stampRoot)
        {
            // Piece picking gets its own deterministic stream, separate from layout.
            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltPiecePick, data.Coord.x, data.Coord.y));
            var missingMasks = new HashSet<EdgeMask>();

            float cellSize = settings.cellSize;
            float pieceScale = settings.PieceScale;
            RoadPieceDefinition pillar = settings.PillarPiece;

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (data.IsCovered(x, y)) continue; // a multi-cell piece owns this cell — stamped below
                var cellCenter = new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);

                // Ramp runs are stamped once, from their foot cell.
                if (data.IsRamp(x, y))
                {
                    if (data.GetRampStep(x, y) == 0) StampRampRun(settings, stampRoot, data, x, y, pieceScale);
                    continue;
                }

                // Overpass deck above; a pillar where no street runs underneath.
                if (data.HasDeck(x, y))
                {
                    EdgeMask upper = data.GetUpperConnections(x, y);
                    if (TryPickPiece(settings, upper, rng, out var deck, out int deckTurns, RoadPieceRole.Deck))
                        Stamp(settings, stampRoot, deck.prefab, cellCenter, deckTurns * 90f + deck.rotationOffset, Vector3.one * pieceScale);
                    else
                        missingMasks.Add(upper);
                    bool selfSupporting = deck != null && deck.includesUnderpass;
                    if (data.IsReserved(x, y) && pillar != null && !selfSupporting)
                        Stamp(settings, stampRoot, pillar.prefab, cellCenter, pillar.rotationOffset, Vector3.one * pieceScale);
                    // Kenney's road-bridge already models its supports and the street underneath — a second ground piece would z-fight it.
                    if (selfSupporting) continue;
                }

                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask == EdgeMask.None) continue;

                if (!TryPickPiece(settings, mask, rng, out var piece, out int quarterTurns))
                {
                    missingMasks.Add(mask);
                    continue;
                }
                Stamp(settings, stampRoot, piece.prefab, cellCenter, quarterTurns * 90f + piece.rotationOffset, Vector3.one * pieceScale);
            }

            // Features: templates (roundabouts) once at the footprint centre; forks from their parts.
            foreach (RoadFeature feature in data.Features)
            {
                if (feature.Kind == RoadFeatureKind.Fork)
                {
                    StampFork(settings, stampRoot, feature, rng, pieceScale);
                    continue;
                }
                RoadPieceDefinition piece = feature.PieceIndex >= 0 && feature.PieceIndex < settings.roadPieces.Count ? settings.roadPieces[feature.PieceIndex] : null;
                if (piece?.prefab == null) continue;
                var center = new Vector3(
                    (feature.Origin.x + feature.Footprint.x * 0.5f) * cellSize,
                    0f,
                    (feature.Origin.y + feature.Footprint.y * 0.5f) * cellSize);
                Stamp(settings, stampRoot, piece.prefab, center, feature.QuarterTurns * 90f + piece.rotationOffset, Vector3.one * pieceScale);
            }

            foreach (var mask in missingMasks)
                Debug.LogWarning($"CityBlockBuilder: no road piece matches socket mask [{mask}] — those cells were left empty. Add a matching piece to the settings (dead ends need a single-socket piece, overpasses a Deck piece).");
        }

        /// <summary>
        /// Stamp one piece. The piece is sunk so its lane, not its pivot, sits
        /// on the drivable plane — see CityGenerationSettings.RoadSurfaceHeight.
        /// </summary>
        static void Stamp(CityGenerationSettings settings, Transform parent, GameObject prefab, Vector3 localPosition, float yaw, Vector3 localScale)
        {
            localPosition.y -= settings.RoadSurfaceHeight;
            var instance = Instantiate(prefab, parent);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, localScale);
        }

        /// <summary>
        /// A ramp run is rampLength cells from its foot (step 0) uphill; the
        /// settings' ramp chain (street → deck links) is spread evenly along
        /// it, each link stretched or compressed along its uphill axis so any
        /// chain length fits any run length.
        /// </summary>
        static void StampRampRun(CityGenerationSettings settings, Transform parent, ChunkData data, int footX, int footY, float pieceScale)
        {
            List<RoadPieceDefinition> chain = settings.RampChain;
            if (chain.Count == 0) return;

            int direction = data.GetRampDirection(footX, footY);
            int length = Mathf.Max(1, data.GetRampLength(footX, footY));
            Vector2Int step = EdgeMaskUtility.Offset(direction);
            float cellsPerLink = (float)length / chain.Count;

            for (int i = 0; i < chain.Count; i++)
            {
                float along = (i + 0.5f) * cellsPerLink; // cells from the foot edge to this link's centre
                var center = new Vector3(
                    (footX + 0.5f + step.x * (along - 0.5f)) * settings.cellSize,
                    0f,
                    (footY + 0.5f + step.y * (along - 0.5f)) * settings.cellSize);
                Stamp(settings, parent, chain[i].prefab, center, direction * 90f + chain[i].rotationOffset,
                    RampScale(chain[i].rotationOffset, pieceScale, cellsPerLink));
            }
        }

        /// <summary>
        /// A fork's pieces, all centred on the seam between the side street's
        /// row and its twin row: a T on the through road (its two outer half
        /// cells refilled with half straights), <c>stem</c> straights, then the
        /// split piece — stem facing the junction, exits along the street.
        /// Convention of the Fork piece: at rotationOffset 0 its stem is West
        /// and its exits East, so facing direction 1 (East) is yaw 0.
        /// </summary>
        static void StampFork(CityGenerationSettings settings, Transform parent, RoadFeature fork, System.Random rng, float pieceScale)
        {
            RoadPieceDefinition split = settings.FirstPieceWithRole(RoadPieceRole.Fork);
            if (split == null) return;

            int dir = fork.QuarterTurns & 3;
            int stem = fork.Footprint.x;
            Vector2Int f = EdgeMaskUtility.Offset(dir);
            Vector2Int p = EdgeMaskUtility.Offset(dir + 1) * fork.Variant;
            EdgeMask axisPair = EdgeMaskUtility.DirectionBit(dir) | EdgeMaskUtility.DirectionBit(dir + 2);
            EdgeMask perpPair = EdgeMask.All & ~axisPair;
            float cell = settings.cellSize;
            Vector3 scale = Vector3.one * pieceScale;
            Vector3 toTwin = new Vector3(p.x, 0f, p.y) * (0.5f * cell); // cell centre → seam

            Vector3 CellCenter(Vector2Int c) => new((c.x + 0.5f) * cell, 0f, (c.y + 0.5f) * cell);
            Vector3 SeamAt(Vector2Int c) => CellCenter(c) + toTwin;

            // Seam junction on the through road.
            if (TryPickPiece(settings, perpPair | EdgeMaskUtility.DirectionBit(dir), rng, out var tee, out int teeTurns))
                Stamp(settings, parent, tee.prefab, SeamAt(fork.Origin), teeTurns * 90f + tee.rotationOffset, scale);
            if (TryPickPiece(settings, perpPair, rng, out var half, out int halfTurns, RoadPieceRole.HalfStraight))
            {
                float yaw = halfTurns * 90f + half.rotationOffset;
                Stamp(settings, parent, half.prefab, CellCenter(fork.Origin) - toTwin * 0.5f, yaw, scale);
                Stamp(settings, parent, half.prefab, CellCenter(fork.Origin + p) + toTwin * 0.5f, yaw, scale);
            }

            // Stem straights on the seam.
            for (int i = 1; i <= stem; i++)
            {
                if (TryPickPiece(settings, axisPair, rng, out var straight, out int turns))
                    Stamp(settings, parent, straight.prefab, SeamAt(fork.Origin + f * i), turns * 90f + straight.rotationOffset, scale);
            }

            // The split: one entrance from the junction side, two exits along the street.
            Stamp(settings, parent, split.prefab, SeamAt(fork.Origin + f * (stem + 1)), (dir - 1) * 90f + split.rotationOffset, scale);
        }

        /// <summary>Piece scale that stretches a ramp link by <paramref name="stretch"/> along its own uphill axis (local +Z before rotationOffset).</summary>
        static Vector3 RampScale(float rotationOffset, float pieceScale, float stretch)
        {
            Vector3 uphillLocal = Quaternion.Euler(0f, -rotationOffset, 0f) * Vector3.forward;
            return new Vector3(
                pieceScale * Mathf.Lerp(1f, stretch, Mathf.Abs(uphillLocal.x)),
                pieceScale,
                pieceScale * Mathf.Lerp(1f, stretch, Mathf.Abs(uphillLocal.z)));
        }

        /// <summary>
        /// Weighted pick among every (piece, rotation) pair whose rotated socket
        /// mask equals the cell's mask. Symmetric pieces match at several
        /// rotations; each counts as its own candidate so weights stay honest.
        /// </summary>
        static bool TryPickPiece(CityGenerationSettings settings, EdgeMask target, System.Random rng, out RoadPieceDefinition picked, out int quarterTurns, RoadPieceRole role = RoadPieceRole.Standard)
        {
            picked = null;
            quarterTurns = 0;
            float totalWeight = 0f;

            foreach (var piece in settings.roadPieces)
            {
                if (piece?.prefab == null || piece.role != role || piece.IsMultiCell) continue; // templates and ramps/pillars are stamped elsewhere
                for (int turns = 0; turns < 4; turns++)
                {
                    if (piece.connectionMask.RotateCw(turns) != target) continue;
                    totalWeight += piece.weight;
                    // Reservoir-style single pass: replace the pick with probability weight/total.
                    if ((float)rng.NextDouble() * totalWeight <= piece.weight)
                    {
                        picked = piece;
                        quarterTurns = turns;
                    }
                }
            }
            return picked != null;
        }
    }
}
