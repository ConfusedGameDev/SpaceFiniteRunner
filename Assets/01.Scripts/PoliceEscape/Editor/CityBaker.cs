using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Offline city → prefab pipeline. Bakes a <see cref="CityDefinition"/>
    /// into the city prefab: generates every block's grid model against one
    /// <see cref="CityLayout"/> (pure, deterministic), stamps the geometry
    /// via <see cref="CityBlockBuilder"/> — with pieces instantiated as
    /// PREFAB INSTANCES, so the city prefab stays a lattice of references
    /// instead of a copy of every mesh — writes the serialized layout into
    /// each <see cref="CityBlock"/>, and saves. The rebuild is IN PLACE:
    /// only Block_* children are replaced, while the AdditionalItems and
    /// DefaultVehicles sockets are created once and never touched — that is
    /// the entire persistence mechanism for designer content. Rebuilding a
    /// single block is safe by construction: borders come from the city
    /// seed, which a block reroll never changes.
    /// </summary>
    public static class CityBaker
    {
        public const string DefaultPrefabPath = "Assets/03.Prefabs/PoliceEscape/City.prefab";
        public const string AdditionalItemsSocket = "AdditionalItems";
        public const string DefaultVehiclesSocket = "DefaultVehicles";

        /// <summary>What a validation pass found. Errors block a bake; warnings don't.</summary>
        public sealed class ValidationReport
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
            public readonly HashSet<Vector2Int> BadBlocks = new();
            public bool Ok => Errors.Count == 0;

            public override string ToString()
            {
                if (Errors.Count == 0 && Warnings.Count == 0) return "City definition is valid.";
                var lines = new List<string>();
                foreach (string error in Errors) lines.Add("ERROR: " + error);
                foreach (string warning in Warnings) lines.Add("Warning: " + warning);
                return string.Join("\n", lines);
            }
        }

        // ---------------------------------------------------------- validation

        public static ValidationReport Validate(CityDefinition definition)
        {
            var report = new ValidationReport();
            if (definition == null)
            {
                report.Errors.Add("No CityDefinition assigned.");
                return report;
            }
            CityGenerationSettings settings = definition.generation;
            if (settings == null)
            {
                report.Errors.Add("The definition has no CityGenerationSettings — assign the shared generation asset.");
                return report;
            }

            if (settings.arterialSpacing > definition.blockSizeInCells)
                report.Errors.Add($"arterialSpacing ({settings.arterialSpacing}) exceeds blockSizeInCells ({definition.blockSizeInCells}) — a block edge could go uncrossed by any road, disconnecting blocks.");

            if (definition.CellsPerAxisX % settings.arterialSpacing != 0 || definition.CellsPerAxisY % settings.arterialSpacing != 0)
                report.Warnings.Add($"City size in cells ({definition.CellsPerAxisX}×{definition.CellsPerAxisY}) is not a multiple of arterialSpacing ({settings.arterialSpacing}) — the band at the wrap seam is truncated and may drop its arterial there.");

            if (definition.gridWidth * definition.gridHeight > 36)
                report.Warnings.Add($"{definition.gridWidth}×{definition.gridHeight} blocks is a very large prefab — expect slow bakes and a heavy asset.");

            var layout = new CityLayout(definition);
            foreach (CityDefinition.BlockEntry entry in definition.blocks)
            {
                if (entry == null || !entry.connectorOnly || !definition.InGrid(entry.coord)) continue;
                if (!settings.HasOverpassPieces)
                {
                    report.Errors.Add("Connector blocks need Ramp and Deck pieces in the generation settings' piece list.");
                    report.BadBlocks.Add(entry.coord);
                    continue;
                }
                if (definition.blockSizeInCells < layout.MinConnectorBlockSize)
                {
                    report.Errors.Add($"Block {entry.coord}: connector blocks need at least {layout.MinConnectorBlockSize} cells per side for ramps + deck (block size is {definition.blockSizeInCells}).");
                    report.BadBlocks.Add(entry.coord);
                }
                if (layout.SpecFor(entry.coord).BridgeLineLocal < 0)
                {
                    report.Errors.Add($"Block {entry.coord}: no arterial line crosses its interior on the chosen axis — the bridge has nothing to follow. Lower arterialSpacing or pick the other axis.");
                    report.BadBlocks.Add(entry.coord);
                }
            }
            return report;
        }

        // --------------------------------------------------------------- bake

        /// <summary>Bake the whole city into the prefab (in place when it exists). Returns the prefab asset, or null on validation failure.</summary>
        public static GameObject BakeCity(CityDefinition definition, string prefabPath = DefaultPrefabPath)
        {
            ValidationReport report = Validate(definition);
            if (!report.Ok)
            {
                Debug.LogError($"CityBaker: bake refused —\n{report}", definition);
                return null;
            }

            definition.EnsureEntries();
            EditorUtility.SetDirty(definition);
            var layout = new CityLayout(definition);

            BakeInto(definition, layout, prefabPath, null);

            AssetDatabase.SaveAssetIfDirty(definition);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Debug.Log($"CityBaker: baked {definition.gridWidth}×{definition.gridHeight} blocks into {prefabPath}.", prefab);
            return prefab;
        }

        /// <summary>
        /// Rebuild a single block in the prefab. Optionally rerolls its seed
        /// first (written back to the definition). Every border road stays put
        /// — borders derive from the city seed, not the block seed.
        /// </summary>
        public static GameObject RebuildBlock(CityDefinition definition, Vector2Int coord, bool newSeed, string prefabPath = DefaultPrefabPath)
        {
            ValidationReport report = Validate(definition);
            if (!report.Ok)
            {
                Debug.LogError($"CityBaker: rebuild refused —\n{report}", definition);
                return null;
            }

            definition.EnsureEntries();
            if (newSeed)
                definition.GetOrCreateEntry(coord).seed = Random.Range(int.MinValue / 2, int.MaxValue / 2);
            EditorUtility.SetDirty(definition);
            var layout = new CityLayout(definition);

            BakeInto(definition, layout, prefabPath, coord);

            AssetDatabase.SaveAssetIfDirty(definition);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Debug.Log($"CityBaker: rebuilt block {coord} in {prefabPath}.", prefab);
            return prefab;
        }

        /// <summary>
        /// The shared bake body: open (or create) the prefab contents, ensure
        /// root + sockets, replace the requested blocks, save. When
        /// <paramref name="onlyCoord"/> is set just that block is replaced;
        /// otherwise all of them.
        /// </summary>
        static void BakeInto(CityDefinition definition, CityLayout layout, string prefabPath, Vector2Int? onlyCoord)
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            GameObject root = existed ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject("City");

            var previousInstantiate = CityBlockBuilder.Instantiate;
            CityBlockBuilder.Instantiate = (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            try
            {
                CityRoot cityRoot = root.GetComponent<CityRoot>();
                if (cityRoot == null) cityRoot = root.AddComponent<CityRoot>();
                cityRoot.definition = definition;
                cityRoot.gridWidth = definition.gridWidth;
                cityRoot.gridHeight = definition.gridHeight;
                cityRoot.blockSizeInCells = definition.blockSizeInCells;
                cityRoot.cellSize = definition.generation.cellSize;
                cityRoot.deckWorldHeight =
                    (definition.generation.DeckNativeHeight - definition.generation.RoadSurfaceNativeHeight) * definition.generation.PieceScale;
                cityRoot.citySeed = definition.citySeed;

                // Sockets: created once, NEVER destroyed or reparented — their
                // children are the designer's persistent content.
                cityRoot.additionalItems = EnsureSocket(root.transform, AdditionalItemsSocket);
                cityRoot.defaultVehicles = EnsureSocket(root.transform, DefaultVehiclesSocket);

                foreach (CityBlock stale in root.GetComponentsInChildren<CityBlock>(true))
                {
                    if (onlyCoord.HasValue && stale.coord != onlyCoord.Value) continue;
                    Object.DestroyImmediate(stale.gameObject);
                }

                for (int y = 0; y < definition.gridHeight; y++)
                for (int x = 0; x < definition.gridWidth; x++)
                {
                    var coord = new Vector2Int(x, y);
                    if (onlyCoord.HasValue && coord != onlyCoord.Value) continue;
                    ChunkData data = layout.GenerateBlock(coord);
                    CityBlockBuilder.BuildBlock(layout, coord, data, root.transform);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                CityBlockBuilder.Instantiate = previousInstantiate;
                if (existed) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        static Transform EnsureSocket(Transform root, string name)
        {
            Transform socket = root.Find(name);
            if (socket != null) return socket;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go.transform;
        }

        // ------------------------------------------------------------- assets

        /// <summary>
        /// Load the definition at <paramref name="path"/>, creating (and
        /// seeding) it only when missing — an existing authored definition is
        /// never touched.
        /// </summary>
        public static CityDefinition EnsureDefinition(string path, CityGenerationSettings generation)
        {
            var definition = AssetDatabase.LoadAssetAtPath<CityDefinition>(path);
            if (definition != null) return definition;

            definition = ScriptableObject.CreateInstance<CityDefinition>();
            definition.generation = generation;
            definition.citySeed = Random.Range(int.MinValue / 2, int.MaxValue / 2);
            definition.EnsureEntries();
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"CityBaker: created city definition at {path}.", definition);
            return definition;
        }
    }
}
