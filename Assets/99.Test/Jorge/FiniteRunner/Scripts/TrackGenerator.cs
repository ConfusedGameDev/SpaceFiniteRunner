using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FiniteRunner
{
    /// <summary>
    /// Optional procedural track builder. When randomize is enabled it
    /// replaces the authored spline with a random layout on scene load
    /// (and on restart), then rebuilds pads and edge markers to match.
    /// Runtime-only: uses Object.Destroy for cleanup.
    /// </summary>
    public class TrackGenerator : MonoBehaviour
    {
        [SerializeField] TrackManager track;

        [Tooltip("Generate a new random track when the scene loads and on restart.")]
        [SerializeField] bool randomize;

        [Tooltip("0 = different layout every time; any other value = repeatable layout.")]
        [SerializeField] int seed;

        [Header("Shape")]
        [SerializeField, Min(3)] int segments = 12;
        [SerializeField] Vector2 segmentLength = new(300f, 420f);
        [Tooltip("Max heading change per segment, degrees. Keep low — the ship is FAST.")]
        [SerializeField, Range(0f, 60f)] float maxTurnPerSegment = 24f;
        [Tooltip("Max total heading away from straight ahead, degrees. Stops the track from doubling back.")]
        [SerializeField, Range(0f, 85f)] float maxHeading = 55f;

        [Header("Pads")]
        [SerializeField] PadDefinition boostDefinition;
        [SerializeField] PadDefinition brakeDefinition;
        [SerializeField] Material boostMaterial;
        [SerializeField] Material brakeMaterial;
        [SerializeField] Transform padsParent;
        [Tooltip("Distance between consecutive boost pads (min, max).")]
        [SerializeField] Vector2 boostSpacing = new(150f, 220f);
        [SerializeField, Range(0f, 1f)] float brakeChance = 0.35f;
        [Tooltip("Pad footprint (width, thickness, length). Also used to keep pads inside the track.")]
        [SerializeField] Vector3 padSize = new(10f, 0.5f, 20f);

        [Header("Pad signs")]
        [Tooltip("Optional sign model placed at each pad, tinted with the pad color.")]
        [SerializeField] GameObject padSignPrefab;
        [SerializeField, Min(0.1f)] float padSignScale = 8f;

        [Header("Markers (optional, superseded by the decorator's barriers)")]
        [SerializeField] Transform markersParent;
        [SerializeField, Min(0f)] float markerSpacing;

        [Header("Decoration")]
        [SerializeField] TrackDecorator decorator;

        public bool Randomize => randomize;

        void Awake()
        {
            if (randomize) Generate();
        }

        public void RegenerateIfRandom()
        {
            if (randomize) Generate();
        }

        [ContextMenu("Regenerate Track")]
        void RegenerateFromMenu() => Generate();

        public void Generate()
        {
            var rng = seed == 0
                ? new Unity.Mathematics.Random((uint)System.Environment.TickCount)
                : new Unity.Mathematics.Random((uint)seed);

            BuildSpline(ref rng);
            track.Recalculate();

            ClearChildren(padsParent);
            ClearChildren(markersParent);
            PlacePads(ref rng);
            PlaceMarkers();
            if (decorator != null) decorator.Decorate();
        }

        void BuildSpline(ref Unity.Mathematics.Random rng)
        {
            var spline = track.Spline.Spline;
            spline.Clear();

            float3 pos = float3.zero;
            float heading = 0f;
            spline.Add(new BezierKnot(pos), TangentMode.AutoSmooth);

            for (int i = 0; i < segments; i++)
            {
                heading = math.clamp(
                    heading + rng.NextFloat(-maxTurnPerSegment, maxTurnPerSegment),
                    -maxHeading, maxHeading);
                float rad = math.radians(heading);
                float len = rng.NextFloat(segmentLength.x, segmentLength.y);
                pos += new float3(math.sin(rad), 0f, math.cos(rad)) * len;
                spline.Add(new BezierKnot(pos), TangentMode.AutoSmooth);
            }
        }

        void PlacePads(ref Unity.Mathematics.Random rng)
        {
            // Keep the whole pad (half its width + a margin) inside the track edge.
            float maxLateral = Mathf.Max(0f, track.HalfWidth - padSize.x * 0.5f - 2f);
            float end = track.Length - 150f;

            for (float d = rng.NextFloat(120f, 200f); d < end; d += rng.NextFloat(boostSpacing.x, boostSpacing.y))
            {
                CreatePad(d, rng.NextFloat(-maxLateral, maxLateral), boostDefinition, boostMaterial);

                if (rng.NextFloat() < brakeChance)
                {
                    float brakeDist = d + rng.NextFloat(60f, 140f);
                    if (brakeDist < end)
                        CreatePad(brakeDist, rng.NextFloat(-maxLateral, maxLateral), brakeDefinition, brakeMaterial);
                }
            }
        }

        void CreatePad(float distance, float lateral, PadDefinition def, Material mat)
        {
            if (def == null) return;

            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = $"{def.displayName}Pad_{distance:0000}";
            pad.transform.SetParent(padsParent, false);
            pad.transform.SetPositionAndRotation(pos + rot * new Vector3(0f, -0.9f, 0f), rot);
            pad.transform.localScale = padSize;
            pad.GetComponent<BoxCollider>().isTrigger = true;
            if (mat != null) pad.GetComponent<Renderer>().sharedMaterial = mat;
            pad.AddComponent<SpeedPad>().SetDefinition(def);

            if (padSignPrefab != null)
            {
                var sign = Instantiate(padSignPrefab, pad.transform.position,
                                       rot * Quaternion.Euler(0f, 90f, 0f), padsParent);
                sign.name = pad.name + "_Sign";
                sign.transform.localScale = Vector3.one * padSignScale;
                if (mat != null) TrackDecorator.OverrideMaterials(sign, mat);
            }
        }

        void PlaceMarkers()
        {
            if (markersParent == null || markerSpacing <= 0f) return;
            float edge = track.HalfWidth + 1.5f;
            for (float d = markerSpacing; d < track.Length; d += markerSpacing)
            {
                foreach (float x in new[] { -edge, edge })
                {
                    track.GetPoseAtDistance(d, x, out Vector3 pos, out Quaternion rot);
                    var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    m.name = $"Marker_{d:0000}_{(x < 0 ? "L" : "R")}";
                    m.transform.SetParent(markersParent, false);
                    m.transform.SetPositionAndRotation(pos, rot);
                    m.transform.localScale = new Vector3(0.6f, 4f, 0.6f);
                    var col = m.GetComponent<Collider>();
                    if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
                }
            }
        }

        static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                TrackDecorator.SafeDestroy(parent.GetChild(i).gameObject);
        }
    }
}
