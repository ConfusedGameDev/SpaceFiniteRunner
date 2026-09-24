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
    /// samples <see cref="TrackManager.GetPoseAtDistance"/> across the whole
    /// road (<see cref="TrackManager.GetRoadBand"/>) into a collision mesh. It
    /// never special-cases a feature: a loop, a curled tube and a banked sweep
    /// all come out of the one pose function the old ship rides, so the two
    /// can never disagree about where the road is.
    ///
    /// <b>The road is wider than the steering lane</b>: outside it a banked
    /// shoulder climbs to the slab's outer lip, and that lip — not the crease
    /// where the bank leaves the lane — is where the walls stand and where an
    /// open edge finally drops away. The shoulder columns are lifted along the
    /// slope, so the collision road is the road the player can see and the bank
    /// is run-off to ride, not scenery to fall through. Walls go up only where
    /// <see cref="TrackManager.IsEdgeOpen"/> says the edge is closed — a flat
    /// sweep's outer side and an open straight are real drops — and a
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

        [TitleGroup("Meshing")]
        [Tooltip("Quads up each banked shoulder — the run-off between the lane edge and the wall. The slope is a straight ramp in the art, so one quad already puts the lip in the right place; two keep the collision crease from reading as a step under a grazing hull.")]
        [PropertyRange(1, 8)]
        [SerializeField] int shoulderColumns = 2;

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

        /// <summary>Builds everything that is settled, now — for the frame a run (re)starts, so the ship is launched onto road that exists.</summary>
        public void BuildNow()
        {
            if (track != null && generator != null) BuildUpTo(generator.SettledDistance);
        }

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
            // A finite track ends where it ends, not on a chunk boundary: once the end is settled the remainder is one last,
            // shorter chunk — the run-up to the end ramps stood on nothing without it.
            if (track.HasEnd && limit >= track.EndDistance && built < track.EndDistance - 0.01f)
            {
                chunks.Add(BuildRoad(built, track.EndDistance));
                built = track.EndDistance;
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
            int laneColumns = Mathf.Max(1, flatColumns);
            bool anyShoulders = false;
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(from, to, r / (float)rows);
                anyShoulders |= track.HasShoulders(d);
                if (track.SectionAt(d) is not TubeSection tube) continue;
                track.GetLateralBand(d, out float min, out float max);
                if (tube.Curl(d - tube.StartDistance) > 0.001f)
                    laneColumns = Mathf.Max(laneColumns, Mathf.CeilToInt((max - min) / curledQuadWidth));
            }

            // The shoulders get columns of their OWN, so a crease lands exactly
            // on a vertex ring: the flat centre stays flat and the slope starts
            // where the art's does. A chunk that straddles a tube keeps the
            // count and spreads it evenly over the band on the rows that have
            // no shoulder, so the grid is still a grid.
            int shoulderCols = anyShoulders ? Mathf.Max(1, shoulderColumns) : 0;
            int columns = laneColumns + 2 * shoulderCols;

            int stride = columns + 1;
            for (int r = 0; r <= rows; r++)
                AddCrossSection(Mathf.Lerp(from, to, r / (float)rows), columns, shoulderCols, laneColumns);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    int l0 = r * stride + c, r0 = l0 + 1, l1 = l0 + stride, r1 = l1 + 1;
                    // Seen from above with forward up the screen: clockwise = facing up.
                    triangles.Add(l0); triangles.Add(l1); triangles.Add(r1);
                    triangles.Add(l0); triangles.Add(r1); triangles.Add(r0);
                }

            // A loop's two halves cross in space when it barely drifts sideways (in track space nothing collided). Its
            // chunks are SURFACE-ONLY — ridden by the hover probes, invisible to the hull sweep — and carry no walls:
            // the guide's lane keeps the ship on the ring (HoverBody.HoldInLane).
            bool loop = false;
            for (int r = 0; r <= rows && !loop; r++)
                loop = track.SectionAt(Mathf.Lerp(from, to, r / (float)rows)) is LoopSection;

            if (wallHeight > 0f && !loop)
            {
                AddWall(from, to, rows, -1);
                AddWall(from, to, rows, 1);
            }
            return Emit($"TrackCollider_{from:00000}", to, loop ? ShipLayers.Surface : ShipLayers.Ground);
        }

        /// <summary>
        /// One ring of surface vertices across the road at a distance: the left
        /// shoulder falling from its lip to the lane, the lane, then the right
        /// shoulder back up. Always <paramref name="columns"/> + 1 points, so
        /// every row of a chunk indexes the same grid. Where the road has no
        /// shoulder (a loop, a tube) the whole band is walked evenly instead
        /// and nothing is lifted.
        /// </summary>
        void AddCrossSection(float d, int columns, int shoulderCols, int laneColumns)
        {
            track.GetLateralBand(d, out float min, out float max);
            bool shoulders = shoulderCols > 0 && track.HasShoulders(d);
            float shoulder = shoulders ? track.ShoulderWidth : 0f;
            float rise = shoulders ? track.ShoulderRise : 0f;

            for (int c = 0; c <= columns; c++)
            {
                float lateral, lift;
                if (!shoulders)
                {
                    lateral = Mathf.Lerp(min, max, c / (float)columns);
                    lift = 0f;
                }
                else if (c < shoulderCols)
                {
                    float u = c / (float)shoulderCols;                              // 0 at the outer lip, 1 at the crease
                    lateral = min - shoulder * (1f - u);
                    lift = rise * (1f - u);
                }
                else if (c <= shoulderCols + laneColumns)
                {
                    lateral = Mathf.Lerp(min, max, (c - shoulderCols) / (float)laneColumns);
                    lift = 0f;
                }
                else
                {
                    float u = (c - shoulderCols - laneColumns) / (float)shoulderCols; // 0 at the crease, 1 at the lip
                    lateral = max + shoulder * u;
                    lift = rise * u;
                }

                track.GetPoseAtDistance(d, lateral, out Vector3 position, out Quaternion rotation);
                Vector3 up = rotation * Vector3.up;
                vertices.Add(position - up * (surfaceSink - lift));
            }
        }

        // A wall stands on the ROAD's outer lip — the top of the banked shoulder, not the crease where the shoulder leaves
        // the lane — along the road's up there, facing the lane, wherever the track says that edge is closed. Everything
        // inside it is solid road: run wide off the lane, ride up the bank, and the wall is what turns you back.
        void AddWall(float from, float to, int rows, int side)
        {
            int previous = -1;
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(from, to, r / (float)rows);
                bool closed = !track.IsEdgeOpen(d, side) && !IsUnboundedTube(d);
                if (!closed) { previous = -1; continue; }

                track.GetRoadBand(d, out float min, out float max);
                float lift = track.HasShoulders(d) ? track.ShoulderRise : 0f;
                track.GetPoseAtDistance(d, side < 0 ? min : max, out Vector3 position, out Quaternion rotation);
                Vector3 up = rotation * Vector3.up;
                Vector3 foot = position - up * (surfaceSink - lift);
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
        // The ramp's slope, rising from the road to the lip along the track's up — as wide as the ramp's RULE, not its
        // picture: in track space a ship is on the ramp once its CENTRE is entryMargin inside the edge and is held out at
        // that same line otherwise, so the wedge ends there. At the picture's width a hull (a 2.5 m sphere) was turned
        // away 5 m earlier than the track-space ship, and the centre line of the road was blocked by most offset ramps.
        Chunk BuildRamp(JumpRamp ramp)
        {
            vertices.Clear();
            triangles.Clear();
            float halfWidth = Mathf.Max(1f, ramp.HalfWidth - ramp.Definition.entryMargin);
            int rows = Mathf.Max(2, Mathf.CeilToInt(ramp.Length / sampleSpacing));
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(ramp.StartDistance, ramp.EndDistance, r / (float)rows);
                float height = r == rows ? ramp.Definition.LipHeight : ramp.HeightAt(d);
                for (int c = 0; c <= 1; c++)
                {
                    float lateral = ramp.Lateral + (c == 0 ? -halfWidth : halfWidth);
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

            // The wedge's flanks, facing OUT. Without them a ship that meets the ramp off-line rides half on the
            // slope and half on the road (it took off and landed in the same tick and never jumped). With them the
            // side of a ramp is what it always was in track space: a wall — a hit, a speed loss, and round you go.
            int slopeVertices = vertices.Count;
            for (int r = 0; r <= rows; r++)
            {
                float d = Mathf.Lerp(ramp.StartDistance, ramp.EndDistance, r / (float)rows);
                for (int c = 0; c <= 1; c++)
                {
                    float lateral = ramp.Lateral + (c == 0 ? -halfWidth : halfWidth);
                    track.GetPoseAtDistance(d, lateral, out Vector3 position, out Quaternion rotation);
                    vertices.Add(position - rotation * Vector3.up * surfaceSink); // the foot, on the road surface
                }
            }
            for (int r = 0; r < rows; r++)
            {
                // Left flank faces −right, right flank faces +right; tops are the slope's own edge vertices.
                int lb0 = slopeVertices + r * 2, lb1 = lb0 + 2, lt0 = r * 2, lt1 = lt0 + 2;
                triangles.Add(lb1); triangles.Add(lt1); triangles.Add(lt0);
                triangles.Add(lb1); triangles.Add(lt0); triangles.Add(lb0);
                int rb0 = lb0 + 1, rb1 = lb1 + 1, rt0 = lt0 + 1, rt1 = lt1 + 1;
                triangles.Add(rb0); triangles.Add(rt0); triangles.Add(rt1);
                triangles.Add(rb0); triangles.Add(rt1); triangles.Add(rb1);
            }
            return Emit($"RampCollider_{ramp.StartDistance:00000}", ramp.EndDistance, ShipLayers.Ground);
        }

        // ------------------------------------------------------------- output
        Chunk Emit(string label, float end, int layer)
        {
            var mesh = new Mesh { name = label };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(label) { layer = layer };
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
