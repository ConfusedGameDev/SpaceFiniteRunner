using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Gives the runner's track a BODY. The track was pure mathematics with a
    /// decorative mesh veneer — no floor, no walls, ramps loops and tubes that
    /// exist only as pose functions — which is exactly why the track-space
    /// ship could never leave it. The standalone ship knows a level only by
    /// its colliders, so this component streams them: chunk by chunk it
    /// samples <see cref="TrackManager.GetPoseAtDistance"/> across the lane
    /// (<see cref="TrackManager.GetLateralBand"/>) into a collision mesh. It
    /// never special-cases a feature: a loop, a curled tube and a banked sweep
    /// all come out of the one pose function the old ship rides, so the two
    /// can never disagree about where the road is. Walls stand on a lane edge
    /// only where <see cref="TrackManager.IsEdgeOpen"/> says it is closed — a
    /// flat sweep's outer side and an open straight are real drops — and a
    /// <see cref="JumpRamp"/> gets a wedge of its own.
    ///
    /// <b>The surface is sunk by the ship's ride height</b>, so a ship
    /// hovering over it rides the flight line exactly: pads, orbs, the camera
    /// and the patrol's pose all stay where they are. Meshes are single-sided
    /// (PhysX queries ignore back-faces), which is what lets a loop cross over
    /// its own entry road. Colliders only — the decorator owns the picture.
    ///
    /// It drives itself off the generator's public state (what is settled,
    /// what is behind the ship, a rebuild), so the generator's streaming code
    /// is untouched: nothing past the settle margin is built (AutoSmooth still
    /// reshapes those curves), and a chunk is culled by its END — a loop is
    /// 630 m of track, and keyed on its mouth it would vanish under a ship
    /// still climbing it.
    /// </summary>
    public sealed class TrackColliderBuilder : MonoBehaviour
    {
        [SerializeField, Required] TrackManager track;
        [SerializeField, Required] TrackGenerator generator;

        [TitleGroup("Surface")]
        [Tooltip("How far under the flight line the collision surface lies — the ship's hover height, so a ship hovering over it rides the flight line exactly.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        [SerializeField] float surfaceSink = 3.5f;

        [TitleGroup("Surface")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        [SerializeField] float wallHeight = 14f;

        [TitleGroup("Meshing")]
        [Tooltip("Track distance per collision chunk.")]
        [PropertyRange(40f, 400f), SuffixLabel("m", true)]
        [SerializeField] float chunkLength = 120f;

        [TitleGroup("Meshing")]
        [Tooltip("Distance between cross-sections. A loop or a tube needs it small; a straight does not care.")]
        [PropertyRange(1f, 20f), SuffixLabel("m", true)]
        [SerializeField] float sampleSpacing = 4f;

        [TitleGroup("Meshing")]
        [Tooltip("Quads across flat road. One would do for a level road, but a road whose bank is changing is a twisted strip, and a single quad folds it along a diagonal — half a metre off at the edges.")]
        [PropertyRange(1, 16)]
        [SerializeField] int flatColumns = 4;

        [TitleGroup("Meshing")]
        [Tooltip("Widest a collision quad may be across the lane where the road is curled (a tube). Flat road is a single quad wide.")]
        [PropertyRange(2f, 40f), SuffixLabel("m", true)]
        [SerializeField] float curledQuadWidth = 10f;

        [TitleGroup("Debug")]
        [Tooltip("Draw the collision meshes (a wireframe-ish checker) — they are invisible otherwise.")]
        [SerializeField] bool showMeshes;

        struct Chunk
        {
            public float end;
            public GameObject go;
            public Mesh mesh;
        }

        readonly List<Chunk> chunks = new();
        readonly Dictionary<JumpRamp, Chunk> ramps = new();
        readonly List<Vector3> vertices = new();
        readonly List<int> triangles = new();
        readonly List<JumpRamp> gone = new();
        float built;
        Material debugMaterial;

        /// <summary>Ramp wedges alive — a debug readout.</summary>
        public int RampCount => ramps.Count;
        /// <summary>Ramp wedges built since the last clear — a debug readout.</summary>
        public int RampsBuilt { get; private set; }

        /// <summary>Track distance the colliders reach.</summary>
        public float BuiltDistance => built;
        public float SurfaceSink { get => surfaceSink; set => surfaceSink = Mathf.Max(0f, value); }

        /// <summary>For a builder added from code: what the inspector would wire.</summary>
        public void Bind(TrackManager track, TrackGenerator generator)
        {
            if (this.generator != null) this.generator.Regenerated -= Clear;
            this.track = track;
            this.generator = generator;
            if (generator != null && isActiveAndEnabled) generator.Regenerated += Clear;
        }

        void OnEnable()
        {
            if (generator != null) generator.Regenerated += Clear;
        }

        void OnDisable()
        {
            if (generator != null) generator.Regenerated -= Clear;
        }

        void OnDestroy()
        {
            Clear();
            if (debugMaterial != null) Destroy(debugMaterial);
        }

        void Update()
        {
            if (track == null || generator == null) return;
            BuildUpTo(generator.SettledDistance);
            CullBefore(generator.CullDistance);
        }

        public void Clear()
        {
            foreach (Chunk chunk in chunks) Drop(chunk);
            foreach (Chunk chunk in ramps.Values) Drop(chunk);
            chunks.Clear();
            ramps.Clear();
            built = 0f;
            RampsBuilt = 0;
        }

        void Drop(Chunk chunk)
        {
            if (chunk.go != null) Destroy(chunk.go);
            if (chunk.mesh != null) Destroy(chunk.mesh);
        }

        // ------------------------------------------------------------- stream
        void BuildUpTo(float distance)
        {
            float limit = Mathf.Min(distance, track.Length);
            // Whole chunks only: a partial one would have to be rebuilt as the track grows.
            while (built + chunkLength <= limit)
            {
                chunks.Add(BuildRoad(built, built + chunkLength));
                built += chunkLength;
            }

            foreach (JumpRamp ramp in JumpRamp.Active)
                if (ramp != null && ramp.Definition != null && !ramps.ContainsKey(ramp) && ramp.EndDistance <= limit)
                {
                    ramps.Add(ramp, BuildRamp(ramp));
                    RampsBuilt++;
                }
        }

        void CullBefore(float distance)
        {
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                if (chunks[i].end >= distance) continue;
                Drop(chunks[i]);
                chunks.RemoveAt(i);
            }

            gone.Clear();
            foreach (KeyValuePair<JumpRamp, Chunk> pair in ramps)
                if (pair.Key == null || pair.Value.end < distance) gone.Add(pair.Key);
            foreach (JumpRamp ramp in gone)
            {
                Drop(ramps[ramp]);
                ramps.Remove(ramp);
            }
        }

        // -------------------------------------------------------------- road
        Chunk BuildRoad(float from, float to)
        {
            vertices.Clear();
            triangles.Clear();

            int rows = Mathf.Max(1, Mathf.CeilToInt((to - from) / sampleSpacing));
            // One column count for the whole chunk, so the grid stays a grid: as many as its most curled cross-section needs.
            int columns = Mathf.Max(1, flatColumns);
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(from, to, r / (float)rows);
                if (track.SectionAt(d) is not TubeSection tube) continue;
                track.GetLateralBand(d, out float min, out float max);
                if (tube.Curl(d - tube.StartDistance) > 0.001f)
                    columns = Mathf.Max(columns, Mathf.CeilToInt((max - min) / curledQuadWidth));
            }

            int stride = columns + 1;
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(from, to, r / (float)rows);
                track.GetLateralBand(d, out float min, out float max);
                for (int c = 0; c <= columns; c++)
                {
                    track.GetPoseAtDistance(d, Mathf.Lerp(min, max, c / (float)columns), out Vector3 position, out Quaternion rotation);
                    vertices.Add(position - rotation * Vector3.up * surfaceSink);
                }
            }

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    int l0 = r * stride + c, r0 = l0 + 1, l1 = l0 + stride, r1 = l1 + 1;
                    // Seen from above with forward up the screen: clockwise = facing up.
                    triangles.Add(l0); triangles.Add(l1); triangles.Add(r1);
                    triangles.Add(l0); triangles.Add(r1); triangles.Add(r0);
                }

            if (wallHeight > 0f)
            {
                AddWall(from, to, rows, -1);
                AddWall(from, to, rows, 1);
            }
            return Emit($"TrackCollider_{from:00000}", to);
        }

        // A wall stands on a lane edge, along the road's up there, facing the lane — wherever the track says that edge is closed.
        void AddWall(float from, float to, int rows, int side)
        {
            int previous = -1;
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(from, to, r / (float)rows);
                bool closed = !track.IsEdgeOpen(d, side) && !IsUnboundedTube(d);
                if (!closed) { previous = -1; continue; }

                track.GetLateralBand(d, out float min, out float max);
                track.GetPoseAtDistance(d, side < 0 ? min : max, out Vector3 position, out Quaternion rotation);
                Vector3 up = rotation * Vector3.up;
                Vector3 foot = position - up * surfaceSink;
                int index = vertices.Count;
                vertices.Add(foot);
                vertices.Add(foot + up * wallHeight);

                if (previous >= 0)
                {
                    int b0 = previous, t0 = previous + 1, b1 = index, t1 = index + 1;
                    if (side < 0) { triangles.Add(b0); triangles.Add(t0); triangles.Add(t1); triangles.Add(b0); triangles.Add(t1); triangles.Add(b1); }
                    else { triangles.Add(b1); triangles.Add(t1); triangles.Add(t0); triangles.Add(b1); triangles.Add(t0); triangles.Add(b0); }
                }
                previous = index;
            }
        }

        bool IsUnboundedTube(float distance) =>
            track.SectionAt(distance) is TubeSection tube && tube.IsUnboundedAt(distance - tube.StartDistance);

        // -------------------------------------------------------------- ramps
        // The ramp's slope, as wide as the ramp, rising from the road to the lip along the track's up.
        Chunk BuildRamp(JumpRamp ramp)
        {
            vertices.Clear();
            triangles.Clear();
            int rows = Mathf.Max(2, Mathf.CeilToInt(ramp.Length / sampleSpacing));
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(ramp.StartDistance, ramp.EndDistance, r / (float)rows);
                float height = r == rows ? ramp.Definition.LipHeight : ramp.HeightAt(d);
                for (int c = 0; c <= 1; c++)
                {
                    float lateral = ramp.Lateral + (c == 0 ? -ramp.HalfWidth : ramp.HalfWidth);
                    track.GetPoseAtDistance(d, lateral, out Vector3 position, out Quaternion rotation);
                    vertices.Add(position + rotation * Vector3.up * (height - surfaceSink));
                }
            }
            for (int r = 0; r < rows; r++)
            {
                int l0 = r * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                triangles.Add(l0); triangles.Add(l1); triangles.Add(r1);
                triangles.Add(l0); triangles.Add(r1); triangles.Add(r0);
            }
            return Emit($"RampCollider_{ramp.StartDistance:00000}", ramp.EndDistance);
        }

        // ------------------------------------------------------------- output
        Chunk Emit(string label, float end)
        {
            var mesh = new Mesh { name = label };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(label) { layer = ShipLayers.Ground };
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // vertices are world-space
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            if (showMeshes)
            {
                mesh.RecalculateNormals();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = DebugMaterial();
            }
            return new Chunk { end = end, go = go, mesh = mesh };
        }

        Material DebugMaterial()
        {
            if (debugMaterial != null) return debugMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            debugMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Track collider debug" };
            debugMaterial.color = new Color(0.1f, 0.9f, 0.5f, 1f);
            return debugMaterial;
        }
    }
}
