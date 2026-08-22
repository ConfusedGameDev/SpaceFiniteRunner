using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The map's own view of the city: chunk data generated on demand for
    /// anywhere in the world, cached, and registered into a road graph that is
    /// never torn down.
    ///
    /// The design rule this exists to enforce: <b>the map must not depend on
    /// what is currently streamed</b>. CityManager keeps only
    /// loadRadiusInChunks around the car and calls
    /// <see cref="RoadGraph.UnregisterChunk"/> on everything else, so
    /// <see cref="CityManager.Graph"/> covers a few hundred metres — useless
    /// for drawing a city-wide map or routing to a distant marker. Road
    /// generation is a pure function of (seed, chunk coord) through
    /// <see cref="DeterministicHash"/>, so this class simply calls
    /// <see cref="RoadNetworkGenerator.Generate"/> for whatever it needs and
    /// gets back exactly the streets the player would drive on, with no
    /// GameObjects created.
    ///
    /// Cells are GLOBAL cell coordinates, matching <see cref="RoadNode.Cell"/>.
    /// World conversion deliberately copies <see cref="RoadGraph.WorldToCell"/>
    /// (which subtracts the city root's origin) and NOT
    /// CityManager.StreamAroundAnchor's version, which omits that subtraction
    /// and is only correct while the city root sits at the world origin.
    /// </summary>
    public class CityMapModel
    {
        readonly CityGenerationSettings settings;
        readonly Vector3 origin;
        readonly int chunkSize;

        readonly Dictionary<Vector2Int, ChunkData> chunks = new();
        readonly LinkedList<Vector2Int> recent = new();                       // LRU: most recent at the front
        readonly Dictionary<Vector2Int, LinkedListNode<Vector2Int>> recentNodes = new();
        readonly Queue<Vector2Int> pending = new();
        readonly HashSet<Vector2Int> queued = new();

        /// <summary>Road graph over every chunk this model has generated. Grows only — nothing here is unregistered by streaming.</summary>
        public RoadGraph Graph { get; }

        public float CellSize => settings.cellSize;
        public int ChunkSizeInCells => chunkSize;

        /// <summary>Chunks still waiting to be generated — the map paints these as background until they land.</summary>
        public int PendingCount => pending.Count;

        public CityMapModel(CityGenerationSettings settings, Vector3 cityOrigin, float deckHeight)
        {
            this.settings = settings;
            origin = cityOrigin;
            chunkSize = Mathf.Max(1, settings.chunkSizeInCells);
            Graph = new RoadGraph(settings.cellSize, deckHeight, cityOrigin);
        }

        // ------------------------------------------------------- coordinates

        /// <summary>World position to global cell. Mirrors <see cref="RoadGraph.WorldToCell"/> exactly.</summary>
        public Vector2Int WorldToCell(Vector3 position) =>
            new(Mathf.FloorToInt((position.x - origin.x) / settings.cellSize),
                Mathf.FloorToInt((position.z - origin.z) / settings.cellSize));

        /// <summary>Centre of a global cell in world space, on the ground plane.</summary>
        public Vector3 CellToWorld(Vector2Int cell) =>
            origin + new Vector3((cell.x + 0.5f) * settings.cellSize, 0f, (cell.y + 0.5f) * settings.cellSize);

        /// <summary>Which chunk a global cell belongs to. Floor division, so negative coordinates behave.</summary>
        public Vector2Int CellToChunk(Vector2Int cell) =>
            new(FloorDiv(cell.x, chunkSize), FloorDiv(cell.y, chunkSize));

        static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            if ((value % divisor != 0) && ((value < 0) != (divisor < 0))) q--;
            return q;
        }

        static int Mod(int value, int divisor)
        {
            int r = value % divisor;
            return r < 0 ? r + divisor : r;
        }

        // ------------------------------------------------------------ queries

        /// <summary>
        /// What occupies a global cell, if its chunk has been generated.
        /// Returns false for cells whose chunk is not built yet — the caller
        /// paints those as background rather than guessing.
        /// </summary>
        public bool TryGetCell(Vector2Int cell, out ChunkData.CellKind kind, out bool isRoad)
        {
            kind = ChunkData.CellKind.Empty;
            isRoad = false;
            Vector2Int coord = CellToChunk(cell);
            if (!chunks.TryGetValue(coord, out ChunkData data)) return false;

            int lx = Mod(cell.x, chunkSize);
            int ly = Mod(cell.y, chunkSize);
            kind = data.GetKind(lx, ly);
            isRoad = data.IsRoad(lx, ly);
            return true;
        }

        public bool IsChunkReady(Vector2Int coord) => chunks.ContainsKey(coord);

        // ---------------------------------------------------------- streaming

        /// <summary>
        /// Ask for every chunk in this window (inclusive), plus the settings'
        /// margin. Already-built chunks are just touched for LRU purposes;
        /// missing ones are queued for <see cref="Pump"/>.
        /// </summary>
        public void EnsureArea(RectInt chunkWindow, int margin)
        {
            for (int y = chunkWindow.yMin - margin; y <= chunkWindow.yMax + margin; y++)
            for (int x = chunkWindow.xMin - margin; x <= chunkWindow.xMax + margin; x++)
            {
                var coord = new Vector2Int(x, y);
                if (chunks.ContainsKey(coord))
                {
                    Touch(coord);
                    continue;
                }
                if (queued.Add(coord)) pending.Enqueue(coord);
            }
        }

        /// <summary>
        /// Generate up to <paramref name="budget"/> queued chunks. Bounded per
        /// frame because a fast pan can queue hundreds at once and generating
        /// them all in one frame would hitch even with the map paused.
        /// Returns true if anything was built, i.e. the schematic changed.
        /// </summary>
        public bool Pump(int budget, int cacheSize)
        {
            bool built = false;
            while (budget-- > 0 && pending.Count > 0)
            {
                Vector2Int coord = pending.Dequeue();
                queued.Remove(coord);
                if (chunks.ContainsKey(coord)) continue;

                ChunkData data = RoadNetworkGenerator.Generate(settings, coord);
                chunks[coord] = data;
                Graph.RegisterChunk(data);
                Touch(coord);
                built = true;
            }
            if (built) Trim(cacheSize);
            return built;
        }

        void Touch(Vector2Int coord)
        {
            if (recentNodes.TryGetValue(coord, out var node))
            {
                recent.Remove(node);
                recent.AddFirst(node);
                return;
            }
            recentNodes[coord] = recent.AddFirst(coord);
        }

        // Evict the coldest chunks once the cache is over budget. Their nodes
        // leave the graph too, otherwise a long session would grow it without
        // bound and every nearest-node scan would pay for city we stopped
        // looking at. Anything evicted regenerates identically on demand.
        void Trim(int cacheSize)
        {
            while (recent.Count > Mathf.Max(16, cacheSize))
            {
                LinkedListNode<Vector2Int> coldest = recent.Last;
                if (coldest == null) return;
                Vector2Int coord = coldest.Value;
                recent.RemoveLast();
                recentNodes.Remove(coord);
                if (chunks.TryGetValue(coord, out ChunkData data))
                {
                    Graph.UnregisterChunk(data);
                    chunks.Remove(coord);
                }
            }
        }

        /// <summary>Drop everything — used when the city itself is regenerated under us.</summary>
        public void Clear()
        {
            foreach (var pair in chunks) Graph.UnregisterChunk(pair.Value);
            chunks.Clear();
            recent.Clear();
            recentNodes.Clear();
            pending.Clear();
            queued.Clear();
        }
    }
}
