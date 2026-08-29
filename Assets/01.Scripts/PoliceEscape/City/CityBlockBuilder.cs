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
            block.isWater = spec.IsWater;
            block.SetData(data);

            // Every content root goes on this list: it is what CityStreamer
            // toggles at runtime, while the block object (and its colliders)
            // stays alive. Recorded rather than found, so the streamer never
            // matches names.
            var streamed = new List<GameObject>();

            if (spec.IsWater)
            {
                BuildWater(layout, coord, data, blockGo, side, streamed);
            }
            else if (settings.generateColliders)
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
            streamed.Add(roadsGo);

            StampRoads(settings, spec.Seed, data, roadsGo.transform);
            if (spec.IsWater && settings.shorelineSet != null)
            {
                var shoreGo = new GameObject("Shoreline");
                shoreGo.transform.SetParent(blockGo.transform, false);
                streamed.Add(shoreGo);
                ShorelinePlacer.Place(layout, coord, data, shoreGo.transform);
            }

            if (!spec.ConnectorOnly && !spec.IsWater)
            {
                BlockKnobs knobs = layout.KnobsFor(coord);
                System.Func<int, int, bool> roadAt = layout.IsRoadCell;
                if (knobs.NatureSet != null)
                {
                    // Before Buildings: the model already claimed the park lots
                    // (ParkLot flags), the populator skips them, this fills them.
                    var natureGo = new GameObject("Nature");
                    natureGo.transform.SetParent(blockGo.transform, false);
                    streamed.Add(natureGo);
                    Decoration.LotNaturePlacer.Populate(settings, knobs.NatureSet, spec.Seed, data, natureGo.transform);
                }
                if (knobs.BuildingSet != null)
                {
                    var buildingsGo = new GameObject("Buildings");
                    buildingsGo.transform.SetParent(blockGo.transform, false);
                    streamed.Add(buildingsGo);
                    Population.CityPopulator.Populate(settings, knobs.BuildingSet, knobs.BuildingDensityMultiplier,
                        spec.Seed, roadAt, data, buildingsGo.transform);
                }
                if (knobs.DecorationSet != null)
                {
                    var decorationsGo = new GameObject("Decorations");
                    decorationsGo.transform.SetParent(blockGo.transform, false);
                    streamed.Add(decorationsGo);
                    Decoration.CityDecorator.Decorate(settings, knobs.DecorationSet, knobs.DecorationDensityMultiplier,
                        spec.Seed, data, decorationsGo.transform);
                }
            }

            block.SetStreamedRoots(streamed.ToArray());
            return blockGo;
        }

        // --------------------------------------------------------------- water

        /// <summary>
        /// What a water block carries INSTEAD of the ground slab. There must
        /// be no collider at y = 0 over the sea, or the water is invisibly
        /// drivable; so the block gets a sea floor (a box whose top is
        /// <see cref="CityGenerationSettings.seaFloorDepth"/>), a splash
        /// trigger filling the water column from the surface down to that
        /// floor (a full-depth box rather than a thin slab, so no fall speed
        /// can tunnel through it) carrying <see cref="WaterSplashZone"/>, and
        /// the visible surface — a quad at <see cref="CityGenerationSettings.waterLevel"/>
        /// a hair larger than the block so adjacent sea blocks show no seam;
        /// the minimap's top-down camera picks it up for free. A causeway's
        /// flat road cells (the bridge line's two border tiles — flat tiles
        /// carry no collider of their own and there is no slab here) get a
        /// per-cell mini-slab with its top at exactly y = 0, flush with the
        /// ramp foot the way the block slab is on land. Under the surface sits
        /// an opaque SEA-FLOOR quad: the depth-based distance fog (DistanceFog)
        /// fogs a transparent surface by whatever depth lies behind it, and with
        /// nothing behind the water that is the far plane, i.e. the sea would
        /// fog solid a few metres from the shore. The floor gives it a finite
        /// depth a few metres below, which is exactly how deep water should read.
        /// </summary>
        static void BuildWater(CityLayout layout, Vector2Int coord, ChunkData data, GameObject blockGo, float side, List<GameObject> streamed)
        {
            CityGenerationSettings settings = layout.Settings;
            float cell = settings.cellSize;
            float waterLevel = Mathf.Min(settings.waterLevel, -0.05f);
            float floorDepth = Mathf.Min(settings.seaFloorDepth, waterLevel - 0.5f);

            if (settings.generateColliders)
            {
                var floor = blockGo.AddComponent<BoxCollider>();
                floor.center = new Vector3(side * 0.5f, floorDepth - 0.5f, side * 0.5f);
                floor.size = new Vector3(side, 1f, side);

                var splashGo = new GameObject("WaterSplashZone");
                splashGo.transform.SetParent(blockGo.transform, false);
                var splash = splashGo.AddComponent<BoxCollider>();
                splash.isTrigger = true;
                float depth = waterLevel - floorDepth;
                splash.center = new Vector3(side * 0.5f, waterLevel - depth * 0.5f, side * 0.5f);
                splash.size = new Vector3(side, depth, side);
                WaterSplashZone.Configure(splashGo, settings.splashDamage);

                for (int y = 0; y < data.SizeInCells; y++)
                for (int x = 0; x < data.SizeInCells; x++)
                {
                    if (!data.IsRoad(x, y) || data.IsRamp(x, y)) continue;
                    var slab = blockGo.AddComponent<BoxCollider>();
                    slab.center = new Vector3((x + 0.5f) * cell, -0.5f, (y + 0.5f) * cell);
                    slab.size = new Vector3(cell, 1f, cell);
                }
            }

            // The surface. A primitive quad rather than a hand-built mesh:
            // its mesh is the engine's built-in asset, so the prefab keeps a
            // valid reference — a Mesh created here would not be saved with it.
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "WaterSurface";
            Object.DestroyImmediate(surface.GetComponent<Collider>()); // never a surface to drive on
            surface.transform.SetParent(blockGo.transform, false);
            surface.transform.localPosition = new Vector3(side * 0.5f, waterLevel, side * 0.5f);
            surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // the quad faces -Z; tipped to face up
            surface.transform.localScale = new Vector3(side * 1.002f, side * 1.002f, 1f);
            var surfaceRenderer = surface.GetComponent<MeshRenderer>();
            surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (settings.waterMaterial != null) surfaceRenderer.sharedMaterial = settings.waterMaterial;
            streamed.Add(surface);

            // The sea floor, a hair above the floor collider's top so the two
            // never z-fight, and still several metres under the surface.
            GameObject seaFloor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            seaFloor.name = "SeaFloor";
            Object.DestroyImmediate(seaFloor.GetComponent<Collider>()); // the box collider on the block is the physical floor
            seaFloor.transform.SetParent(blockGo.transform, false);
            seaFloor.transform.localPosition = new Vector3(side * 0.5f, floorDepth + 0.02f, side * 0.5f);
            seaFloor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            seaFloor.transform.localScale = new Vector3(side * 1.002f, side * 1.002f, 1f);
            var floorRenderer = seaFloor.GetComponent<MeshRenderer>();
            floorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material floorMaterial = settings.seaFloorMaterial != null ? settings.seaFloorMaterial : settings.waterMaterial;
            if (floorMaterial != null) floorRenderer.sharedMaterial = floorMaterial;
            streamed.Add(seaFloor);
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

            // Features: templates (roundabouts) once at the footprint centre; forks and curves from their parts.
            for (int featureIndex = 0; featureIndex < data.Features.Count; featureIndex++)
            {
                RoadFeature feature = data.Features[featureIndex];
                if (feature.Kind == RoadFeatureKind.Fork)
                {
                    StampFork(settings, stampRoot, feature, rng, pieceScale);
                    continue;
                }
                if (feature.Kind == RoadFeatureKind.Curve)
                {
                    StampCurve(settings, stampRoot, data, feature, featureIndex, rng, pieceScale);
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

        /// <summary>
        /// A curved avenue's visual: rebuild the smooth line the carver
        /// recorded — a Unity spline through every chain cell's shifted centre
        /// (the centre offsets ARE the serialized curve, so a rebake
        /// reproduces it exactly) — then stamp road-straight chords along it
        /// at even arc-length steps, each yawed to its local tangent and
        /// stretched a hair past its step to hide the seams. Flat pieces
        /// carry no colliders and ride the block's ground slab, so the ribbon
        /// is drivable by construction.
        /// </summary>
        static void StampCurve(CityGenerationSettings settings, Transform parent, ChunkData data, RoadFeature curve, int featureIndex, System.Random rng, float pieceScale)
        {
            EdgeMask straightMask = EdgeMaskUtility.DirectionBit(0) | EdgeMaskUtility.DirectionBit(2); // N|S
            if (!TryPickPiece(settings, straightMask, rng, out RoadPieceDefinition piece, out int quarterTurns)) return;

            List<Vector3> points = CurveChainPoints(settings, data, curve, featureIndex);
            if (points.Count < 2) return;

            var spline = new UnityEngine.Splines.Spline();
            foreach (Vector3 p in points)
                spline.Add(new UnityEngine.Splines.BezierKnot((Unity.Mathematics.float3)p), UnityEngine.Splines.TangentMode.AutoSmooth);

            // Dense polyline of the spline, then chords at even arc length.
            int sampleCount = Mathf.Max(16, points.Count * 16);
            var samples = new Vector3[sampleCount + 1];
            for (int i = 0; i <= sampleCount; i++)
                samples[i] = (Vector3)UnityEngine.Splines.SplineUtility.EvaluatePosition(spline, i / (float)sampleCount);

            float step = settings.cellSize * Mathf.Clamp(settings.curveChordFraction, 0.25f, 1f);
            float stretch = step / settings.cellSize * 1.04f;
            float pieceYawBase = quarterTurns * 90f + piece.rotationOffset; // the yaw at which the piece runs N–S
            Vector3 chordScale = RampScale(pieceYawBase, pieceScale, stretch);

            float travelled = 0f, nextChord = step * 0.5f;
            for (int i = 1; i <= sampleCount; i++)
            {
                float segment = Vector3.Distance(samples[i - 1], samples[i]);
                if (segment <= 0.0001f) continue;
                while (travelled + segment >= nextChord)
                {
                    float t = (nextChord - travelled) / segment;
                    Vector3 position = Vector3.Lerp(samples[i - 1], samples[i], t);
                    Vector3 tangent = samples[i] - samples[i - 1];
                    float bearing = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
                    Stamp(settings, parent, piece.prefab, position, bearing + pieceYawBase, chordScale);
                    nextChord += step;
                }
                travelled += segment;
            }
        }

        /// <summary>The curve's chain centres in block-local metres, junction to junction, offsets applied — reconstructed by walking the feature's covered cells from the entry.</summary>
        static List<Vector3> CurveChainPoints(CityGenerationSettings settings, ChunkData data, RoadFeature curve, int featureIndex)
        {
            float cell = settings.cellSize;
            Vector3 CenterOf(Vector2Int c)
            {
                Vector2 shift = data.GetCenterOffset(c.x, c.y) * cell;
                return new Vector3((c.x + 0.5f) * cell + shift.x, 0f, (c.y + 0.5f) * cell + shift.y);
            }

            var points = new List<Vector3> { CenterOf(curve.Origin) };
            Vector2Int previous = curve.Origin;
            Vector2Int current = FindChainNeighbour(data, curve.Origin, curve.Origin, featureIndex);
            while (current != previous)
            {
                points.Add(CenterOf(current));
                Vector2Int next = FindChainNeighbour(data, current, previous, featureIndex);
                previous = current;
                if (next == current) break;
                current = next;
            }
            points.Add(CenterOf(curve.Footprint)); // the exit junction carries no featureIndex — appended explicitly
            return points;
        }

        /// <summary>The 4-neighbour of <paramref name="cell"/> that belongs to this curve's chain and isn't <paramref name="exclude"/>; returns <paramref name="cell"/> when the chain ends.</summary>
        static Vector2Int FindChainNeighbour(ChunkData data, Vector2Int cell, Vector2Int exclude, int featureIndex)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                Vector2Int n = cell + EdgeMaskUtility.Offset(dir);
                if (n == exclude || !data.InBounds(n.x, n.y)) continue;
                if (data.GetFeatureIndex(n.x, n.y) == featureIndex && data.IsRoad(n.x, n.y)) return n;
            }
            return cell;
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
