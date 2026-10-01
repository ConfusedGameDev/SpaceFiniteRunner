using System.IO;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Traffic;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Install Oncoming Traffic: sets up the runner's
    /// oncoming traffic (OncomingTrafficPRD.md) in one idempotent pass —
    /// the starter <see cref="TrafficDefinition"/> (Traffic_Default, the
    /// Cyberpunk air-traffic cars minus the one the patrol cruiser wears), the
    /// <c>PF_TrafficSystem</c> prefab, and an instance of it inside
    /// <c>PF_Systems</c> wired to that prefab's <see cref="GameManager"/>, so
    /// every runner scene built on PF_Systems has the system hand-placed
    /// under ===SYSTEMS===. A level turns traffic on by referencing a
    /// definition in <c>RunnerLevelDefinition.traffic</c>; this tool never
    /// touches a level.
    /// </summary>
    public static class TrafficInstaller
    {
        const string DataFolder = "Assets/04.Data/FiniteRunner/Traffic";
        const string DefinitionPath = DataFolder + "/Traffic_Default.asset";
        const string PrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_TrafficSystem.prefab";
        const string SystemsPath = "Assets/03.Prefabs/FiniteRunner/PF_Systems.prefab";
        const string CarFolder = "Assets/Cyberpunk_Megapolis/Prefabs/Car/";

        // CP_Air_Traffic_Car_01 is the patrol cruiser's model: civilians stay off it.
        static readonly (string prefab, float weight)[] StarterCars =
        {
            ("CP_Air_Traffic_Car_02", 3f),
            ("CP_Air_Traffic_Minivan", 2f),
            ("CP_Air_Traffic_Minibus", 1.5f),
            ("CP_Air_Traffic_Truck", 1f),
            ("CP_Air_Traffic_Bus", 0.75f),
            ("CP_Air_Traffic_Garbage_Truck", 0.75f),
        };

        [MenuItem("Tools/FiniteRunner/Install Oncoming Traffic")]
        public static void Install()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("TrafficInstaller: install in edit mode.");
                return;
            }

            TrafficDefinition definition = CreateOrLoadDefinition();
            GameObject prefab = CreateOrLoadPrefab();
            bool placed = PlaceInSystems(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"TrafficInstaller: definition '{definition.name}', prefab '{prefab.name}', PF_Systems {(placed ? "updated" : "already wired")}. " +
                      "Set a RunnerLevelDefinition's Traffic field to turn traffic on for that level.", definition);
        }

        static TrafficDefinition CreateOrLoadDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<TrafficDefinition>(DefinitionPath);
            if (definition != null) return definition;

            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(DataFolder).Replace('\\', '/'), Path.GetFileName(DataFolder));

            definition = ScriptableObject.CreateInstance<TrafficDefinition>();
            foreach (var (name, weight) in StarterCars)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(CarFolder + name + ".prefab");
                if (model == null)
                {
                    Debug.LogWarning($"TrafficInstaller: '{CarFolder}{name}.prefab' not found — skipped.");
                    continue;
                }
                definition.vehicles.Add(new TrafficVehicle { prefab = model, weight = weight });
            }
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            return definition;
        }

        static GameObject CreateOrLoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null) return prefab;

            var go = new GameObject("PF_TrafficSystem");
            go.AddComponent<TrafficSystem>();
            prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Nests the system in PF_Systems (as "TrafficSystem") and wires its GameManager. False when both were already done.</summary>
        static bool PlaceInSystems(GameObject prefab)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SystemsPath);
            try
            {
                bool changed = false;
                TrafficSystem system = root.GetComponentInChildren<TrafficSystem>(true);
                if (system == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    instance.name = "TrafficSystem";
                    system = instance.GetComponent<TrafficSystem>();
                    changed = true;
                }

                GameManager manager = root.GetComponentInChildren<GameManager>(true);
                if (manager == null)
                {
                    Debug.LogError("TrafficInstaller: PF_Systems has no GameManager — wire GameManager.traffic by hand.");
                }
                else
                {
                    var so = new SerializedObject(manager);
                    SerializedProperty field = so.FindProperty("traffic");
                    if (field.objectReferenceValue != system)
                    {
                        field.objectReferenceValue = system;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(root, SystemsPath);
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
