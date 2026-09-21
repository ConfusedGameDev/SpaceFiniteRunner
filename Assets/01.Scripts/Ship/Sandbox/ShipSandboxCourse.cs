using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

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
    /// an R = 60 pipe ridden on its outside — all of that with NO guide — then
    /// the guided stretch (an R = 490 S-curve and a second corkscrew loop under
    /// a full-assist <see cref="SplineGuide"/>, and a gentle flat sweep under a
    /// grip-testing one), and a dead-end wall.
    ///
    /// The geometry is built at <c>Awake</c> (level geometry, like the
    /// runner's streamed track — nothing is saved into the scene or the repo)
    /// by a turtle that walks the course and hands cross-sections to
    /// <see cref="SurfaceRibbonMesher"/>; every section also leaves a
    /// <see cref="Station"/> the debug overlay can teleport the ship to.
    ///
    /// <b>It can also be generated in the editor</b> (the Generate button),
    /// two ways. As a <i>preview</i> (the default) the objects are never
    /// saved: they are there to look at, they follow the sliders as you drag
    /// them, and play regenerates the very same course. With
    /// <see cref="keepGenerated"/> on, the generated objects are ordinary
    /// scene objects instead — move a wall, delete the ramp, reshape a guide's
    /// spline — and play flies them exactly as they stand: Awake only walks
    /// the course to find its stations and builds nothing.
    /// </summary>
    [ExecuteAlways] // only so an editor preview can clean up after itself; the course itself is built in play
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

        [TitleGroup("Editor")]
        [Tooltip("Off = Generate makes a PREVIEW: never saved, follows the sliders, and play regenerates the same course. On = the generated objects are saved with the scene and play flies them as they stand, hand edits included — Generate again only when you want to throw those edits away.")]
        [SerializeField] bool keepGenerated;

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
        readonly List<RibbonFrame> guideFrames = new();
        bool recordingGuide;
        bool stationsOnly; // walking the course for its stations: nothing is created
        Material runtimeMaterial;
        Texture2D runtimeTexture;
        Vector3 cursor;
        Quaternion heading;

        public IReadOnlyList<Station> Stations => stations;
        float HalfWidth => width * 0.5f;

        void Awake()
        {
            if (!Application.isPlaying) return;
            // Kept geometry is flown as it stands in the scene; only the stations are worked out again.
            stationsOnly = keepGenerated && transform.childCount > 0;
            if (!stationsOnly) ClearGenerated();
            Build();
            stationsOnly = false;
        }

        void OnDestroy()
        {
            Kill(runtimeMaterial);
            Kill(runtimeTexture);
        }

        /// <summary>Builds the course in the editor — a preview, or the real scene objects when <see cref="keepGenerated"/> is on.</summary>
        [TitleGroup("Editor"), Button(ButtonSizes.Large), DisableInPlayMode]
        public void Generate()
        {
            ClearGenerated();
            stationsOnly = false;
            Build();
        }

        [TitleGroup("Editor"), Button, DisableInPlayMode]
        public void Clear() => ClearGenerated();

        void ClearGenerated()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                var filter = child.GetComponent<MeshFilter>();
                // Ribbon meshes are ours (no asset behind them); a cube's mesh is Unity's and stays.
                if (filter != null && child.GetComponent<MeshCollider>() != null) Kill(filter.sharedMesh);
                Kill(child);
            }
            Kill(runtimeMaterial);
            Kill(runtimeTexture);
            runtimeMaterial = null;
            runtimeTexture = null;
            stations.Clear();
        }

        static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        /// <summary>A preview object must never reach the scene file; a kept one is an ordinary scene object.</summary>
        T Own<T>(T target) where T : Object
        {
            if (!Application.isPlaying && !keepGenerated) target.hideFlags = HideFlags.DontSave;
            return target;
        }

#if UNITY_EDITOR
        // Preview objects are DontSave, and Unity does not destroy those when a
        // scene unloads — left alone they would sit in play mode as a second,
        // invisible-to-the-hierarchy copy of every collider. So a preview is
        // taken down whenever this component goes (play starts, the scene
        // closes, scripts reload) and put back when it returns in edit mode.
        string PreviewKey => "ShipSandboxCourse.preview." + gameObject.scene.path + "/" + name;

        void OnEnable()
        {
            // Always subscribed: coming back from play, this runs while Unity still says
            // it is playing, and EnteredEditMode is the first moment a rebuild is safe.
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
            UnityEditor.EditorApplication.delayCall += RestorePreview; // a script reload or a reopened scene
        }

        void OnDisable()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (!Application.isPlaying) TakeDownPreview();
        }

        void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingEditMode) TakeDownPreview();
            else if (change == UnityEditor.PlayModeStateChange.EnteredEditMode) RestorePreview();
        }

        // The note is only spent once the preview is really back.
        void RestorePreview()
        {
            if (this == null || keepGenerated || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!UnityEditor.SessionState.GetBool(PreviewKey, false)) return;
            UnityEditor.SessionState.SetBool(PreviewKey, false);
            Generate();
        }

        void TakeDownPreview()
        {
            if (keepGenerated || transform.childCount == 0) return;
            UnityEditor.SessionState.SetBool(PreviewKey, true);
            ClearGenerated();
        }

        // A preview follows the sliders. Kept geometry never does — it may carry hand edits.
        void OnValidate()
        {
            if (Application.isPlaying || keepGenerated || transform.childCount == 0) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && !Application.isPlaying && !keepGenerated && transform.childCount > 0) Generate();
            };
        }
#endif

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
            // Orbs on the road UNDER the flight: a jump must clear them, a ship on the ground must take them. They come back, so every pass finds them.
            for (int i = 0; i < 5; i++)
                Orb("Jump orb " + i, cursor + Forward * (150f + i * 100f) + Up * 3.5f, 0f, respawnSeconds: 2f);
            Mark("Landing orbs", back: -100f);
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

            // ---- the guided stretch: the same kinds of road, with a line to follow
            Straight("Guide run-up", 600f, walls: true);
            BeginGuide();
            Mark("Guided S-curve", back: 0f);
            Straight("Guided lead-in", 400f, walls: true);
            Sweep(490f, 60f, 0f, "Guided S right");
            Sweep(490f, -60f, 0f, "Guided S left");
            Straight("Guided S exit", 600f, walls: true);
            Mark("Guided loop", back: 400f);
            Loop(loopRadius, loopLateralDrift, 200f, "Guided loop");
            Straight("After guided loop", 800f, walls: true);
            EndGuide("Guide (full assist)", assist: 1f, gripTested: false);

            Straight("Between guides", 400f, walls: true);
            BeginGuide();
            Mark("Flat sweep", back: 0f);
            Straight("Flat sweep lead-in", 400f, walls: true);
            Sweep(2500f, 40f, 0f, "Flat sweep");
            Straight("Flat sweep exit", 600f, walls: true);
            EndGuide("Guide (grip tested)", assist: 1f, gripTested: true);

            // ---- falling off: a deck with no walls over a kill volume, then a thin kill curtain across the road
            Straight("Deck run-up", 400f, walls: true);
            Mark("Open deck", back: 200f);
            Vector3 deckStart = cursor;
            Straight("Open deck", 1200f, walls: false);
            Volume<KillVolume>("Pit under the deck", deckStart + Forward * 600f - Up * 60f, new Vector3(4000f, 5f, 2400f));
            Straight("After deck", 600f, walls: true);

            Mark("Kill curtain", back: 0f);
            Straight("Curtain road", 1200f, walls: true);
            Volume<KillVolume>("Kill curtain", cursor - Forward * 600f + Up * 10f, new Vector3(width, 40f, 5f));

            // ---- pickups: a hundred orbs, 50 m apart, to be taken at 36 m per tick
            Mark("Orb line", back: 0f);
            for (int i = 0; i < 100; i++)
                Orb("Orb " + i, cursor + Forward * (300f + i * 50f) + Up * 3.5f, 10f, respawnSeconds: 5f); // they come back, so the run can be repeated in one session
            Straight("Orb line", 5600f, walls: true);

            Straight("Run-out", 1200f, walls: true);
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

        /// <summary>A level turn (positive degrees = right), banked into it: the bank eases in and out so both ends meet flat road.</summary>
        void Sweep(float radius, float degrees, float bank, string label = "Banked sweep")
        {
            frames.Clear();
            float side = Mathf.Sign(degrees);
            float arc = radius * Mathf.Abs(degrees) * Mathf.Deg2Rad;
            int steps = Mathf.Max(2, Mathf.CeilToInt(arc / 8f));
            float ds = arc / steps;
            frames.Add(Frame(cursor, heading, HalfWidth));
            for (int i = 1; i <= steps; i++)
            {
                heading *= Quaternion.Euler(0f, degrees / steps, 0f);
                cursor += Forward * ds;
                float u = i / (float)steps;
                float ease = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, 1f - u) * 4f));
                // A turn drops its inside edge.
                frames.Add(Frame(cursor, heading * Quaternion.Euler(0f, 0f, -bank * ease * side), HalfWidth));
            }
            Emit(label, 1, walls: true);
        }

        /// <summary>A helix standing on the entry pose: once round, drifting sideways and carrying forward so the exit clears the entry.</summary>
        void Loop(float radius, float drift, float carry, string label = "Loop")
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
            Emit(label, 1, walls: true);
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

        // ------------------------------------------------------------- guides
        void BeginGuide()
        {
            guideFrames.Clear();
            recordingGuide = true;
        }

        /// <summary>
        /// Draws a real <see cref="SplineContainer"/> down the middle of
        /// everything emitted since <see cref="BeginGuide"/> — one knot every
        /// ~20 m carrying the road's up in its rotation — and puts a
        /// <see cref="SplineGuide"/> on it: exactly what a level designer would
        /// author by hand, so the acceptance run exercises the real path.
        /// </summary>
        void EndGuide(string label, float assist, bool gripTested)
        {
            recordingGuide = false;
            if (stationsOnly) return;
            var go = Own(new GameObject(label));
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var container = go.AddComponent<SplineContainer>();
            Spline spline = container.Spline;
            spline.Clear();

            Vector3 last = Vector3.positiveInfinity;
            for (int i = 0; i < guideFrames.Count; i++)
            {
                RibbonFrame frame = guideFrames[i];
                bool end = i == guideFrames.Count - 1;
                if (!end && Vector3.Distance(frame.position, last) < 20f) continue;
                last = frame.position;
                Vector3 forward = Vector3.Cross(frame.right, frame.up);
                spline.Add(new BezierKnot((float3)frame.position, float3.zero, float3.zero, Quaternion.LookRotation(forward, frame.up)),
                           TangentMode.AutoSmooth);
            }

            var guide = go.AddComponent<SplineGuide>();
            guide.Configure(assist, captureRange: width, halfWidth: HalfWidth, gripTested: gripTested);
            guide.Rebuild();
        }

        // ------------------------------------------------------------ output
        void Emit(string label, int crossSegments, bool walls)
        {
            if (recordingGuide) guideFrames.AddRange(frames);
            if (stationsOnly) return;
            Mesh mesh = Own(SurfaceRibbonMesher.Build(frames, crossSegments, walls ? wallHeight : 0f, walls, walls, label));
            var go = Own(new GameObject(label) { layer = ShipLayers.Ground });
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = SurfaceMaterial();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        void Box(string label, Vector3 centre, Vector3 size)
        {
            if (stationsOnly) return;
            var go = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = label;
            go.layer = ShipLayers.Ground;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(centre, heading);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = SurfaceMaterial();
        }

        /// <summary>A boost orb: a 3 m ball with a trigger collider on the pickup layer — exactly what a level designer would place by hand.</summary>
        void Orb(string label, Vector3 centre, float speedDelta, float respawnSeconds = 0f)
        {
            if (stationsOnly) return;
            var go = Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            go.name = label;
            go.layer = ShipLayers.Pickup;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(centre, heading);
            go.transform.localScale = Vector3.one * 3f;
            go.GetComponent<Collider>().isTrigger = true;
            go.GetComponent<MeshRenderer>().sharedMaterial = SurfaceMaterial();
            go.AddComponent<ShipBoostPickup>().Configure(speedDelta, consumed: true, respawnSeconds);
        }

        /// <summary>A rule volume (kill, magnet): a trigger box on the ship's volume layer, with no picture.</summary>
        void Volume<T>(string label, Vector3 centre, Vector3 size) where T : Component
        {
            if (stationsOnly) return;
            var go = Own(new GameObject(label) { layer = ShipLayers.Volume });
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(centre, heading);
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;
            go.AddComponent<T>();
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
            runtimeTexture = Own(texture);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            runtimeMaterial = Own(new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Sandbox checker" });
            runtimeMaterial.mainTexture = texture;
            runtimeMaterial.mainTextureScale = new Vector2(1f / 20f, 1f / 20f); // UVs are metres: a 10 m checker
            return runtimeMaterial;
        }
    }
}
