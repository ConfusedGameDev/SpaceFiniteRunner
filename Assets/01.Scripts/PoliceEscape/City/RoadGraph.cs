using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// A navigable spot on the road network: a world grid cell plus a level —
    /// 0 = ground (including ramp cells), 1 = overpass deck. Two nodes may
    /// share a cell (a street passing under a bridge); they are distinct keys.
    /// </summary>
    public readonly struct RoadNode : IEquatable<RoadNode>
    {
        public readonly Vector2Int Cell;
        public readonly int Level;

        public RoadNode(Vector2Int cell, int level)
        {
            Cell = cell;
            Level = level;
        }

        public bool Equals(RoadNode other) => Cell == other.Cell && Level == other.Level;
        public override bool Equals(object obj) => obj is RoadNode other && Equals(other);
        public override int GetHashCode() => unchecked(Cell.x * 73856093 ^ Cell.y * 19349663 ^ Level * 83492791);
        public static bool operator ==(RoadNode a, RoadNode b) => a.Equals(b);
        public static bool operator !=(RoadNode a, RoadNode b) => !a.Equals(b);
        public override string ToString() => $"({Cell.x}, {Cell.y}) L{Level}";
    }

    /// <summary>Per-node data baked at registration: socket mask, world centre (with height), whether the node is a ramp cell, whether it belongs to a curved avenue, and whether lane discipline must collapse to the centre line there.</summary>
    public readonly struct RoadNodeData
    {
        public readonly EdgeMask Mask;
        public readonly Vector3 Center;
        public readonly bool IsRamp;

        /// <summary>
        /// Cars must drive this node's centre line, no lane offset: fork seam
        /// cells (two nodes share one seam line — an offset pushes cars off
        /// the half-width stem) and template footprints like the roundabout,
        /// where "right of travel" has no meaning.
        /// </summary>
        public readonly bool CenterLineOnly;

        /// <summary>
        /// Part of a curved avenue: the centre sits on the fitted curve, the
        /// visual is a chord ribbon rather than grid tiles. Lane discipline
        /// still applies (the ribbon is a full tile wide), but spawners must
        /// keep off — a grid-aligned spawn pose on a diagonal chord can sit
        /// half off the asphalt.
        /// </summary>
        public readonly bool IsCurve;

        public RoadNodeData(EdgeMask mask, Vector3 center, bool isRamp, bool centerLineOnly = false, bool isCurve = false)
        {
            Mask = mask;
            Center = center;
            IsRamp = isRamp;
            CenterLineOnly = centerLineOnly;
            IsCurve = isCurve;
        }
    }

    /// <summary>
    /// Runtime waypoint graph derived from the generated roads — the AI's
    /// navigation source, per the plan's "RoadGraph, not NavMesh" rule: the
    /// generator already knows exactly where roads are. Nodes are
    /// <see cref="RoadNode"/>s (world cell + level); edges follow each node's
    /// socket mask, so paths can never cut through connections the road data
    /// says don't exist. The one neighbour rule (<see cref="TryGetNeighbour"/>)
    /// is mutual connection: the target node must connect back. That is what
    /// links a ramp (level 0) to the deck (level 1) and keeps a deck from
    /// leaking into the street underneath. The baked city's blocks register
    /// their cells at load; A* answers route queries. The graph covers the
    /// whole city and never shrinks — nothing is streamed out any more, so a
    /// plan can never go stale under a driver.
    ///
    /// Every position it hands out is a WORLD position. Cell coordinates are
    /// relative to the city root, so the root's own transform is baked in at
    /// construction (<paramref name="origin"/>): chunks are children of that
    /// root and inherit the same offset, and every consumer — spawners, AI
    /// waypoints, nearest-cell queries — treats <see cref="Center"/> as a
    /// place to drive to. Deriving centres from cell indices alone would put
    /// the whole graph at the world origin while the city it describes sits
    /// somewhere else entirely. The root is assumed not to move after the
    /// graph is built; rebuild it if it ever does.
    /// </summary>
    public class RoadGraph
    {
        readonly Dictionary<RoadNode, RoadNodeData> nodes = new();
        readonly float cellSize;
        readonly float deckHeight;
        readonly Vector3 origin;

        /// <summary>Vertical distance is weighted this much more than horizontal in nearest-node searches, so a car under a bridge never snaps onto it.</summary>
        const float VerticalPenalty = 3f;

        /// <param name="origin">World position of the city root the chunks hang off. Cell (0,0) starts here.</param>
        public RoadGraph(float cellSize, float deckHeight = 0f, Vector3 origin = default)
        {
            this.cellSize = cellSize;
            this.deckHeight = deckHeight;
            this.origin = origin;
        }

        public int Count => nodes.Count;

        /// <summary>World height of the upper (deck) level.</summary>
        public float DeckHeight => deckHeight;

        /// <summary>All registered nodes with their baked data.</summary>
        public IEnumerable<KeyValuePair<RoadNode, RoadNodeData>> Nodes => nodes;

        public void RegisterChunk(ChunkData data)
        {
            Vector2Int origin = data.WorldCellOrigin;
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                var cell = new Vector2Int(origin.x + x, origin.y + y);
                EdgeMask ground = data.GetConnections(x, y);
                if (data.IsRoad(x, y) && ground != EdgeMask.None)
                {
                    bool ramp = data.IsRamp(x, y);
                    float height = ramp ? deckHeight * data.RampHeight01(x, y) : 0f;
                    // A fork stem runs on the seam between two cells: the data shifts both nodes onto it.
                    Vector2 shift = data.GetCenterOffset(x, y) * cellSize;
                    Vector3 center = CellCenterAt(cell, height) + new Vector3(shift.x, 0f, shift.y);
                    // Seam cells and feature footprints (roundabout) refuse
                    // lane offsets — the geometry there isn't a plain two-way
                    // street, and an offset pushes cars off the asphalt.
                    // Curve chain cells are the exception: their offset centres
                    // trace the avenue's smooth line, the ribbon is a full tile
                    // wide, and LaneTarget's miter join handles the polyline —
                    // so they keep ordinary lane discipline.
                    bool curve = data.HasFlag(x, y, ChunkData.CellFlags.Curve);
                    bool centerLineOnly = (data.HasCenterOffset(x, y) || data.IsCovered(x, y)) && !curve;
                    nodes[new RoadNode(cell, 0)] = new RoadNodeData(ground, center, ramp, centerLineOnly, curve);
                }
                EdgeMask upper = data.GetUpperConnections(x, y);
                if (upper != EdgeMask.None)
                    nodes[new RoadNode(cell, 1)] = new RoadNodeData(upper, CellCenterAt(cell, deckHeight), false);
            }
        }

        public bool Contains(RoadNode node) => nodes.ContainsKey(node);

        public EdgeMask Connections(RoadNode node) =>
            nodes.TryGetValue(node, out RoadNodeData data) ? data.Mask : EdgeMask.None;

        /// <summary>World centre of the node's drivable surface (deck height on level 1, part-way up on ramps).</summary>
        public Vector3 Center(RoadNode node) =>
            nodes.TryGetValue(node, out RoadNodeData data) ? data.Center : CellCenterAt(node.Cell, node.Level == 1 ? deckHeight : 0f);

        public bool IsRamp(RoadNode node) => nodes.TryGetValue(node, out RoadNodeData data) && data.IsRamp;

        /// <summary>Lane discipline collapses to the centre line on this node (fork seams, roundabout footprints) — see <see cref="RoadNodeData.CenterLineOnly"/>.</summary>
        public bool IsCenterLineOnly(RoadNode node) => nodes.TryGetValue(node, out RoadNodeData data) && data.CenterLineOnly;

        /// <summary>Centre-line rule for a world position: the node under it, false off-graph. Seam centres sit on a cell boundary, but both seam cells carry the flag, so either resolution answers right.</summary>
        public bool IsCenterLineOnlyAt(Vector3 position) => TryGetNodeAt(position, out RoadNode node) && IsCenterLineOnly(node);

        /// <summary>Part of a curved avenue — drivable, but not a place to spawn (see <see cref="RoadNodeData.IsCurve"/>).</summary>
        public bool IsCurve(RoadNode node) => nodes.TryGetValue(node, out RoadNodeData data) && data.IsCurve;

        /// <summary>Flat ground node — neither a ramp, a deck nor a curve chord. The only kind cars are spawned on: a grid-aligned spawn pose on a curve can sit half off the ribbon.</summary>
        public bool IsFlatGround(RoadNode node) => node.Level == 0 && nodes.TryGetValue(node, out RoadNodeData data) && !data.IsRamp && !data.IsCurve;

        public Vector2Int WorldToCell(Vector3 position) =>
            new(Mathf.FloorToInt((position.x - origin.x) / cellSize),
                Mathf.FloorToInt((position.z - origin.z) / cellSize));

        // Heights (deck, ramp step) are offsets from the city root, so the
        // root's own Y rides along with the horizontal offset.
        Vector3 CellCenterAt(Vector2Int cell, float height) =>
            origin + new Vector3((cell.x + 0.5f) * cellSize, height, (cell.y + 0.5f) * cellSize);

        /// <summary>
        /// The node under a world position: the level whose surface height is
        /// closest to the position's Y (a car on the deck gets the deck, a car
        /// underneath gets the street). False when the cell holds no road.
        /// </summary>
        public bool TryGetNodeAt(Vector3 position, out RoadNode node)
        {
            Vector2Int cell = WorldToCell(position);
            var ground = new RoadNode(cell, 0);
            var upper = new RoadNode(cell, 1);
            bool hasGround = nodes.TryGetValue(ground, out RoadNodeData groundData);
            bool hasUpper = nodes.TryGetValue(upper, out RoadNodeData upperData);
            if (hasGround && hasUpper)
            {
                node = Mathf.Abs(position.y - upperData.Center.y) < Mathf.Abs(position.y - groundData.Center.y) ? upper : ground;
                return true;
            }
            node = hasGround ? ground : upper;
            return hasGround || hasUpper;
        }

        /// <summary>
        /// Nearest registered node to a world position, with height counting
        /// <see cref="VerticalPenalty"/>× so the street below a bridge beats the
        /// deck above. O(n) scan — fine at v1 sizes, revisit with spatial
        /// buckets if streaming makes it hot.
        /// </summary>
        public bool TryGetNearestNode(Vector3 position, out RoadNode nearest, bool flatGroundOnly = false)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var pair in nodes)
            {
                if (flatGroundOnly && (pair.Key.Level != 0 || pair.Value.IsRamp || pair.Value.IsCurve)) continue;
                Vector3 delta = pair.Value.Center - position;
                float score = delta.x * delta.x + delta.z * delta.z + VerticalPenalty * VerticalPenalty * delta.y * delta.y;
                if (score >= best) continue;
                best = score;
                nearest = pair.Key;
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// The node reached by leaving <paramref name="from"/> through edge
        /// <paramref name="direction"/> (0..3 = N,E,S,W). Mutual connection is
        /// the criterion: the target must carry the opposite socket. The own
        /// level is tried first, then the other one — that is how a ramp
        /// (level 0) continues onto the deck (level 1) while the crossing
        /// street under that deck, which has no socket facing the ramp, is
        /// never entered.
        /// </summary>
        public bool TryGetNeighbour(RoadNode from, int direction, out RoadNode to)
        {
            to = default;
            if ((Connections(from) & EdgeMaskUtility.DirectionBit(direction)) == 0) return false;
            Vector2Int cell = from.Cell + EdgeMaskUtility.Offset(direction);
            EdgeMask back = EdgeMaskUtility.DirectionBit(direction + 2);
            for (int i = 0; i < 2; i++)
            {
                var candidate = new RoadNode(cell, i == 0 ? from.Level : 1 - from.Level);
                if (nodes.TryGetValue(candidate, out RoadNodeData data) && (data.Mask & back) != 0)
                {
                    to = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A* route over the road network, start and goal included in the
        /// result. Neighbours come from <see cref="TryGetNeighbour"/>, Manhattan
        /// heuristic on the cell grid (uniform cell cost, level changes are
        /// free — still admissible).
        ///
        /// The open set is a binary heap plus a membership set. It used to be a
        /// plain List with linear extract-min and an O(n) Contains, on the
        /// reasoning that chase routes are only a few hundred nodes — true for
        /// the AI, but the city map routes right across town over a graph of
        /// tens of thousands of nodes, where that quadratic behaviour is the
        /// difference between instant and a visible stall. The AI gets the
        /// speedup for free; the results are identical either way.
        /// </summary>
        public bool TryFindPath(RoadNode start, RoadNode goal, List<RoadNode> path, int maxExpansions = 8192)
        {
            path.Clear();
            if (!Contains(start) || !Contains(goal)) return false;
            if (start == goal)
            {
                path.Add(start);
                return true;
            }

            var open = new MinHeap();
            var closed = new HashSet<RoadNode>();
            var cameFrom = new Dictionary<RoadNode, RoadNode>();
            var gScore = new Dictionary<RoadNode, int> { [start] = 0 };

            open.Push(start, Heuristic(start, goal));

            int expansions = 0;
            while (open.Count > 0 && expansions++ < maxExpansions)
            {
                RoadNode current = open.Pop();

                // A node can be queued more than once with different scores;
                // the first pop is the cheapest, so later ones are stale.
                if (!closed.Add(current)) continue;

                if (current == goal)
                {
                    Reconstruct(cameFrom, current, path);
                    return true;
                }

                for (int dir = 0; dir < 4; dir++)
                {
                    if (!TryGetNeighbour(current, dir, out RoadNode neighbour)) continue;
                    if (closed.Contains(neighbour)) continue;

                    int tentative = gScore[current] + 1;
                    if (gScore.TryGetValue(neighbour, out int known) && tentative >= known) continue;
                    cameFrom[neighbour] = current;
                    gScore[neighbour] = tentative;
                    open.Push(neighbour, tentative + Heuristic(neighbour, goal));
                }
            }
            return false;
        }

        /// <summary>
        /// Minimal binary min-heap over (node, f). Only what A* needs — no
        /// decrease-key: a cheaper route to a node is pushed again and the
        /// stale entry is skipped when popped, which is smaller and faster than
        /// maintaining handles.
        /// </summary>
        class MinHeap
        {
            readonly List<RoadNode> nodes = new();
            readonly List<int> scores = new();

            public int Count => nodes.Count;

            public void Push(RoadNode node, int score)
            {
                nodes.Add(node);
                scores.Add(score);
                int child = nodes.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (scores[parent] <= scores[child]) break;
                    Swap(parent, child);
                    child = parent;
                }
            }

            public RoadNode Pop()
            {
                RoadNode top = nodes[0];
                int last = nodes.Count - 1;
                nodes[0] = nodes[last];
                scores[0] = scores[last];
                nodes.RemoveAt(last);
                scores.RemoveAt(last);

                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= nodes.Count) break;
                    int right = left + 1;
                    int smallest = right < nodes.Count && scores[right] < scores[left] ? right : left;
                    if (scores[parent] <= scores[smallest]) break;
                    Swap(parent, smallest);
                    parent = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (nodes[a], nodes[b]) = (nodes[b], nodes[a]);
                (scores[a], scores[b]) = (scores[b], scores[a]);
            }
        }

        static int Heuristic(RoadNode a, RoadNode b) =>
            Mathf.Abs(a.Cell.x - b.Cell.x) + Mathf.Abs(a.Cell.y - b.Cell.y);

        static void Reconstruct(Dictionary<RoadNode, RoadNode> cameFrom, RoadNode current, List<RoadNode> path)
        {
            path.Add(current);
            while (cameFrom.TryGetValue(current, out RoadNode previous))
            {
                current = previous;
                path.Add(current);
            }
            path.Reverse();
        }
    }
}
