using System.Collections.Generic;
using System.IO;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Population;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds BuildingSets from the Cyberpunk Megapolis "Background" buildings
    /// (Assets/Cyberpunk_Megapolis/Models/Background/Buildings). Unlike the
    /// Kenney kit, whose models are authored in CELL units (1 unit = one road
    /// tile), these are REAL METRES with the pivot at the base: skyscrapers
    /// 24–42 m wide and 45–110 m tall, slums 10–52 m wide. So the sets carry
    /// <c>nativeCellSize = cellSize</c> (the populator's scale comes out at
    /// 1, the models keep their metres) and footprints are measured in SUB-LOTS
    /// of half a cell (<see cref="BuildingSet.lotSubdivision"/> 2, ~18 m) — a
    /// model may overhang a lot by 15% before it claims the next one — with
    /// <see cref="BuildingSet.lotFill"/> scaling each building per axis to
    /// fill what it got; that pairing is what packs four shacks into a cell
    /// and towers wall to wall instead of one model per 37 m lot. The pack's own
    /// prefabs are preferred over the raw FBX where they exist: they add a
    /// BoxCollider (so the populator skips its per-mesh colliders — cheaper)
    /// and a single-LOD LODGroup that culls the model at ~3% screen height.
    /// Pivots are measured too: a model whose bounds centre is off its pivot
    /// (the hospital, slums 07) gets a <see cref="BuildingDefinition.pivotToCenter"/>
    /// so it lands centred on its lot. Three flavours from one folder — All
    /// (mixed), Skyline (skyscraper-heavy) and Slums (low-rise, no towers) —
    /// plus menu items that point the district block settings at them (and
    /// back at the Kenney sets), which is what makes them show up after a
    /// city bake; the test-scene item only changes the city-wide fallback set.
    /// </summary>
    public static class CyberpunkBuildingSetBuilder
    {
        const string ModelsFolder = "Assets/Cyberpunk_Megapolis/Models/Background/Buildings";
        const string PrefabsFolder = "Assets/Cyberpunk_Megapolis/Prefabs/Background";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string DistrictsFolder = SettingsFolder + "/Districts";
        const string AllSetPath = SettingsFolder + "/CyberpunkBuildingSet.asset";
        const string SkylineSetPath = SettingsFolder + "/CyberpunkBuildingSet_Skyline.asset";
        const string SlumsSetPath = SettingsFolder + "/CyberpunkBuildingSet_Slums.asset";
        const string TestSettingsPath = SettingsFolder + "/CityTestSettings.asset";
        const string DefinitionPath = SettingsFolder + "/CityDefinition.asset";

        // How much of a lot a model may overhang before it claims the next one.
        const float FootprintTolerance = 0.15f;
        // Sub-lots per cell side: half cells, so 10-20 m shacks pack four to a cell.
        const int LotSubdivision = 2;
        const float LotFill = 0.9f;
        const float MaxStretch = 1.75f;

        enum Kind { Skyscraper, Slum, Special }

        [MenuItem("Tools/Police Escape/Create Cyberpunk Building Set")]
        public static void CreateSet()
        {
            float cellSize = CellSize();
            float lotSize = cellSize / LotSubdivision;
            List<GameObject> models = LoadModels();
            if (models.Count == 0)
            {
                Debug.LogError($"CyberpunkBuildingSetBuilder: no CP_*.fbx found in {ModelsFolder}.");
                return;
            }

            var all = new List<BuildingDefinition>();
            var skyline = new List<BuildingDefinition>();
            var slums = new List<BuildingDefinition>();
            var report = new System.Text.StringBuilder();
            foreach (GameObject model in models)
            {
                Kind kind = KindOf(model.name);
                Measure(model, out Vector2 size, out float heightMeters, out Vector2 pivotToCenter);
                Vector2Int footprint = Footprint(size, lotSize);
                report.AppendLine($"  {model.name,-28} {size.x,5:0.0} x {size.y,5:0.0} m, {heightMeters,5:0.0} m tall → {footprint.x}x{footprint.y} lots of {lotSize:0.#} m" +
                                  (pivotToCenter.sqrMagnitude > 1f ? $", pivot off by ({pivotToCenter.x:0.0}, {pivotToCenter.y:0.0})" : ""));

                // Mixed: towers lead, slums fill, the oddities are rare.
                all.Add(Make(model, footprint, size, pivotToCenter,
                    weight: kind switch { Kind.Skyscraper => 1.5f, Kind.Slum => 1f, _ => 0.3f },
                    heightJitter: kind == Kind.Skyscraper ? 0.15f : 0.05f));

                // Skyline: nearly all towers, a slum or two at their feet.
                skyline.Add(Make(model, footprint, size, pivotToCenter,
                    weight: kind switch { Kind.Skyscraper => 3f, Kind.Slum => 0.3f, _ => 0.25f },
                    heightJitter: kind == Kind.Skyscraper ? 0.2f : 0.05f));

                // Slums: no towers at all.
                if (kind != Kind.Skyscraper)
                    slums.Add(Make(model, footprint, size, pivotToCenter,
                        weight: kind == Kind.Slum ? 2f : 0.5f, heightJitter: 0.05f));
            }

            EnsureFolder(SettingsFolder);
            SaveSet(AllSetPath, all, cellSize, density: 0.95f);
            SaveSet(SkylineSetPath, skyline, cellSize, density: 1f);
            SaveSet(SlumsSetPath, slums, cellSize, density: 0.9f);
            AssetDatabase.SaveAssets();

            Debug.Log($"CyberpunkBuildingSetBuilder: built All ({all.Count}), Skyline ({skyline.Count}) and Slums ({slums.Count}) sets at real scale (cell {cellSize:0.#} m, {LotSubdivision}x{LotSubdivision} lots per cell, fill {LotFill:0.##}):\n{report}" +
                      "Use 'Districts Use Cyberpunk Buildings' then Bake City to see them; 'Districts Use Kenney Buildings' puts the old sets back.");
        }

        // ------------------------------------------------------------ wiring

        [MenuItem("Tools/Police Escape/Districts Use Cyberpunk Buildings")]
        public static void DistrictsUseCyberpunk() => WireDistricts(
            downtown: SkylineSetPath, midrise: AllSetPath, suburb: SlumsSetPath, beachfront: AllSetPath, label: "Cyberpunk");

        [MenuItem("Tools/Police Escape/Districts Use Kenney Buildings")]
        public static void DistrictsUseKenney() => WireDistricts(
            downtown: SettingsFolder + "/KenneyBuildingSet_Downtown.asset",
            midrise: SettingsFolder + "/KenneyBuildingSet.asset",
            suburb: SettingsFolder + "/KenneyBuildingSet_Suburb.asset",
            beachfront: SettingsFolder + "/KenneyBuildingSet.asset", label: "Kenney");

        [MenuItem("Tools/Police Escape/Test Scene Uses Cyberpunk Buildings")]
        public static void TestSceneUsesCyberpunk()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(AllSetPath);
            if (settings == null || set == null)
            {
                Debug.LogError($"CyberpunkBuildingSetBuilder: missing asset — settings: {settings != null}, set: {set != null}. Run 'Create Cyberpunk Building Set' first.");
                return;
            }
            settings.buildingSet = set;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("CyberpunkBuildingSetBuilder: the city-wide fallback set is now Cyberpunk — blocks with a district still use the district's set (see 'Districts Use Cyberpunk Buildings').");
        }

        /// <summary>Point the four built-up districts' block settings at a set each (Park has no buildings). Missing assets are reported and skipped.</summary>
        static void WireDistricts(string downtown, string midrise, string suburb, string beachfront, string label)
        {
            int wired = 0;
            wired += Wire("Downtown", downtown);
            wired += Wire("Midrise", midrise);
            wired += Wire("Suburb", suburb);
            wired += Wire("Beachfront", beachfront);
            AssetDatabase.SaveAssets();
            Debug.Log($"CyberpunkBuildingSetBuilder: {wired} district(s) now build from the {label} sets — Bake City (City Designer) to see them, then re-bake occlusion.");
        }

        static int Wire(string district, string setPath)
        {
            var blocks = AssetDatabase.LoadAssetAtPath<BlockSettings>($"{DistrictsFolder}/BlockSettings_{district}.asset");
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(setPath);
            if (blocks == null || set == null)
            {
                Debug.LogWarning($"CyberpunkBuildingSetBuilder: skipped {district} — block settings: {blocks != null}, set '{setPath}': {set != null} (run 'Create District Assets' / the set builder first).");
                return 0;
            }
            blocks.buildingSet = set;
            EditorUtility.SetDirty(blocks);
            return 1;
        }

        // --------------------------------------------------------- measuring

        /// <summary>The cell size the city bakes with (definition's generation asset, else the test settings, else 36.9).</summary>
        static float CellSize()
        {
            var definition = AssetDatabase.LoadAssetAtPath<CityDefinition>(DefinitionPath);
            CityGenerationSettings settings = definition != null && definition.generation != null
                ? definition.generation
                : AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            return settings != null && settings.cellSize > 0.1f ? settings.cellSize : 36.9f;
        }

        /// <summary>Every CP_*.fbx in the folder, swapped for the pack's prefab of the same name when one exists (collider + LOD group).</summary>
        static List<GameObject> LoadModels()
        {
            var models = new List<GameObject>();
            if (!Directory.Exists(ModelsFolder)) return models;
            foreach (string file in Directory.GetFiles(ModelsFolder, "CP_*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/{name}.prefab");
                if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                if (prefab != null) models.Add(prefab);
            }
            models.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return models;
        }

        static Kind KindOf(string name)
        {
            if (name.Contains("Skyscraper")) return Kind.Skyscraper;
            if (name.Contains("Slums_") && char.IsDigit(name[name.Length - 1])) return Kind.Slum; // CP_Slums_01..11
            return Kind.Special; // hangar, hospital, antenna, football field, greenhouse
        }

        /// <summary>Renderer bounds of the asset: XZ size, height, and where the bounds centre sits relative to the pivot.</summary>
        static void Measure(GameObject prefab, out Vector2 size, out float height, out Vector2 pivotToCenter)
        {
            size = Vector2.zero;
            height = 0f;
            pivotToCenter = Vector2.zero;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Vector3 pivot = prefab.transform.position;
            size = new Vector2(bounds.size.x, bounds.size.z);
            height = bounds.size.y;
            pivotToCenter = new Vector2(bounds.center.x - pivot.x, bounds.center.z - pivot.z);
            if (pivotToCenter.sqrMagnitude < 1f) pivotToCenter = Vector2.zero; // sub-metre wobble is not worth an offset
        }

        static Vector2Int Footprint(Vector2 sizeMeters, float lotSize) => new(
            Mathf.Max(1, Mathf.CeilToInt(sizeMeters.x / lotSize - FootprintTolerance)),
            Mathf.Max(1, Mathf.CeilToInt(sizeMeters.y / lotSize - FootprintTolerance)));

        static BuildingDefinition Make(GameObject prefab, Vector2Int footprint, Vector2 nativeSize, Vector2 pivotToCenter, float weight, float heightJitter) => new()
        {
            prefab = prefab,
            weight = weight,
            footprintInCells = footprint,
            allowRotation = true,
            pivotToCenter = pivotToCenter,
            nativeSize = nativeSize,
            positionJitter = 0.03f,
            scaleJitter = 0.05f,
            heightJitter = heightJitter,
            minSpacing = 0,
        };

        static void SaveSet(string path, List<BuildingDefinition> definitions, float nativeCell, float density)
        {
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(path);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<BuildingSet>();
            set.buildings = definitions;
            set.nativeCellSize = nativeCell; // metres per cell = the cell itself: scale 1
            set.density = density;
            set.lotSubdivision = LotSubdivision;
            set.lotFill = LotFill;
            set.maxStretch = MaxStretch;
            set.heightFitShare = 1f;
            if (isNew) AssetDatabase.CreateAsset(set, path);
            else EditorUtility.SetDirty(set);
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
