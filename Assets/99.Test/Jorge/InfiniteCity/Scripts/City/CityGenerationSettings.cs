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
        [Tooltip("Master seed — every chunk derives its own RNG from this. Same seed = same city. Recalculate rolls a fresh one each press unless a saved seed is locked in below.")]
        public int globalSeed = 1;

        [TitleGroup("Seed & grid")]
        [Tooltip("Lock generation to the saved seed picked below instead of rolling a fresh one on every Recalculate.")]
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

        /// <summary>
        /// Called by CityManager right before generating: every Recalculate gets
        /// a fresh random seed, unless a saved seed is locked in via the dropdown.
        /// </summary>
        public void PrepareSeedForRecalculate()
        {
            if (useSavedSeed && HasSavedSeeds) globalSeed = savedSeed;
            else RandomizeSeed();
        }

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
        [TableList(AlwaysExpanded = true)]
        [ValidateInput(nameof(ValidatePieces), "Need at least one piece matching each shape the generator emits: straight (2 opposite sockets), corner (2 adjacent), T (3) and crossroad (4).")]
        public List<RoadPieceDefinition> roadPieces = new();

        // ------------------------------------------------------------- buttons
        [TitleGroup("Actions")]
        [Button("Randomize Seed", ButtonSizes.Medium)]
        void RandomizeSeed()
        {
            globalSeed = Random.Range(int.MinValue / 2, int.MaxValue / 2);
            MarkDirty();
        }

        [TitleGroup("Actions")]
        [Button("Save Current Seed", ButtonSizes.Medium)]
        [Tooltip("Keep the current seed in the saved list (and select it in the dropdown) so a layout you like can be locked in and recalled later.")]
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
        [Tooltip("Populate the list with the kit's basic straight/bend/T/cross/end pieces. Masks are best guesses — verify visually after the first Recalculate and fix connectionMask or rotationOffset per piece.")]
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
                if (piece?.prefab == null) continue;
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
