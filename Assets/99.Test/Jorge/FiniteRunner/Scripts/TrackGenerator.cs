using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FiniteRunner
{
    /// <summary>
    /// Procedural track builder and endless streamer. Builds an initial
    /// stretch on load, then — while the ship flies — keeps appending spline
    /// segments ahead of it, placing power-ups and decoration on the newly
    /// settled stretch and culling spawned objects left far behind.
    /// Knots are never removed, so distances measured from the track start
    /// stay valid for the whole run (see TrackManager.DistanceToT).
    /// Runtime-only: uses Object.Destroy for cleanup.
    /// </summary>
    public class TrackGenerator : MonoBehaviour
    {
        /// <summary>
        /// One rarity tier of boost orb. The applied boost is
        /// GameSettings.powerUpSpeedBoost × multiplier; weight drives how often
        /// the tier spawns, and sway makes it drift across the track so the
        /// juicier orbs are harder to catch.
        /// </summary>
        [System.Serializable]
        public class OrbTier
        {
            public string name = "Green";
            [Tooltip("Multiplies GameSettings.powerUpSpeedBoost.")]
            [Min(0f)] public float multiplier = 1f;
            [Tooltip("Relative spawn weight — higher = more common.")]
            [Min(0f)] public float weight = 1f;
            public Color color = new(0.1f, 1f, 0.3f);
            [Tooltip("How far the orb sways side to side across the track, in meters. 0 = holds the flight line.")]
            [Min(0f)] public float swayAmplitude;
            [Tooltip("Sway cycles per second.")]
            [Min(0f)] public float swayFrequency = 0.5f;
        }

        [SerializeField] TrackManager track;

        [Tooltip("Source of powerUpSpeedBoost, the base boost the orb tiers multiply. Auto-found at runtime if left empty.")]
        [SerializeField] GameManager gameManager;

        [Tooltip("Ship the streamer keeps track generated ahead of. Auto-found at runtime if left empty.")]
        [SerializeField] ShipMotor ship;

        [Tooltip("Keep generating track ahead of the ship for as long as the run lasts (time is the limit, not distance).")]
        [SerializeField] bool endless = true;

        [Tooltip("Generate a new random layout when the scene loads and on restart. Off + a non-zero seed = the same endless layout every run.")]
        [SerializeField] bool randomize;

        [Tooltip("0 = different layout every time; any other value = repeatable layout.")]
        [SerializeField] int seed;

        [Header("Streaming")]
        [Tooltip("How much finished (padded, decorated) track to keep ahead of the ship.")]
        [SerializeField, Min(100f)] float aheadDistance = 700f;

        [Tooltip("How far behind the ship pads and decoration survive before being culled. Keep it larger than the patrol's start gap so the chase always has road under it.")]
        [SerializeField, Min(0f)] float behindDistance = 300f;

        [Header("Shape")]
        [Tooltip("Segment count of a non-endless track. Endless mode grows on demand instead.")]
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

        [Header("Power-up orb tiers")]
        [Tooltip("Weighted pool the boost orbs are drawn from. Keep the high multipliers scarce (low weight) and moving (sway) so they must be earned.")]
        [SerializeField] OrbTier[] orbTiers =
        {
            new() { name = "Green",  multiplier = 1f,   weight = 70f, color = new Color(0.1f, 1f, 0.3f) },
            new() { name = "Blue",   multiplier = 2.5f, weight = 25f, color = new Color(0.25f, 0.55f, 1f), swayAmplitude = 4f, swayFrequency = 0.45f },
            new() { name = "Purple", multiplier = 10f,  weight = 5f,  color = new Color(0.75f, 0.3f, 1f),  swayAmplitude = 8f, swayFrequency = 0.8f },
        };

        [Header("Pad signs")]
        [Tooltip("Optional sign model placed at each flat pad, tinted with the pad color.")]
        [SerializeField] GameObject padSignPrefab;
        [SerializeField, Min(0.1f)] float padSignScale = 8f;

        [Header("Markers (non-endless only, superseded by the decorator's barriers)")]
        [SerializeField] Transform markersParent;
        [SerializeField, Min(0f)] float markerSpacing;

        [Header("Decoration")]
        [SerializeField] TrackDecorator decorator;

        public bool Randomize => randomize;

        // Streaming state — all reset by Generate().
        Unity.Mathematics.Random rng;
        float heading;
        float3 endPosition;
        float padCursor;
        readonly List<(float distance, GameObject go)> spawned = new();
        Dictionary<OrbTier, Material> tierMaterials;

        // AutoSmooth reshapes the curves around the previous knot every time a
        // new one lands, so the last two segments are never safe to build on.
        float SettleMargin => 2f * segmentLength.y;

        void Awake()
        {
            if (endless && ship == null) ship = FindFirstObjectByType<ShipMotor>();
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (randomize || endless) Generate();
        }

        void Update()
        {
            if (!endless || ship == null || track == null) return;
            StreamTo(ship.DistanceTravelled + aheadDistance);
            CullBehind(ship.DistanceTravelled - behindDistance);
        }

        /// <summary>Full rebuild for a new run. Endless runs always rebuild — the old stretch behind the start was culled.</summary>
        public void RegenerateForRun()
        {
            if (randomize || endless) Generate();
        }

        [ContextMenu("Regenerate Track")]
        void RegenerateFromMenu() => Generate();

        public void Generate()
        {
            rng = seed == 0
                ? new Unity.Mathematics.Random((uint)System.Environment.TickCount)
                : new Unity.Mathematics.Random((uint)seed);

            spawned.Clear();
            ClearChildren(padsParent);
            ClearChildren(markersParent);
            if (decorator != null) decorator.Clear();

            var spline = track.Spline.Spline;
            spline.Clear();
            heading = 0f;
            endPosition = float3.zero;
            spline.Add(new BezierKnot(endPosition), TangentMode.AutoSmooth);

            // The spline was just replaced — without this, Length still reports
            // the previous track and StreamTo would think there is already
            // plenty of track, placing everything on a one-knot spline.
            track.Recalculate();

            padCursor = rng.NextFloat(120f, 200f);

            if (endless)
            {
                StreamTo(aheadDistance);
            }
            else
            {
                for (int i = 0; i < segments; i++) AddSegment();
                track.Recalculate();
                PlacePadsUpTo(track.Length - 150f);
                PlaceMarkers();
                if (decorator != null) decorator.DecorateUpTo(track.Length);
            }
        }

        /// <summary>
        /// Grows the spline until at least <paramref name="target"/> distance
        /// of track is settled, then stamps pads and decoration on the settled
        /// region. The trailing SettleMargin stays bare until more knots land.
        /// </summary>
        void StreamTo(float target)
        {
            while (track.Length - SettleMargin < target)
            {
                AddSegment();
                track.Recalculate();
            }

            float settled = track.Length - SettleMargin;
            PlacePadsUpTo(settled);
            if (decorator != null) decorator.DecorateUpTo(settled);
        }

        void AddSegment()
        {
            heading = math.clamp(
                heading + rng.NextFloat(-maxTurnPerSegment, maxTurnPerSegment),
                -maxHeading, maxHeading);
            float rad = math.radians(heading);
            endPosition += new float3(math.sin(rad), 0f, math.cos(rad)) *
                           rng.NextFloat(segmentLength.x, segmentLength.y);
            track.Spline.Spline.Add(new BezierKnot(endPosition), TangentMode.AutoSmooth);
        }

        void PlacePadsUpTo(float limit)
        {
            // A boost's optional brake trails it by up to 140 m — only place a
            // boost when its whole pattern fits, and resume here next stream.
            while (padCursor + 140f < limit)
            {
                OrbTier tier = PickOrbTier();
                float lat = MaxLateral(boostDefinition, tier != null ? tier.swayAmplitude : 0f);
                CreatePad(padCursor, rng.NextFloat(-lat, lat), boostDefinition,
                          tier != null ? TierMaterial(tier) : boostMaterial, tier);

                if (rng.NextFloat() < brakeChance)
                {
                    float brakeDist = padCursor + rng.NextFloat(60f, 140f);
                    lat = MaxLateral(brakeDefinition);
                    CreatePad(brakeDist, rng.NextFloat(-lat, lat), brakeDefinition, brakeMaterial);
                }

                padCursor += rng.NextFloat(boostSpacing.x, boostSpacing.y);
            }
        }

        void CullBehind(float minDistance)
        {
            if (minDistance <= 0f) return;
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                if (spawned[i].distance >= minDistance) continue;
                if (spawned[i].go != null) Destroy(spawned[i].go);
                spawned.RemoveAt(i);
            }
            if (decorator != null) decorator.CullBefore(minDistance);
        }

        // Keep the whole pad (half its scaled width + a margin) inside the
        // track edge — including the full sway arc of a moving orb.
        float MaxLateral(PadDefinition def, float sway = 0f)
        {
            float width = padSize.x * (def != null ? def.sizeMultiplier : 1f);
            return Mathf.Max(0f, track.HalfWidth - width * 0.5f - 2f - sway);
        }

        // Weighted draw from the tier pool; uses the layout rng so seeded runs
        // reproduce the same tier sequence.
        OrbTier PickOrbTier()
        {
            if (orbTiers == null || orbTiers.Length == 0) return null;
            float total = 0f;
            foreach (var t in orbTiers) total += t.weight;
            if (total <= 0f) return orbTiers[0];

            float roll = rng.NextFloat(0f, total);
            foreach (var t in orbTiers)
            {
                roll -= t.weight;
                if (roll <= 0f) return t;
            }
            return orbTiers[^1];
        }

        float EffectiveBoost(OrbTier tier)
        {
            float baseBoost = gameManager != null ? gameManager.PowerUpSpeedBoost
                            : boostDefinition != null ? boostDefinition.speedDelta : 15f;
            return baseBoost * tier.multiplier;
        }

        // The SpeedPad's MPB tint alone is unreliable with the SRP Batcher
        // (see TrackDecorator), so each tier gets its own recolored instance
        // of the boost material. Play mode only — edit-mode previews would
        // leak the instances into the scene.
        Material TierMaterial(OrbTier tier)
        {
            if (!Application.isPlaying || boostMaterial == null) return boostMaterial;
            tierMaterials ??= new Dictionary<OrbTier, Material>();
            if (!tierMaterials.TryGetValue(tier, out var mat))
            {
                mat = new Material(boostMaterial);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tier.color);
                else mat.color = tier.color;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", tier.color);
                tierMaterials.Add(tier, mat);
            }
            return mat;
        }

        void CreatePad(float distance, float lateral, PadDefinition def, Material mat, OrbTier tier = null)
        {
            if (def == null) return;

            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            GameObject pad;

            if (def.floatingOrb)
            {
                // Floating orb centered on the flight line so the ship's trigger
                // collider passes straight through it. Small — must be aimed for.
                pad = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pad.transform.SetParent(padsParent, false);
                pad.transform.SetPositionAndRotation(pos, rot);
                pad.transform.localScale = Vector3.one * (padSize.x * def.sizeMultiplier);
                pad.GetComponent<SphereCollider>().isTrigger = true;
                if (Application.isPlaying)
                {
                    var hover = pad.AddComponent<OrbHover>();
                    if (tier != null) hover.Configure(tier.swayAmplitude, tier.swayFrequency);
                }
            }
            else
            {
                pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.SetParent(padsParent, false);
                pad.transform.SetPositionAndRotation(pos + rot * new Vector3(0f, -0.9f, 0f), rot);
                pad.transform.localScale = padSize * def.sizeMultiplier;
                pad.GetComponent<BoxCollider>().isTrigger = true;
            }

            pad.name = tier != null
                ? $"{tier.name}{def.displayName}Pad_{distance:00000}"
                : $"{def.displayName}Pad_{distance:00000}";
            if (mat != null) pad.GetComponent<Renderer>().sharedMaterial = mat;
            var speedPad = pad.AddComponent<SpeedPad>();
            if (tier != null) speedPad.SetDefinition(def, EffectiveBoost(tier), tier.color, tier.name);
            else speedPad.SetDefinition(def);
            spawned.Add((distance, pad));

            // Orbs are their own landmark; the gate-style sign only suits flat pads.
            if (!def.floatingOrb && padSignPrefab != null)
            {
                var sign = Instantiate(padSignPrefab, pad.transform.position,
                                       rot * Quaternion.Euler(0f, 90f, 0f), padsParent);
                sign.name = pad.name + "_Sign";
                sign.transform.localScale = Vector3.one * padSignScale;
                if (mat != null) TrackDecorator.OverrideMaterials(sign, mat);
                spawned.Add((distance, sign));
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
