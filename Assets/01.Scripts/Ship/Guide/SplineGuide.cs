using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The guide any level can have: put it beside a <see cref="SplineContainer"/>
    /// drawn along the road and every ship in reach is helped along it — no
    /// other wiring, the ship finds it through the <see cref="ShipGuideRegistry"/>.
    /// The knots' rotations carry the road's up, so a line drawn through a
    /// loop or along a banked wall guides there too.
    ///
    /// The spline is baked once into an arc-length table (position, tangent,
    /// up, signed curvature every <see cref="sampleSpacing"/> metres) and every
    /// query runs on the table: a projection is a nearest-segment search in a
    /// window around the last answer, a few dozen dot products, which is what
    /// lets a ship ask it every substep at Light Speed. The window is also
    /// what tells the two passes of a line that crosses itself apart. Call
    /// <see cref="Rebuild"/> after editing the spline at runtime.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    public sealed class SplineGuide : MonoBehaviour, IShipGuide
    {
        [TitleGroup("Guide")]
        [Tooltip("How strongly this level helps: 0 = the line is only there for coordinates, 1 = the ship's heading IS the line's and the stick strafes (the runner's feel). The ship's own assist setting multiplies it.")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float assist = 1f;

        [TitleGroup("Guide")]
        [Tooltip("How far from the line a ship is still guided.")]
        [PropertyRange(5f, 500f), SuffixLabel("m", true)]
        [SerializeField] float captureRange = 80f;

        [TitleGroup("Guide")]
        [Tooltip("Half width of the steering lane the guide reports around the line.")]
        [PropertyRange(1f, 200f), SuffixLabel("m", true)]
        [SerializeField] float halfWidth = 52f;

        [TitleGroup("Guide")]
        [Tooltip("The road does not hold the ship by itself: curves taken beyond the ship's grip slide it outward, like the runner's flat sweeps.")]
        [SerializeField] bool gripTested;

        [TitleGroup("Guide")]
        [Tooltip("Both edges are open: past the lane is a fall.")]
        [SerializeField] bool openEdges;

        [TitleGroup("Baking")]
        [Tooltip("Distance between baked samples. Smaller follows tight geometry (a loop) more exactly.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true)]
        [SerializeField] float sampleSpacing = 2f;

        [TitleGroup("Baking")]
        [Tooltip("How far around its last answer a projection looks, each way.")]
        [PropertyRange(20f, 1000f), SuffixLabel("m", true)]
        [SerializeField] float searchWindow = 150f;

        struct Baked
        {
            public Vector3 position, forward, up;
            public float distance, curvature;
        }

        const float EndSlack = 5f;

        Baked[] samples = new Baked[0];
        bool closed;

        public float Length { get; private set; }
        public float CaptureRange => captureRange;
        public float Assist => assist;
        public Scene Scene => gameObject.scene;

        /// <summary>For a guide built from code (a generated level): the inspector's knobs in one call. Follow it with <see cref="Rebuild"/> once the spline has its knots.</summary>
        public void Configure(float assist, float captureRange, float halfWidth, bool gripTested = false, bool openEdges = false)
        {
            this.assist = Mathf.Clamp01(assist);
            this.captureRange = captureRange;
            this.halfWidth = halfWidth;
            this.gripTested = gripTested;
            this.openEdges = openEdges;
        }

        void OnEnable()
        {
            Rebuild();
            ShipGuideRegistry.Register(this);
        }

        void OnDisable() => ShipGuideRegistry.Unregister(this);

        /// <summary>Re-bakes the table from the container's first spline.</summary>
        [Button]
        public void Rebuild()
        {
            var container = GetComponent<SplineContainer>();
            samples = new Baked[0];
            Length = 0f;
            if (container == null || container.Spline == null || container.Spline.Count < 2) return;

            closed = container.Spline.Closed;
            float splineLength = container.CalculateLength();
            int count = Mathf.Max(2, Mathf.CeilToInt(splineLength / Mathf.Max(sampleSpacing, 0.1f)) + 1);
            samples = new Baked[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                container.Evaluate(t, out float3 position, out float3 tangent, out float3 up);
                Vector3 forward = math.lengthsq(tangent) > 1e-10f ? (Vector3)math.normalize(tangent) : Vector3.forward;
                Vector3 upward = Vector3.ProjectOnPlane(up, forward);
                samples[i].position = position;
                samples[i].forward = forward;
                samples[i].up = upward.sqrMagnitude > 1e-8f ? upward.normalized : Vector3.up;
                samples[i].distance = i == 0 ? 0f : samples[i - 1].distance + Vector3.Distance(samples[i - 1].position, position);
            }
            Length = samples[count - 1].distance;

            // Signed turn rate about the road's up, positive to the right.
            for (int i = 0; i < count; i++)
            {
                int a = Mathf.Max(i - 1, 0), b = Mathf.Min(i + 1, count - 1);
                float span = samples[b].distance - samples[a].distance;
                if (span < 1e-4f) continue;
                Vector3 turn = samples[b].forward - samples[a].forward;
                Vector3 right = Vector3.Cross(samples[i].up, samples[i].forward);
                samples[i].curvature = Vector3.Dot(turn, right) / span;
            }
        }

        public bool TryProject(Vector3 worldPosition, ref float hint, out GuideSample sample)
        {
            sample = default;
            int count = samples.Length;
            if (count < 2) return false;

            int from = 0, to = count - 2;
            if (!float.IsNaN(hint))
            {
                from = Mathf.Max(0, IndexAt(hint - searchWindow));
                to = Mathf.Min(count - 2, IndexAt(hint + searchWindow));
            }

            int best = -1;
            float bestT = 0f, bestSqr = float.MaxValue;
            for (int i = from; i <= to; i++)
            {
                Vector3 a = samples[i].position, ab = samples[i + 1].position - a;
                float lengthSqr = ab.sqrMagnitude;
                float t = lengthSqr > 1e-10f ? Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / lengthSqr) : 0f;
                float sqr = (a + ab * t - worldPosition).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = i;
                bestT = t;
            }
            if (best < 0) return false;

            Fill(best, bestT, out sample);
            Vector3 offset = worldPosition - sample.position;
            // Beside the line the offset has no part along it; one that does means the point is past an open end.
            if (!closed && Mathf.Abs(Vector3.Dot(offset, sample.forward)) > EndSlack) return false;
            sample.lateral = Vector3.Dot(offset, sample.right);
            sample.height = Vector3.Dot(offset, sample.up);
            hint = sample.distance;
            return true;
        }

        public void SampleAt(float distance, out GuideSample sample)
        {
            sample = default;
            if (samples.Length < 2) return;
            distance = closed ? Mathf.Repeat(distance, Length) : Mathf.Clamp(distance, 0f, Length);
            int i = Mathf.Min(IndexAt(distance), samples.Length - 2);
            float span = samples[i + 1].distance - samples[i].distance;
            Fill(i, span > 1e-6f ? Mathf.Clamp01((distance - samples[i].distance) / span) : 0f, out sample);
        }

        /// <summary>A plain spline has no set pieces to avoid: anywhere on it is a respawn point.</summary>
        public float FindRespawn(float from, float clearance) =>
            closed ? Mathf.Repeat(from, Length) : Mathf.Clamp(from, 0f, Mathf.Max(0f, Length - clearance));

        void Fill(int i, float t, out GuideSample sample)
        {
            Baked a = samples[i], b = samples[i + 1];
            Vector3 forward = Vector3.Lerp(a.forward, b.forward, t).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.Lerp(a.up, b.up, t), forward).normalized;
            sample = new GuideSample
            {
                distance = Mathf.Lerp(a.distance, b.distance, t),
                position = Vector3.Lerp(a.position, b.position, t),
                forward = forward,
                up = up,
                right = Vector3.Cross(up, forward),
                curvature = Mathf.Lerp(a.curvature, b.curvature, t),
                bandMin = -halfWidth,
                bandMax = halfWidth,
                openLeft = openEdges,
                openRight = openEdges,
                gripTested = gripTested,
            };
        }

        /// <summary>Index of the sample at or just before a distance (the table is near-uniform, so a guess plus a short walk).</summary>
        int IndexAt(float distance)
        {
            int count = samples.Length;
            if (count < 2 || Length <= 0f) return 0;
            int i = Mathf.Clamp((int)(distance / Length * (count - 1)), 0, count - 1);
            while (i > 0 && samples[i].distance > distance) i--;
            while (i < count - 1 && samples[i + 1].distance <= distance) i++;
            return i;
        }
    }
}
