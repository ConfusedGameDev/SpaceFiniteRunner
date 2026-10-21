using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Electricity crawling over a model: looping sprite-sheet quads pinned to
    /// random points on the meshes under <c>source</c>. Each spark lies along
    /// the surface (tilted toward the camera so it never goes edge on),
    /// follows its mesh part as it moves, plays a loop of the sheet and then
    /// hops to a new point. Ported from RevolverBallad's <c>FX_KakoSparks</c>
    /// (<c>MeshSurfaceSparksFX</c>).
    ///
    /// <b>The quads are never under the model</b>: they are children of this
    /// object and posed in world space every LateUpdate, so the model can sit
    /// anywhere (the ship's <c>Visual</c>, whose renderers and meshes the dash
    /// ghosts, the respawn blink and the livery all walk, must not gain spark
    /// quads). <b>It is a burst, not a loop</b>: <see cref="Play"/> lights it
    /// at full and <c>fadeCurve</c> takes it out over <c>fadeSeconds</c>; a
    /// play mid-fade starts it over at full. Idle, the quads are hidden and
    /// nothing updates. The look is the prefab's (sheet, material, count,
    /// size, tint) — tune it there. Scaled time, so a pause freezes it.
    /// Meshes must have Read/Write enabled; one without gets a warning and no
    /// sparks.
    /// </summary>
    public class MeshSurfaceSparks : MonoBehaviour
    {
        [BoxGroup("Sheet"), Required, Tooltip("URP Particles/Unlit material with the spark sheet.")]
        [SerializeField] Material material;
        [BoxGroup("Sheet"), HorizontalGroup("Sheet/Grid"), MinValue(1)]
        [SerializeField] int columns = 6;
        [BoxGroup("Sheet"), HorizontalGroup("Sheet/Grid"), MinValue(1)]
        [SerializeField] int rows = 3;
        [BoxGroup("Sheet"), PropertyRange(1, 64)]
        [SerializeField] int frameCount = 12;
        [BoxGroup("Sheet"), PropertyRange(1f, 120f), SuffixLabel("fps", true)]
        [SerializeField] float framesPerSecond = 30f;

        [BoxGroup("Sparks"), Tooltip("The model the sparks crawl over: every MeshFilter under it. Empty = this object's parent.")]
        [SerializeField] Transform source;
        [BoxGroup("Sparks"), PropertyRange(1, 64)]
        [SerializeField] int sparkCount = 12;
        [BoxGroup("Sparks"), MinMaxSlider(0.1f, 20f, true), SuffixLabel("m", true), Tooltip("World height of one spark.")]
        [SerializeField] Vector2 size = new(0.9f, 1.5f);
        [BoxGroup("Sparks"), PropertyRange(0f, 2f), SuffixLabel("m", true), Tooltip("Lift off the surface so the quad isn't hidden inside it.")]
        [SerializeField] float surfaceOffset = 0.06f;
        [BoxGroup("Sparks"), PropertyRange(0f, 1f), Tooltip("0 = lie flat on the surface, 1 = face the camera.")]
        [SerializeField] float cameraFacing = 0.5f;
        [BoxGroup("Sparks"), MinMaxSlider(0.5f, 3f, true), Tooltip("Sheet loops played before hopping to a new spot.")]
        [SerializeField] Vector2 loopsPerHop = new(0.6f, 1.2f);
        [BoxGroup("Sparks"), ColorUsage(true, true)]
        [SerializeField] Color tint = new(2f, 3f, 4.5f, 1f);

        [BoxGroup("Fade"), PropertyRange(0.1f, 10f), SuffixLabel("s", true), Tooltip("How long a burst lasts, from Play to gone.")]
        [SerializeField] float fadeSeconds = 2.5f;
        [BoxGroup("Fade"), Tooltip("Strength over the burst (x 0..1 = the share of fadeSeconds gone, y = alpha multiplier).")]
        [SerializeField] AnimationCurve fadeCurve = new(new Keyframe(0f, 1f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));
        [BoxGroup("Fade"), Tooltip("Play once on Start — for previewing the look in play mode.")]
        [SerializeField] bool playOnStart;

        /// <summary>Per-mesh triangle data, shared by every instance.</summary>
        class Surface
        {
            public Vector3[] vertices, normals;
            public int[] triangles;
            public float[] cumulativeArea; // local space, per triangle
            public float totalArea;
        }

        class Spark
        {
            public Transform transform;
            public MeshRenderer renderer;
            public FlipbookQuad quad;
            public MeshFilter target;
            public int source;
            public Vector3 localPoint, localNormal, localTangent;
            public float age, duration, size, frameOffset;
            public bool mirror;
        }

        // Domain reload is off: emptied on every play so a Read/Write change is picked up.
        static readonly Dictionary<Mesh, Surface> surfaces = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => surfaces.Clear();

        readonly List<MeshFilter> sources = new();
        readonly List<Renderer> sourceRenderers = new();
        readonly List<float> sourceWeights = new();
        float totalWeight;
        Spark[] sparks;
        Transform cam;
        float aspect = 1f;
        float burstAge = float.PositiveInfinity; // past fadeSeconds = idle
        bool built;
        bool held; // the inspector's toggle: on at full until toggled off

        /// <summary>A burst is showing.</summary>
        public bool IsPlaying => burstAge < fadeSeconds;

        void Start()
        {
            if (playOnStart) Play();
        }

        /// <summary>Lights the sparks at full and starts the fade; mid-fade, starts it over.</summary>
        public void Play()
        {
            if (!built) Build();
            if (sparks == null) return;
            bool wasIdle = !IsPlaying;
            burstAge = 0f;
            if (!wasIdle) return;
            foreach (var s in sparks)
            {
                Hop(s);
                s.age = Random.Range(0f, s.duration); // desync the first hops
                s.renderer.enabled = true;
            }
            UpdateSparks(0f);
        }

        /// <summary>The sparks are held on by <see cref="ToggleHeld"/>.</summary>
        public bool IsHeld => held;

        /// <summary>
        /// Holds the sparks on at full strength until called again, which lets
        /// them fade out as a burst would — for tuning the look in play mode.
        /// </summary>
        [Button("Toggle Sparks", ButtonSizes.Large), PropertyOrder(-1), EnableIf("@UnityEngine.Application.isPlaying")]
        [InfoBox("Play mode only: holds the sparks on until pressed again.", InfoMessageType.None, "@!UnityEngine.Application.isPlaying")]
        public void ToggleHeld()
        {
            if (!Application.isPlaying) return;
            held = !held;
            if (held) Play();
            else burstAge = 0f; // fade out from full
        }

        /// <summary>Hides the sparks at once (and releases the toggle).</summary>
        public void Stop()
        {
            held = false;
            burstAge = float.PositiveInfinity;
            SetVisible(false);
        }

        void Build()
        {
            built = true;
            if (Camera.main) cam = Camera.main.transform;
            CollectSources();
            if (sources.Count == 0) return;

            sparks = new Spark[sparkCount];
            for (int i = 0; i < sparkCount; i++)
            {
                var go = new GameObject($"Spark{i}");
                go.transform.SetParent(transform, false);
                var quad = new FlipbookQuad(columns, rows, false, go.name);
                go.AddComponent<MeshFilter>().sharedMesh = quad.Mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                aspect = quad.CellAspect(material ? material.mainTexture : null);
                sparks[i] = new Spark { transform = go.transform, renderer = r, quad = quad };
            }
        }

        void CollectSources()
        {
            Transform root = source != null ? source : (transform.parent ? transform.parent : transform);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh || mf.transform.IsChildOf(transform)) continue;
                var surface = GetSurface(mf.sharedMesh);
                if (surface == null) continue;
                // Weight by world-space size so big parts get more sparks than small ones.
                var s = mf.transform.lossyScale;
                float weight = surface.totalArea * Mathf.Abs(s.x * s.y + s.y * s.z + s.z * s.x) / 3f;
                if (weight <= 0f) continue;
                sources.Add(mf);
                sourceRenderers.Add(mf.GetComponent<Renderer>());
                sourceWeights.Add(weight);
                totalWeight += weight;
            }
            if (sources.Count == 0)
                Debug.LogWarning($"MeshSurfaceSparks: '{name}' found no readable mesh under '{root.name}' — no sparks.", this);
        }

        static Surface GetSurface(Mesh mesh)
        {
            if (surfaces.TryGetValue(mesh, out var cached)) return cached;
            if (!mesh.isReadable)
            {
                Debug.LogWarning($"MeshSurfaceSparks: enable Read/Write on '{mesh.name}' to put sparks on it.");
                surfaces[mesh] = null;
                return null;
            }
            var surface = new Surface { vertices = mesh.vertices, normals = mesh.normals, triangles = mesh.triangles };
            int triCount = surface.triangles.Length / 3;
            surface.cumulativeArea = new float[triCount];
            for (int t = 0; t < triCount; t++)
            {
                var a = surface.vertices[surface.triangles[t * 3]];
                var b = surface.vertices[surface.triangles[t * 3 + 1]];
                var c = surface.vertices[surface.triangles[t * 3 + 2]];
                surface.totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                surface.cumulativeArea[t] = surface.totalArea;
            }
            surfaces[mesh] = surface;
            return surface;
        }

        /// <summary>Moves a spark to a random point on the model (area-weighted) and rerolls its look.</summary>
        void Hop(Spark spark)
        {
            // Skip parts that are currently hidden.
            int index = 0;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float pick = Random.value * totalWeight;
                index = 0;
                while (index < sources.Count - 1 && pick > sourceWeights[index]) pick -= sourceWeights[index++];
                if (IsVisible(index)) break;
            }
            var mf = sources[index];
            var surface = surfaces[mf.sharedMesh];

            int tri = System.Array.BinarySearch(surface.cumulativeArea, Random.value * surface.totalArea);
            if (tri < 0) tri = ~tri;
            tri = Mathf.Min(tri, surface.cumulativeArea.Length - 1);
            int i0 = surface.triangles[tri * 3], i1 = surface.triangles[tri * 3 + 1], i2 = surface.triangles[tri * 3 + 2];
            float u = Random.value, v = Random.value;
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            var a = surface.vertices[i0];
            var b = surface.vertices[i1];
            var c = surface.vertices[i2];

            spark.target = mf;
            spark.source = index;
            spark.localPoint = a + (b - a) * u + (c - a) * v;
            var faceNormal = Vector3.Cross(b - a, c - a).normalized;
            spark.localNormal = surface.normals.Length > 0
                ? (surface.normals[i0] * (1f - u - v) + surface.normals[i1] * u + surface.normals[i2] * v).normalized
                : faceNormal;
            // Random direction in the surface plane, so the arcs don't all line up.
            spark.localTangent = Quaternion.AngleAxis(Random.Range(0f, 360f), spark.localNormal) * (b - a).normalized;
            spark.age = 0f;
            spark.duration = Random.Range(loopsPerHop.x, loopsPerHop.y) * frameCount / framesPerSecond;
            spark.size = Random.Range(size.x, size.y);
            spark.frameOffset = Random.Range(0, frameCount);
            spark.mirror = Random.value < 0.5f;
        }

        bool IsVisible(int index)
        {
            var r = sourceRenderers[index];
            return sources[index] && (!r || (r.enabled && r.gameObject.activeInHierarchy));
        }

        void LateUpdate()
        {
            if (sparks == null || !IsPlaying) return;
            if (!held) burstAge += Time.deltaTime;
            if (!IsPlaying)
            {
                SetVisible(false);
                return;
            }
            UpdateSparks(Time.deltaTime);
        }

        void SetVisible(bool visible)
        {
            if (sparks == null) return;
            foreach (var s in sparks) if (s.renderer.enabled != visible) s.renderer.enabled = visible;
        }

        void UpdateSparks(float dt)
        {
            if (cam == null && Camera.main) cam = Camera.main.transform;
            float strength = Mathf.Clamp01(fadeCurve.Evaluate(burstAge / fadeSeconds));
            float unit = 1f / Mathf.Max(1e-4f, transform.lossyScale.x); // sizes are world metres, whatever this object's scale
            int frames = Mathf.Max(1, frameCount);
            foreach (var s in sparks)
            {
                s.age += dt;
                if (s.age >= s.duration || !s.target || !IsVisible(s.source)) Hop(s);

                var t = s.target.transform;
                var normal = t.TransformDirection(s.localNormal).normalized;
                var tangent = Vector3.ProjectOnPlane(t.TransformDirection(s.localTangent), normal);
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.ProjectOnPlane(Vector3.up, normal);
                var position = t.TransformPoint(s.localPoint) + normal * surfaceOffset;
                s.transform.position = position;

                // The quad's visible side faces its -Z: lay it on the surface facing outward, then tilt toward the camera.
                var rotation = Quaternion.LookRotation(-normal, tangent);
                if (cam && cameraFacing > 0f)
                    rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(position - cam.position, tangent), cameraFacing);
                s.transform.rotation = rotation;
                s.transform.localScale = new Vector3(s.size * aspect, s.size, 1f) * unit;

                int frame = Mathf.FloorToInt(s.age * framesPerSecond + s.frameOffset) % frames;
                s.quad.SetFrame(frame, s.mirror);
                // Quick fade at both ends of each hop so sparks don't pop, under the burst's fade.
                float env = Mathf.Clamp01(Mathf.Min(s.age, s.duration - s.age) / 0.06f);
                var c = tint;
                c.a *= env * strength;
                s.quad.SetColor(c);
            }
        }

        void OnDestroy()
        {
            if (sparks == null) return;
            foreach (var s in sparks) s.quad?.Destroy();
        }
    }
}
