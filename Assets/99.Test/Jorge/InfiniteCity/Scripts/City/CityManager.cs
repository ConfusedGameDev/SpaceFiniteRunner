using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Entry point and orchestrator of the procedural city. Recalculate works
    /// in edit mode (no play required): it clears previous output, generates a
    /// ChunkData per chunk of the initial grid and stamps socket-matched road
    /// pieces under City/Chunk_{x}_{y}/Roads. Generated objects carry DontSave
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
        [Tooltip("Camera-feel settings for the ChaseCamera attached to the main camera when the car spawns.")]
        public Vehicles.ChaseCameraSettings chaseCameraSettings;

        [TitleGroup("Police")]
        [AssetsOnly]
        [Tooltip("Police car prefab — needs a CarController and a PoliceCarInput on its root. When both police fields are wired, a PatrolManager is spawned at play start.")]
        public GameObject policeCarPrefab;

        [TitleGroup("Police")]
        [Tooltip("All pursuit tunables (fleet size, detection, driving) live on this asset.")]
        public AI.PursuitSettings pursuitSettings;

        /// <summary>Waypoint graph over the generated roads — the AI's navigation source. Rebuilt on every Recalculate; null before the first one.</summary>
        public RoadGraph Graph { get; private set; }

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
            Graph = new RoadGraph(settings.cellSize);
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

            Vehicles.CarFactory.Spawn(carPrefab, chaseCameraSettings, center, yaw);
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

            float pieceScale = settings.roadScaleMultiplier;
            if (settings.scaleToCellSize && settings.pieceNativeSize > 0.0001f)
                pieceScale *= settings.cellSize / settings.pieceNativeSize;

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask == EdgeMask.None) continue;

                if (!TryPickPiece(mask, rng, out var piece, out int quarterTurns))
                {
                    missingMasks.Add(mask);
                    continue;
                }

                // Copies for the closure — the loop variables move on.
                var localPosition = new Vector3((x + 0.5f) * settings.cellSize, 0f, (y + 0.5f) * settings.cellSize);
                var localRotation = Quaternion.Euler(0f, quarterTurns * 90f + piece.rotationOffset, 0f);
                GameObject prefab = piece.prefab;
                float scale = pieceScale;
                void SpawnPiece()
                {
                    if (roadsGo == null) return; // chunk unloaded before its turn in the queue
                    var instance = Instantiate(prefab, roadsGo.transform);
                    ApplyGeneratedFlags(instance);
                    instance.transform.localPosition = localPosition;
                    instance.transform.localRotation = localRotation;
                    if (!Mathf.Approximately(scale, 1f))
                        instance.transform.localScale = Vector3.one * scale;
                }

                if (instant) SpawnPiece();
                else spawnQueue.Enqueue(SpawnPiece);
            }

            foreach (var mask in missingMasks)
                Debug.LogWarning($"CityManager: no road piece matches socket mask [{mask}] — those cells were left empty. Add a matching piece to the settings (dead ends need a single-socket piece).");

            PopulateChunk(chunkGo.transform, data, instant ? null : spawnQueue.Enqueue);
        }

        /// <summary>
        /// Nearest road cell center (and its connection mask) across all built
        /// chunks — used for car spawning and respawning. Prefers cells whose
        /// airspace is physically clear (building meshes can overhang road
        /// cells the grid model calls free); falls back to any road cell so a
        /// cluttered network still returns something. Brute-force over the
        /// grid models — flagged for replacement by the RoadGraph in M5.
        /// </summary>
        public bool TryFindNearestRoadCell(Vector3 from, out Vector3 center, out EdgeMask connections)
        {
            center = default;
            connections = EdgeMask.None;
            if (settings == null) return false;

            Physics.SyncTransforms(); // colliders may have been created this frame
            return PickNearestRoadCell(from, true, ref center, ref connections)
                || PickNearestRoadCell(from, false, ref center, ref connections);
        }

        /// <summary>
        /// Uniformly random road cell across all built chunks (reservoir pick,
        /// single pass — no allocation of the full cell list). Same clearance
        /// preference and fallback as <see cref="TryFindNearestRoadCell"/>.
        /// Not tied to the city's deterministic seed: every press is a fresh spot.
        /// </summary>
        public bool TryGetRandomRoadCell(out Vector3 center, out EdgeMask connections)
        {
            center = default;
            connections = EdgeMask.None;
            if (settings == null) return false;

            Physics.SyncTransforms();
            return PickRandomRoadCell(true, ref center, ref connections)
                || PickRandomRoadCell(false, ref center, ref connections);
        }

        bool PickNearestRoadCell(Vector3 from, bool requireClear, ref Vector3 center, ref EdgeMask connections)
        {
            float bestSqr = float.MaxValue;
            foreach ((Vector3 candidate, EdgeMask mask) in RoadCells())
            {
                float sqr = (candidate - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                if (requireClear && !IsCellClear(candidate)) continue;
                bestSqr = sqr;
                center = candidate;
                connections = mask;
            }
            return bestSqr < float.MaxValue;
        }

        bool PickRandomRoadCell(bool requireClear, ref Vector3 center, ref EdgeMask connections)
        {
            int seen = 0;
            foreach ((Vector3 candidate, EdgeMask mask) in RoadCells())
            {
                if (requireClear && !IsCellClear(candidate)) continue;
                seen++;
                if (Random.Range(0, seen) != 0) continue;
                center = candidate;
                connections = mask;
            }
            return seen > 0;
        }

        /// <summary>Every road cell center + connection mask across the built chunks.</summary>
        IEnumerable<(Vector3 center, EdgeMask connections)> RoadCells()
        {
            float cell = settings.cellSize;
            foreach (var chunk in GetComponentsInChildren<CityChunk>())
            {
                if (chunk.Data == null) continue;
                Vector3 origin = chunk.transform.position;
                for (int y = 0; y < chunk.Data.SizeInCells; y++)
                for (int x = 0; x < chunk.Data.SizeInCells; x++)
                {
                    if (!chunk.Data.IsRoad(x, y)) continue;
                    yield return (origin + new Vector3((x + 0.5f) * cell, 0f, (y + 0.5f) * cell),
                        chunk.Data.GetConnections(x, y));
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

        /// <summary>Yaw (degrees, 0 = +Z) of a random direction the cell actually connects to, so a spawned car launches along the road.</summary>
        static float RandomConnectedYaw(EdgeMask connections)
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
        bool TryPickPiece(EdgeMask target, System.Random rng, out RoadPieceDefinition picked, out int quarterTurns)
        {
            picked = null;
            quarterTurns = 0;
            float totalWeight = 0f;

            foreach (var piece in settings.roadPieces)
            {
                if (piece?.prefab == null) continue;
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
                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                Gizmos.color = mask.ConnectionCount() switch
                {
                    1 => Color.red,                                  // dead end
                    2 => mask.RotateCw(2) == mask
                        ? new Color(0.3f, 0.9f, 1f)                  // straight
                        : Color.yellow,                              // corner
                    3 => Color.magenta,                              // T-junction
                    4 => Color.white,                                // crossroad
                    _ => Color.grey,
                };
                Vector3 cellCenter = origin + new Vector3((x + 0.5f) * cell, 0.05f, (y + 0.5f) * cell);
                Gizmos.DrawCube(cellCenter, new Vector3(cell * 0.85f, 0.05f, cell * 0.85f));
            }
        }
    }
}
