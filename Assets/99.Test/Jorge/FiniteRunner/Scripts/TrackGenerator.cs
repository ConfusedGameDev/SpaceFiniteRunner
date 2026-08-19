using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
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
        /// One spawnable pad/orb kind. Probability is the share of every spawn
        /// roll this entry wins (the sliders auto-rebalance so the table always
        /// sums to 100%). A prefab replaces the code-built primitive; boosts
        /// apply GameSettings.powerUpSpeedBoost × multiplier, and sway makes
        /// the juicier orbs drift across the track so they must be earned.
        /// </summary>
        [System.Serializable]
        public class PadSpawnEntry
        {
            public string name = "Green";

            [Tooltip("Optional model spawned instead of the code-built primitive. Colliders are forced to triggers; one is added if the prefab has none.")]
            public GameObject prefab;

            [Tooltip("What the pad does on pickup (boost/brake, orb or flat pad, size).")]
            [Required] public PadDefinition definition;

            [Tooltip("Share of every spawn roll this entry wins. The table always sums to 100%.")]
            [PropertyRange(0f, 100f), SuffixLabel("%", true)]
            public float probability = 25f;

            [Tooltip("Boosts only: multiplies GameSettings.powerUpSpeedBoost. Brakes use their definition's own delta.")]
            [Min(0f)] public float multiplier = 1f;

            [Tooltip("Tint of the code-built primitive (prefabs keep their own materials) and of the pickup's story color.")]
            public Color color = new(0.1f, 1f, 0.3f);

            [Tooltip("How far the orb sways side to side across the track, in meters. 0 = holds the flight line.")]
            [Min(0f)] public float swayAmplitude;

            [Tooltip("Sway cycles per second.")]
            [Min(0f)] public float swayFrequency = 0.5f;
        }

        // ------------------------------------------------------ Core Settings
        [TitleGroup("Core Settings")]
        [Tooltip("Full width of the track in meters. Drives the ship's steering clamp, the pad placement bounds and the road meshes (which are authored for 60 m and stretch proportionally). Regenerate to see it.")]
        [PropertyRange(10f, 120f), SuffixLabel("m", true)]
        [SerializeField] float trackWidth = 60f;

        [TitleGroup("Core Settings")]
        [Tooltip("One entry per power-up / slowdown kind. Every spawn step draws one entry by probability; the sliders auto-rebalance to always total 100%.")]
        [OnValueChanged(nameof(NormalizeProbabilities), true)]
        [SerializeField]
        PadSpawnEntry[] spawnTable =
        {
            new() { name = "Green",  probability = 46f, multiplier = 1f,   color = new Color(0.1f, 1f, 0.3f) },
            new() { name = "Blue",   probability = 16f, multiplier = 2.5f, color = new Color(0.25f, 0.55f, 1f), swayAmplitude = 4f, swayFrequency = 0.45f },
            new() { name = "Purple", probability = 3f,  multiplier = 10f,  color = new Color(0.75f, 0.3f, 1f),  swayAmplitude = 8f, swayFrequency = 0.8f },
            new() { name = "Brake",  probability = 35f, multiplier = 1f,   color = new Color(1f, 0.25f, 0.2f) },
        };

        [TitleGroup("Core Settings")]
        [Tooltip("100% = a dead straight line, 0% = the curviest track the Shape settings below allow. Regenerate to see it.")]
        [PropertyRange(0f, 100f), SuffixLabel("%", true)]
        [SerializeField] float straightness = 100f;

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
        [Tooltip("Base material of code-built boost primitives; each entry gets a recolored instance. Prefab entries keep their own materials.")]
        [SerializeField] Material boostMaterial;
        [Tooltip("Material of code-built brake primitives.")]
        [SerializeField] Material brakeMaterial;
        [SerializeField] Transform padsParent;
        [Tooltip("Distance between consecutive spawn rolls (min, max) — each roll places one entry from the Core Settings spawn table.")]
        [FormerlySerializedAs("boostSpacing")]
        [SerializeField] Vector2 padSpacing = new(150f, 220f);
        [Tooltip("Pad footprint (width, thickness, length). Also used to keep pads inside the track.")]
        [SerializeField] Vector3 padSize = new(10f, 0.5f, 20f);

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

        // Live access for the pause menu's debug tab. Width/straightness only
        // take effect on the next Generate (the debug tab reloads the scene);
        // spawn-table edits affect streaming immediately.
        public float TrackWidth { get => trackWidth; set => trackWidth = Mathf.Clamp(value, 10f, 120f); }
        public float Straightness { get => straightness; set => straightness = Mathf.Clamp(value, 0f, 100f); }
        public PadSpawnEntry[] SpawnTable => spawnTable;

        // Streaming state — all reset by Generate().
        Unity.Mathematics.Random rng;
        float heading;
        float3 endPosition;
        float padCursor;
        readonly List<(float distance, GameObject go)> spawned = new();
        Dictionary<PadSpawnEntry, Material> entryMaterials;
        float[] lastProbabilities; // change-detection cache for the 100% rebalance

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
            // Debug-tab tweaks (saved to the TrackDebugSettings asset) override
            // the scene's Core Settings — play mode only, so edit-mode previews
            // and the inspector always reflect the authored scene values.
            if (Application.isPlaying) TrackDebugSettings.Load().ApplyTo(this);

            // One width knob for everything: steering clamp, pad bounds, meshes.
            if (track != null) track.SetWidth(trackWidth);
            if (decorator != null) decorator.SetTrackWidth(trackWidth);

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
            // Straightness scales the Shape limits down: 100% pins the heading
            // to dead ahead, 0% lets the full turn/heading ranges act.
            float curviness = 1f - straightness / 100f;
            float turnLimit = maxTurnPerSegment * curviness;
            float headingLimit = maxHeading * curviness;
            heading = math.clamp(
                heading + rng.NextFloat(-turnLimit, turnLimit),
                -headingLimit, headingLimit);
            float rad = math.radians(heading);
            endPosition += new float3(math.sin(rad), 0f, math.cos(rad)) *
                           rng.NextFloat(segmentLength.x, segmentLength.y);
            track.Spline.Spline.Add(new BezierKnot(endPosition), TangentMode.AutoSmooth);
        }

        void PlacePadsUpTo(float limit)
        {
            // One weighted draw from the Core Settings spawn table per step;
            // the cursor resumes here on the next stream.
            while (padCursor < limit)
            {
                PadSpawnEntry entry = PickSpawnEntry();
                if (entry != null && entry.definition != null)
                {
                    float lat = MaxLateral(entry.definition, entry.swayAmplitude);
                    CreatePad(padCursor, rng.NextFloat(-lat, lat), entry);
                }
                padCursor += rng.NextFloat(padSpacing.x, padSpacing.y);
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

        // Weighted draw from the spawn table; uses the layout rng so seeded
        // runs reproduce the same sequence. Probabilities are normalized by
        // their sum, so the draw stays correct even mid-edit.
        PadSpawnEntry PickSpawnEntry()
        {
            if (spawnTable == null || spawnTable.Length == 0) return null;
            float total = 0f;
            foreach (var e in spawnTable) total += e.probability;
            if (total <= 0f) return spawnTable[0];

            float roll = rng.NextFloat(0f, total);
            foreach (var e in spawnTable)
            {
                roll -= e.probability;
                if (roll <= 0f) return e;
            }
            return spawnTable[^1];
        }

        // Keeps the Core Settings probability sliders honest: whichever slider
        // the designer just moved keeps its value, the others rebalance
        // proportionally so the table always totals 100%.
        void NormalizeProbabilities()
        {
            if (spawnTable == null || spawnTable.Length == 0) { lastProbabilities = null; return; }

            if (spawnTable.Length == 1)
            {
                spawnTable[0].probability = 100f;
            }
            else if (lastProbabilities != null && lastProbabilities.Length == spawnTable.Length)
            {
                int changed = -1;
                for (int i = 0; i < spawnTable.Length; i++)
                    if (!Mathf.Approximately(spawnTable[i].probability, lastProbabilities[i])) { changed = i; break; }

                if (changed >= 0)
                {
                    float kept = Mathf.Clamp(spawnTable[changed].probability, 0f, 100f);
                    spawnTable[changed].probability = kept;

                    float othersSum = 0f;
                    for (int i = 0; i < spawnTable.Length; i++)
                        if (i != changed) othersSum += spawnTable[i].probability;

                    float remainder = 100f - kept;
                    for (int i = 0; i < spawnTable.Length; i++)
                    {
                        if (i == changed) continue;
                        spawnTable[i].probability = othersSum > 0f
                            ? spawnTable[i].probability * remainder / othersSum
                            : remainder / (spawnTable.Length - 1);
                    }
                }
            }
            else
            {
                // Entry added/removed (or first touch): scale everything to 100.
                float total = 0f;
                foreach (var e in spawnTable) total += e.probability;
                for (int i = 0; i < spawnTable.Length; i++)
                    spawnTable[i].probability = total > 0f
                        ? spawnTable[i].probability * 100f / total
                        : 100f / spawnTable.Length;
            }

            lastProbabilities = new float[spawnTable.Length];
            for (int i = 0; i < spawnTable.Length; i++) lastProbabilities[i] = spawnTable[i].probability;
        }

        void OnValidate() => NormalizeProbabilities();

        float EffectiveBoost(PadSpawnEntry entry)
        {
            float baseBoost = gameManager != null ? gameManager.PowerUpSpeedBoost : 15f;
            return baseBoost * entry.multiplier;
        }

        // The SpeedPad's MPB tint alone is unreliable with the SRP Batcher
        // (see TrackDecorator), so each boost entry gets its own recolored
        // instance of the boost material. Play mode only — edit-mode previews
        // would leak the instances into the scene.
        Material EntryMaterial(PadSpawnEntry entry)
        {
            bool boost = entry.definition == null || entry.definition.speedDelta >= 0f;
            if (!boost) return brakeMaterial;
            if (!Application.isPlaying || boostMaterial == null) return boostMaterial;

            entryMaterials ??= new Dictionary<PadSpawnEntry, Material>();
            if (!entryMaterials.TryGetValue(entry, out var mat))
            {
                mat = new Material(boostMaterial);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", entry.color);
                else mat.color = entry.color;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", entry.color);
                entryMaterials.Add(entry, mat);
            }
            return mat;
        }

        void CreatePad(float distance, float lateral, PadSpawnEntry entry)
        {
            PadDefinition def = entry.definition;
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            // Orbs sit on the flight line; flat pads sink to road level.
            Vector3 padPos = def.floatingOrb ? pos : pos + rot * new Vector3(0f, -0.9f, 0f);
            Material mat = EntryMaterial(entry);
            GameObject pad;

            if (entry.prefab != null)
            {
                // Designer-authored look: the prefab keeps its own materials,
                // only the definition's size multiplier scales it.
                pad = Instantiate(entry.prefab, padPos, rot, padsParent);
                pad.transform.localScale *= def.sizeMultiplier;

                var colliders = pad.GetComponentsInChildren<Collider>();
                foreach (var c in colliders) c.isTrigger = true;
                if (colliders.Length == 0)
                {
                    if (def.floatingOrb)
                    {
                        var sphere = pad.AddComponent<SphereCollider>();
                        sphere.isTrigger = true;
                        sphere.radius = padSize.x * 0.5f;
                    }
                    else
                    {
                        var box = pad.AddComponent<BoxCollider>();
                        box.isTrigger = true;
                        box.size = padSize;
                    }
                }
            }
            else if (def.floatingOrb)
            {
                // Floating orb centered on the flight line so the ship's trigger
                // collider passes straight through it. Small — must be aimed for.
                pad = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pad.transform.SetParent(padsParent, false);
                pad.transform.SetPositionAndRotation(padPos, rot);
                pad.transform.localScale = Vector3.one * (padSize.x * def.sizeMultiplier);
                pad.GetComponent<SphereCollider>().isTrigger = true;
                if (mat != null) pad.GetComponent<Renderer>().sharedMaterial = mat;
            }
            else
            {
                pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.SetParent(padsParent, false);
                pad.transform.SetPositionAndRotation(padPos, rot);
                pad.transform.localScale = padSize * def.sizeMultiplier;
                pad.GetComponent<BoxCollider>().isTrigger = true;
                if (mat != null) pad.GetComponent<Renderer>().sharedMaterial = mat;
            }

            if (def.floatingOrb && Application.isPlaying)
            {
                var hover = pad.AddComponent<OrbHover>();
                hover.Configure(entry.swayAmplitude, entry.swayFrequency);
            }

            pad.name = $"{entry.name}{def.displayName}Pad_{distance:00000}";
            var speedPad = pad.AddComponent<SpeedPad>();
            // Boosts scale off the shared power-up base; brakes keep their
            // definition's own delta so dodging stays predictable.
            if (def.speedDelta >= 0f) speedPad.SetDefinition(def, EffectiveBoost(entry), entry.color, entry.name);
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
