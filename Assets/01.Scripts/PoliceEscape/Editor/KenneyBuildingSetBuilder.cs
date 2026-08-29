using System.Collections.Generic;
using System.IO;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Population;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds the district BuildingSets from the Kenney building FBXs: every
    /// model is measured (renderer bounds) and its footprint in cells derived
    /// from the road piece's footprint, so the sets survive asset swaps without
    /// hand-typed numbers. Three flavours from one kit: Midrise (the classic
    /// mixed set, skyscrapers as rare landmarks), Downtown (skyscraper-heavy,
    /// packed tight) and Suburb (low-detail silhouettes, spaced out and
    /// jittered). Companion menu items flip the test scene between the midrise
    /// set and the primitive box set — Repopulate on the CityManager shows the
    /// difference without touching the road layout.
    /// </summary>
    public static class KenneyBuildingSetBuilder
    {
        const string BuildingsFolder = "Assets/02.Art/01.Models/InfiniteCity/Buildings";
        const string RoadReferencePath = "Assets/02.Art/01.Models/InfiniteCity/Roads/road-straight.fbx";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string SetPath = SettingsFolder + "/KenneyBuildingSet.asset";
        const string DowntownSetPath = SettingsFolder + "/KenneyBuildingSet_Downtown.asset";
        const string SuburbSetPath = SettingsFolder + "/KenneyBuildingSet_Suburb.asset";
        const string TestSettingsPath = "Assets/04.Data/InfiniteCity/CityTestSettings.asset";
        const string TestBoxSetPath = "Assets/04.Data/InfiniteCity/CityTestBuildingSet.asset";

        [MenuItem("Tools/Police Escape/Create Kenney Building Set")]
        public static void CreateSet()
        {
            // The kit's cell unit comes from the road tile, not a hardcoded guess.
            float nativeCell = MeasureFootprint(AssetDatabase.LoadAssetAtPath<GameObject>(RoadReferencePath)).x;
            if (nativeCell <= 0.0001f)
            {
                Debug.LogError($"KenneyBuildingSetBuilder: could not measure {RoadReferencePath} — aborting.");
                return;
            }

            List<GameObject> detailed = LoadModels("building-*.fbx");
            List<GameObject> lowDetail = LoadModels("low-detail-building-*.fbx");
            if (detailed.Count == 0)
            {
                Debug.LogError($"KenneyBuildingSetBuilder: no building-*.fbx found in {BuildingsFolder}.");
                return;
            }

            EnsureFolder(SettingsFolder);

            // Midrise — the classic mixed set, rebuilt in place at the original path.
            var midrise = new List<BuildingDefinition>();
            foreach (GameObject prefab in detailed)
            {
                bool skyscraper = prefab.name.Contains("skyscraper");
                midrise.Add(MakeDefinition(prefab, nativeCell,
                    weight: skyscraper ? 0.4f : 2f, positionJitter: 0.02f,
                    heightJitter: skyscraper ? 0.25f : 0.1f, minSpacing: 0));
            }
            SaveSet(SetPath, midrise, nativeCell, density: 0.95f);

            // Downtown — skyscraper-heavy, packed tight, tallest skyline swing.
            var downtown = new List<BuildingDefinition>();
            foreach (GameObject prefab in detailed)
            {
                bool skyscraper = prefab.name.Contains("skyscraper");
                downtown.Add(MakeDefinition(prefab, nativeCell,
                    weight: skyscraper ? 3f : 0.6f, positionJitter: 0.02f,
                    heightJitter: 0.3f, minSpacing: 0));
            }
            SaveSet(DowntownSetPath, downtown, nativeCell, density: 1f);

            // Suburb — low-detail silhouettes lead, no skyscrapers, spaced and jittered.
            var suburb = new List<BuildingDefinition>();
            foreach (GameObject prefab in lowDetail)
                suburb.Add(MakeDefinition(prefab, nativeCell,
                    weight: 2f, positionJitter: 0.15f, heightJitter: 0.1f, minSpacing: 1));
            foreach (GameObject prefab in detailed)
            {
                if (prefab.name.Contains("skyscraper")) continue;
                suburb.Add(MakeDefinition(prefab, nativeCell,
                    weight: 1f, positionJitter: 0.15f, heightJitter: 0.1f, minSpacing: 1));
            }
            SaveSet(SuburbSetPath, suburb, nativeCell, density: 0.7f);

            AssetDatabase.SaveAssets();
            Debug.Log($"KenneyBuildingSetBuilder: built Midrise ({midrise.Count}), Downtown ({downtown.Count}) and Suburb ({suburb.Count}) sets (native cell {nativeCell:0.##} m). " +
                      (lowDetail.Count == 0 ? "No low-detail-building-*.fbx found — the suburb set has only detailed models. " : "") +
                      "Wire them into the district assets, or use 'Test Scene Uses Kenney Buildings' for the midrise set.");
        }

        static List<GameObject> LoadModels(string pattern)
        {
            var models = new List<GameObject>();
            foreach (string path in Directory.GetFiles(BuildingsFolder, pattern))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                if (prefab != null) models.Add(prefab);
            }
            return models;
        }

        static BuildingDefinition MakeDefinition(GameObject prefab, float nativeCell, float weight, float positionJitter, float heightJitter, int minSpacing)
        {
            Vector2 size = MeasureFootprint(prefab);
            // Slight tolerance so a 0.98-unit model still counts as one cell.
            var footprint = new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(size.x / nativeCell - 0.1f)),
                Mathf.Max(1, Mathf.CeilToInt(size.y / nativeCell - 0.1f)));
            return new BuildingDefinition
            {
                prefab = prefab,
                weight = weight,
                footprintInCells = footprint,
                allowRotation = true,
                positionJitter = positionJitter,
                scaleJitter = 0.05f,
                heightJitter = heightJitter,
                minSpacing = minSpacing,
            };
        }

        static void SaveSet(string path, List<BuildingDefinition> definitions, float nativeCell, float density)
        {
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(path);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<BuildingSet>();
            set.buildings = definitions;
            set.nativeCellSize = nativeCell;
            set.density = density;
            if (isNew) AssetDatabase.CreateAsset(set, path);
            else EditorUtility.SetDirty(set);
        }

        [MenuItem("Tools/Police Escape/Test Scene Uses Kenney Buildings")]
        public static void UseKenneyBuildings() => AssignSet(SetPath);

        [MenuItem("Tools/Police Escape/Test Scene Uses Box Buildings")]
        public static void UseBoxBuildings() => AssignSet(TestBoxSetPath);

        static void AssignSet(string setPath)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(setPath);
            if (settings == null || set == null)
            {
                Debug.LogError($"KenneyBuildingSetBuilder: missing asset — settings: {settings != null}, set '{setPath}': {set != null}. " +
                               "Run 'Create City Test Scene' and 'Create Kenney Building Set' first.");
                return;
            }
            settings.buildingSet = set;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"KenneyBuildingSetBuilder: test scene now populates from '{set.name}' — press Repopulate (or Recalculate) on the CityManager.");
        }

        /// <summary>XZ footprint of a prefab's combined renderer bounds, in its native units.</summary>
        static Vector2 MeasureFootprint(GameObject prefab)
        {
            if (prefab == null) return Vector2.zero;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return Vector2.zero;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return new Vector2(bounds.size.x, bounds.size.z);
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
