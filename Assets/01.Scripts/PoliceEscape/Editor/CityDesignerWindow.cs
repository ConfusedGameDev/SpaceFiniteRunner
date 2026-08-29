using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// The city authoring window (Tools → Police Escape → City Designer):
    /// edits the <see cref="CityDefinition"/> asset — grid size, seeds,
    /// per-block settings and connector flags — through a clickable block
    /// grid, validates the connectivity rules, and drives
    /// <see cref="CityBaker"/> to (re)bake the whole city or a single block
    /// into the city prefab. All edits land on the definition asset (dirty
    /// per change, saved at bake points); the window itself holds nothing but
    /// the current selection. Colours: orange = connector/bridge block,
    /// blue = has a settings override, red = failed validation.
    /// </summary>
    public class CityDesignerWindow : OdinEditorWindow
    {
        const string DefinitionPath = "Assets/04.Data/InfiniteCity/CityDefinition.asset";
        const string SettingsPath = "Assets/04.Data/InfiniteCity/CityTestSettings.asset";

        [PropertyOrder(0)]
        [InlineEditor(Expanded = false)]
        [Tooltip("The authored city this window edits. Auto-loaded (or created) from Assets/04.Data/InfiniteCity.")]
        public CityDefinition definition;

        [SerializeField, HideInInspector] Vector2Int selected;

        CityBaker.ValidationReport lastReport;

        [MenuItem("Tools/Police Escape/City Designer")]
        static void Open() => GetWindow<CityDesignerWindow>("City Designer");

        protected override void OnEnable()
        {
            base.OnEnable();
            if (definition == null) AutoLoadDefinition();
        }

        void AutoLoadDefinition()
        {
            definition = AssetDatabase.LoadAssetAtPath<CityDefinition>(DefinitionPath);
            if (definition != null) return;
            // Fall back to any definition in the project before creating one.
            foreach (string guid in AssetDatabase.FindAssets("t:CityDefinition"))
            {
                definition = AssetDatabase.LoadAssetAtPath<CityDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null) return;
            }
            var generation = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(SettingsPath);
            if (generation != null) definition = CityBaker.EnsureDefinition(DefinitionPath, generation);
        }

        // ---------------------------------------------------------------- grid

        [OnInspectorGUI, PropertyOrder(5)]
        void DrawGrid()
        {
            if (definition == null)
            {
                EditorGUILayout.HelpBox("Assign (or create) a CityDefinition to start designing.", MessageType.Info);
                return;
            }

            if (definition.blocks.Count != definition.gridWidth * definition.gridHeight)
            {
                definition.EnsureEntries();
                EditorUtility.SetDirty(definition);
            }
            selected = new Vector2Int(
                Mathf.Clamp(selected.x, 0, definition.gridWidth - 1),
                Mathf.Clamp(selected.y, 0, definition.gridHeight - 1));

            GUILayout.Space(8);
            EditorGUILayout.LabelField($"City grid — {definition.gridWidth}×{definition.gridHeight} blocks of {definition.blockSizeInCells} cells ({definition.blockSizeInCells * definition.generation?.cellSize ?? 0:0} m). North is up; edges wrap pacman-style.", EditorStyles.miniLabel);

            Color previous = GUI.backgroundColor;
            // Row gridHeight-1 first: north (+Z, higher y) belongs at the top.
            for (int y = definition.gridHeight - 1; y >= 0; y--)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = 0; x < definition.gridWidth; x++)
                {
                    var coord = new Vector2Int(x, y);
                    CityDefinition.BlockEntry entry = definition.GetOrCreateEntry(coord);
                    DistrictDefinition district = definition.DistrictFor(coord);

                    // District tint is the lowest layer; the override/bridge/error colours keep their precedence.
                    Color color = district != null ? district.mapColor : new Color(0.55f, 0.55f, 0.55f);
                    if (entry.settingsOverride != null) color = new Color(0.45f, 0.65f, 1f);
                    if (entry.connectorOnly) color = new Color(1f, 0.6f, 0.2f);
                    if (lastReport != null && lastReport.BadBlocks.Contains(coord)) color = new Color(1f, 0.3f, 0.3f);
                    if (coord == selected) color = Color.Lerp(color, Color.white, 0.45f);
                    GUI.backgroundColor = color;

                    string content = entry.settingsOverride != null ? entry.settingsOverride.name
                        : district != null ? DistrictLabel(district)
                        : "default";
                    string label = entry.connectorOnly
                        ? (entry.connectorAxis == CityDefinition.BridgeAxis.EastWest ? $"{x},{y}\n═ bridge ═" : $"{x},{y}\n║ bridge ║")
                        : $"{x},{y}\n{content}";
                    if (GUILayout.Button(label, GUILayout.Height(40), GUILayout.MinWidth(64)))
                    {
                        selected = coord;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = previous;
            DrawDistrictLegend();

            if (lastReport != null)
            {
                MessageType type = !lastReport.Ok ? MessageType.Error
                    : lastReport.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(lastReport.ToString(), type);
            }
            GUILayout.Space(4);
        }

        static string DistrictLabel(DistrictDefinition district) =>
            string.IsNullOrEmpty(district.displayName) ? district.name : district.displayName;

        /// <summary>One colour swatch per district the seeded map can produce, so the grid tints are readable at a glance.</summary>
        void DrawDistrictLegend()
        {
            var seen = new System.Collections.Generic.List<DistrictDefinition>();
            void Add(DistrictDefinition district)
            {
                if (district != null && !seen.Contains(district)) seen.Add(district);
            }
            Add(definition.downtownDistrict);
            Add(definition.innerRingDistrict);
            foreach (CityDefinition.WeightedDistrict entry in definition.outerDistricts) Add(entry.district);
            Add(definition.defaultDistrict);
            foreach (CityDefinition.BlockEntry entry in definition.blocks) Add(entry?.districtOverride);
            if (seen.Count == 0) return;

            Color previous = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Districts:", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            foreach (DistrictDefinition district in seen)
            {
                GUI.backgroundColor = district.mapColor;
                GUILayout.Label(DistrictLabel(district), EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            }
            GUI.backgroundColor = previous;
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------ selected block

        [TitleGroup("Selected block"), PropertyOrder(10)]
        [ShowInInspector, InlineProperty, HideLabel, EnableIf(nameof(HasDefinition))]
        CityDefinition.BlockEntry Selected
        {
            get => definition != null && definition.InGrid(selected) ? definition.GetOrCreateEntry(selected) : null;
            set { } // edits go through the reference; setter exists so Odin draws it editable
        }

        bool HasDefinition => definition != null;

        [ButtonGroup("Selected block/actions"), PropertyOrder(11), Button("Rebuild (Keep Seed)"), EnableIf(nameof(HasDefinition))]
        void RebuildSelectedKeepSeed() => RebuildSelected(false);

        [ButtonGroup("Selected block/actions"), PropertyOrder(12), Button("Rebuild (New Seed)"), EnableIf(nameof(HasDefinition))]
        void RebuildSelectedNewSeed() => RebuildSelected(true);

        [ButtonGroup("Selected block/actions"), PropertyOrder(13), Button("Rebuild + Neighbours"), EnableIf(nameof(HasDefinition))]
        [Tooltip("Rebuild this block AND its four (wrapped) neighbours. Needed after edits that move border roads — a district override changing the secondary-arterial tier, or connectorOnly — because the neighbours' baked sockets were computed against the old answer.")]
        void RebuildSelectedWithNeighbours()
        {
            SaveDefinitionEdits();
            lastReport = CityBaker.Validate(definition);
            if (lastReport.Ok) CityBaker.RebuildBlockAndNeighbours(definition, selected);
            Repaint();
        }

        void RebuildSelected(bool newSeed)
        {
            SaveDefinitionEdits();
            lastReport = CityBaker.Validate(definition);
            if (lastReport.Ok) CityBaker.RebuildBlock(definition, selected, newSeed);
            Repaint();
        }

        // ----------------------------------------------------------- city-wide

        [TitleGroup("City"), PropertyOrder(20)]
        [Button(ButtonSizes.Medium), EnableIf(nameof(HasDefinition))]
        [Tooltip("Check the connectivity rules without baking: arterial spacing vs block size, bridge feasibility, wrap-seam bands.")]
        void Validate()
        {
            SaveDefinitionEdits();
            lastReport = CityBaker.Validate(definition);
            Debug.Log($"CityDesigner: {lastReport}", definition);
            Repaint();
        }

        [TitleGroup("City"), PropertyOrder(21)]
        [Button("Bake City", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f), EnableIf(nameof(HasDefinition))]
        [Tooltip("Bake every block into the city prefab, in place: sockets (AdditionalItems / DefaultVehicles) and their contents survive; only Block_* children are replaced. Seeds are kept.")]
        void BakeCity()
        {
            SaveDefinitionEdits();
            GameObject prefab = CityBaker.BakeCity(definition);
            lastReport = CityBaker.Validate(definition);
            if (prefab != null) EditorGUIUtility.PingObject(prefab);
            Repaint();
        }

        [TitleGroup("City"), PropertyOrder(21)]
        [Button("Save As New City…", ButtonSizes.Medium), EnableIf(nameof(HasDefinition))]
        [Tooltip("Bake the current definition into a NEW prefab of your choosing, leaving the main city prefab untouched — keep several city variants side by side. Hand-placed socket content is copied into the variant. The window keeps targeting the main prefab afterwards; swap the scene's city instance to play a variant.")]
        void SaveAsNewCity()
        {
            SaveDefinitionEdits();
            string path = EditorUtility.SaveFilePanelInProject(
                "Save As New City", "City_Variant", "prefab",
                "Choose where to save the new city prefab.", "Assets/03.Prefabs/PoliceEscape");
            if (string.IsNullOrEmpty(path)) return;

            GameObject prefab = CityBaker.SaveAsNewCity(definition, path);
            lastReport = CityBaker.Validate(definition);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"CityDesigner: saved city variant to {path}. The scene still uses {CityBaker.DefaultPrefabPath} — swap its city instance to play this one.", prefab);
            }
            Repaint();
        }

        [TitleGroup("City"), PropertyOrder(22)]
        [Button("Rebuild City (New Seeds)", ButtonSizes.Medium), GUIColor(1f, 0.7f, 0.4f), EnableIf(nameof(HasDefinition))]
        [Tooltip("Roll a brand-new city seed (and derived block seeds), then bake. The one destructive path — every road moves.")]
        void RebuildCityNewSeeds()
        {
            if (!EditorUtility.DisplayDialog("Rebuild city?",
                "This rolls a NEW city seed: every road, block and bridge line moves. Sockets and their contents survive, but hand-placed items may end up off-road.\n\nRebuild?",
                "Rebuild", "Cancel"))
                return;
            definition.RerollAllSeeds();
            EditorUtility.SetDirty(definition);
            SaveDefinitionEdits();
            GameObject prefab = CityBaker.BakeCity(definition);
            lastReport = CityBaker.Validate(definition);
            if (prefab != null) EditorGUIUtility.PingObject(prefab);
            Repaint();
        }

        [TitleGroup("City"), PropertyOrder(22)]
        [Button("Clear Redundant Block Overrides", ButtonSizes.Medium), EnableIf(nameof(HasDefinition))]
        [Tooltip("Null the settingsOverride reference on every block whose override asset is a value-identical clone of another block's — the mass-produced copies that mask the district settings. Hand-tuned outliers are kept, and the asset files stay on disk.")]
        void ClearRedundantOverrides()
        {
            var overridden = new System.Collections.Generic.List<CityDefinition.BlockEntry>();
            foreach (CityDefinition.BlockEntry entry in definition.blocks)
                if (entry?.settingsOverride != null) overridden.Add(entry);

            var cleared = new System.Collections.Generic.List<string>();
            foreach (CityDefinition.BlockEntry entry in overridden)
            {
                bool isClone = false;
                foreach (CityDefinition.BlockEntry other in overridden)
                {
                    if (other == entry || other.settingsOverride == null) continue;
                    if (BlockSettingsValueEqual(entry.settingsOverride, other.settingsOverride)) { isClone = true; break; }
                }
                if (!isClone) continue;
                cleared.Add($"({entry.coord.x},{entry.coord.y}) '{entry.settingsOverride.name}'");
                entry.settingsOverride = null;
            }

            if (cleared.Count > 0)
            {
                EditorUtility.SetDirty(definition);
                SaveDefinitionEdits();
            }
            Debug.Log(cleared.Count > 0
                ? $"CityDesigner: cleared {cleared.Count} clone override(s) — districts now drive those blocks. Assets left on disk.\n{string.Join("\n", cleared)}"
                : "CityDesigner: no value-identical clone overrides found — nothing cleared.", definition);
            Repaint();
        }

        static bool BlockSettingsValueEqual(BlockSettings a, BlockSettings b) =>
            Mathf.Approximately(a.connectorDensity, b.connectorDensity)
            && Mathf.Approximately(a.turnProbability, b.turnProbability)
            && a.allowDeadEnds == b.allowDeadEnds
            && a.placeFeatures == b.placeFeatures
            && Mathf.Approximately(a.overpassChance, b.overpassChance)
            && Mathf.Approximately(a.forkChance, b.forkChance)
            && a.buildingSet == b.buildingSet
            && a.decorationSet == b.decorationSet
            && Mathf.Approximately(a.buildingDensityMultiplier, b.buildingDensityMultiplier)
            && Mathf.Approximately(a.decorationDensityMultiplier, b.decorationDensityMultiplier);

        [TitleGroup("City"), PropertyOrder(23)]
        [Button("Ping City Prefab", ButtonSizes.Small)]
        void PingPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CityBaker.DefaultPrefabPath);
            if (prefab != null) EditorGUIUtility.PingObject(prefab);
            else Debug.Log("CityDesigner: no city prefab yet — press Bake City.");
        }

        /// <summary>Flush pending inspector edits to disk at the window's commit points (the project's deferred write-back rule).</summary>
        void SaveDefinitionEdits()
        {
            if (definition == null) return;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
        }
    }
}
