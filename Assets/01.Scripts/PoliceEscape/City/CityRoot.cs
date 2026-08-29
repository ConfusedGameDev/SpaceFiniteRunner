using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Root component of the baked city prefab. Carries copies of everything
    /// the runtime needs (grid dimensions, cell size, deck height, seed), the
    /// two persistent sockets (<see cref="additionalItems"/> — designer
    /// content that survives every rebake — and <see cref="defaultVehicles"/>
    /// — parked cars that wake on player proximity), and rebuilds the
    /// <see cref="RoadGraph"/> from the blocks' serialized layouts on demand:
    /// pure array walks, no generator, no RNG — which is the entire point of
    /// baking. All spatial queries (nearest road cell, straight spawn
    /// runways, cell clearance) live here; <see cref="CityManager"/> stays a
    /// thin facade over them so its ~15 consumers never changed. The graph
    /// deliberately never shrinks: geometry is activity-culled by
    /// <see cref="CityStreamer"/> (content roots off, block objects on), so
    /// the graph and every block stay, and AI plans can never go stale.
    /// </summary>
    public class CityRoot : MonoBehaviour
    {
        [ReadOnly, Tooltip("The definition this prefab was baked from — editor tooling only, never read at runtime.")]
        public CityDefinition definition;

        [TitleGroup("Baked city"), ReadOnly] public int gridWidth = 1;
        [TitleGroup("Baked city"), ReadOnly] public int gridHeight = 1;
        [TitleGroup("Baked city"), ReadOnly] public int blockSizeInCells = 14;
        [TitleGroup("Baked city"), ReadOnly, SuffixLabel("m", true)] public float cellSize = 20f;
        [TitleGroup("Baked city"), ReadOnly, SuffixLabel("m", true)] public float deckWorldHeight;
        [TitleGroup("Baked city"), ReadOnly] public int citySeed;

        [TitleGroup("Sockets")]
        [Tooltip("Everything under this transform is persistent designer content — the baker never touches it.")]
        public Transform additionalItems;

        [TitleGroup("Sockets")]
        [Tooltip("Hand-placed vehicles that are never despawned; they sit still until the player comes close. The baker never touches this either.")]
        public Transform defaultVehicles;

        [TitleGroup("Gameplay")]
        [Tooltip("Teleport the player to the opposite side when they drive off a city edge (the arterials are periodic, so the road continues seamlessly).")]
        public bool pacmanWrap = true;

        [TitleGroup("Gameplay")]
        [Tooltip("NPCs may spawn/roam into a neighbour block once the player is this close to the shared edge.")]
        [PropertyRange(10f, 300f), SuffixLabel("m", true)]
        public float npcEdgeEnterDistance = 60f;

        [TitleGroup("Gameplay")]
        [Tooltip("The neighbour block stays active until the player is this far from the shared edge — hysteresis, so cruising along an edge can't thrash spawns.")]
        [PropertyRange(20f, 400f), SuffixLabel("m", true)]
        public float npcEdgeExitDistance = 90f;

        [TitleGroup("Gameplay")]
        [Tooltip("Keep only the blocks near the player loaded: the baked content of every other block is switched off (the block objects, their colliders and the road graph stay). Pair with the DistanceFog so the pop-in lands behind the fog.")]
        public bool streamBlocks = true;

        [TitleGroup("Gameplay")]
        [Tooltip("A block's content is switched on once its rectangle is this close to the player. Must be at least the police despawn reach (PursuitSettings: despawnDistance, or spawn max + 50) and the traffic active radius + padding, or NPCs drive on missing colliders; and at least the fog end, or blocks pop in ahead of the fog.")]
        [PropertyRange(200f, 1500f), SuffixLabel("m", true), EnableIf(nameof(streamBlocks))]
        public float streamEnterDistance = 500f;

        [TitleGroup("Gameplay")]
        [Tooltip("A loaded block's content stays on until it is this far away. Hysteresis, so driving along a block edge cannot thrash a few hundred objects on and off.")]
        [PropertyRange(250f, 2000f), SuffixLabel("m", true), EnableIf(nameof(streamBlocks))]
        public float streamExitDistance = 650f;

        [TitleGroup("Gameplay")]
        [Tooltip("Content roots (Roads, Buildings, ...) toggled per frame. 1 spreads a block's arrival over a handful of frames; a Buildings root alone drops dozens of mesh colliders into PhysX.")]
        [PropertyRange(1, 8), EnableIf(nameof(streamBlocks))]
        public int activationsPerFrame = 1;

        [TitleGroup("Gizmos")]
        [Tooltip("Draw the per-block grid overlays (road cells, block borders, seed labels).")]
        public bool drawGizmos = true;

        RoadGraph graph;
        CityBounds bounds;
        CityStreamer streamer;
        Dictionary<Vector2Int, CityBlock> blockLookup;
        Vehicles.CarController trackedPlayer;
        float boundsTimer;

        /// <summary>World size of one block along either axis.</summary>
        public float BlockWorldSize => blockSizeInCells * cellSize;

        /// <summary>World size of the whole city along X / Z.</summary>
        public float CitySizeX => gridWidth * BlockWorldSize;
        public float CitySizeZ => gridHeight * BlockWorldSize;

        /// <summary>Which blocks NPCs may currently occupy (player's block + close-edge neighbours).</summary>
        public CityBounds Bounds => bounds ??= new CityBounds(this);

        /// <summary>
        /// Waypoint graph over the baked roads — the AI's navigation source.
        /// Built lazily from the blocks' serialized layouts, so it survives
        /// domain reloads and works in edit mode for tooling.
        /// </summary>
        public RoadGraph Graph
        {
            get
            {
                if (graph == null) RebuildGraph();
                return graph;
            }
        }

        void Awake()
        {
            if (!Application.isPlaying) return;
            RebuildGraph();
            if (pacmanWrap && GetComponent<CityWrap>() == null) gameObject.AddComponent<CityWrap>();
            if (streamBlocks)
            {
                streamer = GetComponent<CityStreamer>();
                if (streamer == null) streamer = gameObject.AddComponent<CityStreamer>();
            }
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            // NPC block scoping follows the player on the managers' cadence.
            boundsTimer -= Time.deltaTime;
            if (boundsTimer > 0f) return;
            boundsTimer = 1f;
            if (trackedPlayer == null) trackedPlayer = AI.PatrolManager.FindPlayerCar();
            if (trackedPlayer == null) return;
            Bounds.Tick(trackedPlayer.transform.position);
            // Same cadence, same player: the visual ring follows the NPC ring.
            if (streamer != null && streamer.enabled) streamer.Tick(trackedPlayer.transform.position);
        }

        /// <summary>Rebuild the graph from the child blocks' serialized layouts. Editor tooling calls this after a rebake.</summary>
        public void RebuildGraph()
        {
            graph = new RoadGraph(cellSize, deckWorldHeight, transform.position);
            blockLookup = new Dictionary<Vector2Int, CityBlock>();
            foreach (CityBlock block in GetComponentsInChildren<CityBlock>())
            {
                blockLookup[block.coord] = block;
                ChunkData data = block.Data;
                if (data != null) graph.RegisterChunk(data);
            }
        }

        /// <summary>The baked block at a grid coordinate (wrapped). False for a coordinate no block was baked for.</summary>
        public bool TryGetBlock(Vector2Int coord, out CityBlock block)
        {
            if (blockLookup == null) RebuildGraph();
            return blockLookup.TryGetValue(WrapBlockCoord(coord), out block) && block != null;
        }

        /// <summary>Was this block baked as water (open sea or a causeway)?</summary>
        public bool IsWaterBlock(Vector2Int coord) => TryGetBlock(coord, out CityBlock block) && block.isWater;

        /// <summary>
        /// Is a world position over open sea — a water block's cell that
        /// carries no road? A causeway's bridge line answers false, so a
        /// wrap landing on its deck road is still a wrap. Positions outside
        /// the city rectangle are wrapped in first.
        /// </summary>
        public bool IsOpenWater(Vector3 world)
        {
            Vector2Int coord = BlockCoordAt(world);
            if (!TryGetBlock(coord, out CityBlock block) || !block.isWater) return false;
            ChunkData data = block.Data;
            if (data == null) return true;
            Vector3 origin = transform.position;
            int size = Mathf.Max(1, blockSizeInCells);
            int cx = DeterministicHash.Mod(Mathf.FloorToInt((world.x - origin.x) / cellSize), size);
            int cy = DeterministicHash.Mod(Mathf.FloorToInt((world.z - origin.z) / cellSize), size);
            return data.IsWater(cx, cy);
        }

        // -------------------------------------------------------- coordinates

        /// <summary>Grid coordinate of the block under a world position, wrapped into the grid (pacman topology).</summary>
        public Vector2Int BlockCoordAt(Vector3 world)
        {
            float block = BlockWorldSize;
            Vector3 origin = transform.position;
            int bx = DeterministicHash.Mod(Mathf.FloorToInt((world.x - origin.x) / block), gridWidth);
            int by = DeterministicHash.Mod(Mathf.FloorToInt((world.z - origin.z) / block), gridHeight);
            return new Vector2Int(bx, by);
        }

        /// <summary>Wrap a grid coordinate into the grid (pacman topology).</summary>
        public Vector2Int WrapBlockCoord(Vector2Int coord) =>
            new(DeterministicHash.Mod(coord.x, gridWidth), DeterministicHash.Mod(coord.y, gridHeight));

        /// <summary>
        /// If the position lies outside the city's ground rectangle, the
        /// pacman-wrapped equivalent inside it. Returns false when it is
        /// already inside (wrapped = position).
        /// </summary>
        public bool TryWrap(Vector3 position, out Vector3 wrapped)
        {
            wrapped = position;
            Vector3 origin = transform.position;
            float sizeX = CitySizeX, sizeZ = CitySizeZ;
            bool changed = false;
            if (position.x < origin.x) { wrapped.x += sizeX; changed = true; }
            else if (position.x >= origin.x + sizeX) { wrapped.x -= sizeX; changed = true; }
            if (position.z < origin.z) { wrapped.z += sizeZ; changed = true; }
            else if (position.z >= origin.z + sizeZ) { wrapped.z -= sizeZ; changed = true; }
            return changed;
        }

        // ------------------------------------------------------------ queries
        // Moved verbatim from the streaming CityManager (which now delegates
        // here) — only the data source changed: CityBlock layouts instead of
        // transient CityChunk models.

        /// <summary>
        /// Nearest road cell center (and its connection mask) across the baked
        /// city — used for car spawning and respawning. Prefers cells whose
        /// airspace is physically clear (building meshes can overhang road
        /// cells the grid model calls free); falls back to any road cell so a
        /// cluttered network still returns something.
        /// </summary>
        public bool TryFindNearestRoadCell(Vector3 from, out Vector3 center, out EdgeMask connections, bool groundOnly = false)
        {
            center = default;
            connections = EdgeMask.None;

            Physics.SyncTransforms(); // colliders may have been created this frame
            return PickNearestRoadCell(from, true, groundOnly, ref center, ref connections)
                || PickNearestRoadCell(from, false, groundOnly, ref center, ref connections);
        }

        /// <summary>
        /// Uniformly random road cell (reservoir pick, single pass). Same
        /// clearance preference and fallback as <see cref="TryFindNearestRoadCell"/>.
        /// Not tied to the city's deterministic seed: every press is a fresh spot.
        /// </summary>
        public bool TryGetRandomRoadCell(out Vector3 center, out EdgeMask connections, bool groundOnly = false)
        {
            center = default;
            connections = EdgeMask.None;

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
        /// Every road spot across the baked city: surface centre (deck height
        /// for overpasses, part-way up for ramps), connection mask and whether
        /// it is flat ground (the only kind cars spawn on).
        /// </summary>
        public IEnumerable<(Vector3 center, EdgeMask connections, bool flatGround)> RoadCells()
        {
            foreach (var pair in Graph.Nodes)
                yield return (pair.Value.Center, pair.Value.Mask, pair.Key.Level == 0 && !pair.Value.IsRamp && !pair.Value.IsCurve);
        }

        /// <summary>
        /// True when nothing solid (building box, overhanging mesh collider,
        /// the previous car) intrudes into the cell's airspace. The checked
        /// box covers 90% of the cell from just above the ground slab up to
        /// ~4 m, so the block ground colliders (top at y = 0) never trip it
        /// and tiny roof overhangs at the very border are tolerated.
        /// Decoration props are ignored — they live on the sidewalk band of
        /// nearly every road tile, and counting them would disqualify most of
        /// the network as a spawn spot.
        /// </summary>
        public bool IsCellClear(Vector3 cellCenter)
        {
            float half = cellSize * 0.45f;
            var halfExtents = new Vector3(half, 2f, half);
            int count = Physics.OverlapBoxNonAlloc(cellCenter + Vector3.up * (halfExtents.y + 0.05f), halfExtents,
                clearanceHits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (count >= clearanceHits.Length) return false; // buffer overflow — too cluttered to trust, treat as blocked
            for (int i = 0; i < count; i++)
            {
                if (clearanceHits[i].GetComponentInParent<Decoration.DecorationProp>() == null) return false;
            }
            return true;
        }

        static readonly Collider[] clearanceHits = new Collider[32];

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
        /// intruding into the spawn cell or any runway cell). Runways are
        /// checked within a single block's grid — cells whose runway would
        /// cross a block border simply don't qualify, which plenty of
        /// arterial cells survive.
        /// </summary>
        IEnumerable<(Vector3 center, float yaw)> StraightSpawns(int runwayCells)
        {
            float cell = cellSize;
            foreach (CityBlock block in GetComponentsInChildren<CityBlock>())
            {
                ChunkData data = block.Data;
                if (data == null) continue;
                Vector3 origin = block.transform.position;
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
            float cell = cellSize;
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
            if (data.HasFlag(x, y, ChunkData.CellFlags.Curve)) return false; // a curve junction's yaw won't match the chord ribbon behind it
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
    }
}
