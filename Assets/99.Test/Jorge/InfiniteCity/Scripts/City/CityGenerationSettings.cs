using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Every knob of the procedural city in one designer-facing asset: seed,
    /// grid dimensions, layout bias (long arterials, occasional connectors) and
    /// the socket-tagged road piece list. The CityManager draws this inline in
    /// its inspector, so tuning happens without leaving the scene. Same seed +
    /// same settings always reproduce the same city — generated content is
    /// never saved into the scene.
    /// </summary>
    [CreateAssetMenu(fileName = "CityGenerationSettings", menuName = "PoliceEscape/City Generation Settings")]
    public class CityGenerationSettings : ScriptableObject
    {
        // ----------------------------------------------------------- seed/grid
        [TitleGroup("Seed & grid")]
        [Tooltip("Master seed — every chunk derives its own RNG from this. Same seed = same city. Play and Recalculate never change it: the seed in force is the player's pinned city (CitySaveData), or the saved seed below when locked. Use the CityManager's 'Clear & Generate New City' button to roll and pin a new one.")]
        public int globalSeed = 1;

        [TitleGroup("Seed & grid")]
        [Tooltip("Author-time override: lock generation to the saved seed picked below, ignoring the player's pinned city. Leave off for normal play.")]
        [ShowIf(nameof(HasSavedSeeds))]
        public bool useSavedSeed;

        [TitleGroup("Seed & grid")]
        [Tooltip("Which saved seed to lock to. Save the current one with the button under Actions.")]
        [ShowIf(nameof(HasSavedSeeds)), EnableIf(nameof(useSavedSeed))]
        [ValueDropdown(nameof(savedSeeds))]
        public int savedSeed;

        [TitleGroup("Seed & grid")]
        [Tooltip("Seeds worth keeping — layouts you liked. Remove entries here to forget them.")]
        [ShowIf(nameof(HasSavedSeeds))]
        [ListDrawerSettings(DefaultExpandedState = false)]
        public List<int> savedSeeds = new();

        public bool HasSavedSeeds => savedSeeds != null && savedSeeds.Count > 0;

        [TitleGroup("Seed & grid")]
        [Tooltip("Side length of one grid cell in meters — should match the road piece footprint. Use 'Measure Cell Size' below to read it off the assigned pieces.")]
        [PropertyRange(0.5f, 60f), SuffixLabel("m", true)]
        public float cellSize = 20f;

        [TitleGroup("Seed & grid")]
        [Tooltip("Chunk side length in cells. Chunks are the streaming unit later; for now they size the generated stretch.")]
        [PropertyRange(8, 64)]
        public int chunkSizeInCells = 24;

        [TitleGroup("Seed & grid")]
        [Tooltip("Start parameters: how many chunks per side are generated up front (3 = a 3×3 block around the origin).")]
        [PropertyRange(1, 7)]
        public int initialCitySizeInChunks = 1;

        // -------------------------------------------------------------- layout
        [TitleGroup("Layout")]
        [Tooltip("Cells between arterial roads — the long straights that span chunks. Lower = denser road network, smaller blocks.")]
        [PropertyRange(4, 32)]
        public int arterialSpacing = 8;

        [Tooltip("0 = arterials sit dead-center of their band (perfectly regular grid), 1 = fully random placement inside the band.")]
        [TitleGroup("Layout")]
        [PropertyRange(0f, 1f)]
        public float arterialJitter = 0.75f;

        [TitleGroup("Layout")]
        [Tooltip("Chance that a block between arterials gets carved with a secondary connector road — more connectors = more alternate routes.")]
        [PropertyRange(0f, 1f)]
        public float connectorDensity = 0.6f;

        [TitleGroup("Layout")]
        [Tooltip("Chance a connector is L-shaped (adds corners) instead of a straight span between arterials.")]
        [PropertyRange(0f, 1f)]
        public float turnProbability = 0.35f;

        [TitleGroup("Layout")]
        [Tooltip("Keep dead-end stubs instead of repairing them away. Needs a single-socket (dead-end) piece in the list below.")]
        public bool allowDeadEnds;

        // ----------------------------------------------------------- buildings
        [TitleGroup("Buildings")]
        [Tooltip("Building set the populator fills non-road cells from. Leave empty for roads only. Later, districts can pick different sets by noise.")]
        public Population.BuildingSet buildingSet;

        // ----------------------------------------------------------- streaming
        [TitleGroup("Streaming")]
        [Tooltip("Stream chunks around the player at runtime: generate ahead as they approach an edge, unload far-behind chunks so the scene never overloads. Off = only the initial grid exists.")]
        public bool endlessStreaming = true;

        [TitleGroup("Streaming")]
        [Tooltip("Chunks kept loaded in every direction around the player's chunk (1 = a 3×3 block).")]
        [PropertyRange(1, 4)]
        public int loadRadiusInChunks = 1;

        [TitleGroup("Streaming")]
        [Tooltip("Extra ring beyond the load radius a chunk may drift into before it is unloaded — hysteresis, so driving along a chunk border doesn't load/unload in a loop.")]
        [PropertyRange(1, 3)]
        public int unloadPaddingInChunks = 1;

        [TitleGroup("Streaming")]
        [Tooltip("Spawn operations (road pieces, buildings) executed per frame while streaming — the time-slice budget that keeps chunk builds hitch-free. Raise it if chunks visibly build too slowly at speed.")]
        [PropertyRange(10, 500)]
        public int maxSpawnsPerFrame = 80;

        // ------------------------------------------------------------- physics
        [TitleGroup("Physics")]
        [Tooltip("Add colliders to generated content: a flat ground slab per chunk (top at road level, y = 0) and a fitted box per building — enough for WheelCollider driving without per-mesh colliders.")]
        public bool generateColliders = true;

        // -------------------------------------------------------------- pieces
        [TitleGroup("Road pieces")]
        [Tooltip("Scale spawned pieces so their footprint fills the cell exactly (cell size ÷ native size). Leave on unless the assets already match the cell size.")]
        public bool scaleToCellSize = true;

        [TitleGroup("Road pieces")]
        [Tooltip("The road piece's native footprint in meters, before any scaling. Set by 'Measure Cell Size'.")]
        [PropertyRange(0.1f, 60f), SuffixLabel("m", true)]
        public float pieceNativeSize = 1f;

        [TitleGroup("Road pieces")]
        [Tooltip("Extra uniform scale on spawned road pieces, on top of the cell fit. Grid spacing stays cellSize — slightly above 1 overlaps neighbouring tiles to hide seams, below 1 shrinks pieces inside their cell.")]
        [PropertyRange(0.5f, 2f)]
        public float roadScaleMultiplier = 1f;

        [TitleGroup("Road pieces")]
        [ListDrawerSettings(ShowIndexLabels = true, ListElementLabelName = nameof(RoadPieceDefinition.Label))]
        [ValidateInput(nameof(ValidatePieces), "Need at least one single-cell piece matching each shape the generator emits: straight (2 opposite sockets), corner (2 adjacent), T (3) and crossroad (4).")]
        [Tooltip("Every piece the generator may stamp. Single-cell Standard pieces are socket-matched per cell; multi-cell ones are templates (roundabout, split); Ramp/Deck/Pillar build the overpasses. Fill from the Kenney kit with Tools → Police Escape → Create Kenney Road Set.")]
        public List<RoadPieceDefinition> roadPieces = new();

        // ------------------------------------------------------------ features
        [TitleGroup("Road features")]
        [Tooltip("Master switch for the feature pass: overpasses and multi-cell templates (roundabouts, splits). Off = the plain one-piece-per-cell network.")]
        public bool placeFeatures = true;

        [TitleGroup("Road features")]
        [Tooltip("Chance that an eligible arterial crossing becomes a flyover: ramp up → deck over the crossing street → ramp down. Needs Ramp + Deck pieces in the list.")]
        [PropertyRange(0f, 1f), EnableIf(nameof(placeFeatures))]
        public float overpassChance = 0.5f;

        [TitleGroup("Road features")]
        [Tooltip("How many elevated deck cells a flyover may span (the crossing street sits under one of them). Longer decks need longer straight runs, so they appear less often.")]
        [MinMaxSlider(1f, 4f, true), EnableIf(nameof(placeFeatures))]
        public Vector2 overpassDeckCells = new(1f, 2f);

        [TitleGroup("Road features")]
        [Tooltip("Cells a ramp run occupies from street to deck. The ramp chain pieces are spread evenly over them (stretched or compressed along the slope), so 1 = steep jump kicker, 2+ = gentle climb. Create Kenney Road Set sets it to the kit's native chain length.")]
        [PropertyRange(1, 3), EnableIf(nameof(placeFeatures))]
        public int rampLengthInCells = 2;

        [TitleGroup("Road features")]
        [Tooltip("Chance that a straight side street forks right after leaving its arterial: the road-split piece turns one entrance into two parallel exits that both rejoin the next arterial. Needs Fork + HalfStraight pieces in the list.")]
        [PropertyRange(0f, 1f), EnableIf(nameof(placeFeatures))]
        public float forkChance = 0.5f;

        [TitleGroup("Road features")]
        [Tooltip("Straight stem cells between the arterial junction and the split piece. 0 = the road forks immediately.")]
        [MinMaxSlider(0f, 3f, true), EnableIf(nameof(placeFeatures))]
        public Vector2 forkStemCells = new(0f, 1f);

        public int OverpassDeckMin => Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(overpassDeckCells.x, overpassDeckCells.y)));
        public int OverpassDeckMax => Mathf.Max(OverpassDeckMin, Mathf.RoundToInt(overpassDeckCells.y));
        public int ForkStemMin => Mathf.Max(0, Mathf.RoundToInt(Mathf.Min(forkStemCells.x, forkStemCells.y)));
        public int ForkStemMax => Mathf.Max(ForkStemMin, Mathf.RoundToInt(forkStemCells.y));

        /// <summary>Forks need the split piece and the half straight that fills the seam junction.</summary>
        public bool HasForkPieces => FirstPieceWithRole(RoadPieceRole.Fork) != null && FirstPieceWithRole(RoadPieceRole.HalfStraight) != null;

        /// <summary>Ramp links ordered from the street up, each with its prefab assigned.</summary>
        public List<RoadPieceDefinition> RampChain
        {
            get
            {
                var chain = new List<RoadPieceDefinition>();
                if (roadPieces == null) return chain;
                foreach (var piece in roadPieces)
                    if (piece != null && piece.prefab != null && piece.role == RoadPieceRole.Ramp) chain.Add(piece);
                chain.Sort((a, b) => a.rampStartHeight.CompareTo(b.rampStartHeight));
                return chain;
            }
        }

        public RoadPieceDefinition FirstPieceWithRole(RoadPieceRole role)
        {
            if (roadPieces == null) return null;
            foreach (var piece in roadPieces)
                if (piece != null && piece.prefab != null && piece.role == role) return piece;
            return null;
        }

        public RoadPieceDefinition PillarPiece => FirstPieceWithRole(RoadPieceRole.Pillar);

        /// <summary>Overpasses need at least one ramp link and one deck piece.</summary>
        public bool HasOverpassPieces => FirstPieceWithRole(RoadPieceRole.Ramp) != null && FirstPieceWithRole(RoadPieceRole.Deck) != null;

        /// <summary>Deck surface height in the pieces' native units (the Deck piece's value; 0.5 for the Kenney kit).</summary>
        public float DeckNativeHeight
        {
            get
            {
                var deck = FirstPieceWithRole(RoadPieceRole.Deck);
                return deck != null ? deck.deckHeight : 0.5f;
            }
        }

        /// <summary>
        /// Uniform scale every stamped piece gets, so a kit authored at
        /// <see cref="pieceNativeSize"/> fills one <see cref="cellSize"/> cell.
        /// Lives here rather than on the CityManager because the populator and
        /// the road builder need it too, and it reads nothing but settings.
        /// </summary>
        public float PieceScale
        {
            get
            {
                float scale = roadScaleMultiplier;
                if (scaleToCellSize && pieceNativeSize > 0.0001f) scale *= cellSize / pieceNativeSize;
                return scale;
            }
        }

        /// <summary>World height the stamped city is sunk by so its lanes land on the drivable plane.</summary>
        public float RoadSurfaceHeight => RoadSurfaceNativeHeight * PieceScale;

        /// <summary>
        /// Height of the flat roads' driving lane in native units (the first
        /// Standard piece's value; 0.01 for the Kenney kit). The chunk ground
        /// slab and the road graph's ground nodes both sit at this height, so
        /// the plane cars actually drive on is the asphalt they can see —
        /// leave it at 0 and every ramp, which carries a real collider,
        /// presents a step the height of a curb at its foot.
        /// </summary>
        public float RoadSurfaceNativeHeight
        {
            get
            {
                var road = FirstPieceWithRole(RoadPieceRole.Standard);
                return road != null ? road.laneHeight : 0f;
            }
        }

        // ------------------------------------------------------------- buttons
        [TitleGroup("Actions")]
        /// <summary>
        /// Author-time seed shuffling for previewing layouts. It does NOT pin
        /// the result — the player's city is only changed by the CityManager's
        /// "Clear &amp; Generate New City" button, which writes through to
        /// <see cref="CitySaveData"/>. Press Recalculate after this to see the
        /// rolled layout, and note the next Play reverts to the pinned city.
        /// </summary>
        [Button("Randomize Seed (preview only)", ButtonSizes.Medium)]
        [Tooltip("Rolls a seed for previewing layouts in the editor. Does not pin it — use the CityManager's 'Clear & Generate New City' to actually change the player's city.")]
        void RandomizeSeed()
        {
            globalSeed = Random.Range(int.MinValue / 2, int.MaxValue / 2);
            MarkDirty();
        }

        [TitleGroup("Actions")]
        [Button("Save Current Seed", ButtonSizes.Medium)]
        [Tooltip("Keep the current seed in the saved list (and select it in the dropdown) so a layout you like can be locked in and recalled later. Locking needs 'Use Saved Seed' ticked above.")]
        void SaveCurrentSeed()
        {
            savedSeeds ??= new List<int>();
            if (!savedSeeds.Contains(globalSeed)) savedSeeds.Add(globalSeed);
            savedSeed = globalSeed;
            MarkDirty();
        }

        void MarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        const string RoadsFolder = "Assets/99.Test/Jorge/InfiniteCity/Roads";

        [TitleGroup("Actions")]
        [Button("Auto-Fill Road Pieces", ButtonSizes.Medium)]
        [Tooltip("Populate the list with the kit's basic straight/bend/T/cross/end pieces only. Prefer Tools → Police Escape → Create Kenney Road Set, which also adds the roundabout, split and overpass parts and measures the ramps. Masks are best guesses — verify in the Road Kit Showcase scene and fix connectionMask or rotationOffset per piece.")]
        void AutoFillRoadPieces()
        {
            (string file, EdgeMask mask)[] wanted =
            {
                ("road-straight.fbx", EdgeMask.North | EdgeMask.South),
                ("road-bend.fbx", EdgeMask.North | EdgeMask.East),
                ("road-intersection.fbx", EdgeMask.North | EdgeMask.East | EdgeMask.West),
                ("road-crossroad.fbx", EdgeMask.All),
                ("road-end.fbx", EdgeMask.North),
            };

            roadPieces.Clear();
            foreach (var (file, mask) in wanted)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{RoadsFolder}/{file}");
                if (prefab == null)
                {
                    Debug.LogWarning($"CityGenerationSettings: '{file}' not found in {RoadsFolder} — skipped.");
                    continue;
                }
                roadPieces.Add(new RoadPieceDefinition { prefab = prefab, connectionMask = mask, weight = 1f });
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [TitleGroup("Actions")]
        [Button("Measure Cell Size From Pieces", ButtonSizes.Medium)]
        [Tooltip("Reads the renderer bounds of the first assigned piece and sets pieceNativeSize (and cellSize, if scaleToCellSize is off) to its XZ footprint.")]
        void MeasureCellSize()
        {
            foreach (var piece in roadPieces)
            {
                if (piece?.prefab == null) continue;
                var renderers = piece.prefab.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;

                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                pieceNativeSize = Mathf.Max(bounds.size.x, bounds.size.z);
                if (!scaleToCellSize) cellSize = pieceNativeSize;
                Debug.Log($"CityGenerationSettings: measured '{piece.prefab.name}' footprint = {bounds.size.x:0.##} × {bounds.size.z:0.##} m → pieceNativeSize = {pieceNativeSize:0.##} m.");
                UnityEditor.EditorUtility.SetDirty(this);
                return;
            }
            Debug.LogWarning("CityGenerationSettings: no piece with renderers assigned — nothing to measure.");
        }
#endif

        bool ValidatePieces(List<RoadPieceDefinition> pieces)
        {
            if (pieces == null) return false;
            bool straight = false, corner = false, tee = false, cross = false;
            foreach (var piece in pieces)
            {
                if (piece?.prefab == null || !piece.IsStandard || piece.IsMultiCell) continue; // templates and overpass parts don't cover the basic shapes
                int count = piece.connectionMask.ConnectionCount();
                switch (count)
                {
                    case 2:
                        // Opposite sockets survive a half-turn unchanged; adjacent ones don't.
                        if (piece.connectionMask.RotateCw(2) == piece.connectionMask) straight = true;
                        else corner = true;
                        break;
                    case 3: tee = true; break;
                    case 4: cross = true; break;
                }
            }
            return straight && corner && tee && cross;
        }
    }
}
