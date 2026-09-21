using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship.Sandbox
{
    /// <summary>
    /// The standalone ship's proving ground: one long course that holds every
    /// kind of surface the ship has to survive, built from plain colliders
    /// with NO guide spline and no ship-specific markup — if the ship flies
    /// it, it will fly a level. In order: a 9 km walled straight (the
    /// acceleration and brake numbers, with a wall slalom on it), a run of
    /// separate 19 m tiles (seams must never read as walls), a 60 m / 20°
    /// ramp, a gap, a crest, an R = 100 corkscrew loop, an 80° banked sweep,
    /// an R = 60 pipe ridden on its outside, and a dead-end wall.
    ///
    /// The geometry is built at <c>Awake</c> (level geometry, like the
    /// runner's streamed track — nothing is saved into the scene or the repo)
    /// by a turtle that walks the course and hands cross-sections to
    /// <see cref="SurfaceRibbonMesher"/>; every section also leaves a
    /// <see cref="Station"/> the debug overlay can teleport the ship to.
    /// </summary>
    public sealed class ShipSandboxCourse : MonoBehaviour
    {
        /// <summary>A named spot on the course, a little before a feature, facing along it.</summary>
        public readonly struct Station
        {
            public readonly string Name;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public Station(string name, Vector3 position, Quaternion rotation)
            {
                Name = name; Position = position; Rotation = rotation;
            }
        }

        [TitleGroup("Road")]
        [PropertyRange(20f, 200f), SuffixLabel("m", true)]
        [SerializeField] float width = 104f;

        [TitleGroup("Road")]
        [PropertyRange(0f, 40f), SuffixLabel("m", true)]
        [SerializeField] float wallHeight = 12f;

        [TitleGroup("Road")]
        [Tooltip("Optional. Left empty the course paints itself with a generated 10 m checker, so speed is readable.")]
        [SerializeField] Material surfaceMaterial;

        [TitleGroup("Features")]
        [PropertyRange(30f, 400f), SuffixLabel("m", true)]
        [SerializeField] float loopRadius = 100f;

        [TitleGroup("Features")]
        [PropertyRange(0f, 400f), SuffixLabel("m", true)]
        [SerializeField] float loopLateralDrift = 140f;

        [TitleGroup("Features")]
        [PropertyRange(20f, 200f), SuffixLabel("m", true)]
        [SerializeField] float tubeRadius = 60f;

        [TitleGroup("Features")]
        [PropertyRange(0f, 89f), SuffixLabel("°", true)]
        [SerializeField] float sweepBank = 80f;

        readonly List<Station> stations = new();
        readonly List<RibbonFrame> frames = new();
        Material runtimeMaterial;
        Texture2D runtimeTexture;
        Vector3 cursor;
        Quaternion heading;

        public IReadOnlyList<Station> Stations => stations;
        float HalfWidth => width * 0.5f;

        void Awake() => Build();

        void OnDestroy()
        {
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            if (runtimeTexture != null) Destroy(runtimeTexture);
        }

        void Build()
        {
            stations.Clear();
            cursor = transform.position;
            heading = transform.rotation;

            Mark("Start");
            Straight("Straight", 9000f, walls: true);
            Slalom(cursor - Forward * 6000f);

            Mark("Tiles");
            Tiles(20, 19f);

            Straight("Ramp run-up", 300f, walls: true);
            Mark("Ramp", back: 250f);
            Ramp(60f, 20f, HalfWidth * 0.5f);
            Straight("Landing", 1000f, walls: true);

            Mark("Gap", back: 200f);
            cursor += Forward * 40f;
            Straight("After gap", 400f, walls: true);

            Mark("Crest", back: 200f);
            PitchArc("Crest rise", 600f, 8f);
            PitchArc("Crest", 300f, -16f);
            PitchArc("Crest settle", 600f, 8f);
            Straight("After crest", 600f, walls: true);

            Mark("Loop", back: 400f);
            Loop(loopRadius, loopLateralDrift, 200f);
            Straight("After loop", 800f, walls: true);

            Mark("Banked sweep", back: 300f);
            Sweep(1500f, 90f, sweepBank);
            Straight("After sweep", 800f, walls: true);

            Mark("Tube", back: 300f);
            Tube(1500f, tubeRadius, 300f);
            Straight("Final straight", 1500f, walls: true);

            Mark("End wall", back: 600f);
            Box("End wall", cursor + Up * 20f, new Vector3(width, 40f, 4f));
        }

        Vector3 Forward => heading * Vector3.forward;
        Vector3 Right => heading * Vector3.right;
        Vector3 Up => heading * Vector3.up;

        void Mark(string label, float back = 0f) =>
            stations.Add(new Station(label, cursor - Forward * back + Up * 4f, heading));

        RibbonFrame Frame(Vector3 position, Quaternion rotation, float halfWidth, float pipeRadius = 0f) => new()
        {
            position = position,
            right = rotation * Vector3.right,
            up = rotation * Vector3.up,
            halfWidth = halfWidth,
            pipeRadius = pipeRadius,
        };

        // ----------------------------------------------------------- sections
        void Straight(string label, float length, bool walls)
        {
            frames.Clear();
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 100f));
            for (int i = 0; i <= steps; i++)
                frames.Add(Frame(cursor + Forward * (length * i / steps), heading, HalfWidth));
            cursor += Forward * length;
            Emit(label, 1, walls);
        }

        /// <summary>Separate box tiles laid end to end, top faces flush — the runner's stamped road, as colliders.</summary>
        void Tiles(int count, float tileLength)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 centre = cursor + Forward * (tileLength * (i + 0.5f)) - Up * 0.5f;
                Box("Tile " + i, centre, new Vector3(width, 1f, tileLength));
            }
            cursor += Forward * (tileLength * count);
        }

        /// <summary>A wedge standing on the road that follows: the road ribbon runs on underneath it.</summary>
        void Ramp(float length, float angle, float halfWidth)
        {
            frames.Clear();
            Quaternion pitched = heading * Quaternion.Euler(-angle, 0f, 0f);
            Vector3 slope = pitched * Vector3.forward;
            float run = length / Mathf.Cos(angle * Mathf.Deg2Rad);
            for (int i = 0; i <= 8; i++)
                frames.Add(Frame(cursor + slope * (run * i / 8f), pitched, halfWidth));
            Emit("Ramp", 1, walls: false);
        }

        /// <summary>A vertical arc: positive degrees pitch the nose up (a dip's far side), negative is a crest.</summary>
        void PitchArc(string label, float radius, float degrees)
        {
            frames.Clear();
            float arc = radius * Mathf.Abs(degrees) * Mathf.Deg2Rad;
            int steps = Mathf.Max(2, Mathf.CeilToInt(arc / 4f));
            float ds = arc / steps;
            frames.Add(Frame(cursor, heading, HalfWidth));
            for (int i = 0; i < steps; i++)
            {
                heading *= Quaternion.Euler(-degrees / steps, 0f, 0f);
                cursor += Forward * ds;
                frames.Add(Frame(cursor, heading, HalfWidth));
            }
            Emit(label, 1, walls: true);
        }

        /// <summary>A level turn to the right, banked into it: the bank eases in and out so both ends meet flat road.</summary>
        void Sweep(float radius, float degrees, float bank)
        {
            frames.Clear();
            float arc = radius * degrees * Mathf.Deg2Rad;
            int steps = Mathf.Max(2, Mathf.CeilToInt(arc / 8f));
            float ds = arc / steps;
            frames.Add(Frame(cursor, heading, HalfWidth));
            for (int i = 1; i <= steps; i++)
            {
                heading *= Quaternion.Euler(0f, degrees / steps, 0f);
                cursor += Forward * ds;
                float u = i / (float)steps;
                float ease = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, 1f - u) * 4f));
                // A right turn drops the right edge.
                frames.Add(Frame(cursor, heading * Quaternion.Euler(0f, 0f, -bank * ease), HalfWidth));
            }
            Emit("Banked sweep", 1, walls: true);
        }

        /// <summary>A helix standing on the entry pose: once round, drifting sideways and carrying forward so the exit clears the entry.</summary>
        void Loop(float radius, float drift, float carry)
        {
            frames.Clear();
            Vector3 origin = cursor, f = Forward, r = Right, up = Up;
            Vector3 Point(float u)
            {
                float a = u * Mathf.PI * 2f;
                float ease = Mathf.SmoothStep(0f, 1f, u);
                return origin + f * (radius * Mathf.Sin(a) + carry * ease)
                              + up * (radius * (1f - Mathf.Cos(a)))
                              + r * (drift * ease);
            }

            int steps = Mathf.CeilToInt(radius * Mathf.PI * 2f / 3f);
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                Vector3 p = Point(u);
                Vector3 tangent = (Point(Mathf.Min(1f, u + 0.0005f)) - Point(Mathf.Max(0f, u - 0.0005f))).normalized;
                // The surface faces the ring's axis, which travels with the drift and the carry.
                float ease = Mathf.SmoothStep(0f, 1f, u);
                Vector3 axis = origin + up * radius + f * (carry * ease) + r * (drift * ease);
                Vector3 inward = Vector3.ProjectOnPlane(axis - p, tangent).normalized;
                Vector3 across = Vector3.Cross(inward, tangent).normalized;
                frames.Add(new RibbonFrame { position = p, right = across, up = inward, halfWidth = HalfWidth });
            }
            cursor = Point(1f);
            Emit("Loop", 1, walls: true);
        }

        /// <summary>The road curls into a pipe the ship rides the OUTSIDE of, its top staying on the flight line, and uncurls again.</summary>
        void Tube(float length, float radius, float curl)
        {
            frames.Clear();
            int steps = Mathf.CeilToInt(length / 10f);
            for (int i = 0; i <= steps; i++)
            {
                float d = length * i / steps;
                float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(d, length - d) / curl));
                float pipe = s < 0.001f ? 0f : radius / s;
                float half = Mathf.Lerp(HalfWidth, Mathf.PI * radius * 0.97f, s);
                frames.Add(Frame(cursor + Forward * d, heading, half, pipe));
            }
            cursor += Forward * length;
            Emit("Tube", 48, walls: false);
        }

        void Slalom(Vector3 from)
        {
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Vector3 centre = from + Forward * (i * 500f) + Right * (side * HalfWidth * 0.6f) + Up * 10f;
                Box("Slalom " + i, centre, new Vector3(HalfWidth * 0.6f, 20f, 6f));
            }
        }

        // ------------------------------------------------------------ output
        void Emit(string label, int crossSegments, bool walls)
        {
            Mesh mesh = SurfaceRibbonMesher.Build(frames, crossSegments, walls ? wallHeight : 0f, walls, walls, label);
            var go = new GameObject(label) { layer = ShipLayers.Ground };
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = SurfaceMaterial();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        void Box(string label, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.layer = ShipLayers.Ground;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(centre, heading);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = SurfaceMaterial();
        }

        Material SurfaceMaterial()
        {
            if (surfaceMaterial != null) return surfaceMaterial;
            if (runtimeMaterial != null) return runtimeMaterial;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var dark = new Color(0.10f, 0.11f, 0.14f);
            var light = new Color(0.22f, 0.25f, 0.32f);
            texture.SetPixels(new[] { dark, light, light, dark });
            texture.Apply();
            runtimeTexture = texture;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            runtimeMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Sandbox checker" };
            runtimeMaterial.mainTexture = texture;
            runtimeMaterial.mainTextureScale = new Vector2(1f / 20f, 1f / 20f); // UVs are metres: a 10 m checker
            return runtimeMaterial;
        }
    }
}
