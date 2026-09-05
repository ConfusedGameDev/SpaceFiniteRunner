using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;

using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>Which lane a pad spawns on: the flight line, or the air lane above it that only a jump reaches.</summary>
    public enum PadLane { Ground, Air }

    /// <summary>A spawn-table entry with a probability share, so both tables rebalance through one rule.</summary>
    public interface IWeightedEntry
    {
        float Probability { get; set; }
    }

    /// <summary>
    /// Procedural track builder and endless streamer. Builds an initial
    /// stretch on load, then — while the ship flies — keeps appending spline
    /// segments ahead of it, placing power-ups and decoration on the newly
    /// settled stretch and culling spawned objects left far behind.
    /// Knots are never removed, so distances measured from the track start
    /// stay valid for the whole run (see TrackManager.DistanceToT).
    /// <b>Track features</b> (jump ramps now; loops and tubes later) come
    /// from a second seeded table: each placed feature claims its footprint
    /// (no pad spawns on it) and an exclusion ahead (no feature starts in it
    /// — for a jump, the longest arc it can throw the ship). Definitions are
    /// assets; in play the generator hands out runtime clones, which is what
    /// the debug menu edits. <b>Money collectibles</b> (the "Collectibles"
    /// toggle group) stream between the orbs as short rows of coins on the
    /// flight line — the shared <see cref="Collectible"/> component, so the
    /// city's pickup prefabs drop straight in — placed after the pads so a
    /// coin never sits on an orb, and off claimed ground like everything
    /// else. Runtime-only: uses Object.Destroy for cleanup.
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
        public class PadSpawnEntry : IWeightedEntry
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

            [Tooltip("Ground = on the flight line. Air = GameSettings.airLaneHeight above it, where only a jump reaches — the future air lane, keep such entries at 0% until jumps ship them.")]
            public PadLane lane = PadLane.Ground;

            public float Probability { get => probability; set => probability = value; }
        }

        /// <summary>
        /// One placeable track feature kind. Drawn by probability at every
        /// feature step (the sliders rebalance to 100% like the pad table);
        /// minSpacing is the least track between this kind and the next
        /// feature, on top of the definition's own footprint and exclusion.
        /// </summary>
        [System.Serializable]
        public class FeatureSpawnEntry : IWeightedEntry
        {
            public string name = "Jump";

            [Tooltip("Optional model spawned instead of the code-built ramp. Authored as a UNIT ramp (1 m wide, 1 m tall lip, 1 m long, foot at its origin, rising toward +Z); it is scaled to the ramp's width, lip height and length. Colliders are stripped — detection is analytic.")]
            public GameObject prefab;

            [Tooltip("What the feature is and how it behaves (a JumpDefinition for ramps). Cloned at play, so the debug menu never edits the asset.")]
            [Required] public TrackFeatureDefinition definition;

            [Tooltip("Share of every feature roll this entry wins. The table always sums to 100%.")]
            [PropertyRange(0f, 100f), SuffixLabel("%", true)]
            public float probability = 100f;

            [Tooltip("Least metres of track between this feature and the next one, on top of its footprint and exclusion.")]
            [PropertyRange(0f, 3000f), SuffixLabel("m", true)]
            public float minSpacing = 300f;

            [Tooltip("Takeoff boost as a multiple of GameSettings.powerUpSpeedBoost (1 = a green orb's worth).")]
            [Min(0f)] public float multiplier = 1f;

            [Tooltip("Tint of the code-built ramp (prefabs keep their own materials) and of its debug rows.")]
            public Color color = new(1f, 0.8f, 0.2f);

            /// <summary>The definition actually played: a runtime clone in play mode, the asset itself in edit-mode previews.</summary>
            [System.NonSerialized] public TrackFeatureDefinition Runtime;

            public float Probability { get => probability; set => probability = value; }
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

        [TitleGroup("Core Settings")]
        [Tooltip("One entry per track feature kind (jump ramps). Every feature step draws one entry by probability; the sliders auto-rebalance to always total 100%.")]
        [OnValueChanged(nameof(NormalizeFeatureProbabilities), true)]
        [SerializeField] FeatureSpawnEntry[] featureTable = System.Array.Empty<FeatureSpawnEntry>();

        [TitleGroup("Core Settings")]
        [Tooltip("Metres of track between feature steps (min, max), before each entry's own minimum spacing, footprint and exclusion are added.")]
        [MinMaxSlider(100f, 5000f, true), SuffixLabel("m", true)]
        [SerializeField] Vector2 featureSpacing = new(600f, 1200f);

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

        // ------------------------------------------------------- Collectibles
        [ToggleGroup("spawnCollectibles", "Collectibles")]
        [Tooltip("Stream money pickups along the track: short rows of coins on the flight line, each worth a few dollars, banked at pickup.")]
        [SerializeField] bool spawnCollectibles = true;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Optional pickup prefab carrying a Collectible set to Money (the city's collectible prefabs work as they are). Empty = a code-built gold coin.")]
        [SerializeField] GameObject collectiblePrefab;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Metres of track between one row of coins and the next (min, max).")]
        [MinMaxSlider(50f, 2000f, true), SuffixLabel("m", true)]
        [SerializeField] Vector2 collectibleSpacing = new(250f, 500f);

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Coins per row (min, max), all at one lateral.")]
        [MinMaxSlider(1, 10, true)]
        [SerializeField] Vector2Int collectibleGroupSize = new(1, 5);

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Metres between the coins of a row.")]
        [PropertyRange(5f, 40f), SuffixLabel("m", true)]
        [SerializeField] float collectibleStep = 15f;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Dollars a coin is worth (min, max), rolled per coin.")]
        [MinMaxSlider(1, 100, true), SuffixLabel("$", true)]
        [SerializeField] Vector2Int collectibleValue = new(1, 5);

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Trigger box of a coin (width, height, length). Long along the track: at Light Speed the ship covers ~36 m per physics step against a 12 m trigger box, so a short volume would be tunnelled.")]
        [SerializeField] Vector3 collectibleTriggerSize = new(5f, 5f, 20f);

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Diameter of the code-built coin, metres.")]
        [PropertyRange(0.5f, 5f), SuffixLabel("m", true)]
        [SerializeField] float collectibleSize = 1.6f;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Tint of the code-built coin (a recolored instance of the boost material).")]
        [SerializeField] Color collectibleColor = new(1f, 0.8f, 0.2f);

        [Header("Features")]
        [Tooltip("Material of the code-built ramp slab and rails; each entry gets a recolored instance. Empty = the boost material.")]
        [SerializeField] Material featureMaterial;

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
        public FeatureSpawnEntry[] FeatureTable => featureTable;
        public Vector2 FeatureSpacing { get => featureSpacing; set => featureSpacing = new Vector2(Mathf.Max(100f, value.x), Mathf.Max(Mathf.Max(100f, value.x), value.y)); }

        /// <summary>Height of the air lane above the flight line, from GameSettings (30 m without a manager).</summary>
        float AirLaneHeight => gameManager != null ? gameManager.AirLaneHeight : 30f;

        // Streaming state — all reset by Generate().
        Unity.Mathematics.Random rng;
        float heading;
        float3 endPosition;
        float padCursor;
        float collectibleCursor;
        float featureCursor;
        FeatureSpawnEntry pendingFeature; // drawn for featureCursor, waiting for its footprint to settle
        bool pendingClaimed;
        TrackSection pendingSection;      // the insert a pending loop already routed the track through
        readonly List<(float distance, GameObject go)> spawned = new();
        readonly List<(float start, float end)> claims = new(); // feature footprints pads keep off
        readonly List<float> padDistances = new();               // where pads landed — coins keep off them
        Dictionary<PadSpawnEntry, Material> entryMaterials;
        Dictionary<FeatureSpawnEntry, Material> featureMaterials;
        Material collectibleMaterial;
        float[] lastProbabilities; // change-detection cache for the 100% rebalance
        float[] lastFeatureProbabilities;

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

            // Features play a runtime clone of their definition asset (the
            // debug menu edits the clone, never the asset); edit-mode previews
            // read the asset as is. Debug tweaks land on the clones.
            if (featureTable != null)
                foreach (var entry in featureTable)
                {
                    if (Application.isPlaying && entry.Runtime != null && entry.Runtime != entry.definition)
                        Destroy(entry.Runtime); // last run's clone
                    entry.Runtime = entry.definition != null && Application.isPlaying
                        ? Instantiate(entry.definition)
                        : entry.definition;
                }
            if (Application.isPlaying) FeatureDebugSettings.Load().ApplyTo(this);

            // One width knob for everything: steering clamp, pad bounds, meshes.
            if (track != null) track.SetWidth(trackWidth);
            if (decorator != null) decorator.SetTrackWidth(trackWidth);

            rng = seed == 0
                ? new Unity.Mathematics.Random((uint)System.Environment.TickCount)
                : new Unity.Mathematics.Random((uint)seed);

            spawned.Clear();
            claims.Clear();
            padDistances.Clear();
            ClearChildren(padsParent);
            ClearChildren(markersParent);
            if (decorator != null) decorator.Clear();

            // The spline is only ever touched through the TrackManager (this
            // also drops last run's inserted sections).
            track.ClearKnots();
            heading = 0f;
            endPosition = float3.zero;
            track.AppendKnot(endPosition);

            // The spline was just replaced — without this, Length still reports
            // the previous track and StreamTo would think there is already
            // plenty of track, placing everything on a one-knot spline.
            track.Recalculate();

            padCursor = rng.NextFloat(120f, 200f);
            collectibleCursor = rng.NextFloat(collectibleSpacing.x, collectibleSpacing.y);
            featureCursor = rng.NextFloat(featureSpacing.x, featureSpacing.y);
            pendingFeature = null;
            pendingClaimed = false;
            pendingSection = null;

            if (endless)
            {
                StreamTo(aheadDistance);
            }
            else
            {
                for (int i = 0; i < segments; i++) AddSegment();
                track.Recalculate();
                PlaceFeaturesUpTo(track.Length - 150f);
                PlacePadsUpTo(track.Length - 150f);
                PlaceCollectiblesUpTo(track.Length - 150f);
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

            PlaceFeaturesUpTo(track.Length - SettleMargin); // first: features claim footprints the pads then avoid
            float settled = track.Length - SettleMargin;    // re-read: a loop just inserted track
            PlacePadsUpTo(settled);
            PlaceCollectiblesUpTo(settled); // after the pads: coins keep off where they landed
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
            track.AppendKnot(endPosition);
        }

        /// <summary>
        /// One weighted draw from the feature table per step. A feature only
        /// lands once the SPLINE its footprint covers is settled — but it
        /// claims that footprint the moment its spot is decided, so the pads
        /// placed while it waits keep off it, and a feature that inserts track
        /// (a loop) registers its section right then too, before any pad or
        /// road is placed beyond it: an insert under already-placed objects
        /// would shift their distances. The cursor then jumps past the
        /// footprint, the exclusion and the larger of the spacing roll and the
        /// entry's own minimum.
        /// </summary>
        void PlaceFeaturesUpTo(float limit)
        {
            if (featureTable == null || featureTable.Length == 0) return;
            while (featureCursor < limit)
            {
                if (pendingFeature == null)
                {
                    pendingFeature = PickWeighted(featureTable, rng.NextFloat(0f, 1f)) as FeatureSpawnEntry;
                    if (pendingFeature == null || pendingFeature.Runtime == null)
                    {
                        pendingFeature = null;
                        featureCursor += rng.NextFloat(featureSpacing.x, featureSpacing.y);
                        continue;
                    }
                }
                if (!pendingClaimed)
                {
                    // The section IS the feature's geometry: a loop inserts
                    // track, a tube reshapes the spline under it. Either way
                    // it exists from this moment on, so every pad and road
                    // stamp beyond this point already rides it.
                    pendingSection = pendingFeature.Runtime.CreateSection(track, featureCursor, rng.NextFloat(0f, 1f));
                    if (pendingSection != null) track.AddSection(pendingSection);
                    float claimed = pendingSection != null ? pendingSection.Length : pendingFeature.Runtime.FootprintLength;
                    if (pendingFeature.Runtime.ClaimsFootprint) claims.Add((featureCursor, featureCursor + claimed));
                    pendingClaimed = true;
                    if (pendingSection != null && pendingSection.InsertsDistance) limit += pendingSection.Length; // the settled stretch grew with the insert
                }
                float footprint = pendingSection != null ? pendingSection.Length : pendingFeature.Runtime.FootprintLength;
                // A feature with a section never waits: the section is its
                // geometry and already routes the track, and its pose is only
                // ever sampled where pads and road are placed — settled spline.
                // Only a road-bound feature (a ramp) needs its footprint settled.
                float splineExtent = pendingSection != null ? 0f : footprint;
                if (featureCursor + splineExtent > limit) return; // wait for the next stream

                CreateFeature(featureCursor, pendingFeature, pendingSection);
                featureCursor += footprint + pendingFeature.Runtime.ExclusionAhead
                               + Mathf.Max(rng.NextFloat(featureSpacing.x, featureSpacing.y), pendingFeature.minSpacing);
                pendingFeature = null;
                pendingClaimed = false;
                pendingSection = null;
            }
        }

        /// <summary>End of the claimed footprint covering <paramref name="distance"/>, or -1 when it is free.</summary>
        float ClaimEnd(float distance)
        {
            foreach (var c in claims)
                if (distance >= c.start - padSize.z && distance < c.end + padSize.z) return c.end + padSize.z;
            return -1f;
        }

        void PlacePadsUpTo(float limit)
        {
            // One weighted draw from the Core Settings spawn table per step;
            // the cursor resumes here on the next stream.
            while (padCursor < limit)
            {
                // A feature's footprint is claimed ground: skip to its far end.
                float claimEnd = ClaimEnd(padCursor);
                if (claimEnd >= 0f) { padCursor = claimEnd; continue; }

                PadSpawnEntry entry = PickSpawnEntry();
                if (entry != null && entry.definition != null)
                {
                    // Inside the lane at this distance — on a tube that is the
                    // arc round the pipe, so orbs hang off its sides and under it.
                    track.GetLateralBand(padCursor, out float bandMin, out float bandMax);
                    float margin = PadMargin(entry.definition, entry.swayAmplitude);
                    float lo = bandMin + margin;
                    float hi = bandMax - margin;
                    CreatePad(padCursor, hi > lo ? rng.NextFloat(lo, hi) : (bandMin + bandMax) * 0.5f, entry);
                }
                padCursor += rng.NextFloat(padSpacing.x, padSpacing.y);
            }
        }

        /// <summary>
        /// Rows of coins between the orbs: one lateral per row, coins a step
        /// apart along the track, every coin skipped where a pad already sits
        /// (within a pad length) or a feature claimed the ground.
        /// </summary>
        void PlaceCollectiblesUpTo(float limit)
        {
            if (!spawnCollectibles) return;
            while (collectibleCursor < limit)
            {
                float claimEnd = ClaimEnd(collectibleCursor);
                if (claimEnd >= 0f) { collectibleCursor = claimEnd; continue; }

                int count = rng.NextInt(collectibleGroupSize.x, Mathf.Max(collectibleGroupSize.x, collectibleGroupSize.y) + 1);
                track.GetLateralBand(collectibleCursor, out float bandMin, out float bandMax);
                float margin = collectibleTriggerSize.x * 0.5f + 2f;
                float lo = bandMin + margin;
                float hi = bandMax - margin;
                float lateral = hi > lo ? rng.NextFloat(lo, hi) : (bandMin + bandMax) * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    float d = collectibleCursor + i * collectibleStep;
                    if (d >= limit || ClaimEnd(d) >= 0f || NearPad(d)) continue;
                    CreateCollectible(d, lateral);
                }

                collectibleCursor += count * collectibleStep + rng.NextFloat(collectibleSpacing.x, collectibleSpacing.y);
            }
        }

        bool NearPad(float distance)
        {
            foreach (float pad in padDistances)
                if (Mathf.Abs(pad - distance) < padSize.z) return true;
            return false;
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
            for (int i = claims.Count - 1; i >= 0; i--)
                if (claims[i].end < minDistance) claims.RemoveAt(i);
            for (int i = padDistances.Count - 1; i >= 0; i--)
                if (padDistances[i] < minDistance) padDistances.RemoveAt(i);
            if (decorator != null) decorator.CullBefore(minDistance);
        }

        // Keep the whole pad (half its scaled width + a margin) inside the
        // lane edge — including the full sway arc of a moving orb.
        float PadMargin(PadDefinition def, float sway = 0f)
        {
            float width = padSize.x * (def != null ? def.sizeMultiplier : 1f);
            return width * 0.5f + 2f + sway;
        }

        // Weighted draw from the spawn table; uses the layout rng so seeded
        // runs reproduce the same sequence. Probabilities are normalized by
        // their sum, so the draw stays correct even mid-edit.
        PadSpawnEntry PickSpawnEntry() =>
            spawnTable == null || spawnTable.Length == 0 ? null : PickWeighted(spawnTable, rng.NextFloat(0f, 1f)) as PadSpawnEntry;

        // Weighted draw off a 0..1 roll; probabilities are normalized by
        // their sum, so the draw stays correct even mid-edit.
        static IWeightedEntry PickWeighted(IWeightedEntry[] table, float roll01)
        {
            if (table == null || table.Length == 0) return null;
            float total = 0f;
            foreach (var e in table) total += e.Probability;
            if (total <= 0f) return table[0];

            float roll = roll01 * total;
            foreach (var e in table)
            {
                roll -= e.Probability;
                if (roll <= 0f) return e;
            }
            return table[^1];
        }

        // Keeps the Core Settings probability sliders honest: whichever slider
        // the designer just moved keeps its value, the others rebalance
        // proportionally so the table always totals 100%.
        void NormalizeProbabilities() => Normalize(spawnTable, ref lastProbabilities);
        void NormalizeFeatureProbabilities() => Normalize(featureTable, ref lastFeatureProbabilities);

        static void Normalize(IWeightedEntry[] table, ref float[] last)
        {
            if (table == null || table.Length == 0) { last = null; return; }

            if (table.Length == 1)
            {
                table[0].Probability = 100f;
            }
            else if (last != null && last.Length == table.Length)
            {
                int changed = -1;
                for (int i = 0; i < table.Length; i++)
                    if (!Mathf.Approximately(table[i].Probability, last[i])) { changed = i; break; }

                if (changed >= 0)
                {
                    float kept = Mathf.Clamp(table[changed].Probability, 0f, 100f);
                    table[changed].Probability = kept;

                    float othersSum = 0f;
                    for (int i = 0; i < table.Length; i++)
                        if (i != changed) othersSum += table[i].Probability;

                    float remainder = 100f - kept;
                    for (int i = 0; i < table.Length; i++)
                    {
                        if (i == changed) continue;
                        table[i].Probability = othersSum > 0f
                            ? table[i].Probability * remainder / othersSum
                            : remainder / (table.Length - 1);
                    }
                }
            }
            else
            {
                // Entry added/removed (or first touch): scale everything to 100.
                float total = 0f;
                foreach (var e in table) total += e.Probability;
                for (int i = 0; i < table.Length; i++)
                    table[i].Probability = total > 0f
                        ? table[i].Probability * 100f / total
                        : 100f / table.Length;
            }

            last = new float[table.Length];
            for (int i = 0; i < table.Length; i++) last[i] = table[i].Probability;
        }

        void OnValidate()
        {
            NormalizeProbabilities();
            NormalizeFeatureProbabilities();
        }

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

        // The code-built coin's gold: one recolored boost-material instance,
        // play mode only for the same leak reason as the pad entries.
        Material CollectibleMaterial()
        {
            if (!Application.isPlaying || boostMaterial == null) return boostMaterial;
            if (collectibleMaterial == null)
            {
                collectibleMaterial = new Material(boostMaterial);
                if (collectibleMaterial.HasProperty("_BaseColor")) collectibleMaterial.SetColor("_BaseColor", collectibleColor);
                else collectibleMaterial.color = collectibleColor;
                if (collectibleMaterial.HasProperty("_EmissionColor")) collectibleMaterial.SetColor("_EmissionColor", collectibleColor);
            }
            return collectibleMaterial;
        }

        Material FeatureEntryMaterial(FeatureSpawnEntry entry)
        {
            Material source = featureMaterial != null ? featureMaterial : boostMaterial;
            if (!Application.isPlaying || source == null) return source;

            featureMaterials ??= new Dictionary<FeatureSpawnEntry, Material>();
            if (!featureMaterials.TryGetValue(entry, out var mat))
            {
                mat = new Material(source);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", entry.color);
                else mat.color = entry.color;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", entry.color);
                featureMaterials.Add(entry, mat);
            }
            return mat;
        }

        void CreateFeature(float distance, FeatureSpawnEntry entry, TrackSection section)
        {
            switch (entry.Runtime)
            {
                case JumpDefinition jump: CreateJump(distance, entry, jump); break;
                case LoopDefinition loop when section is LoopSection loopSection: CreateLoop(distance, entry, loop, loopSection); break;
                case TubeDefinition: break; // the section registered at decision time is the whole feature: the decorator stamps the pipe
                default:
                    Debug.LogWarning($"TrackGenerator: no builder for feature definition {entry.Runtime.GetType().Name}.", this);
                    break;
            }
        }

        /// <summary>
        /// A jump ramp: a JumpRamp component carrying the numbers the ship's
        /// analytic detection reads, plus a picture — the entry's unit prefab
        /// scaled to the ramp, or a code-built slab pitched to the ramp angle
        /// with a rail down each edge. No colliders: nothing here is physics.
        /// </summary>
        void CreateJump(float distance, FeatureSpawnEntry entry, JumpDefinition def)
        {
            float rampHalf = track.HalfWidth * Mathf.Clamp01(def.widthFraction);
            float maxLat = Mathf.Max(0f, track.HalfWidth - rampHalf - 2f);
            float lateral = rng.NextFloat(-maxLat, maxLat);
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);

            var go = new GameObject($"{entry.name}{def.displayName}Ramp_{distance:00000}");
            go.transform.SetParent(padsParent, false);
            go.transform.SetPositionAndRotation(pos, rot);

            float width = rampHalf * 2f;
            float lip = def.LipHeight;
            if (entry.prefab != null)
            {
                var visual = Instantiate(entry.prefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = new Vector3(width, lip, def.length);
                foreach (var c in visual.GetComponentsInChildren<Collider>()) DestroyComponent(c);
            }
            else
            {
                Material mat = FeatureEntryMaterial(entry);
                float slopeLength = Mathf.Sqrt(def.length * def.length + lip * lip);
                var pitch = Quaternion.Euler(-def.rampAngle, 0f, 0f);
                const float thickness = 1.5f;
                Vector3 slopeCentre = new Vector3(0f, lip * 0.5f, def.length * 0.5f);
                Vector3 normal = pitch * Vector3.up;

                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Slab";
                slab.transform.SetParent(go.transform, false);
                slab.transform.localRotation = pitch;
                slab.transform.localPosition = slopeCentre - normal * (thickness * 0.5f);
                slab.transform.localScale = new Vector3(width, thickness, slopeLength);
                DestroyComponent(slab.GetComponent<Collider>());
                if (mat != null) slab.GetComponent<Renderer>().sharedMaterial = mat;

                const float railHeight = 3f;
                const float railWidth = 0.8f;
                foreach (float side in new[] { -1f, 1f })
                {
                    var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    rail.name = side < 0f ? "RailL" : "RailR";
                    rail.transform.SetParent(go.transform, false);
                    rail.transform.localRotation = pitch;
                    rail.transform.localPosition = slopeCentre + pitch * new Vector3(side * (rampHalf - railWidth * 0.5f), railHeight * 0.5f, 0f);
                    rail.transform.localScale = new Vector3(railWidth, railHeight, slopeLength);
                    DestroyComponent(rail.GetComponent<Collider>());
                    if (mat != null) rail.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }

            var ramp = go.AddComponent<JumpRamp>();
            float baseBoost = gameManager != null ? gameManager.PowerUpSpeedBoost : 15f;
            ramp.Configure(def, distance, lateral, rampHalf, baseBoost * entry.multiplier);
            spawned.Add((distance, go));
        }

        /// <summary>
        /// A loop: the LoopFeature carrying the section (already inserted at
        /// decision time), the entry speed fixed for this distance by the
        /// GameSettings rule, and a gate at the mouth — a portal frame across
        /// the whole track (two posts and a crossbar, or the entry's unit
        /// prefab scaled to the radius) the ship's speed recolours, with the
        /// required km/h standing above it as a fixed label. The road
        /// round the loop needs nothing here: the decorator stamps it chord by
        /// chord off the section's poses like any stretch of track.
        /// </summary>
        void CreateLoop(float distance, FeatureSpawnEntry entry, LoopDefinition def, LoopSection section)
        {
            track.GetPoseAtDistance(distance, 0f, out Vector3 pos, out Quaternion rot);
            var go = new GameObject($"{entry.name}{def.displayName}Loop_{distance:00000}");
            go.transform.SetParent(padsParent, false);
            go.transform.SetPositionAndRotation(pos, rot);

            var loop = go.AddComponent<LoopFeature>();
            float required = gameManager != null ? gameManager.LoopRequiredSpeed(distance) : 0f;
            loop.Configure(def, section, required);
            float labelHeight = def.labelHeight;

            if (entry.prefab != null)
            {
                var visual = Instantiate(entry.prefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * def.radius;
                foreach (var c in visual.GetComponentsInChildren<Collider>()) DestroyComponent(c);
                foreach (var r in visual.GetComponentsInChildren<Renderer>()) loop.AddGateRenderer(r);
            }
            else
            {
                Material mat = FeatureEntryMaterial(entry);
                float half = track.HalfWidth + 4f;
                const float postWidth = 3f;
                float postHeight = Mathf.Clamp(track.HalfWidth * 0.6f, 20f, 60f);
                labelHeight = Mathf.Max(labelHeight, postHeight + postWidth);
                foreach (float side in new[] { -1f, 1f })
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.name = side < 0f ? "PostL" : "PostR";
                    post.transform.SetParent(go.transform, false);
                    post.transform.localPosition = new Vector3(side * half, postHeight * 0.5f - 1f, 0f);
                    post.transform.localScale = new Vector3(postWidth, postHeight, postWidth);
                    DestroyComponent(post.GetComponent<Collider>());
                    var r = post.GetComponent<Renderer>();
                    if (mat != null) r.sharedMaterial = mat;
                    loop.AddGateRenderer(r);
                }
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Crossbar";
                bar.transform.SetParent(go.transform, false);
                bar.transform.localPosition = new Vector3(0f, postHeight - 1f, 0f);
                bar.transform.localScale = new Vector3(half * 2f + postWidth, postWidth, postWidth);
                DestroyComponent(bar.GetComponent<Collider>());
                var barRenderer = bar.GetComponent<Renderer>();
                if (mat != null) barRenderer.sharedMaterial = mat;
                loop.AddGateRenderer(barRenderer);
            }
            loop.SetGateColor(false);
            loop.BuildLabel(labelHeight, def.labelSize);
            // Culled by its EXIT, not its mouth: the loop is 2πR of track
            // (630 m at R = 100) and the cull line trails the ship by far
            // less, so keyed on the mouth it was destroyed with the ship
            // still climbing it.
            spawned.Add((section.EndDistance, go));
        }

        void CreatePad(float distance, float lateral, PadSpawnEntry entry)
        {
            PadDefinition def = entry.definition;
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            // Orbs sit on the flight line; flat pads sink to road level. The
            // air lane rides the track's up so it stays overhead on a roll.
            Vector3 padPos = def.floatingOrb ? pos : pos + rot * new Vector3(0f, -0.9f, 0f);
            if (entry.lane == PadLane.Air) padPos += rot * (Vector3.up * AirLaneHeight);
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
            padDistances.Add(distance);

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

        /// <summary>
        /// One money pickup on the flight line: the assigned prefab (its
        /// Collectible must be set to Money) or a code-built coin — a flat
        /// gold cylinder standing on the track, spun round the track's up by
        /// the Collectible itself — under a root carrying the long trigger box.
        /// </summary>
        void CreateCollectible(float distance, float lateral)
        {
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            GameObject go;
            Collectible collectible;

            if (collectiblePrefab != null)
            {
                go = Instantiate(collectiblePrefab, pos, rot, padsParent);
                collectible = go.GetComponent<Collectible>();
                if (collectible == null)
                {
                    Debug.LogError($"TrackGenerator: collectible prefab '{collectiblePrefab.name}' carries no Collectible component.", collectiblePrefab);
                    TrackDecorator.SafeDestroy(go);
                    return;
                }
                if (go.GetComponent<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = collectibleTriggerSize;
                }
            }
            else
            {
                go = new GameObject();
                go.transform.SetParent(padsParent, false);
                go.transform.SetPositionAndRotation(pos, rot);

                var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                coin.name = "Mesh";
                DestroyComponent(coin.GetComponent<Collider>()); // the trigger is on the root
                coin.transform.SetParent(go.transform, false);
                // A cylinder's axis is its local Y; laid on its side it faces
                // the ship like a coin, and its local Z is then the track's up.
                coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                coin.transform.localScale = new Vector3(collectibleSize, 0.04f, collectibleSize);
                Material mat = CollectibleMaterial();
                if (mat != null) coin.GetComponent<Renderer>().sharedMaterial = mat;

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = collectibleTriggerSize;

                collectible = go.AddComponent<Collectible>();
                collectible.Configure("Money", CollectibleKind.Money, Collectible.SpinAxis.Z, coin.transform);
            }

            // NextInt's max is exclusive.
            collectible.SetValue(rng.NextInt(collectibleValue.x, Mathf.Max(collectibleValue.x, collectibleValue.y) + 1));
            go.name = $"Money_{distance:00000}";
            spawned.Add((distance, go));
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

        // Feature visuals are pictures only — detection is analytic — so their
        // primitive colliders go (edit-mode previews included).
        static void DestroyComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Destroy(component); else DestroyImmediate(component);
        }

        static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                TrackDecorator.SafeDestroy(parent.GetChild(i).gameObject);
        }
    }

}
