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

                    Color color = new(0.55f, 0.55f, 0.55f);
                    if (entry.settingsOverride != null) color = new Color(0.45f, 0.65f, 1f);
                    if (entry.connectorOnly) color = new Color(1f, 0.6f, 0.2f);
                    if (lastReport != null && lastReport.BadBlocks.Contains(coord)) color = new Color(1f, 0.3f, 0.3f);
                    if (coord == selected) color = Color.Lerp(color, Color.white, 0.45f);
                    GUI.backgroundColor = color;

                    string label = entry.connectorOnly
                        ? (entry.connectorAxis == CityDefinition.BridgeAxis.EastWest ? $"{x},{y}\n═ bridge ═" : $"{x},{y}\n║ bridge ║")
                        : $"{x},{y}\n{(entry.settingsOverride != null ? entry.settingsOverride.name : "default")}";
                    if (GUILayout.Button(label, GUILayout.Height(40), GUILayout.MinWidth(64)))
                    {
                        selected = coord;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = previous;

            if (lastReport != null)
            {
                MessageType type = !lastReport.Ok ? MessageType.Error
                    : lastReport.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(lastReport.ToString(), type);
            }
            GUILayout.Space(4);
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
