using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Entry point and orchestrator of the procedural city. Recalculate works
    /// in edit mode (no play required): it clears previous output, generates a
    /// ChunkData per chunk of the initial grid and stamps socket-matched road
    /// pieces under City/Chunk_{x}_{y}/Roads — plus the features the placer
    /// carved into the data: multi-cell templates once at their footprint
    /// centre, ramp chains per run, decks and pillars. Generated objects carry DontSave
    /// flags — they are never written into the scene file and are rebuilt from
    /// seed + settings on demand (play mode regenerates automatically).
    /// Gizmos overlay the grid model: road cells colored by socket count,
    /// chunk borders, per-chunk seed labels.
    /// </summary>
    public class CityManager : MonoBehaviour
    {
        const int SaltPiecePick = 404;

        [Required, InlineEditor]
        [Tooltip("All generation tunables live on this asset — add new knobs there, not here.")]
        public CityGenerationSettings settings;

        [TitleGroup("Player car")]
        [AssetsOnly]
        [Tooltip("Car prefab dropped by Create Car — needs a CarController and an ICarInput on its root.")]
        public GameObject carPrefab;

        [TitleGroup("Player car")]
        [Tooltip("Camera-feel settings for the Cinemachine orbit rig set up when the car spawns.")]
        public Vehicles.OrbitCameraSettings orbitCameraSettings;

        [TitleGroup("Police")]
        [AssetsOnly]
        [Tooltip("Police car prefab — needs a CarController and a PoliceCarInput on its root. When both police fields are wired, a PatrolManager is spawned at play start.")]
        public GameObject policeCarPrefab;

        [TitleGroup("Police")]
        [Tooltip("All pursuit tunables (fleet size, detection, driving) live on this asset.")]
        public AI.PursuitSettings pursuitSettings;

        [TitleGroup("Traffic")]
        [Tooltip("Civilian traffic tunables — when assigned, a TrafficManager is spawned at play start (vehicles exist only within its active radius of the player).")]
        public AI.TrafficSettings trafficSettings;

        [TitleGroup("UI")]
        [Tooltip("Circular radar settings — when assigned, a minimap is spawned at play start (bottom-right, GTA-style).")]
        public UI.MinimapSettings minimapSettings;

        [TitleGroup("UI")]
        [Tooltip("Speedometer settings — when assigned, an analog gauge is spawned at play start (bottom-left).")]
        public UI.SpeedometerSettings speedometerSettings;

        /// <summary>Waypoint graph over the generated roads — the AI's navigation source. Rebuilt on every Recalculate; null before the first one.</summary>
        public RoadGraph Graph { get; private set; }

        /// <summary>Uniform scale applied to every spawned road piece: cell fit (cellSize ÷ native footprint) × the extra multiplier.</summary>
        public float PieceScale => settings != null ? settings.PieceScale : 1f;

        /// <summary>
        /// World height of an overpass deck's LANE above the drivable ground
        /// plane. Measured from the sunk city, so it tracks both the piece
        /// scale and the surface offset and lands on the deck's asphalt.
        /// </summary>
        public float DeckWorldHeight =>
            settings != null ? (settings.DeckNativeHeight - settings.RoadSurfaceNativeHeight) * PieceScale : 0f;

        /// <summary>
        /// How far every stamped piece is sunk so its driving lane lands on the
        /// chunk ground slab at y = 0.
        ///
        /// The Kenney tiles carry their asphalt above their pivot (lane 0.01,
        /// curb 0.02 native). Flat tiles have no collider and ride the slab,
        /// but ramps and decks carry real mesh colliders — so stamping
        /// everything at y = 0 left the flat plane a whole lane-height BELOW
        /// the ramp collider, i.e. a curb-high step at the foot of every ramp
        /// (and at the mouth of every bridge underpass). Sinking the art is
        /// the one-line-per-funnel fix: "y = 0 is the drivable surface" then
        /// holds everywhere, so the slab, the road graph, the cell-clearance
        /// boxes, the spawn runways and the gizmos all stay as they are.
        /// </summary>
        public float RoadSurfaceHeight => settings != null ? settings.RoadSurfaceNativeHeight * PieceScale : 0f;

        [TitleGroup("Gizmos")]
        [Tooltip("Draw the grid model overlay (road cells, chunk borders, seed labels).")]
        public bool drawGizmos = true;

        readonly Dictionary<Vector2Int, CityChunk> chunkMap = new();
        readonly Queue<System.Action> spawnQueue = new();
        Vehicles.CarController streamingTarget;
        float anchorRefreshTimer;

        void Awake()
        {
            // Generated content is never saved with the scene, so a play-mode
            // session always starts empty and rebuilds from seed + settings.
            if (!Application.isPlaying) return;
            Recalculate();

            // Police fleet: the manager is spawned, not scene-placed, so any
            // scene with a wired CityManager gets patrols with zero setup.
            if (policeCarPrefab != null && pursuitSettings != null)
            {
                var managerGo = new GameObject("PatrolManager");
                var patrolManager = managerGo.AddComponent<AI.PatrolManager>();
                patrolManager.settings = pursuitSettings;
                patrolManager.policeCarPrefab = policeCarPrefab;
            }

            // Civilian traffic: same spawn-when-wired pattern as the police.
            if (trafficSettings != null)
            {
                var trafficGo = new GameObject("TrafficManager");
                trafficGo.AddComponent<AI.TrafficManager>().settings = trafficSettings;
            }

            // HUD pieces: same deal — spawned when wired, each builds its own canvas.
            if (minimapSettings != null)
            {
                var minimapGo = new GameObject("Minimap");
                minimapGo.AddComponent<UI.Minimap>().settings = minimapSettings;
            }
            if (speedometerSettings != null)
            {
                var speedometerGo = new GameObject("Speedometer");
                speedometerGo.AddComponent<UI.Speedometer>().settings = speedometerSettings;
            }
        }

        // ------------------------------------------------------------- buttons

        [TitleGroup("Actions")]
        [Button("Recalculate", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void Recalculate()
        {
            if (settings == null)
            {
                Debug.LogWarning("CityManager: assign a CityGenerationSettings asset first.");
                return;
            }

            // Fresh seed every press — unless a saved seed is locked in on the settings.
            settings.PrepareSeedForRecalculate();

            Clear();

            // The grid model is axis-aligned and unscaled: cell size is in
            // world metres and the clearance boxes are world-axis boxes. A
            // moved root is fine (the graph is built around it), a turned or
            // scaled one is not, and the failure is silent — so say so.
            if (transform.rotation != Quaternion.identity || transform.localScale != Vector3.one)
                Debug.LogWarning($"CityManager: the city root is rotated or scaled ({transform.rotation.eulerAngles}, " +
                                 $"{transform.localScale}). The road graph and cell clearance checks assume an " +
                                 "axis-aligned, unscaled root — move it if you need to, but leave rotation and scale alone.", this);

            // The graph hands out world positions cars drive to, and chunks are
            // children of this transform — so it has to start where they do.
            Graph = new RoadGraph(settings.cellSize, DeckWorldHeight, transform.position);
            int half = settings.initialCitySizeInChunks / 2;
            int size = settings.initialCitySizeInChunks;
            for (int cy = -half; cy < size - half; cy++)
            for (int cx = -half; cx < size - half; cx++)
                EnsureChunk(new Vector2Int(cx, cy), instant: true);
        }

        // ----------------------------------------------------------- streaming

        void Update()
        {
            if (!Application.isPlaying || settings == null || !settings.endlessStreaming || Graph == null) return;
            StreamAroundAnchor();
            ProcessSpawnQueue();
        }

        /// <summary>
        /// Keep loadRadius chunks alive around the anchor's chunk; unload
        /// anything beyond loadRadius + unloadPadding. The padding is the
        /// hysteresis: a chunk loads at one distance and unloads at a larger
        /// one, so cruising along a border can't thrash.
        /// </summary>
        void StreamAroundAnchor()
        {
            if (!TryGetStreamingAnchor(out Vector3 anchor)) return;

            float side = settings.chunkSizeInCells * settings.cellSize;
            var center = new Vector2Int(Mathf.FloorToInt(anchor.x / side), Mathf.FloorToInt(anchor.z / side));

            int load = settings.loadRadiusInChunks;
            for (int dy = -load; dy <= load; dy++)
            for (int dx = -load; dx <= load; dx++)
                EnsureChunk(new Vector2Int(center.x + dx, center.y + dy), instant: false);

            int unload = load + settings.unloadPaddingInChunks;
            List<Vector2Int> toUnload = null;
            foreach (var pair in chunkMap)
            {
                int distance = Mathf.Max(Mathf.Abs(pair.Key.x - center.x), Mathf.Abs(pair.Key.y - center.y));
                if (distance <= unload) continue;
                (toUnload ??= new List<Vector2Int>()).Add(pair.Key);
            }
            if (toUnload == null) return;
            foreach (Vector2Int coord in toUnload) UnloadChunk(coord);
        }

        /// <summary>Drain the time-slice budget: streamed chunks materialize a few objects per frame instead of hitching on one.</summary>
        void ProcessSpawnQueue()
        {
            int budget = settings.maxSpawnsPerFrame;
            while (budget-- > 0 && spawnQueue.Count > 0)
                spawnQueue.Dequeue().Invoke();
        }

        void EnsureChunk(Vector2Int coord, bool instant)
        {
            if (chunkMap.ContainsKey(coord)) return;
            var data = RoadNetworkGenerator.Generate(settings, coord);
            Graph?.RegisterChunk(data);
            BuildChunk(coord, data, instant);
        }

        void UnloadChunk(Vector2Int coord)
        {
            if (!chunkMap.TryGetValue(coord, out CityChunk chunk)) return;
            chunkMap.Remove(coord);
            if (chunk == null) return;
            if (chunk.Data != null) Graph?.UnregisterChunk(chunk.Data);
            Destroy(chunk.gameObject);
        }

        /// <summary>Stream around the player's car; before one exists, around the main camera.</summary>
        bool TryGetStreamingAnchor(out Vector3 anchor)
        {
            anchorRefreshTimer -= Time.deltaTime;
            if (streamingTarget == null && anchorRefreshTimer <= 0f)
            {
                anchorRefreshTimer = 1f;
                streamingTarget = AI.PatrolManager.FindPlayerCar();
            }
            if (streamingTarget != null)
            {
                anchor = streamingTarget.transform.position;
                return true;
            }
            var camera = Camera.main;
            if (camera != null)
            {
                anchor = camera.transform.position;
                return true;
            }
            anchor = default;
            return false;
        }

        [TitleGroup("Actions")]
        [Button("Repopulate", ButtonSizes.Large)]
        [Tooltip("Rebuild buildings only, keeping the current roads — much faster iteration on the building set than a full Recalculate.")]
        public void Repopulate()
        {
            foreach (var chunk in GetComponentsInChildren<CityChunk>())
            {
                if (chunk.Data == null)
                {
                    Debug.LogWarning("CityManager: chunk data was lost (domain reload) — press Recalculate instead.");
                    return;
                }
                var oldBuildings = chunk.transform.Find("Buildings");
                if (oldBuildings != null)
                {
                    if (Application.isPlaying) Destroy(oldBuildings.gameObject);
                    else DestroyImmediate(oldBuildings.gameObject);
                }
                PopulateChunk(chunk.transform, chunk.Data);
            }
        }

        /// <summary>
        /// Drop the player car on a random road cell, already rolling
        /// (CarConfig.spawnSpeedKmh) and facing along the road, with the chase
        /// camera retargeted. The factory removes any existing car first, so
        /// pressing this repeatedly always leaves exactly one car.
        /// </summary>
        [TitleGroup("Actions")]
        [Button("Create Car", ButtonSizes.Large), GUIColor(0.6f, 0.8f, 1f)]
        [EnableIf("@UnityEngine.Application.isPlaying")]
        public void CreateCar()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("CityManager: Create Car works in play mode — the car is physics-driven.");
                return;
            }
            if (carPrefab == null)
            {
                Debug.LogWarning("CityManager: assign a car prefab first.");
                return;
            }
            // Preferred: a straight piece with a clear runway ahead, so the
            // rolling start never launches into a corner or junction.
            if (!TryGetRandomStraightSpawn(SpawnRunwayCells(), out Vector3 center, out float yaw))
            {
                if (!TryGetRandomRoadCell(out center, out EdgeMask connections))
                {
                    Debug.LogWarning("CityManager: no road cells found — Recalculate first.");
                    return;
                }
                yaw = RandomConnectedYaw(connections);
                Debug.LogWarning($"CityManager: no straight stretch with {SpawnRunwayCells()} clear cells ahead — spawning on a random road cell instead. Longer arterials (lower arterialSpacing jitter) or a shorter spawnRunwayCells fix this.");
            }

            Vehicles.CarFactory.Spawn(carPrefab, orbitCameraSettings, center, yaw);
        }

        /// <summary>Runway length demanded by the car prefab's config (CarConfig.spawnRunwayCells), with a safe default when unwired.</summary>
        int SpawnRunwayCells()
        {
            var controller = carPrefab != null ? carPrefab.GetComponent<Vehicles.CarController>() : null;
            return controller != null && controller.config != null ? controller.config.spawnRunwayCells : 4;
        }

        [TitleGroup("Actions")]
        [Button("Clear", ButtonSizes.Large)]
        public void Clear()
        {
            chunkMap.Clear();
            spawnQueue.Clear();
            // Also sweep by component so orphans from before a domain reload are found.
            var stale = GetComponentsInChildren<CityChunk>(true);
            foreach (var chunk in stale)
            {
                if (chunk == null) continue;
                if (Application.isPlaying) Destroy(chunk.gameObject);
                else DestroyImmediate(chunk.gameObject);
            }
        }

        // ------------------------------------------------------------ building

        /// <summary>
        /// Build one chunk. Cheap scaffolding (root, marker, ground collider)
        /// is always immediate so physics and road-cell queries work right
        /// away; the many Instantiates (road pieces, buildings) run inline
        /// when <paramref name="instant"/>, otherwise through the streamer's
        /// per-frame spawn budget. All picking decisions and RNG draws happen
        /// here either way, so streamed chunks equal instant ones.
        /// </summary>
        void BuildChunk(Vector2Int coord, ChunkData data, bool instant)
        {
            var chunkGo = new GameObject($"Chunk_{coord.x}_{coord.y}");
            ApplyGeneratedFlags(chunkGo);
            chunkGo.transform.SetParent(transform, false);
            chunkGo.transform.localPosition = new Vector3(
                coord.x * settings.chunkSizeInCells * settings.cellSize,
                0f,
                coord.y * settings.chunkSizeInCells * settings.cellSize);

            var chunk = chunkGo.AddComponent<CityChunk>();
            chunk.Initialize(coord, data);
            chunkMap[coord] = chunk;

            if (settings.generateColliders)
            {
                // One flat slab per chunk, top at road level (y = 0) — roads and
                // lots alike are drivable; buildings block with their own boxes.
                // Immediate, so a car never drives onto a floorless chunk.
                // Every stamped piece is sunk by RoadSurfaceHeight so its
                // asphalt lands exactly here, which is what keeps the ramps
                // (they carry real colliders) flush with the flat tiles (they
                // carry none and ride this slab).
                float side = settings.chunkSizeInCells * settings.cellSize;
                var ground = chunkGo.AddComponent<BoxCollider>();
                ground.center = new Vector3(side * 0.5f, -0.5f, side * 0.5f);
                ground.size = new Vector3(side, 1f, side);
            }

            var roadsGo = new GameObject("Roads");
            ApplyGeneratedFlags(roadsGo);
            roadsGo.transform.SetParent(chunkGo.transform, false);

            // Piece picking gets its own deterministic stream, separate from layout.
            var rng = new System.Random(DeterministicHash.Combine(settings.globalSeed, SaltPiecePick, coord.x, coord.y));
            var missingMasks = new HashSet<EdgeMask>();

            float pieceScale = PieceScale;
            Transform stampRoot = roadsGo.transform;
            System.Action<System.Action> schedule = instant
                ? (System.Action<System.Action>)(spawn => spawn())
                : spawnQueue.Enqueue;
            RoadPieceDefinition pillar = settings.PillarPiece;

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (data.IsCovered(x, y)) continue; // a multi-cell piece owns this cell — stamped below
                var cellCenter = new Vector3((x + 0.5f) * settings.cellSize, 0f, (y + 0.5f) * settings.cellSize);

                // Ramp runs are stamped once, from their foot cell.
                if (data.IsRamp(x, y))
                {
                    if (data.GetRampStep(x, y) == 0) StampRampRun(stampRoot, data, x, y, pieceScale, schedule);
                    continue;
                }

                // Overpass deck above; a pillar where no street runs underneath.
                if (data.HasDeck(x, y))
                {
                    EdgeMask upper = data.GetUpperConnections(x, y);
                    if (TryPickPiece(upper, rng, out var deck, out int deckTurns, RoadPieceRole.Deck))
                        Stamp(stampRoot, deck.prefab, cellCenter, deckTurns * 90f + deck.rotationOffset, Vector3.one * pieceScale, schedule);
                    else
                        missingMasks.Add(upper);
                    bool selfSupporting = deck != null && deck.includesUnderpass;
                    if (data.IsReserved(x, y) && pillar != null && !selfSupporting)
                        Stamp(stampRoot, pillar.prefab, cellCenter, pillar.rotationOffset, Vector3.one * pieceScale, schedule);
                    // Kenney's road-bridge already models its supports and the street underneath — a second ground piece would z-fight it.
                    if (selfSupporting) continue;
                }

                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask == EdgeMask.None) continue;

                if (!TryPickPiece(mask, rng, out var piece, out int quarterTurns))
                {
                    missingMasks.Add(mask);
                    continue;
                }
                Stamp(stampRoot, piece.prefab, cellCenter, quarterTurns * 90f + piece.rotationOffset, Vector3.one * pieceScale, schedule);
            }

            // Features: templates (roundabouts) once at the footprint centre; forks from their parts.
            foreach (RoadFeature feature in data.Features)
            {
                if (feature.Kind == RoadFeatureKind.Fork)
                {
                    StampFork(stampRoot, feature, rng, pieceScale, schedule);
                    continue;
                }
                RoadPieceDefinition piece = feature.PieceIndex >= 0 && feature.PieceIndex < settings.roadPieces.Count ? settings.roadPieces[feature.PieceIndex] : null;
                if (piece?.prefab == null) continue;
                var center = new Vector3(
                    (feature.Origin.x + feature.Footprint.x * 0.5f) * settings.cellSize,
                    0f,
                    (feature.Origin.y + feature.Footprint.y * 0.5f) * settings.cellSize);
                Stamp(stampRoot, piece.prefab, center, feature.QuarterTurns * 90f + piece.rotationOffset, Vector3.one * pieceScale, schedule);
            }

            foreach (var mask in missingMasks)
                Debug.LogWarning($"CityManager: no road piece matches socket mask [{mask}] — those cells were left empty. Add a matching piece to the settings (dead ends need a single-socket piece, overpasses a Deck piece).");

            PopulateChunk(chunkGo.transform, data, instant ? null : spawnQueue.Enqueue);
        }

        /// <summary>
        /// Queue (or run) one piece instantiation. All placement values are
        /// resolved here, so a streamed chunk equals an instant one; the
        /// closure only checks that the chunk still exists when its turn comes.
        /// </summary>
        void Stamp(Transform parent, GameObject prefab, Vector3 localPosition, float yaw, Vector3 localScale, System.Action<System.Action> schedule)
        {
            var localRotation = Quaternion.Euler(0f, yaw, 0f);
            // Sink the piece so its lane, not its pivot, sits on the drivable
            // plane — see RoadSurfaceHeight. Resolved out here with every other
            // placement value, so a streamed chunk equals an instant one.
            localPosition.y -= RoadSurfaceHeight;
            schedule(() =>
            {
                if (parent == null) return; // chunk unloaded before its turn in the queue
                var instance = Instantiate(prefab, parent);
                ApplyGeneratedFlags(instance);
                instance.transform.localPosition = localPosition;
                instance.transform.localRotation = localRotation;
                instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, localScale);
            });
        }

        /// <summary>
        /// A ramp run is rampLength cells from its foot (step 0) uphill; the
        /// settings' ramp chain (street → deck links) is spread evenly along
        /// it, each link stretched or compressed along its uphill axis so any
        /// chain length fits any run length.
        /// </summary>
        void StampRampRun(Transform parent, ChunkData data, int footX, int footY, float pieceScale, System.Action<System.Action> schedule)
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
                Stamp(parent, chain[i].prefab, center, direction * 90f + chain[i].rotationOffset,
                    RampScale(chain[i].rotationOffset, pieceScale, cellsPerLink), schedule);
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
        void StampFork(Transform parent, RoadFeature fork, System.Random rng, float pieceScale, System.Action<System.Action> schedule)
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
            if (TryPickPiece(perpPair | EdgeMaskUtility.DirectionBit(dir), rng, out var tee, out int teeTurns))
                Stamp(parent, tee.prefab, SeamAt(fork.Origin), teeTurns * 90f + tee.rotationOffset, scale, schedule);
            if (TryPickPiece(perpPair, rng, out var half, out int halfTurns, RoadPieceRole.HalfStraight))
            {
                float yaw = halfTurns * 90f + half.rotationOffset;
                Stamp(parent, half.prefab, CellCenter(fork.Origin) - toTwin * 0.5f, yaw, scale, schedule);
                Stamp(parent, half.prefab, CellCenter(fork.Origin + p) + toTwin * 0.5f, yaw, scale, schedule);
            }

            // Stem straights on the seam.
            for (int i = 1; i <= stem; i++)
            {
                if (TryPickPiece(axisPair, rng, out var straight, out int turns))
                    Stamp(parent, straight.prefab, SeamAt(fork.Origin + f * i), turns * 90f + straight.rotationOffset, scale, schedule);
            }

            // The split: one entrance from the junction side, two exits along the street.
            Stamp(parent, split.prefab, SeamAt(fork.Origin + f * (stem + 1)), (dir - 1) * 90f + split.rotationOffset, scale, schedule);
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
        /// Nearest road cell center (and its connection mask) across all built
        /// chunks — used for car spawning and respawning. Prefers cells whose
        /// airspace is physically clear (building meshes can overhang road
        /// cells the grid model calls free); falls back to any road cell so a
        /// cluttered network still returns something. Brute-force over the
        /// grid models — flagged for replacement by the RoadGraph in M5.
        /// </summary>
        public bool TryFindNearestRoadCell(Vector3 from, out Vector3 center, out EdgeMask connections, bool groundOnly = false)
        {
            center = default;
            connections = EdgeMask.None;
            if (settings == null) return false;

            Physics.SyncTransforms(); // colliders may have been created this frame
            return PickNearestRoadCell(from, true, groundOnly, ref center, ref connections)
                || PickNearestRoadCell(from, false, groundOnly, ref center, ref connections);
        }

        /// <summary>
        /// Uniformly random road cell across all built chunks (reservoir pick,
        /// single pass — no allocation of the full cell list). Same clearance
        /// preference and fallback as <see cref="TryFindNearestRoadCell"/>.
        /// Not tied to the city's deterministic seed: every press is a fresh spot.
        /// </summary>
        public bool TryGetRandomRoadCell(out Vector3 center, out EdgeMask connections, bool groundOnly = false)
        {
            center = default;
            connections = EdgeMask.None;
            if (settings == null) return false;

            Physics.SyncTransforms();
            return PickRandomRoadCell(true, groundOnly, ref center, ref connections)
                || PickRandomRoadCell(false, groundOnly, ref center, ref connections);
        }

        bool PickNearestRoadCell(Vector3 from, bool requireClear, bool groundOnly, ref Vector3 center, ref EdgeMask connections)
        {
            float best = float.MaxValue;
            foreach ((Vector3 candidate, EdgeMask mask, bool flatGround) in RoadCells())
            {
                if (groundOnly && !flatGround) continue;
                // Height counts triple, so a spot under a bridge resolves to the street, not the deck above it.
                Vector3 delta = candidate - from;
                float score = delta.x * delta.x + delta.z * delta.z + 9f * delta.y * delta.y;
                if (score >= best) continue;
                if (requireClear && !IsCellClear(candidate)) continue;
                best = score;
                center = candidate;
                connections = mask;
            }
            return best < float.MaxValue;
        }

        bool PickRandomRoadCell(bool requireClear, bool groundOnly, ref Vector3 center, ref EdgeMask connections)
        {
            int seen = 0;
            foreach ((Vector3 candidate, EdgeMask mask, bool flatGround) in RoadCells())
            {
                if (groundOnly && !flatGround) continue;
                if (requireClear && !IsCellClear(candidate)) continue;
                seen++;
                if (Random.Range(0, seen) != 0) continue;
                center = candidate;
                connections = mask;
            }
            return seen > 0;
        }

        /// <summary>
        /// Every road spot across the built chunks: surface centre (deck
        /// height for overpasses, part-way up for ramps), connection mask and
        /// whether it is flat ground (the only kind cars spawn on). Served by
        /// the RoadGraph when it exists; after a domain reload the graph is
        /// gone but the chunk markers may still hold their data, so the grid
        /// scan remains as the fallback.
        /// </summary>
        IEnumerable<(Vector3 center, EdgeMask connections, bool flatGround)> RoadCells()
        {
            if (Graph != null)
            {
                foreach (var pair in Graph.Nodes)
                    yield return (pair.Value.Center, pair.Value.Mask, pair.Key.Level == 0 && !pair.Value.IsRamp);
                yield break;
            }

            float cell = settings.cellSize;
            foreach (var chunk in GetComponentsInChildren<CityChunk>())
            {
                if (chunk.Data == null) continue;
                Vector3 origin = chunk.transform.position;
                for (int y = 0; y < chunk.Data.SizeInCells; y++)
                for (int x = 0; x < chunk.Data.SizeInCells; x++)
                {
                    if (!chunk.Data.IsRoad(x, y)) continue;
                    Vector2 shift = chunk.Data.GetCenterOffset(x, y) * cell;
                    yield return (origin + new Vector3((x + 0.5f) * cell + shift.x, 0f, (y + 0.5f) * cell + shift.y),
                        chunk.Data.GetConnections(x, y), !chunk.Data.IsRamp(x, y));
                }
            }
        }

        /// <summary>
        /// True when nothing solid (building box, overhanging mesh collider,
        /// the previous car) intrudes into the cell's airspace. The checked
        /// box covers 90% of the cell from just above the ground slab up to
        /// ~4 m, so the chunk ground colliders (top at y = 0) never trip it
        /// and tiny roof overhangs at the very border are tolerated.
        /// </summary>
        public bool IsCellClear(Vector3 cellCenter)
        {
            float half = settings.cellSize * 0.45f;
            var halfExtents = new Vector3(half, 2f, half);
            return !Physics.CheckBox(cellCenter + Vector3.up * (halfExtents.y + 0.05f), halfExtents,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Random spawn pose on a straight road cell with at least
        /// <paramref name="runwayCells"/> more straight cells ahead in the
        /// facing direction — a launch runway, never a corner, junction or
        /// building cell. Reservoir-sampled so every qualifying (cell,
        /// direction) pair is equally likely.
        /// </summary>
        public bool TryGetRandomStraightSpawn(int runwayCells, out Vector3 center, out float yaw)
        {
            center = default;
            yaw = 0f;
            if (settings == null) return false;

            Physics.SyncTransforms();
            int seen = 0;
            foreach ((Vector3 candidate, float candidateYaw) in StraightSpawns(runwayCells))
            {
                seen++;
                if (Random.Range(0, seen) != 0) continue;
                center = candidate;
                yaw = candidateYaw;
            }
            return seen > 0;
        }

        /// <summary>Nearest qualifying straight-runway spawn pose (see <see cref="TryGetRandomStraightSpawn"/>).</summary>
        public bool TryFindNearestStraightSpawn(Vector3 from, int runwayCells, out Vector3 center, out float yaw)
        {
            center = default;
            yaw = 0f;
            if (settings == null) return false;

            Physics.SyncTransforms();
            float bestSqr = float.MaxValue;
            foreach ((Vector3 candidate, float candidateYaw) in StraightSpawns(runwayCells))
            {
                float sqr = (candidate - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                center = candidate;
                yaw = candidateYaw;
            }
            return bestSqr < float.MaxValue;
        }

        /// <summary>
        /// Every (cell center, yaw) pose sitting on a straight road piece with
        /// a clear straight runway ahead — clear both in the grid model (only
        /// straight pieces, same axis) and physically (no building geometry
        /// intruding into the spawn cell or any runway cell; meshes can
        /// overhang cells the grid calls road). Runways are checked within a
        /// single chunk's grid — cells whose runway would cross a chunk border
        /// simply don't qualify, which plenty of arterial cells survive.
        /// </summary>
        IEnumerable<(Vector3 center, float yaw)> StraightSpawns(int runwayCells)
        {
            float cell = settings.cellSize;
            foreach (var chunk in GetComponentsInChildren<CityChunk>())
            {
                if (chunk.Data == null) continue;
                ChunkData data = chunk.Data;
                Vector3 origin = chunk.transform.position;
                for (int y = 0; y < data.SizeInCells; y++)
                for (int x = 0; x < data.SizeInCells; x++)
                {
                    if (!IsStraightRoad(data, x, y, out bool northSouth)) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        int dir = side * 2 + (northSouth ? 0 : 1); // N/S or E/W, both ways
                        if (!HasStraightRunway(data, x, y, dir, runwayCells)) continue;
                        if (!IsRunwayPhysicallyClear(origin, x, y, dir, runwayCells)) continue;
                        yield return (origin + new Vector3((x + 0.5f) * cell, 0f, (y + 0.5f) * cell), dir * 90f);
                    }
                }
            }
        }

        /// <summary>Physics pass over spawn cell + runway: every cell's airspace must be free of intruding geometry. Data checks run first, so this only ever probes short qualifying stretches.</summary>
        bool IsRunwayPhysicallyClear(Vector3 origin, int x, int y, int dir, int runwayCells)
        {
            float cell = settings.cellSize;
            Vector2Int step = EdgeMaskUtility.Offset(dir);
            for (int i = 0; i <= runwayCells; i++)
            {
                Vector3 center = origin + new Vector3(
                    (x + step.x * i + 0.5f) * cell, 0f, (y + step.y * i + 0.5f) * cell);
                if (!IsCellClear(center)) return false;
            }
            return true;
        }

        static bool IsStraightRoad(ChunkData data, int x, int y, out bool northSouth)
        {
            northSouth = false;
            if (!data.InBounds(x, y) || !data.IsRoad(x, y)) return false;
            if (data.IsRamp(x, y) || data.HasDeck(x, y) || data.HasCenterOffset(x, y)) return false; // slopes, underpasses and seam roads are no launch runway
            EdgeMask mask = data.GetConnections(x, y);
            if (mask == (EdgeMask.North | EdgeMask.South))
            {
                northSouth = true;
                return true;
            }
            return mask == (EdgeMask.East | EdgeMask.West);
        }

        static bool HasStraightRunway(ChunkData data, int x, int y, int dir, int length)
        {
            Vector2Int step = EdgeMaskUtility.Offset(dir);
            bool wantNorthSouth = dir == 0 || dir == 2;
            for (int i = 1; i <= length; i++)
            {
                if (!IsStraightRoad(data, x + step.x * i, y + step.y * i, out bool northSouth) || northSouth != wantNorthSouth)
                    return false;
            }
            return true;
        }

        /// <summary>Yaw (degrees, 0 = +Z) of a random direction the cell actually connects to, so a spawned (or recovered) car launches along the road.</summary>
        public static float RandomConnectedYaw(EdgeMask connections)
        {
            int count = 0;
            int picked = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((connections & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                count++;
                if (Random.Range(0, count) == 0) picked = dir;
            }
            return count > 0 ? picked * 90f : 0f;
        }

        void PopulateChunk(Transform chunkRoot, ChunkData data, System.Action<System.Action> scheduler = null)
        {
            if (settings.buildingSet == null) return;
            var buildingsGo = new GameObject("Buildings");
            ApplyGeneratedFlags(buildingsGo);
            buildingsGo.transform.SetParent(chunkRoot, false);
            Population.CityPopulator.Populate(settings, data, buildingsGo.transform, scheduler);
        }

        /// <summary>
        /// Weighted pick among every (piece, rotation) pair whose rotated socket
        /// mask equals the cell's mask. Symmetric pieces match at several
        /// rotations; each counts as its own candidate so weights stay honest.
        /// </summary>
        bool TryPickPiece(EdgeMask target, System.Random rng, out RoadPieceDefinition picked, out int quarterTurns, RoadPieceRole role = RoadPieceRole.Standard)
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

        public static void ApplyGeneratedFlags(GameObject go)
        {
            // DontSave keeps edit-mode output out of the scene file; in play
            // mode normal scene teardown handles cleanup, so don't set it there
            // (DontSave objects would survive the reload and leak).
            go.hideFlags = Application.isPlaying
                ? HideFlags.NotEditable
                : HideFlags.DontSave | HideFlags.NotEditable;
        }

        // -------------------------------------------------------------- gizmos

        void OnDrawGizmos()
        {
            if (!drawGizmos || settings == null) return;

            // After a domain reload the runtime chunk list is empty but the
            // spawned objects may still exist — read data off the markers.
            foreach (var chunk in GetComponentsInChildren<CityChunk>())
            {
                if (chunk.Data == null) continue;
                DrawChunkGizmos(chunk);
            }
        }

        void DrawChunkGizmos(CityChunk chunk)
        {
            ChunkData data = chunk.Data;
            float cell = settings.cellSize;
            Vector3 origin = chunk.transform.position;
            float sideMeters = data.SizeInCells * cell;

            // Chunk border + seed label.
            Gizmos.color = Color.green;
            Vector3 center = origin + new Vector3(sideMeters * 0.5f, 0f, sideMeters * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(sideMeters, 0.1f, sideMeters));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                origin + new Vector3(0.5f, 0f, 0.5f) * cell,
                $"Chunk {chunk.Coord}  seed {RoadNetworkGenerator.ChunkSeed(settings, chunk.Coord)}");
#endif

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                Vector3 cellCenter = origin + new Vector3((x + 0.5f) * cell, 0.05f, (y + 0.5f) * cell);
                var slab = new Vector3(cell * 0.85f, 0.05f, cell * 0.85f);

                if (data.HasDeck(x, y))
                {
                    // Upper level at deck height; the street underneath (if any) keeps its own slab below.
                    Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
                    Gizmos.DrawCube(cellCenter + Vector3.up * DeckWorldHeight, slab);
                }
                if (data.IsReserved(x, y))
                {
                    Gizmos.color = new Color(0.35f, 0.35f, 0.35f, 0.6f);  // feature-owned, no road, no building
                    Gizmos.DrawCube(cellCenter, slab);
                    continue;
                }
                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                Gizmos.color = data.IsRamp(x, y)
                    ? new Color(1f, 0.55f, 0.1f)                         // ramp
                    : mask.ConnectionCount() switch
                    {
                        1 => Color.red,                                  // dead end
                        2 => mask.RotateCw(2) == mask
                            ? new Color(0.3f, 0.9f, 1f)                  // straight
                            : Color.yellow,                              // corner
                        3 => Color.magenta,                              // T-junction
                        4 => Color.white,                                // crossroad
                        _ => Color.grey,
                    };
                if (data.IsRamp(x, y)) cellCenter.y += DeckWorldHeight * data.RampHeight01(x, y);
                Vector2 shift = data.GetCenterOffset(x, y) * cell; // fork seam roads draw where their node is
                cellCenter += new Vector3(shift.x, 0f, shift.y);
                Gizmos.DrawCube(cellCenter, slab);
            }
        }
    }
}
