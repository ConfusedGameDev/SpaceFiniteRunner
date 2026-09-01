using ConfusedGameDev.FiniteRunner.Debugging;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Debugging
{
    /// <summary>
    /// Draws the <see cref="RoadGraph"/> the AI actually navigates on — not the
    /// road meshes, the graph: one marker per node and a half-edge for every
    /// connection it will accept. Half-edges (centre → midpoint) are the trick
    /// that makes the graph's own rule visible: a connection is only drivable
    /// when it is <b>mutual</b>, so a pair of half-edges meeting in the middle
    /// is a real link, and a lone stub is a socket whose neighbour never
    /// answered — the shape of a routing bug, drawn.
    ///
    /// Levels are coloured apart because an overpass deck shares its XZ with
    /// the street underneath: ground, ramp and deck have to be told apart by
    /// eye or the whole picture is a lie.
    ///
    /// The graph is streamed and can hold thousands of nodes, so the overlay
    /// draws a radius around the player (the camera when there is no player)
    /// and rebuilds on an interval rather than every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoadGraphVisualizer : DebugVisualizer
    {
        [TitleGroup("Scope")]
        [Tooltip("Only nodes within this many metres of the focus are drawn — the graph spans the whole streamed city.")]
        [PropertyRange(20f, 1000f)]
        public float radius = 250f;

        [TitleGroup("Scope")]
        [Tooltip("Hard cap on drawn nodes, so a huge graph can never stall the editor.")]
        [PropertyRange(100, 20000)]
        public int maxNodes = 4000;

        [TitleGroup("Scope")]
        [Tooltip("Optional focus. Empty, the player car is used — or the main camera when there is no player yet.")]
        public Transform focus;

        [TitleGroup("Draw")]
        [Tooltip("Metres the whole overlay is lifted off the road surface, so it is not z-fighting the asphalt.")]
        [PropertyRange(0f, 3f)]
        public float lift = 0.4f;

        [TitleGroup("Draw")]
        [Tooltip("Mark the node each AI car and the player are currently standing on.")]
        public bool markOccupiedNodes = true;

        static readonly Color GroundColor = new(0.30f, 0.55f, 0.85f, 0.85f);
        static readonly Color RampColor = new(1f, 0.55f, 0.1f, 0.95f);
        static readonly Color DeckColor = new(0.2f, 0.9f, 0.9f, 0.95f);
        static readonly Color NodeColor = new(0.75f, 0.85f, 1f, 0.8f);
        static readonly Color OccupiedColor = new(1f, 1f, 0.2f, 1f);

        CityManager city;

        [TitleGroup("Scope"), ShowInInspector, ReadOnly]
        [Tooltip("Nodes drawn in the last rebuild, against the graph's total.")]
        public string NodesDrawn { get; private set; } = "-";

        protected override bool ChannelEnabled => DebugManager.ShowRoadGraph;

        protected override void Awake()
        {
            base.Awake();
            if (refreshInterval <= 0f) refreshInterval = 0.25f; // the graph changes at streaming speed, not frame speed
        }

        protected override void Rebuild()
        {
            RoadGraph graph = Graph();
            if (graph == null) return;

            Vector3 center = FocusPosition();
            float radiusSqr = radius * radius;
            Vector3 up = Vector3.up * lift;
            int drawn = 0;

            foreach (var pair in graph.Nodes)
            {
                if (drawn >= maxNodes) break;
                RoadNode node = pair.Key;
                RoadNodeData data = pair.Value;

                Vector3 from = data.Center;
                float dx = from.x - center.x;
                float dz = from.z - center.z;
                if (dx * dx + dz * dz > radiusSqr) continue;
                drawn++;

                Color color = node.Level == 1 ? DeckColor : data.IsRamp ? RampColor : GroundColor;
                Lines.Diamond(from + up, 1.2f, NodeColor);

                // Half-edges: each node draws to the midpoint of its own
                // connection. Two halves meeting = a mutual (drivable) link; a
                // lone stub = a socket the neighbour does not answer.
                for (int direction = 0; direction < 4; direction++)
                {
                    if ((data.Mask & EdgeMaskUtility.DirectionBit(direction)) == 0) continue;
                    // Physical links, traffic rules lifted: a roundabout's
                    // one-way ring and island short-cut are real sockets that
                    // TryGetNeighbour refuses by rule, not missing neighbours.
                    bool mutual = graph.TryGetNeighbour(node, direction, out RoadNode neighbour, cutThrough: true);
                    Vector3 to = mutual
                        ? Vector3.Lerp(from, graph.Center(neighbour), 0.5f)
                        : from + Offset(direction) * (CellSize * 0.4f);
                    Lines.Line(from + up, to + up, mutual ? color : Color.red);
                    if (!mutual) Lines.Cross(to + up, 0.5f, Color.red);
                }
            }

            NodesDrawn = $"{drawn} / {graph.Count}";

            if (markOccupiedNodes) MarkOccupied(graph, up);
        }

        /// <summary>Ring the node every car stands on — the graph cell the AI thinks it is in, which is the first thing to check when a route looks mad.</summary>
        void MarkOccupied(RoadGraph graph, Vector3 up)
        {
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (!graph.TryGetNodeAt(car.transform.position, out RoadNode node)) continue;
                Lines.Circle(graph.Center(node) + up, CellSize * 0.32f, OccupiedColor, 12);
            }
        }

        float CellSize => city != null && city.settings != null ? city.settings.cellSize : 20f;

        RoadGraph Graph()
        {
            // Re-found rather than cached: the city is destroyed and rebuilt on
            // every scene reload, while this overlay rides DontDestroyOnLoad.
            if (city == null) city = FindAnyObjectByType<CityManager>();
            return city != null ? city.Graph : null;
        }

        Vector3 FocusPosition()
        {
            if (focus != null) return focus.position;
            CarController player = PatrolManager.FindPlayerCar();
            if (player != null) return player.transform.position;
            Camera camera = Camera.main;
            return camera != null ? camera.transform.position : transform.position;
        }

        static Vector3 Offset(int direction)
        {
            Vector2Int offset = EdgeMaskUtility.Offset(direction);
            return new Vector3(offset.x, 0f, offset.y);
        }
    }
}
