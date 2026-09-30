using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.Contracts;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Procedural track builder and streamer, in two halves that never mix.
    /// <b>Deciding</b> lays the road (knots, sweeps, banks, features, the end)
    /// and decides everything that goes on it as <see cref="TrackPlacement"/>
    /// records — every random draw happens here. On a finite play track the
    /// WHOLE track is decided in <see cref="Generate"/> (a couple of hundred
    /// knots and records: cheap), so the run knows its full shape and length
    /// from the first frame; an endless preview decides as it streams.
    /// <b>Building</b> turns records into GameObjects only inside the stream
    /// window — from the ship to <c>aheadDistance</c> ahead — and draws
    /// nothing, so the same records always build the same track;
    /// <see cref="SettledDistance"/> is how far it has built, which the
    /// colliders and the decoration follow, and objects are culled once
    /// <c>behindDistance</c> behind.
    /// <b>Spawnables</b> — speed orbs, repair orbs, laser gates, anything else
    /// — are <see cref="TrackSpawner"/> assets listed in the
    /// <see cref="TrackSpawnSet"/>: the generator knows none of them by name,
    /// it only runs each one's stream over the settled track (ground claimers
    /// first) through a shared <see cref="TrackSpawnContext"/>.
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
    /// city's pickup prefabs drop straight in — placed after the spawners so a
    /// coin never sits on an orb, and off claimed ground like everything
    /// else. Runtime-only: uses Object.Destroy for cleanup.
    /// </summary>
    public class TrackGenerator : MonoBehaviour
    {
        /// <summary>
        /// One placeable track feature kind. Drawn by probability at every
        /// feature step (the sliders rebalance to 100% like the orb tiers);
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
        [Tooltip("The track's one tuning asset: width, straightness, the feature table and spacing, elevation, sweeps and banking. Play mode runs on a runtime clone; the debug menu edits this asset and mirrors onto the clone. Regenerate to see it.")]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [SerializeField] TrackShapeSettings trackShape;

        [TitleGroup("Core Settings")]
        [Tooltip("The three ramps the track ends in. Its definition is a JumpDefinition of its own (entry margin 0, so the whole ramp counts); left empty, Resources/FiniteRunner_EndRamp is loaded, else built-in defaults. Width and lateral come from the track width and the GameSettings gaps, never from the definition's width fraction; there is no boost and no arc. Probability and spacing are unused.")]
        [SerializeField] FeatureSpawnEntry endRamp = new() { name = "End", color = new Color(0.2f, 1f, 0.85f) };

        [SerializeField] TrackManager track;

        // Collaborators, by contract only (the track never names the ship or the
        // game flow): what it streams ahead of, the vehicle stats that shape
        // loops and ramps, and the run's rules (length, end, boosts). A
        // composition root hands them in with Bind; otherwise Awake discovers
        // them by interface, and with none present the track runs on its own
        // defaults (an endless preview).
        IStreamFocus focus;
        IShipPerformance performance;
        ITrackRunRules rules;

        [Tooltip("Stream the track ahead of the ship instead of building it all at once. In play the streamed track is still FINITE: it stops at the run's track length (RunnerLevelDefinition / GameSettings) in a straight run-up and three end ramps.")]
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
        // Turn rate, arc and drift live on the TrackShapeSettings asset (Turns group).

        [Header("Pads")]
        [Tooltip("Base material of code-built pickups (orbs, repair orbs, coins); each tint gets a recolored instance. Prefabs keep their own materials.")]
        [SerializeField] Material boostMaterial;
        [SerializeField] Transform padsParent;
        [Tooltip("Pad footprint (width, thickness, length): the unit the spawners' orbs and pads scale from, and how far pickups and coins keep off each other and off claimed ground.")]
        [SerializeField] Vector3 padSize = new(10f, 0.5f, 20f);

        // ------------------------------------------------------- Spawnables
        [TitleGroup("Spawnables")]
        [Tooltip("What streams onto the track — speed orbs, repair orbs, laser gates, and any other TrackSpawner asset listed in it. Add or remove spawner assets in the set; each one's spacing, chance and look live on its own asset. Play mode runs a runtime clone of each, which is what the debug menu edits.")]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [SerializeField] TrackSpawnSet spawnSet;

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
        [Tooltip("Minimum width and height of a coin's pickup volume, metres — never smaller than the coin itself. The player's ship takes coins by sweeping its hull box against this trigger (the patrol reads the same size analytically), so its length does not matter.")]
        [UnityEngine.Serialization.FormerlySerializedAs("collectibleTriggerSize")]
        [SerializeField] Vector2 collectiblePickupSize = new(5f, 5f);

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Coin diameter as a share of the repair orb's (the spawn set's Spawner_RepairOrbs): 0.5 = half a health pack. 0 = use the fixed size below. Also used when the set has no repair orbs.")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float collectibleShareOfRepairOrb = 0.5f;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Diameter of the code-built coin, metres, when it does not follow the repair orb (share 0, or no repair orbs in the set).")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true)]
        [SerializeField] float collectibleSize = 1.6f;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Gap between the visible road and the coin's LOWEST point (the bottom of its hover), along the track's up — like the repair orb's, so a coin always floats on top of the road and in the ship's path.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        [SerializeField] float collectibleRoadClearance = 1.5f;

        [ToggleGroup("spawnCollectibles")]
        [Tooltip("Tint of the code-built coin (a recolored instance of the boost material).")]
        [SerializeField] Color collectibleColor = new(1f, 0.8f, 0.2f);

        [Header("Features")]
        [Tooltip("Material of the code-built ramp slab and rails; each entry gets a recolored instance. Empty = the boost material.")]
        [SerializeField] Material featureMaterial;

        [Header("Markers (non-endless only, superseded by the decorator's barriers)")]
        [SerializeField] Transform markersParent;
        [SerializeField, Min(0f)] float markerSpacing;

        [Header("Decoration")]
        [SerializeField] TrackDecorator decorator;

        public bool Randomize => randomize;

        // Layout knobs, read off the shape in force. Width, straightness and
        // the feature table take effect on the next Generate.
        public float TrackWidth => Mathf.Clamp(Shape.trackWidth, 10f, 120f);
        public float Straightness => Mathf.Clamp(Shape.straightness, 0f, 100f);
        public FeatureSpawnEntry[] FeatureTable => Shape.featureTable;
        public Vector2 FeatureSpacing => new(Mathf.Max(100f, Shape.featureSpacing.x), Mathf.Max(Mathf.Max(100f, Shape.featureSpacing.x), Shape.featureSpacing.y));

        /// <summary>The authored shape asset — what the debug pages edit (then mirror onto <see cref="Shape"/>). Null when none is wired.</summary>
        public TrackShapeSettings ShapeAsset => trackShape;

        /// <summary>The authored spawn set (the assets). Null when none is wired.</summary>
        public TrackSpawnSet SpawnSet => spawnSet;

        /// <summary>
        /// The spawners in force, index for index with <see cref="SpawnSet"/>'s
        /// list (a null slot stays null): runtime clones in play, the assets in
        /// edit-mode previews. Empty until the first Generate.
        /// </summary>
        public IReadOnlyList<TrackSpawner> Spawners => runtimeSpawners;

        /// <summary>The first spawner in force of type <typeparamref name="T"/>, or null.</summary>
        public T GetSpawner<T>() where T : TrackSpawner
        {
            foreach (var spawner in runtimeSpawners)
                if (spawner is T match) return match;
            return null;
        }

        /// <summary>Resources path of the end ramps' definition, used when the scene wires none.</summary>
        public const string EndRampResourcePath = "FiniteRunner_EndRamp";

        /// <summary>The track length this run was generated for, metres; 0 on an endless track.</summary>
        public float TargetLength => targetLength;

        /// <summary>True when this run's track ends (play mode with a track length); an edit-mode preview and a scene with no GameManager stay endless.</summary>
        public bool IsFinite => targetLength > 0f;

        /// <summary>
        /// Where the road stops: the BUILT end once the last knot is down, the
        /// authored target until then (they differ by what the last segments
        /// could not split exactly), +infinity on an endless track.
        /// </summary>
        public float EndDistance =>
            track != null && track.HasEnd ? track.EndDistance
            : IsFinite ? targetLength
            : float.PositiveInfinity;

        /// <summary>
        /// The shape knobs in force: the runtime clone in play, the asset in
        /// edit-mode previews, a throwaway default when nothing is wired. The
        /// debug menu edits the asset and mirrors onto this. Takes effect on the next Generate.
        /// </summary>
        public TrackShapeSettings Shape
        {
            get
            {
                if (shapeRuntime == null) PrepareShape();
                return shapeRuntime;
            }
        }

        // Gameplay never writes the shape asset: a clone per Generate, like the
        // feature definitions. The debug menu writes the asset, then the clone.
        void PrepareShape()
        {
            if (shapeRuntime != null && shapeIsClone)
                DestroyObject(shapeRuntime); // last run's clone (or the fallback) — never an asset

            TrackShapeSettings source = ShapeSource;
            if (source != null)
            {
                shapeRuntime = Application.isPlaying ? Instantiate(source) : source;
                shapeIsClone = Application.isPlaying;
            }
            else
            {
                shapeRuntime = ScriptableObject.CreateInstance<TrackShapeSettings>();
                shapeRuntime.hideFlags = HideFlags.HideAndDontSave;
                shapeIsClone = true;
            }
        }

        // A saved track builds with the shape and the spawn set it was
        // generated with (its ramp records point into that feature table, its
        // orb records into those tiers); a generated one with the scene's.
        TrackShapeSettings ShapeSource => loadedAsset != null && loadedAsset.shape != null ? loadedAsset.shape : trackShape;
        TrackSpawnSet SpawnSetSource => loadedAsset != null && loadedAsset.spawnSet != null ? loadedAsset.spawnSet : spawnSet;

        /// <summary>The saved track the last <see cref="Generate"/> loaded, or null when it decided one.</summary>
        public TrackLayoutAsset LoadedTrack => loadedAsset;

        static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // Streaming state — all reset by Generate().
        Unity.Mathematics.Random rng;
        // Coins and ramp laterals draw from streams of their own, seeded off the
        // layout stream's state like the spawners': how many segments one
        // Decide call appends depends on the frame, so anything drawn from the
        // layout stream between segments would make the road frame-dependent.
        Unity.Mathematics.Random collectibleRng;
        Unity.Mathematics.Random rampRng;
        float heading;
        float pitch;   // grade of the last segment, degrees (elevation walk)
        float bank;    // roll of the last knot, degrees, right edge up positive (banking)
        int turnKnotsLeft;   // knots left in the current sweep (0 = on a straight)
        float turnRate;      // signed heading change per knot of the current sweep
        int straightKnots;   // knots laid since the last sweep ended
        float lastTurnSign;  // direction of the last sweep (0 = none yet)
        bool sweepFlat;      // the current sweep is authored flat: no bank, grip tested, outer edge open
        int deferredFlatKnots; // a flat sweep rolled while the last bank was still unwinding: it starts once the road is level
        TrackManager.FlatSweep flatSweep; // ...and its span while it is still being laid
        bool flatSweepGrew;  // the knot just laid belongs to it: push its end out once the length is known
        float lastKnotDistance; // track distance of the knot BEFORE the spline's end knot
        bool lastKnotStraight;  // that knot was plain straight road (see AddSegment's open-straight rule)
        bool straightRunRolled; // this straight run's open-or-walled roll is made...
        bool straightRunOpen;   // ...and this is what it said
        TrackManager.OpenStretch openStretch; // the open straight being grown, or null
        float3 endPosition;
        TrackShapeSettings shapeRuntime;
        float collectibleCursor;
        float featureCursor;
        readonly List<(FeatureSpawnEntry entry, float distance)> pendingRamps = new(); // decided at their knot, waiting for the run-up to settle
        float straightUntil;   // track distance up to which the road is held straight, level and flat: a ramp's landing zone
        float targetLength;    // the run's authored track length, 0 = endless
        float endZoneTarget;   // where the final run-up should begin (targetLength - the run-up)
        float endRunUp;        // length of that run-up
        float endTarget;       // where the road should stop: the BUILT run-up start + the run-up
        bool inEndZone;        // the run-up's first knot is down: everything from here on is collinear
        bool trackComplete;    // the last knot is down: nothing more is ever appended
        JumpDefinition endRampFallback; // built-in end ramp numbers, when neither the scene nor Resources has a definition

        /// <summary>What the knot <see cref="AddSegment"/> just laid landed on.</summary>
        enum SpotKind { None, Feature, EndZoneStart, End }
        readonly List<(float distance, GameObject go)> spawned = new();
        readonly List<(float start, float end)> claims = new(); // feature footprints pads keep off
        readonly List<float> padDistances = new();               // where pickups landed — later pickups and coins keep off them
        readonly List<TrackSpawner> runtimeSpawners = new();     // the set's spawners in force, index for index
        TrackSpawnContext spawnContext;
        readonly List<(float start, float end)> featureKeepOuts = new(); // every feature's ground + what lies ahead of it (a ramp's landing), and the end zone
        readonly List<TrackPlacement> placements = new(); // the run's decided layout, in the order it was decided
        readonly List<TrackPlacement> unbuilt = new();    // decided, not built yet, by distance
        int placementsQueued;                             // placements[0..this) are in unbuilt (or built)
        readonly List<float> knotDistances = new();       // track distance of every knot, index for index with the spline
        readonly List<TrackLayout.Section> sectionRecords = new(); // every section decided, replayable (a saved track stores them)
        TrackLayoutAsset loadedAsset;                     // the saved track this Generate loaded, or null (decided here)
        bool baking;                                      // the editor's Generate Track: a finite track with play's rules, in edit mode
        bool shapeIsClone;                                // shapeRuntime is ours to destroy (a play clone or the fallback), never an asset
        float builtTo;                                    // everything decided before this distance is built
        Dictionary<FeatureSpawnEntry, Material> featureMaterials;
        Material collectibleMaterial;

        // AutoSmooth reshapes the curves around the previous knot every time a
        // new one lands, so the last two segments are never safe to build on.
        float SettleMargin => 2f * segmentLength.y;

        void Awake()
        {
            if (endless) focus ??= ContractLookup.Find<IStreamFocus>();
            performance ??= ContractLookup.Find<IShipPerformance>();
            rules ??= ContractLookup.Find<ITrackRunRules>();
            if (randomize || endless) Generate();
        }

        /// <summary>
        /// The composition root's hook: hands the track what it streams for,
        /// the vehicle stats and the run's rules. Any may be null (that part
        /// falls back to the defaults). Takes effect on the next Generate.
        /// </summary>
        public void Bind(IStreamFocus streamFocus, IShipPerformance shipPerformance, ITrackRunRules runRules)
        {
            focus = streamFocus;
            performance = shipPerformance;
            rules = runRules;
        }

        /// <summary>
        /// Track distance up to which the track is decided AND built — what the
        /// colliders and the decoration stream to. On a finite play track the
        /// whole road is decided up front, so this is the stream window's edge.
        /// </summary>
        public float SettledDistance => builtTo;

        // Track distance that is DECIDED for good: past it, AutoSmooth still
        // reshapes the trailing curves when the next knot lands.
        float DecidedDistance => track == null ? 0f
            : trackComplete || !endless ? track.Length
            : Mathf.Max(0f, track.Length - SettleMargin);

        /// <summary>The run's decided placements (orbs, gates, ramps, coins…), built or not, in decision order.</summary>
        public IReadOnlyList<TrackPlacement> Placements => placements;

        /// <summary>Everything that ENDS before this distance is behind the ship and may go.</summary>
        public float CullDistance => endless && focus != null ? focus.Distance - behindDistance : float.NegativeInfinity;

        /// <summary>The track was thrown away and is being rebuilt: whatever was built along it (the streamed colliders) must go too.</summary>
        public event System.Action Regenerated;

        void Update()
        {
            if (!endless || focus == null || track == null) return;
            float edge = focus.Distance + aheadDistance;
            Decide(edge); // nothing left to decide on a finite track: it was decided whole in Generate
            BuildUpTo(edge);
            CullBehind(focus.Distance - behindDistance);
        }

        /// <summary>Full rebuild for a new run. Endless runs always rebuild — the old stretch behind the start was culled.</summary>
        public void RegenerateForRun()
        {
            if (randomize || endless) Generate();
        }

        [ContextMenu("Regenerate Track")]
        void RegenerateFromMenu() => Generate();

        /// <summary>The seed the last <see cref="Generate"/> ran on — the rolled one when <c>seed</c> is 0, so a random layout can be reproduced.</summary>
        public uint LastSeed { get; private set; }

        /// <summary>The track this generator builds.</summary>
        public TrackManager Track => track;

        /// <summary>The decorator stamping the road art (null when none is wired) — where the visible road sits.</summary>
        public TrackDecorator Decorator => decorator;

        /// <summary>Length of one end ramp, metres (the definition in force, else the built-in 120).</summary>
        public float EndRampLength => endRamp?.Runtime is JumpDefinition runtimeDef ? runtimeDef.length
            : endRamp?.definition is JumpDefinition def ? def.length : 120f;

        /// <summary>Half the width of one end ramp, metres, from the same layout rule that builds them.</summary>
        public float EndRampHalfWidth
        {
            get
            {
                float gap = EndRampLayout(out float width, out _);
                EndRampLateral(0, width, gap, out float half);
                return half;
            }
        }

        /// <summary>
        /// Rebuilds the track for a run: loads the level's saved track when it
        /// names one (<see cref="ITrackRunRules.AuthoredTrack"/>, play only),
        /// else decides one — a finite play track whole, an edit-mode preview
        /// as an endless stretch. Builds the first stretch either way.
        /// </summary>
        public void Generate() => GenerateCore(null);

        /// <summary>
        /// The editor's Generate Track: decides a whole FINITE track in edit
        /// mode, with the run's rules (<paramref name="runRules"/> — the scene's
        /// game flow: length, run-up, end ramps, boosts, hull) exactly as a
        /// play run would, ready for <see cref="CaptureLayout"/>. Builds the
        /// first stretch as a preview.
        /// </summary>
        public void GenerateForBake(ITrackRunRules runRules)
        {
            rules = runRules;
            baking = true;
            try { GenerateCore(null); }
            finally { baking = false; }
        }

        /// <summary>Loads a saved track into the scene in edit mode, as play would, and builds its first stretch — a preview.</summary>
        public void PreviewSavedTrack(TrackLayoutAsset asset) => GenerateCore(asset);

        void GenerateCore(TrackLayoutAsset preview)
        {
            if (track == null)
            {
                Debug.LogError($"[TrackGenerator] '{name}' has no TrackManager assigned — nothing to generate.", this);
                return;
            }

            loadedAsset = preview != null ? preview
                : !baking && Application.isPlaying && endless && rules != null ? rules.AuthoredTrack as TrackLayoutAsset
                : null;
            if (loadedAsset != null && !loadedAsset.IsValid)
            {
                Debug.LogWarning($"[TrackGenerator] saved track '{loadedAsset.name}' is empty or from an older format — generating one instead.", loadedAsset);
                loadedAsset = null;
            }

            // Last run's feature clones live on last run's shape clone, which
            // PrepareShape is about to replace.
            if (Application.isPlaying && shapeRuntime != null && shapeRuntime.featureTable != null)
                foreach (var old in shapeRuntime.featureTable)
                    if (old.Runtime != null && old.Runtime != old.definition) Destroy(old.Runtime);

            // Fresh clones of the shape and the spawners, straight from their
            // assets (the debug menu writes the assets, so a reload keeps its
            // edits); edit-mode previews read the assets as they are.
            PrepareShape();
            PrepareSpawners();

            // Features play a runtime clone of their definition asset; edit-mode
            // previews read the asset as is.
            if (FeatureTable != null)
                foreach (var entry in FeatureTable)
                    entry.Runtime = entry.definition != null && Application.isPlaying
                        ? Instantiate(entry.definition)
                        : entry.definition;

            // One width knob for everything: steering clamp, pad bounds, meshes.
            // A saved track keeps the width it was generated at.
            float width = loadedAsset != null ? loadedAsset.Layout.width : TrackWidth;
            track.SetWidth(width);
            if (decorator != null) decorator.SetTrackWidth(width);

            LastSeed = loadedAsset != null ? loadedAsset.Layout.seed
                : seed != 0 ? (uint)seed : ((uint)System.Environment.TickCount | 1u);
            rng = new Unity.Mathematics.Random(LastSeed);

            // Every spawner seeds a stream of its own off the layout stream's
            // STATE, not a draw from it: the road never depends on what spawns.
            uint layoutSeed = rng.state;
            collectibleRng = new Unity.Mathematics.Random(math.hash(new uint2(layoutSeed, TrackSpawner.NameHash("Collectibles"))) | 1u);
            rampRng = new Unity.Mathematics.Random(math.hash(new uint2(layoutSeed, TrackSpawner.NameHash("JumpRamps"))) | 1u);

            spawned.Clear();
            claims.Clear();
            padDistances.Clear();
            featureKeepOuts.Clear();
            placements.Clear();
            unbuilt.Clear();
            placementsQueued = 0;
            builtTo = 0f;
            sectionRecords.Clear();
            ClearChildren(padsParent);
            ClearChildren(markersParent);
            if (decorator != null) decorator.Clear();
            Regenerated?.Invoke();

            // The spline is only ever touched through the TrackManager (this
            // also drops last run's inserted sections).
            track.ClearKnots();
            heading = 0f;
            pitch = 0f;
            bank = 0f;
            turnKnotsLeft = 0;
            turnRate = 0f;
            straightKnots = 0;
            lastTurnSign = 0f;
            sweepFlat = false;
            deferredFlatKnots = 0;
            flatSweep = null;
            flatSweepGrew = false;
            lastKnotDistance = 0f;
            lastKnotStraight = false;
            straightRunRolled = false;
            straightRunOpen = false;
            openStretch = null;
            endPosition = float3.zero;
            track.AppendKnot(endPosition);
            knotDistances.Clear();
            knotDistances.Add(0f);

            // The spline was just replaced — without this, Length still reports
            // the previous track and Decide would think there is already
            // plenty of track, placing everything on a one-knot spline.
            track.Recalculate();

            spawnContext = new TrackSpawnContext(track, rules, padsParent, padSize, boostMaterial, layoutSeed,
                                                 decorator != null ? decorator.RoadYOffset : -1.2f,
                                                 spawned, claims, padDistances, featureKeepOuts, placements);
            foreach (var spawner in runtimeSpawners)
                if (spawner != null) spawner.Begin(spawnContext);
            collectibleCursor = collectibleRng.NextFloat(collectibleSpacing.x, collectibleSpacing.y);
            featureCursor = rng.NextFloat(FeatureSpacing.x, FeatureSpacing.y);
            pendingRamps.Clear();
            straightUntil = 0f;

            // The run's length is PULLED (the GameManager resolves its level
            // on demand — this Awake may run before its own), in play or when
            // baking: a plain edit-mode preview stays an endless stretch. A
            // saved track carries its own.
            targetLength = 0f;
            inEndZone = false;
            trackComplete = false;
            if (loadedAsset != null) targetLength = loadedAsset.Layout.endDistance;
            else if ((Application.isPlaying || baking) && endless && rules != null) targetLength = rules.TrackLengthMeters;
            // (Only asked of the manager for a finite track: the getter
            // resolves the run's data, which a plain edit-mode preview must not.)
            endRunUp = loadedAsset != null ? loadedAsset.Layout.endRunUp
                : IsFinite && rules != null ? rules.EndRunUpMeters : GameSettings.Default.endRunUpMeters;
            // Never a run-up that eats the whole track.
            endRunUp = Mathf.Min(endRunUp, targetLength * 0.5f);
            endZoneTarget = targetLength - endRunUp;
            endTarget = targetLength;
            PrepareEndRamp();

            if (endless && loadedAsset != null)
            {
                LoadLayout(loadedAsset.Layout);
                BuildUpTo(aheadDistance);
                return;
            }

            if (endless)
            {
                // A finite track is decided whole, now; an endless preview
                // only as far as it streams. Either way only the first stretch
                // is built — from the START, not from where the ship was last
                // run (a restart regenerates before the ship relaunches).
                Decide(IsFinite ? float.PositiveInfinity : aheadDistance);
                BuildUpTo(aheadDistance);
            }
            else
            {
                for (int i = 0; i < segments; i++)
                {
                    SpotKind spot = AddSegment();
                    track.Recalculate(); // the level rule and the feature spots read the spline end
                    knotDistances.Add(track.Length);
                    GrowFlatSweep();
                    if (spot == SpotKind.Feature) DecideFeature();
                }
                DecidePlacementsUpTo(track.Length - 150f);
                BuildUpTo(track.Length);
                PlaceMarkers();
            }
        }

        /// <summary>
        /// DECIDES the track piece by piece until at least <paramref name="target"/>
        /// distance of it is settled — a knot that lands on the next feature
        /// spot decides the feature there and then, and the spline continues
        /// from the feature — then decides what goes on the settled stretch.
        /// Builds nothing. The trailing SettleMargin stays undecided until more
        /// knots land; a finished track has none. +infinity decides a finite
        /// track to its end.
        /// </summary>
        void Decide(float target)
        {
            while (!trackComplete && track.Length - SettleMargin < target)
            {
                if (!IsFinite && float.IsPositiveInfinity(target)) break; // an endless track has no end to decide to
                SpotKind spot = AddSegment();
                track.Recalculate();
                knotDistances.Add(track.Length);
                GrowFlatSweep();
                switch (spot)
                {
                    case SpotKind.Feature: DecideFeature(); break;
                    case SpotKind.EndZoneStart: BeginEndZone(); break;
                    case SpotKind.End: FinishTrack(); break;
                }
            }
            DecidePlacementsUpTo(DecidedDistance);
        }

        // Everything that goes on the road, decided up to the settled limit:
        // ramps first (their footprint is already claimed, so everything keeps
        // off it), then the spawn set (ground claimers, then pickups in set
        // order), then coins (off where the pickups landed). New records join
        // the build queue in distance order.
        void DecidePlacementsUpTo(float limit)
        {
            SpawnPendingRamps(limit);
            float pickupsTo = PlaceSpawnersUpTo(limit);
            PlaceCollectiblesUpTo(pickupsTo - StageGuard);

            if (placementsQueued == placements.Count) return;
            for (int i = placementsQueued; i < placements.Count; i++) unbuilt.Add(placements[i]);
            placementsQueued = placements.Count;
            unbuilt.Sort((a, b) => a.distance.CompareTo(b.distance));
        }

        /// <summary>
        /// BUILDS every decided placement before <paramref name="edge"/> (never
        /// past what is decided) and stamps the decoration up to it. Draws
        /// nothing: the records carry every roll.
        /// </summary>
        void BuildUpTo(float edge)
        {
            edge = Mathf.Min(edge, DecidedDistance);
            if (edge <= builtTo) return;
            int count = 0;
            while (count < unbuilt.Count && unbuilt[count].distance < edge) Build(unbuilt[count++]);
            unbuilt.RemoveRange(0, count);
            builtTo = edge;
            if (decorator != null) decorator.DecorateUpTo(edge);
            if (!Application.isPlaying)
            {
                MarkPreview(padsParent);
                if (decorator != null) decorator.MarkPreview(); // the end markers stamp outside DecorateUpTo
            }
        }

        /// <summary>
        /// An edit-mode preview is never saved into the scene: play rebuilds
        /// (or loads) the track anyway, and a saved preview is thousands of
        /// lines of road pieces in the scene file.
        /// </summary>
        public static void MarkPreview(Transform parent)
        {
            if (parent == null) return;
            foreach (Transform child in parent)
                foreach (Transform t in child.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
        }

        // One record, built: the features the generator owns, else the spawner
        // that emits that kind.
        void Build(in TrackPlacement placement)
        {
            switch (placement.kind)
            {
                case TrackPlacementKind.Ramp:
                    if (FeatureEntry(placement.variant) is { Runtime: JumpDefinition jump } rampEntry)
                        BuildJump(placement.distance, placement.lateral, rampEntry, jump);
                    break;
                case TrackPlacementKind.Loop:
                    if (FeatureEntry(placement.variant) is { Runtime: LoopDefinition loop } loopEntry
                        && track.SectionAt(placement.distance) is LoopSection section)
                        CreateLoop(placement.distance, loopEntry, loop, section);
                    break;
                case TrackPlacementKind.EndRamp:
                    BuildEndRamp(placement);
                    break;
                case TrackPlacementKind.Collectible:
                    CreateCollectible(placement.distance, placement.lateral, Mathf.RoundToInt(placement.data.x));
                    break;
                default:
                    foreach (var spawner in runtimeSpawners)
                        if (spawner != null && spawner.Kind == placement.kind) { spawner.Build(spawnContext, placement); break; }
                    break;
            }
        }

        FeatureSpawnEntry FeatureEntry(int index) =>
            FeatureTable != null && index >= 0 && index < FeatureTable.Length ? FeatureTable[index] : null;

        /// <summary>
        /// Lays the next knot. Returns true when it landed ON the next feature
        /// spot: when the normal roll would reach or pass <c>featureCursor</c>
        /// the segment is cut to it (never shorter than a minimum segment —
        /// a closer spot is pushed out), the bank is zero and the knot carries
        /// explicit tangents so its pose is fixed whatever lands next — the
        /// feature is then decided at this knot (<see cref="DecideFeature"/>).
        /// A FINITE track has two more spots, landed on the same way: where
        /// its final run-up begins, and — every knot after that one being
        /// collinear, so chords are arc length — where the road stops.
        /// </summary>
        SpotKind AddSegment()
        {
            var shape = Shape;

            // No feature spot is left that the final run-up would swallow.
            if (IsFinite && !inEndZone && featureCursor >= endZoneTarget - segmentLength.y)
                featureCursor = float.MaxValue;

            float remaining = featureCursor - track.Length;
            SpotKind spotKind = FeatureTable != null && FeatureTable.Length > 0 && remaining <= segmentLength.y
                ? SpotKind.Feature
                : SpotKind.None;

            // The end spots: the nearer of the feature spot and the end spot
            // is the one this knot may land on (the feature cursor is already
            // out of the way when they are close).
            bool splitToEnd = false;
            if (IsFinite)
            {
                float toEnd = (inEndZone ? endTarget : endZoneTarget) - track.Length;
                if (spotKind == SpotKind.None || toEnd <= remaining)
                {
                    if (toEnd <= segmentLength.y)
                    {
                        spotKind = inEndZone ? SpotKind.End : SpotKind.EndZoneStart;
                        remaining = toEnd;
                    }
                    else if (toEnd < segmentLength.x + segmentLength.y)
                    {
                        // Too far for one segment, too near for two full ones:
                        // split the rest evenly instead of pushing the spot out.
                        spotKind = SpotKind.None;
                        remaining = toEnd;
                        splitToEnd = true;
                    }
                }
            }
            bool landOnSpot = spotKind != SpotKind.None;

            // Turns are SWEEPS, not a per-knot wobble: a sweep holds one
            // direction at one rate for as many knots as its arc needs (that
            // is what lets the bank build into a wall), then the road runs
            // straight for a few. Straightness is the chance a straight knot
            // starts a sweep — one draw per straight knot, so 100% never
            // turns and still consumes the flat track's draws.
            float curviness = 1f - Straightness / 100f;
            float previousHeading = heading;
            // A ramp's landing zone: straight, level and flat until the
            // longest jump the ship can make has landed — no sweep, no bank,
            // no grade change, so a jump always comes down on the road it
            // left, never on a wall or round a corner.
            // The final run-up is the same thing all the way to the end.
            bool holdStraight = track.Length < straightUntil || inEndZone;
            if (turnKnotsLeft == 0)
            {
                float roll = rng.NextFloat(0f, 1f);
                straightKnots++;
                if (deferredFlatKnots > 0)
                {
                    // A flat sweep waiting for level road (two knots at most):
                    // dropped if the road ahead was claimed in the meantime.
                    if (holdStraight || !TurnFits(shape, flat: true)) deferredFlatKnots = 0;
                    else if (Mathf.Approximately(bank, 0f))
                    {
                        turnKnotsLeft = deferredFlatKnots;
                        deferredFlatKnots = 0;
                        sweepFlat = true;
                        lastTurnSign = Mathf.Sign(turnRate);
                    }
                }
                else if (!holdStraight && roll < curviness && straightKnots > shape.minStraightKnots) StartTurn(shape);
            }
            if (turnKnotsLeft > 0)
            {
                if (holdStraight || LevelRequired(shape))
                {
                    // A feature is coming (or a landing): end the sweep now so the bank unwinds.
                    turnKnotsLeft = 0;
                    straightKnots = 0;
                }
                else
                {
                    heading += turnRate;
                    if (--turnKnotsLeft == 0) straightKnots = 0;
                }
            }
            float turnDelta = heading - previousHeading;

            // A flat sweep's span: a heading change at the spline's end knot
            // bends the segment BEFORE that knot as well as the one being laid
            // (AutoSmooth), so the span opens at the previous knot — still
            // inside the unstamped settle margin — and its end is pushed out
            // to each new knot once its distance is known (GrowFlatSweep).
            float here = track.Length;
            if (turnDelta != 0f && sweepFlat)
            {
                flatSweep ??= track.AddFlatSweep(lastKnotDistance, here, turnDelta > 0f ? -1 : 1);
                flatSweepGrew = true;
            }
            else flatSweep = null;

            // Open straights: a segment is plain straight road when the knots
            // at BOTH its ends are — no heading change (AutoSmooth bends the
            // segments either side of one), no bank, no landing zone, no
            // feature inside the level lead, no section. That is only known
            // once the knot after it is being laid, so the segment BEHIND the
            // spline's end is the one opened here (still unstamped: the
            // settle margin is two segments). One roll per straight run.
            bool straightKnot = turnDelta == 0f && turnKnotsLeft == 0 && deferredFlatKnots == 0
                                && Mathf.Approximately(bank, 0f) && !holdStraight && !landOnSpot
                                && !LevelRequired(shape) && track.SectionAt(here) == null;
            if (straightKnot && lastKnotStraight)
            {
                if (!straightRunRolled)
                {
                    straightRunRolled = true;
                    straightRunOpen = shape.openStraightChance > 0f && rng.NextFloat(0f, 1f) < shape.openStraightChance;
                }
                if (straightRunOpen)
                {
                    if (openStretch != null) openStretch.End = here;
                    else openStretch = track.AddOpenStretch(lastKnotDistance, here);
                }
            }
            else if (!straightKnot)
            {
                straightRunRolled = false;
                openStretch = null;
            }
            lastKnotStraight = straightKnot;
            lastKnotDistance = here;

            // Elevation: a grade walk inside a band around the baseline — a
            // random step per knot, leaned back home in proportion to the
            // height already gained, forced home outside the band, capped.
            // Off, it draws nothing, so a seed reproduces the flat track exactly.
            if (holdStraight)
            {
                // Under a jump the grade holds: the arc is authored against the
                // road's own up at every distance, but a crest or dip moving
                // under it reads as the ground rushing up or dropping away.
            }
            else if (shape.elevationEnabled && shape.maxGrade > 0f)
            {
                float step = shape.maxGradeStepPerKnot;
                float band = Mathf.Max(shape.elevationBand, 1f);
                float y = endPosition.y;
                pitch += rng.NextFloat(-step, step);
                pitch -= shape.baselinePull * (y / band) * step;
                if (Mathf.Abs(y) > band) pitch = -Mathf.Sign(y) * Mathf.Max(Mathf.Abs(pitch), step);
                pitch = Mathf.Clamp(pitch, -shape.maxGrade, shape.maxGrade);
            }
            else pitch = 0f;

            // Banking: lean into the turn at this knot (a right turn drops the
            // right edge), eased per knot, and level wherever a feature is
            // coming. No random draws, so a seed with banking off reproduces
            // the unbanked track exactly.
            float bankTarget = 0f;
            if (shape.bankEnabled && shape.maxBankAngle > 0f && !holdStraight && !sweepFlat && !LevelRequired(shape))
                bankTarget = Mathf.Clamp(-turnDelta * shape.bankPerDegreeOfTurn, -shape.maxBankAngle, shape.maxBankAngle);
            bank = shape.bankEnabled
                ? Mathf.MoveTowards(bank, bankTarget, Mathf.Max(shape.maxBankStepPerKnot, 0.01f))
                : 0f;

            float yaw = math.radians(heading);
            float grade = math.radians(pitch);
            float3 direction = new float3(math.sin(yaw) * math.cos(grade), math.sin(grade), math.cos(yaw) * math.cos(grade));
            float chord = landOnSpot
                ? Mathf.Max(remaining, spotKind == SpotKind.Feature ? segmentLength.x : segmentLength.x * 0.5f)
                : rng.NextFloat(segmentLength.x, segmentLength.y);
            if (splitToEnd) chord = remaining * 0.5f;
            if (landOnSpot) bank = 0f; // the level rule has unwound it already; a feature's entry is level, full stop
            endPosition += direction * chord;
            // The knot carries heading, grade and bank: AutoSmooth keeps the
            // authored up (world up rolled about the segment, projected onto
            // the sloped tangent), which is how the pose between knots leans.
            quaternion rotation = quaternion.LookRotationSafe(direction, math.up());
            if (bank != 0f) rotation = math.mul(quaternion.AxisAngle(direction, math.radians(bank)), rotation);
            if (landOnSpot) track.AppendKnot(endPosition, rotation, direction * (chord / 3f)); // pinned: the feature's entry pose
            else track.AppendKnot(endPosition, rotation);
            return spotKind;
        }

        // The knot just laid closed another stretch of a flat sweep: its
        // distance is only known now that the length was recalculated.
        void GrowFlatSweep()
        {
            if (!flatSweepGrew) return;
            flatSweepGrew = false;
            if (flatSweep != null) flatSweep.End = track.Length;
        }

        /// <summary>
        /// Features need level road (a loop must stand upright, a tube curls
        /// from a flat pose, a ramp rides its rails), so the bank target is
        /// zero at the spline's end whenever the next feature spot is within
        /// the level lead PLUS the knots the current bank needs to unwind, or
        /// the end is being laid under a tube section.
        /// </summary>
        bool LevelRequired(TrackShapeSettings shape)
        {
            float end = track.Length;
            if (track.SectionAt(end) is TubeSection) return true;
            float unwind = Mathf.Ceil(Mathf.Abs(bank) / Mathf.Max(shape.maxBankStepPerKnot, 0.01f)) * segmentLength.y;
            return NextLevelSpot - end <= shape.levelLeadDistance + unwind;
        }

        /// <summary>
        /// The next spot the road must arrive level at: the next feature, or
        /// the start of a finite track's final run-up when that comes first —
        /// so the bank is unwound and no sweep is cut short going into it.
        /// </summary>
        float NextLevelSpot => IsFinite && !inEndZone ? Mathf.Min(featureCursor, endZoneTarget) : featureCursor;

        /// <summary>
        /// A sweep may only start when its shortest possible run PLUS the full
        /// bank's unwind PLUS the level lead fit before the next feature spot,
        /// and never under a tube — so a sweep is never cut short by the
        /// level rule in the common case, and a feature never lands mid-bank.
        /// A FLAT sweep has no bank to unwind, so it fits in gaps a banked one
        /// cannot.
        /// </summary>
        bool TurnFits(TrackShapeSettings shape, bool flat)
        {
            float end = track.Length;
            if (end < straightUntil) return false; // a ramp's landing zone
            if (track.SectionAt(end) is TubeSection) return false;
            float sweepKnots = Mathf.Ceil(shape.TurnArcMin / shape.TurnRateMax);
            float unwindKnots = shape.bankEnabled && !flat
                ? Mathf.Ceil(shape.maxBankAngle / Mathf.Max(shape.maxBankStepPerKnot, 0.01f))
                : 0f;
            float needed = (sweepKnots + unwindKnots) * segmentLength.y + shape.levelLeadDistance;
            return NextLevelSpot - end > needed;
        }

        /// <summary>
        /// Rolls a sweep: rate and arc off their bands (knots = arc / rate),
        /// direction alternating by chance — or straight back toward the start
        /// heading once the road has drifted past <c>maxHeadingDrift</c>.
        /// </summary>
        void StartTurn(TrackShapeSettings shape)
        {
            // Nothing is drawn for a sweep that cannot fit either way — and at
            // flat chance 0 this is exactly the old gate, so that reproduces
            // the all-banked layout draw for draw.
            bool bankedFits = TurnFits(shape, flat: false);
            bool flatPossible = shape.unbankedSweepChance > 0f && TurnFits(shape, flat: true);
            if (!bankedFits && !flatPossible) return;

            float rate = rng.NextFloat(shape.TurnRateMin, shape.TurnRateMax);
            float arc = rng.NextFloat(shape.TurnArcMin, shape.TurnArcMax);
            float sign;
            if (Mathf.Abs(heading) > shape.maxHeadingDrift) sign = -Mathf.Sign(heading);
            else if (lastTurnSign == 0f) sign = rng.NextFloat(0f, 1f) < 0.5f ? -1f : 1f;
            else sign = rng.NextFloat(0f, 1f) < shape.alternateTurnChance ? -lastTurnSign : lastTurnSign;

            int knots = Mathf.Max(1, Mathf.RoundToInt(arc / rate));

            // Flat or banked? One draw per sweep — none at chance 0.
            bool flat = flatPossible && rng.NextFloat(0f, 1f) < shape.unbankedSweepChance;
            if (!flat && !bankedFits) return; // only a flat sweep fitted this gap, and the roll said banked

            turnRate = sign * rate;
            sweepFlat = false;
            if (flat && !Mathf.Approximately(bank, 0f))
            {
                // A flat sweep must stand on level road: while the last
                // sweep's bank is still unwinding it waits (AddSegment starts
                // it at the first level knot) instead of being thrown away —
                // otherwise most flat rolls were lost and the chance lied.
                deferredFlatKnots = knots;
                return;
            }
            turnKnotsLeft = knots;
            sweepFlat = flat;
            lastTurnSign = sign;
        }

        /// <summary>
        /// The piece-sequenced builder's decision, made AT THE KNOT that landed
        /// on the feature spot: one weighted draw from the feature table (a
        /// loop only when the ship can already reach its required speed —
        /// <see cref="LoopReachable"/> — else a redraw among the other entries
        /// off the SAME roll, so a seeded layout only diverges where a loop was
        /// refused). A section is registered right here, before any pad or
        /// road is placed beyond the spot, and the spline CONTINUES FROM THE
        /// FEATURE: a loop's exit knot is appended at its (displaced) exit pose
        /// and the bridge between the entry and exit knots — spline the ship
        /// never rides — becomes the section's spline extent. A ramp claims
        /// its footprint now and lands once its run-up is settled
        /// (<see cref="SpawnPendingRamps"/>). The cursor then jumps past the
        /// footprint, the exclusion and the larger of the spacing roll and the
        /// entry's own minimum.
        /// </summary>
        void DecideFeature()
        {
            float spot = track.Length; // the knot that just landed, at its true track distance
            featureCursor = spot;
            if (FeatureTable == null || FeatureTable.Length == 0)
            {
                featureCursor = spot + rng.NextFloat(FeatureSpacing.x, FeatureSpacing.y);
                return;
            }

            float roll = rng.NextFloat(0f, 1f);
            var entry = WeightedTable.Pick(FeatureTable, roll) as FeatureSpawnEntry;
            if (entry != null && !LoopReachable(entry, spot))
                entry = WeightedTable.Pick(FeatureTable, roll, exclude: entry) as FeatureSpawnEntry;
            if (entry == null || entry.Runtime == null)
            {
                featureCursor = spot + rng.NextFloat(FeatureSpacing.x, FeatureSpacing.y);
                return;
            }

            uint sectionState = rng.state; // a saved track replays the section's rolls from here
            TrackSection section = entry.Runtime.CreateSection(track, spot, ref rng);
            float footprint = section != null ? section.Length : entry.Runtime.FootprintLength;

            // A ramp only lands where the road stays straight, level and flat
            // for the LONGEST jump this ship can make — the definition's cap ×
            // the ship's jump strength, which the Store raises — plus a landing
            // clearance. The road beyond the spot is not laid yet, so the
            // builder reserves it (straightUntil) rather than checking it, and
            // the next feature keeps off the whole zone.
            float exclusion = entry.Runtime.ExclusionAhead;
            var jump = entry.Runtime as JumpDefinition;
            if (jump != null) exclusion = jump.MaxAirDistance(JumpStrength) + jump.landingClearance;

            // Nothing may reach into a finite track's final run-up: not a
            // loop, not a tube, not the longest landing off a ramp. The spot
            // is skipped (the section, if any, was never registered) and a
            // later, shorter draw may still fit.
            if (IsFinite && spot + footprint + exclusion > endZoneTarget)
            {
                featureCursor = spot + rng.NextFloat(FeatureSpacing.x, FeatureSpacing.y);
                return;
            }

            if (entry.Runtime.ClaimsFootprint) claims.Add((spot, spot + footprint));
            // Laser gates keep off EVERY feature — a tube too, which claims
            // nothing — and off what lies ahead of it (a ramp's longest landing).
            featureKeepOuts.Add((spot, spot + footprint + exclusion));
            if (jump != null) straightUntil = spot + jump.length + exclusion;

            if (section is LoopSection loop)
            {
                track.AddSection(loop);
                ContinueFromLoopExit(loop);
                sectionRecords.Add(new TrackLayout.Section { featureIndex = System.Array.IndexOf(FeatureTable, entry), start = spot, rngState = sectionState, splineExtent = loop.SplineExtent });
                placements.Add(new TrackPlacement(TrackPlacementKind.Loop, spot, 0f, System.Array.IndexOf(FeatureTable, entry)));
            }
            else if (section != null)
            {
                // A tube: the section is the whole feature; the spline keeps
                // coming underneath it, level (LevelRequired sees the section).
                track.AddSection(section);
                sectionRecords.Add(new TrackLayout.Section { featureIndex = System.Array.IndexOf(FeatureTable, entry), start = spot, rngState = sectionState, splineExtent = section.SplineExtent });
            }
            else
            {
                // A ramp is road-bound: it lands once its run-up is settled.
                pendingRamps.Add((entry, spot));
            }

            featureCursor = spot + footprint + exclusion
                          + Mathf.Max(rng.NextFloat(FeatureSpacing.x, FeatureSpacing.y), entry.minSpacing);
        }

        /// <summary>
        /// The jump strength the landing room after a ramp is sized for: the
        /// STRONGEST the Store can make it (never the player's own level), so
        /// one seed — or one saved track — is one road for every player, and
        /// the longest jump anyone can make still lands on straight road.
        /// </summary>
        float JumpStrength => Mathf.Max(1f, StoreUpgrades.MaxMultiplier(UpgradeIds.ShipJumpStrength));

        // The spline continues from the loop's exit: an explicit-tangent knot
        // at the exit pose (fixed whatever lands next), the curve between the
        // entry and exit knots measured as the section's spline extent, and
        // the builder's heading, grade and bank re-read from the exit. With
        // no displacement the two knots coincide and the bridge is a stub the
        // ship never sees either.
        void ContinueFromLoopExit(LoopSection loop)
        {
            loop.GetExitPose(0f, out Vector3 exitPosition, out Quaternion exitRotation);
            track.WorldToKnotSpace(ref exitPosition, ref exitRotation); // the loop stands in the world; knots (and this builder) live in the container's space
            Vector3 exitForward = exitRotation * Vector3.forward;
            float bridgeChord = Mathf.Max(Vector3.Distance((Vector3)endPosition, exitPosition), 1f);

            float before = track.SplineLength;
            track.AppendKnot((float3)exitPosition, (quaternion)exitRotation, (float3)(exitForward * (bridgeChord / 3f)));
            track.Recalculate();
            loop.SetSplineExtent(track.SplineLength - before);
            track.Recalculate();
            knotDistances.Add(track.Length);

            endPosition = exitPosition;
            heading = Mathf.Atan2(exitForward.x, exitForward.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(exitForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            bank = 0f;
            turnKnotsLeft = 0;
            straightKnots = 0;
        }

        // ---------------------------------------------------------- track end

        /// <summary>
        /// The knot that landed on the final run-up's start: from here to the
        /// end every knot is collinear (straight, the grade held, no bank, both
        /// walls up), nothing is placed (one claim over the whole zone keeps
        /// pads and coins off, the feature cursor is parked) and the end spot
        /// is measured off the BUILT start — inside the zone a chord is arc
        /// length, so the road stops exactly one run-up on.
        /// </summary>
        void BeginEndZone()
        {
            inEndZone = true;
            float start = track.Length;
            track.SetEndZone(start);
            endTarget = start + endRunUp;
            claims.Add((start, endTarget + 1000f));
            featureKeepOuts.Add((start, float.PositiveInfinity));
            featureCursor = float.MaxValue;
        }

        /// <summary>The last knot is down: the road stops here, for good, in three ramps.</summary>
        void FinishTrack()
        {
            trackComplete = true;
            track.SetEnd(track.Length);
            CreateEndRamps();
        }

        // The end ramps' definition: the scene's, else the Resources asset,
        // else built-in numbers — cloned in play like every feature definition.
        void PrepareEndRamp()
        {
            if (endRamp == null) endRamp = new FeatureSpawnEntry { name = "End", color = new Color(0.2f, 1f, 0.85f) };
            if (Application.isPlaying && endRamp.Runtime != null && endRamp.Runtime != endRamp.definition)
                Destroy(endRamp.Runtime); // last run's clone
            endRamp.Runtime = null;
            if (!IsFinite) return;

            TrackFeatureDefinition source = endRamp.definition;
            if (!(source is JumpDefinition)) source = Resources.Load<JumpDefinition>(EndRampResourcePath);
            if (source == null)
            {
                if (endRampFallback == null)
                {
                    endRampFallback = ScriptableObject.CreateInstance<JumpDefinition>();
                    endRampFallback.hideFlags = HideFlags.HideAndDontSave;
                    endRampFallback.displayName = "End";
                    endRampFallback.length = 120f;
                    endRampFallback.rampAngle = 15f;
                    endRampFallback.entryMargin = 0f;
                    endRampFallback.sideHitSpeedLoss = 0.05f;
                }
                source = endRampFallback;
            }
            endRamp.Runtime = Application.isPlaying ? Instantiate(source) : source; // edit mode (baking) reads the asset: a clone would leak
        }

        /// <summary>
        /// The three ramps the track ends in, side by side, their lips ON the
        /// end of the road: equal widths filling the track between the
        /// GameSettings gaps (one between each pair — the drops a ship that
        /// misses goes through — and one at each wall, 0 by default). The
        /// outer ramps reach a little past the wall so a ship pressed against
        /// it is on the ramp, never on a float's rounding. No boost and no
        /// arc: the lip is where the body leaves the track.
        /// </summary>
        void CreateEndRamps()
        {
            if (!(endRamp?.Runtime is JumpDefinition def)) return;

            float gap = EndRampLayout(out float width, out _);
            float start = track.EndDistance - def.length;
            for (int i = -1; i <= 1; i++)
                placements.Add(new TrackPlacement(TrackPlacementKind.EndRamp, start, EndRampLateral(i, width, gap, out _), i + 1));
        }

        // One of the three end ramps (slot 0 left, 1 middle, 2 right); the
        // middle one also marks the two gaps as drops.
        void BuildEndRamp(in TrackPlacement placement)
        {
            if (!(endRamp?.Runtime is JumpDefinition def)) return;
            float gap = EndRampLayout(out float width, out _);
            EndRampLateral(placement.variant - 1, width, gap, out float half);
            BuildRamp(placement.distance, placement.lateral, half, endRamp, def, 0f, isEndRamp: true);

            if (placement.variant == 1 && decorator != null)
                foreach (float side in new[] { -1f, 1f })
                    decorator.StampEndMarker(placement.distance, side * (width + gap) * 0.5f, gap, def.length);
        }

        // The end ramps' widths and gaps — ONE rule, shared by the ramps
        // themselves and by EndRampLaterals (the hyperspace autopilot aims at
        // them before they exist, so the two must never disagree).
        float EndRampLayout(out float width, out float sideGap)
        {
            float gap = rules != null ? rules.EndRampGapMeters : GameSettings.Default.endRampGapMeters;
            sideGap = rules != null ? rules.EndRampSideGapMeters : 0f;
            float halfWidth = track != null ? track.HalfWidth : 0f;
            width = Mathf.Max(2f, (halfWidth * 2f - 2f * gap - 2f * sideGap) / 3f);
            return gap;
        }

        // Ramp i (-1 left, 0 middle, 1 right): its centre lateral and half
        // width. The outer ramps sit flush against the wall — they reach a
        // little past it, so a ship pressed against it is on the ramp.
        float EndRampLateral(int i, float width, float gap, out float half)
        {
            float lateral = i * (width + gap);
            half = width * 0.5f;
            EndRampLayout(out _, out float sideGap);
            if (i != 0 && sideGap <= 0f)
            {
                half += 0.25f;
                lateral += i * 0.25f;
            }
            return lateral;
        }

        /// <summary>
        /// The centre laterals of the three end ramps (left, middle, right),
        /// from the same layout rule that builds them — valid before the ramps
        /// exist, which is what an autopilot lining the ship up needs.
        /// </summary>
        public float[] EndRampLaterals()
        {
            float gap = EndRampLayout(out float width, out _);
            return new[]
            {
                EndRampLateral(-1, width, gap, out _),
                EndRampLateral(0, width, gap, out _),
                EndRampLateral(1, width, gap, out _)
            };
        }

        /// <summary>
        /// The track just decided, as data (<see cref="TrackLayout"/>) — what
        /// the editor saves. Meaningful after a finite track was decided whole
        /// (<see cref="GenerateForBake"/>); a copy, safe to keep.
        /// </summary>
        public TrackLayout CaptureLayout()
        {
            var layout = new TrackLayout
            {
                seed = LastSeed,
                width = track.HalfWidth * 2f,
                length = track.Length,
                endZoneStart = track.EndZoneStart,
                endDistance = track.EndDistance,
                endRunUp = endRunUp,
            };
            // The knots as they were handed in, not as AutoSmooth left them:
            // replayed in order, they rebuild the spline bit for bit.
            foreach (var (knot, mode) in track.AppendedKnots) layout.knots.Add(new TrackLayout.Knot(knot, mode));
            layout.knotDistances.AddRange(knotDistances);
            layout.sections.AddRange(sectionRecords);
            foreach (var sweep in track.FlatSweeps) layout.flatSweeps.Add(new TrackLayout.Span(sweep.Start, sweep.End, sweep.OuterSide));
            foreach (var stretch in track.OpenStretches) layout.openStretches.Add(new TrackLayout.Span(stretch.Start, stretch.End));
            foreach (var keepOut in featureKeepOuts) layout.keepOuts.Add(new Vector2(keepOut.start, keepOut.end));
            layout.placements.AddRange(placements);
            return layout;
        }

        /// <summary>
        /// Puts a saved track in place of deciding one: the knots re-added in
        /// order (AutoSmooth knots recompute their tangents off the same
        /// neighbours), each section replayed from its saved random state on
        /// the same road, the spans, the end, the keep-outs and a copy of every
        /// record — queued to build like decided ones. Nothing more is decided:
        /// the spawners and the coins are parked. The hyperspace jump can still
        /// cut it back (<see cref="CutBackForEnd"/> works off the knots).
        /// </summary>
        void LoadLayout(TrackLayout layout)
        {
            track.ClearKnots();
            foreach (var knot in layout.knots) track.AppendKnot(knot.ToBezier(), knot.mode);
            track.Recalculate();

            foreach (var record in layout.sections)
            {
                TrackFeatureDefinition definition = FeatureEntry(record.featureIndex)?.Runtime;
                if (definition == null) continue;
                var replay = new Unity.Mathematics.Random { state = record.rngState };
                TrackSection section = definition.CreateSection(track, record.start, ref replay);
                if (section == null) continue;
                track.AddSection(section);
                if (section is LoopSection loop) loop.SetSplineExtent(record.splineExtent);
                track.Recalculate();
                sectionRecords.Add(record);
            }
            foreach (var span in layout.flatSweeps) track.AddFlatSweep(span.start, span.end, span.outerSide);
            foreach (var span in layout.openStretches) track.AddOpenStretch(span.start, span.end);
            if (layout.endZoneStart >= 0f) track.SetEndZone(layout.endZoneStart);
            track.SetEnd(layout.endDistance);
            if (Mathf.Abs(track.Length - layout.length) > 1f)
                Debug.LogWarning($"[TrackGenerator] saved track '{(loadedAsset != null ? loadedAsset.name : "?")}' rebuilt to {track.Length:0} m but was saved at {layout.length:0} m.", loadedAsset);

            knotDistances.Clear();
            knotDistances.AddRange(layout.knotDistances);
            foreach (var keepOut in layout.keepOuts) featureKeepOuts.Add((keepOut.x, keepOut.y));
            placements.AddRange(layout.placements);
            unbuilt.AddRange(placements);
            unbuilt.Sort((a, b) => a.distance.CompareTo(b.distance));
            placementsQueued = placements.Count;

            foreach (var spawner in runtimeSpawners)
                if (spawner != null) spawner.Park();
            collectibleCursor = float.MaxValue;
            featureCursor = float.MaxValue;
            pendingRamps.Clear();
            endZoneTarget = layout.endZoneStart;
            inEndZone = true;
            trackComplete = true;
        }

        /// <summary>
        /// Brings the end of a finite track in NOW (the runner's hyperspace
        /// jump). The track was decided whole at run start, so the road past
        /// what is already BUILT round the ship is cut away
        /// (<see cref="CutBackForEnd"/>) and the end is laid from there: the
        /// final run-up starts where the road then stops, plus whatever the
        /// road there still owes — the bank unwinding to level, a section
        /// (tube, loop) it is inside. Everything downstream is the ordinary
        /// end: the zone lands on its spot, <see cref="FinishTrack"/> records
        /// the three ramps, the colliders, the patrol's end and the HUD's
        /// distance follow. The run-up is at least one ramp plus a segment.
        /// False when the track is endless, or the real end is already that
        /// close — nothing changes then.
        /// </summary>
        public bool ForceEndAhead(float runUpMeters)
        {
            if (!IsFinite || track == null) return false;
            var shape = Shape;
            if (trackComplete || inEndZone)
            {
                if (!CutBackForEnd(shape)) return false;
            }
            else if (EndZoneStartFromTip(shape) >= endZoneTarget) return false; // still streaming (never on a play track): the old rule

            float start = EndZoneStartFromTip(shape);
            pendingRamps.Clear();
            deferredFlatKnots = 0;
            featureCursor = float.MaxValue;

            float rampLength = endRamp?.Runtime is JumpDefinition def ? def.length : 120f;
            endRunUp = Mathf.Max(runUpMeters, rampLength + segmentLength.x * 0.5f);
            endZoneTarget = start;
            targetLength = start + endRunUp; // the HUD's distance left, until the built end takes over
            Decide(float.PositiveInfinity);  // lay the new end now: the autopilot aims at it
            return true;
        }

        // Where a run-up could begin if the end were brought in at the spline's
        // current tip: never a zero-length chord onto the spot, after the bank
        // has unwound (and a sweep finished), never inside a section.
        float EndZoneStartFromTip(TrackShapeSettings shape)
        {
            float start = track.Length + segmentLength.x * 0.5f;
            float step = Mathf.Max(shape.maxBankStepPerKnot, 0.01f);
            float unwind = Mathf.Ceil(Mathf.Abs(bank) / step) * segmentLength.y;
            if (unwind > 0f || turnKnotsLeft > 0)
                start += turnKnotsLeft * segmentLength.y + unwind + shape.levelLeadDistance;
            TrackSection section = track.SectionAt(track.Length);
            if (section != null) start = Mathf.Max(start, section.EndDistance + segmentLength.x * 0.5f);
            return start;
        }

        // Room kept between the built road and the cut: what the last built
        // objects (a ramp, a rotor gate, a 40 m road stamp) may reach past
        // their own distance.
        const float CutClearance = 200f;

        /// <summary>
        /// Cuts a fully decided track back so a new end can be laid (the
        /// hyperspace jump). The last knot kept is the one AFTER the first knot
        /// at least <see cref="CutClearance"/> past the built road (removing
        /// the knots after it reshapes the segment ending there — AutoSmooth —
        /// so that whole segment must be unbuilt), and never one inside a
        /// section. Everything decided on the reshaped segment or past it goes;
        /// the builder resumes from the kept knot's heading, grade and bank,
        /// out of any sweep. False — nothing touched — when the real end zone
        /// already starts before the new one could.
        /// </summary>
        bool CutBackForEnd(TrackShapeSettings shape)
        {
            int count = knotDistances.Count;
            int first = knotDistances.FindIndex(d => d >= builtTo + CutClearance);
            if (first < 0) return false;
            int keep = first + 1;
            while (keep < count && track.SectionAt(knotDistances[keep - 1]) != null) keep++;
            if (keep >= count - 1) return false;

            float cut = knotDistances[keep];
            float reshapedFrom = knotDistances[keep - 1];
            float zoneStart = track.EndZoneStart >= 0f ? track.EndZoneStart : endZoneTarget;
            float keptBank = track.GetBankAtDistance(cut);
            float unwind = Mathf.Ceil(Mathf.Abs(keptBank) / Mathf.Max(shape.maxBankStepPerKnot, 0.01f)) * segmentLength.y;
            float newStart = cut + segmentLength.x * 0.5f + (unwind > 0f ? unwind + shape.levelLeadDistance : 0f);
            if (newStart >= zoneStart) return false; // the real end is already that close

            BezierKnot knot = track.Spline.Spline[keep];
            track.TruncateKnots(keep + 1, cut);
            knotDistances.RemoveRange(keep + 1, count - keep - 1);

            placements.RemoveAll(p => p.distance >= reshapedFrom);
            sectionRecords.RemoveAll(r => r.start >= cut);
            placementsQueued = placements.Count;
            unbuilt.RemoveAll(p => p.distance >= reshapedFrom);
            claims.RemoveAll(c => c.start >= reshapedFrom);
            featureKeepOuts.RemoveAll(k => k.start >= reshapedFrom);
            padDistances.RemoveAll(d => d >= reshapedFrom);

            // The builder, as it stood at the kept knot, out of any sweep.
            float3 forward = math.mul(knot.Rotation, new float3(0f, 0f, 1f));
            endPosition = knot.Position;
            heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            bank = keptBank;
            turnKnotsLeft = 0;
            turnRate = 0f;
            straightKnots = 0;
            sweepFlat = false;
            deferredFlatKnots = 0;
            flatSweep = null;
            flatSweepGrew = false;
            lastKnotDistance = knotDistances[keep - 1];
            lastKnotStraight = false;
            straightRunRolled = false;
            openStretch = null;
            inEndZone = false;
            trackComplete = false;
            return true;
        }

        /// <summary>Decided ramps land once the spline under their run-up is settled (AutoSmooth reshapes the last two segments as knots land).</summary>
        void SpawnPendingRamps(float limit)
        {
            // In road order: each ramp draws its lateral as it is built, so the
            // order must not depend on how many were waiting (one long pass vs
            // a pass per frame).
            for (int i = 0; i < pendingRamps.Count;)
            {
                var (entry, distance) = pendingRamps[i];
                if (distance + entry.Runtime.FootprintLength > limit) { i++; continue; }
                if (entry.Runtime is JumpDefinition jump) DecideJump(distance, entry, jump);
                pendingRamps.RemoveAt(i);
            }
        }

        /// <summary>
        /// One streaming pass of the spawn set up to <paramref name="limit"/>:
        /// every ground claimer first (a laser gate claims its stretch, so no
        /// pickup lands in a beam), then every pickup spawner in set order (each
        /// keeps off the pickups placed before it).
        /// Each stage stops <see cref="StageGuard"/> short of the one before it,
        /// so whatever a placement keeps off (a claim or a pickup within a pad
        /// length AHEAD of it) is always decided first — the layout is then the
        /// same whether the track streams in one pass or a pass per frame.
        /// Returns the limit the last stage ran to.
        /// </summary>
        float PlaceSpawnersUpTo(float limit)
        {
            if (spawnContext == null) return limit;
            foreach (var spawner in runtimeSpawners)
                if (spawner != null && spawner.Phase == SpawnPhase.ClaimsGround) spawner.PlaceUpTo(spawnContext, limit);
            foreach (var spawner in runtimeSpawners)
            {
                if (spawner == null || spawner.Phase == SpawnPhase.ClaimsGround) continue;
                limit -= StageGuard;
                spawner.PlaceUpTo(spawnContext, limit);
            }
            return limit;
        }

        // How far a later placement stage stays behind the one before it: the
        // reach of ClaimEnd's widening and NearPickup (one pad length). Zero
        // once the track is complete — every earlier stage then ran to the end.
        float StageGuard => trackComplete ? 0f : padSize.z + 1f;

        // The spawn set's assets are never mutated in play: a clone of each per
        // Generate, like the shape and the feature definitions, so the debug
        // menu edits the run's own copy. Index for index with the set's list.
        void PrepareSpawners()
        {
            foreach (var spawner in runtimeSpawners)
            {
                if (spawner == null) continue;
                spawner.Cleanup();
                // In play every spawner in force is a clone (last run's); edit-mode previews run the assets.
                if (Application.isPlaying) Destroy(spawner);
            }
            runtimeSpawners.Clear();
            TrackSpawnSet set = SpawnSetSource;
            if (set == null) return;

            foreach (var asset in set.Spawners)
                runtimeSpawners.Add(asset == null ? null : Application.isPlaying ? Instantiate(asset) : asset);
        }

        /// <summary>
        /// True when nothing claimed sits anywhere in [<paramref name="from"/>,
        /// <paramref name="to"/>]: no feature footprint, no ramp landing zone
        /// and not the final run-up. The same <see cref="featureKeepOuts"/>
        /// list the laser gates test against (<see cref="TrackSpawnContext.KeepOutUntil"/>),
        /// exposed as a plain predicate for anything that needs the rule
        /// rather than the next free spot — the patrol's attack run holds off
        /// on exactly this ground.
        /// Entries behind the streamer's cull line are gone, so ask about
        /// ground at or ahead of the ship.
        /// </summary>
        public bool IsGroundClear(float from, float to)
        {
            foreach (var keepOut in featureKeepOuts)
                if (to > keepOut.start && from < keepOut.end) return false;
            return true;
        }

        /// <summary>
        /// Rows of coins between the orbs: one lateral per row, coins a step
        /// apart along the track, every coin skipped where a pad already sits
        /// (within a pad length) or a feature claimed the ground.
        /// </summary>
        void PlaceCollectiblesUpTo(float limit)
        {
            if (!spawnCollectibles || spawnContext == null) return;
            // A row is only started when all of it fits under the limit: a row
            // cut at the settled edge would lose its far coins, and where it was
            // cut would depend on the frame.
            float rowReach = trackComplete ? 0f : (Mathf.Max(collectibleGroupSize.x, collectibleGroupSize.y) - 1) * collectibleStep;
            while (collectibleCursor + rowReach < limit)
            {
                float claimEnd = spawnContext.ClaimEnd(collectibleCursor);
                if (claimEnd >= 0f) { collectibleCursor = claimEnd; continue; }

                int count = collectibleRng.NextInt(collectibleGroupSize.x, Mathf.Max(collectibleGroupSize.x, collectibleGroupSize.y) + 1);
                track.GetLateralBand(collectibleCursor, out float bandMin, out float bandMax);
                float margin = collectiblePickupSize.x * 0.5f + 2f;
                float lo = bandMin + margin;
                float hi = bandMax - margin;
                float lateral = hi > lo ? collectibleRng.NextFloat(lo, hi) : (bandMin + bandMax) * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    float d = collectibleCursor + i * collectibleStep;
                    if (d >= limit || spawnContext.ClaimEnd(d) >= 0f || spawnContext.NearPickup(d)) continue;
                    // NextInt's max is exclusive.
                    int value = collectibleRng.NextInt(collectibleValue.x, Mathf.Max(collectibleValue.x, collectibleValue.y) + 1);
                    placements.Add(new TrackPlacement(TrackPlacementKind.Collectible, d, lateral, 0, new Vector4(value, 0f, 0f, 0f)));
                }

                collectibleCursor += count * collectibleStep + collectibleRng.NextFloat(collectibleSpacing.x, collectibleSpacing.y);
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
            for (int i = claims.Count - 1; i >= 0; i--)
                if (claims[i].end < minDistance) claims.RemoveAt(i);
            for (int i = padDistances.Count - 1; i >= 0; i--)
                if (padDistances[i] < minDistance) padDistances.RemoveAt(i);
            for (int i = featureKeepOuts.Count - 1; i >= 0; i--)
                if (featureKeepOuts[i].end < minDistance) featureKeepOuts.RemoveAt(i);
            if (decorator != null) decorator.CullBefore(minDistance);
        }

        /// <summary>
        /// The loop placement gate. True for anything but a loop, and for a
        /// loop in edit-mode or non-endless previews (no ship speed to read).
        /// In play, the ship's speed when it reaches <paramref name="distance"/>
        /// is predicted as its current speed minus the over-cruise bleed over
        /// the gap — which never takes it below cruise (or below where it is,
        /// if it is under cruise already: the throttle holds that) — and must clear the loop's required speed by the definition's
        /// <see cref="LoopDefinition.gateHeadroom"/>. The requirement is the
        /// same number the gate will be built with, so what is reachable now
        /// stays reachable unless the player loses speed on the way.
        /// </summary>
        bool LoopReachable(FeatureSpawnEntry entry, float distance)
        {
            if (!Application.isPlaying || !endless || focus == null || performance == null || rules == null) return true;
            if (entry.Runtime is not LoopDefinition loop) return true;

            // The track is decided whole at run start, long before the ship is
            // anywhere near most spots (and standing still at the start line):
            // predict from at least the cruise speed the throttle holds.
            float speed = Mathf.Max(focus.Speed, performance.CruiseSpeed);
            float gap = Mathf.Max(0f, distance - focus.Distance);
            float bled = speed - performance.PassiveDeceleration * gap / Mathf.Max(speed, 1f);
            float predicted = Mathf.Max(bled, Mathf.Min(speed, performance.CruiseSpeed));
            float required = rules.LoopRequiredSpeed(distance) * (1f + Mathf.Max(0f, loop.gateHeadroom));
            return predicted >= required;
        }

        // Keeps the feature probability sliders honest: whichever slider the
        // designer just moved keeps its value, the others rebalance
        // proportionally so the table always totals 100%.


        // The code-built coin's gold: one recolored boost-material instance,
        // play mode only: edit-mode previews would leak the instance into the scene.
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

        // A ramp's lateral is rolled when its run-up settles, in road order,
        // off the ramps' own stream. (A tube's section is the whole feature —
        // the decorator stamps the pipe; a loop is recorded at its knot.)
        void DecideJump(float distance, FeatureSpawnEntry entry, JumpDefinition def)
        {
            float rampHalf = track.HalfWidth * Mathf.Clamp01(def.widthFraction);
            float maxLat = Mathf.Max(0f, track.HalfWidth - rampHalf - 2f);
            float lateral = rampRng.NextFloat(-maxLat, maxLat);
            placements.Add(new TrackPlacement(TrackPlacementKind.Ramp, distance, lateral, System.Array.IndexOf(FeatureTable, entry)));
        }

        /// <summary>
        /// A jump ramp: a JumpRamp component carrying the numbers the ship's
        /// analytic detection reads, plus a picture — the entry's unit prefab
        /// scaled to the ramp, or a code-built slab pitched to the ramp angle
        /// with a rail down each edge. No colliders: nothing here is physics.
        /// </summary>
        void BuildJump(float distance, float lateral, FeatureSpawnEntry entry, JumpDefinition def)
        {
            float rampHalf = track.HalfWidth * Mathf.Clamp01(def.widthFraction);
            float baseBoost = rules != null ? rules.PowerUpSpeedBoost : GameSettings.Default.powerUpSpeedBoost;
            float boost = baseBoost * entry.multiplier, rollBonus = 0f;
            // A ramp prefab that carries a RampBoost sets its takeoff as a share of a green orb's boost.
            var authored = entry.prefab != null ? entry.prefab.GetComponent<RampBoost>() : null;
            if (authored != null)
            {
                boost = baseBoost * GreenOrbMultiplier() * authored.GreenOrbShare;
                rollBonus = authored.BarrelRollBonus;
            }
            BuildRamp(distance, lateral, rampHalf, entry, def, boost, isEndRamp: false, rollBonus);
        }

        // The green tier's multiplier of the base boost (the runtime spawner's), 1 with no orb spawner.
        float GreenOrbMultiplier()
        {
            var orbs = GetSpawner<SpeedOrbSpawner>();
            if (orbs == null || orbs.Tiers == null || orbs.Tiers.Length == 0) return 1f;
            foreach (var tier in orbs.Tiers)
                if (tier.name == "Green") return tier.multiplier;
            return orbs.Tiers[0].multiplier;
        }

        // One ramp at a given lateral: the JumpRamp record and its picture.
        void BuildRamp(float distance, float lateral, float rampHalf, FeatureSpawnEntry entry, JumpDefinition def, float boost, bool isEndRamp, float rollBonus = 0f)
        {
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);

            var go = new GameObject($"{entry.name}{def.displayName}Ramp_{distance:00000}_{lateral:0}");
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
            ramp.Configure(def, distance, lateral, rampHalf, boost, isEndRamp, rollBonus);
            // An end ramp is keyed on its END for the cull: it must outlive
            // the ship's and the patrol's whole run-up.
            spawned.Add((isEndRamp ? distance + def.length : distance, go));
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
            float required = rules != null ? rules.LoopRequiredSpeed(distance) : 0f;
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

        /// <summary>The coin's diameter: a share of the repair orb's when the set has one, else <c>collectibleSize</c>.</summary>
        float CoinDiameter
        {
            get
            {
                var repair = collectibleShareOfRepairOrb > 0f ? GetSpawner<RepairOrbSpawner>() : null;
                return repair != null ? repair.Diameter(padSize) * collectibleShareOfRepairOrb : collectibleSize;
            }
        }

        /// <summary>
        /// One money pickup on the flight line: the assigned prefab (its
        /// Collectible must be set to Money) or a code-built coin — a flat
        /// gold cylinder standing on the track, spun round the track's up by
        /// the Collectible itself — under a root carrying the trigger box.
        /// </summary>
        void CreateCollectible(float distance, float lateral, int value)
        {
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            float diameter = CoinDiameter;
            // The pickup volume is never smaller than the coin you can see.
            Vector2 pickup = Vector2.Max(collectiblePickupSize, new Vector2(diameter, diameter));
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
            }
            else
            {
                go = new GameObject();
                go.transform.SetParent(padsParent, false);
                go.transform.SetPositionAndRotation(pos, rot);

                var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                coin.name = "Mesh";
                // NOW, not at the end of the frame: Collectible.Awake (below)
                // only adds its own trigger when it finds no collider, so a
                // deferred Destroy left the coin with none at all and the
                // ship's collider sweep could never take it.
                DestroyImmediate(coin.GetComponent<Collider>());
                coin.transform.SetParent(go.transform, false);
                // A cylinder's axis is its local Y; laid on its side it faces
                // the ship like a coin, and its local Z is then the track's up.
                coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                coin.transform.localScale = new Vector3(diameter, diameter * 0.03f, diameter);
                Material mat = CollectibleMaterial();
                if (mat != null) coin.GetComponent<Renderer>().sharedMaterial = mat;

                // The pickup trigger on the root, sized to the pickup volume:
                // what the ship's hull box sweeps against.
                var trigger = go.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(pickup.x, pickup.y, 1f);

                collectible = go.AddComponent<Collectible>();
                collectible.Configure("Money", CollectibleKind.Money, Collectible.SpinAxis.Z, coin.transform);
            }

            // Lifted along the track's up (so it holds on a bank or a tube) until
            // its lowest point — radius and hover below the centre — clears the
            // visible road by the clearance, like the repair orb.
            float roadSurface = decorator != null ? decorator.RoadYOffset : -1.2f;
            float lift = roadSurface + collectibleRoadClearance + collectible.HoverAmplitude + diameter * 0.5f;
            go.transform.position = pos + rot * (Vector3.up * lift);

            collectible.SetValue(value);
            // The patrol's analytic sweep reads the same volume, at the same height.
            collectible.PlaceOnTrack(distance, lateral, lift,
                                     new Vector3(pickup.x * 0.5f, pickup.y * 0.5f, 0.5f));
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
