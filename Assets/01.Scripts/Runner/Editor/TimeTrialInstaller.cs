using System.IO;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.TimeTrial;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Install Time Trials: sets up time trial levels
    /// (TimeTrialPRD.md) in one idempotent pass — the two starter
    /// <see cref="TimeTrialDefinition"/>s (TimeTrial_BeatTheClock,
    /// TimeTrial_Arcade), the <c>PF_TimeTrialSystem</c> prefab, an instance of
    /// it inside <c>PF_Systems</c> wired to that prefab's
    /// <see cref="GameManager"/> (so every runner scene built on PF_Systems has
    /// it hand-placed under ===SYSTEMS===), and two test levels that reference
    /// them. Never overwrites an existing asset and never touches a level other
    /// than the ones it creates.
    /// </summary>
    public static class TimeTrialInstaller
    {
        const string DataFolder = "Assets/04.Data/FiniteRunner/TimeTrial";
        const string BeatTheClockPath = DataFolder + "/TimeTrial_BeatTheClock.asset";
        const string ArcadePath = DataFolder + "/TimeTrial_Arcade.asset";
        const string BeatTheClockLevelPath = "Assets/04.Data/FiniteRunner/FiniteRunner_LevelDefinition_BeatTheClock.asset";
        const string ArcadeLevelPath = "Assets/04.Data/FiniteRunner/FiniteRunner_LevelDefinition_Arcade.asset";
        const string PrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_TimeTrialSystem.prefab";
        const string SystemsPath = "Assets/03.Prefabs/FiniteRunner/PF_Systems.prefab";

        [MenuItem("Tools/FiniteRunner/Install Time Trials")]
        public static void Install()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("TimeTrialInstaller: install in edit mode.");
                return;
            }

            TimeTrialDefinition beatTheClock = CreateOrLoadDefinition(BeatTheClockPath, TimeTrialMode.BeatTheClock);
            TimeTrialDefinition arcade = CreateOrLoadDefinition(ArcadePath, TimeTrialMode.Arcade);
            GameObject prefab = CreateOrLoadPrefab();
            bool placed = PlaceInSystems(prefab);
            RunnerLevelDefinition btcLevel = CreateOrLoadLevel(BeatTheClockLevelPath, "Beat the Clock", beatTheClock, 25000f);
            RunnerLevelDefinition arcadeLevel = CreateOrLoadLevel(ArcadeLevelPath, "Arcade Time Trial", arcade, 35000f);
            AssetDatabase.SaveAssets();
            Debug.Log($"TimeTrialInstaller: definitions '{beatTheClock.name}' + '{arcade.name}', prefab '{prefab.name}', " +
                      $"PF_Systems {(placed ? "updated" : "already wired")}, test levels '{btcLevel.name}' + '{arcadeLevel.name}'. " +
                      "Set a RunnerLevelDefinition's Time Trial field (or point the GameManager at a test level) to play one.", arcade);
        }

        static TimeTrialDefinition CreateOrLoadDefinition(string path, TimeTrialMode mode)
        {
            var definition = AssetDatabase.LoadAssetAtPath<TimeTrialDefinition>(path);
            if (definition != null) return definition;

            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(DataFolder).Replace('\\', '/'), Path.GetFileName(DataFolder));

            definition = ScriptableObject.CreateInstance<TimeTrialDefinition>();
            definition.mode = mode;
            // Beat the Clock is a pure time attack by default; Arcade needs the
            // patrol for its kill bonus (TimeTrialPRD D5).
            definition.patrolEnabled = mode == TimeTrialMode.Arcade;
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        static GameObject CreateOrLoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null) return prefab;

            var go = new GameObject("PF_TimeTrialSystem");
            go.AddComponent<TimeTrialSystem>();
            prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Nests the system in PF_Systems (as "TimeTrialSystem") and wires its GameManager. False when both were already done.</summary>
        static bool PlaceInSystems(GameObject prefab)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SystemsPath);
            try
            {
                bool changed = false;
                TimeTrialSystem system = root.GetComponentInChildren<TimeTrialSystem>(true);
                if (system == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    instance.name = "TimeTrialSystem";
                    system = instance.GetComponent<TimeTrialSystem>();
                    changed = true;
                }

                GameManager manager = root.GetComponentInChildren<GameManager>(true);
                if (manager == null)
                {
                    Debug.LogError("TimeTrialInstaller: PF_Systems has no GameManager — wire GameManager.timeTrial by hand.");
                }
                else
                {
                    var so = new SerializedObject(manager);
                    SerializedProperty field = so.FindProperty("timeTrial");
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

        // A time trial level to test with: the trial, a generated track of the
        // given length, no Reach Speed objective (D3), no traffic, a Finish In
        // Time goal and a "finish with time to spare" challenge.
        static RunnerLevelDefinition CreateOrLoadLevel(string path, string levelName, TimeTrialDefinition trial, float length)
        {
            var level = AssetDatabase.LoadAssetAtPath<RunnerLevelDefinition>(path);
            if (level != null) return level;

            level = ScriptableObject.CreateInstance<RunnerLevelDefinition>();
            level.levelName = levelName;
            level.trackLengthMeters = length;
            level.timeTrial = trial;
            level.objectives.Add(new RunnerObjective { type = RunnerObjectiveType.FinishWithTime, reward = 500 });
            level.optionalChallenges.Add(new RunnerOptionalChallenge
            {
                type = RunnerObjectiveType.FinishWithTime,
                minSecondsLeft = trial.IsArcade ? 10f : 20f,
                multiplier = 2,
            });
            AssetDatabase.CreateAsset(level, path);
            return level;
        }
    }
}
