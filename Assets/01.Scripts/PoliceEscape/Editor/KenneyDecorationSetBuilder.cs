using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds a DecorationSet from the Kenney decorator FBXs with the intended
    /// physics feel baked in: light posts heavy and slow to topple, cones
    /// featherweight so they fly, construction barriers near-immovable. The
    /// native cell unit is measured off the road tile (same rule as the
    /// building set builder), and the set is wired straight into the test
    /// settings — Repopulate on the CityManager shows it without touching the
    /// road layout. Running it again refreshes an existing set in place.
    /// </summary>
    public static class KenneyDecorationSetBuilder
    {
        const string DecoratorsFolder = "Assets/02.Art/01.Models/InfiniteCity/Roads/Decorators";
        const string RoadReferencePath = "Assets/02.Art/01.Models/InfiniteCity/Roads/road-straight.fbx";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string SetPath = SettingsFolder + "/KenneyDecorationSet.asset";
        const string TestSettingsPath = "Assets/04.Data/InfiniteCity/CityTestSettings.asset";

        [MenuItem("Tools/Police Escape/Create Kenney Decoration Set")]
        public static void CreateSet()
        {
            float nativeCell = MeasureFootprint(AssetDatabase.LoadAssetAtPath<GameObject>(RoadReferencePath));
            if (nativeCell <= 0.0001f)
            {
                Debug.LogError($"KenneyDecorationSetBuilder: could not measure {RoadReferencePath} — aborting.");
                return;
            }

            // (file, placement, weight, mass, angularDamping, yawJitter): the physics
            // triplet is the whole design — same impact momentum for everyone,
            // mass decides who flies and who stands firm.
            (string file, DecorationPlacement placement, float weight, float mass, float angularDamping, float yawJitter)[] wanted =
            {
                ("light-square.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f),
                ("light-curved.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f),
                ("construction-cone.fbx", DecorationPlacement.RoadEdge, 6f, 2f, 0.05f, 180f),
                ("construction-barrier.fbx", DecorationPlacement.RoadEdge, 2f, 3000f, 1f, 10f),
                ("construction-light.fbx", DecorationPlacement.RoadEdge, 1f, 250f, 3f, 15f),
            };

            var definitions = new List<DecorationDefinition>();
            foreach (var (file, placement, weight, mass, angularDamping, yawJitter) in wanted)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{DecoratorsFolder}/{file}");
                if (prefab == null)
                {
                    Debug.LogWarning($"KenneyDecorationSetBuilder: '{file}' not found in {DecoratorsFolder} — skipped.");
                    continue;
                }
                definitions.Add(new DecorationDefinition
                {
                    prefab = prefab,
                    placement = placement,
                    weight = weight,
                    mass = mass,
                    angularDamping = angularDamping,
                    yawJitter = yawJitter,
                });
            }

            if (definitions.Count == 0)
            {
                Debug.LogError($"KenneyDecorationSetBuilder: no decorator FBXs found in {DecoratorsFolder}.");
                return;
            }

            EnsureFolder(SettingsFolder);
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(SetPath);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<DecorationSet>();
            set.decorations = definitions;
            set.nativeCellSize = nativeCell;
            if (isNew) AssetDatabase.CreateAsset(set, SetPath);
            else EditorUtility.SetDirty(set);

            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            if (settings != null)
            {
                settings.decorationSet = set;
                EditorUtility.SetDirty(settings);
            }
            else
            {
                Debug.LogWarning($"KenneyDecorationSetBuilder: test settings not found at {TestSettingsPath} — assign the set to your CityGenerationSettings manually.");
            }
            AssetDatabase.SaveAssets();

            Debug.Log($"KenneyDecorationSetBuilder: {definitions.Count} props → {SetPath} (native cell {nativeCell:0.##} m), " +
                      "wired into the test settings. Press Repopulate (or Recalculate) on the CityManager to see them.");
        }

        /// <summary>XZ footprint (largest side) of a prefab's combined renderer bounds, in its native units.</summary>
        static float MeasureFootprint(GameObject prefab)
        {
            if (prefab == null) return 0f;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return Mathf.Max(bounds.size.x, bounds.size.z);
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
