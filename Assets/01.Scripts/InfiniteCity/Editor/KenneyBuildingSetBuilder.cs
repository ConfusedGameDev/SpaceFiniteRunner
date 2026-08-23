using System.Collections.Generic;
using System.IO;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Population;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds a BuildingSet from the Kenney building FBXs: every building-*
    /// model is measured (renderer bounds) and its footprint in cells derived
    /// from the road piece's footprint, so the set survives asset swaps without
    /// hand-typed numbers. Skyscrapers get a low weight so they stay landmarks.
    /// Companion menu items flip the test scene between this set and the
    /// primitive box set — Repopulate on the CityManager shows the difference
    /// without touching the road layout.
    /// </summary>
    public static class KenneyBuildingSetBuilder
    {
        const string BuildingsFolder = "Assets/02.Art/01.Models/InfiniteCity/Buildings";
        const string RoadReferencePath = "Assets/02.Art/01.Models/InfiniteCity/Roads/road-straight.fbx";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string SetPath = SettingsFolder + "/KenneyBuildingSet.asset";
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

            var definitions = new List<BuildingDefinition>();
            foreach (string path in Directory.GetFiles(BuildingsFolder, "building-*.fbx"))
            {
                string assetPath = path.Replace('\\', '/');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null) continue;

                Vector2 size = MeasureFootprint(prefab);
                bool skyscraper = prefab.name.Contains("skyscraper");
                // Slight tolerance so a 0.98-unit model still counts as one cell.
                var footprint = new Vector2Int(
                    Mathf.Max(1, Mathf.CeilToInt(size.x / nativeCell - 0.1f)),
                    Mathf.Max(1, Mathf.CeilToInt(size.y / nativeCell - 0.1f)));

                definitions.Add(new BuildingDefinition
                {
                    prefab = prefab,
                    weight = skyscraper ? 0.4f : 2f,
                    footprintInCells = footprint,
                    allowRotation = true,
                    positionJitter = 0.02f,
                    scaleJitter = 0.05f,
                    heightJitter = skyscraper ? 0.25f : 0.1f,
                });
                Debug.Log($"KenneyBuildingSetBuilder: {prefab.name} = {size.x:0.##} × {size.y:0.##} m → footprint {footprint.x}×{footprint.y}");
            }

            if (definitions.Count == 0)
            {
                Debug.LogError($"KenneyBuildingSetBuilder: no building-*.fbx found in {BuildingsFolder}.");
                return;
            }

            EnsureFolder(SettingsFolder);
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(SetPath);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<BuildingSet>();
            set.buildings = definitions;
            set.nativeCellSize = nativeCell;
            set.density = 0.95f;
            if (isNew) AssetDatabase.CreateAsset(set, SetPath);
            else EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            Debug.Log($"KenneyBuildingSetBuilder: {definitions.Count} buildings → {SetPath} (native cell {nativeCell:0.##} m). " +
                      "Use 'Test Scene Uses Kenney Buildings' to try it, then Repopulate on the CityManager.");
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
