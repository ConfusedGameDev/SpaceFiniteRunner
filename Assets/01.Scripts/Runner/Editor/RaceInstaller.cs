using System.IO;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Race;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Install Race Mode: sets up race levels
    /// (RaceModePRD.md) in one idempotent pass — the starter
    /// <see cref="RaceDefinition"/> (Race_Default: five rivals with spread
    /// personalities), the <c>PF_RaceSystem</c> prefab, an instance of it inside
    /// <c>PF_Systems</c> wired to that prefab's <see cref="GameManager"/> (so
    /// every runner scene built on PF_Systems has it hand-placed under
    /// ===SYSTEMS===), and a test level (FiniteRunner_LevelDefinition_Race)
    /// that references it. Never overwrites an existing asset and never
    /// touches a level other than the one it creates.
    /// </summary>
    public static class RaceInstaller
    {
        const string DataFolder = "Assets/04.Data/FiniteRunner/Race";
        const string DefinitionPath = DataFolder + "/Race_Default.asset";
        const string LevelPath = "Assets/04.Data/FiniteRunner/FiniteRunner_LevelDefinition_Race.asset";
        const string PrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_RaceSystem.prefab";
        const string SystemsPath = "Assets/03.Prefabs/FiniteRunner/PF_Systems.prefab";
        const string ModelFolder = "Assets/Cyberpunk_Megapolis/Prefabs/Car/";

        // A fast clean pilot, a reckless one, a slow one and two in between
        // (D9), each in its own model and paint — never CP_Air_Traffic_Car_01,
        // which is the patrol's. Every model is built along X (yaw -90) and
        // scaled to the ship's ~19 m length; the half sizes are the scaled
        // mesh bounds.
        static readonly (string name, string model, float scale, float halfLength, float halfWidth, Color color,
                         float pace, float thrust, float aggression, float mistakes, float appetite)[] Starters =
        {
            ("Vega",  "CP_Quadron",             3.6f,  9.5f, 4.2f, new Color(1f, 0.2f, 0.25f), 0.82f, 1.06f, 0.15f, 0.01f, 0.7f),
            ("Kade",  "CP_Taxi",                3.8f,  9.5f, 4.5f, new Color(1f, 0.6f, 0.1f),  0.78f, 1.02f, 0.85f, 0.06f, 0.9f),
            ("Mirai", "CP_Air_Traffic_Car_02",  2.55f, 9.6f, 3.0f, new Color(0.3f, 1f, 0.45f), 0.74f, 0.98f, 0.35f, 0.03f, 0.5f),
            ("Orso",  "CP_Minivan",             4.0f,  9.5f, 4.4f, new Color(0.75f, 0.4f, 1f), 0.70f, 0.95f, 0.5f,  0.04f, 0.4f),
            ("Dune",  "CP_Air_Traffic_Minivan", 3.9f,  9.4f, 4.3f, new Color(1f, 0.95f, 0.3f), 0.64f, 0.90f, 0.25f, 0.02f, 0.3f),
        };

        const float PaintStrength = 0.65f;

        // Bounty and ram hull per starter (same order): the faster pilot is worth more, the van takes a hit more.
        static readonly (int bounty, int ramHull)[] StarterStakes = { (400, 3), (300, 3), (250, 3), (200, 4), (150, 3) };

        [MenuItem("Tools/FiniteRunner/Install Race Mode")]
        public static void Install()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("RaceInstaller: install in edit mode.");
                return;
            }

            RaceDefinition definition = CreateOrLoadDefinition();
            GameObject prefab = CreateOrLoadPrefab();
            bool placed = PlaceInSystems(prefab);
            RunnerLevelDefinition level = CreateOrLoadLevel(definition);
            AssetDatabase.SaveAssets();
            Debug.Log($"RaceInstaller: definition '{definition.name}', prefab '{prefab.name}', PF_Systems {(placed ? "updated" : "already wired")}, " +
                      $"test level '{level.name}'. Set a RunnerLevelDefinition's Race field (or point the GameManager at the test level) to race.", definition);
        }

        static RaceDefinition CreateOrLoadDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RaceDefinition>(DefinitionPath);
            if (definition != null) return definition;

            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(DataFolder).Replace('\\', '/'), Path.GetFileName(DataFolder));

            definition = ScriptableObject.CreateInstance<RaceDefinition>();
            FillStarters(definition);
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            return definition;
        }

        /// <summary>
        /// Tools → FiniteRunner → Race: Reset Starter Rivals — rewrites
        /// Race_Default's roster to the starters (models, paint, pace, thrust,
        /// personality). The rest of the definition is kept.
        /// </summary>
        [MenuItem("Tools/FiniteRunner/Race: Reset Starter Rivals")]
        public static void ResetStarterRivals()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RaceDefinition>(DefinitionPath);
            if (definition == null)
            {
                Debug.LogWarning($"RaceInstaller: no '{DefinitionPath}' — run Install Race Mode first.");
                return;
            }
            Undo.RecordObject(definition, "Reset Starter Rivals");
            FillStarters(definition);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
            Debug.Log($"RaceInstaller: '{definition.name}' roster reset to the {Starters.Length} starters.", definition);
        }

        static void FillStarters(RaceDefinition definition)
        {
            definition.rivals.Clear();
            for (int i = 0; i < Starters.Length; i++)
            {
                var s = Starters[i];
                var stakes = StarterStakes[i];
                string path = ModelFolder + s.model + ".prefab";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) Debug.LogWarning($"RaceInstaller: '{path}' not found — {s.name} gets a placeholder hull.");
                definition.rivals.Add(new RivalEntry
                {
                    displayName = s.name,
                    prefab = model,
                    scale = model != null ? s.scale : 1f,
                    yawOffset = model != null ? -90f : 0f,
                    halfLength = s.halfLength,
                    halfWidth = s.halfWidth,
                    paintTint = new Color(s.color.r, s.color.g, s.color.b, PaintStrength),
                    mapColor = s.color,
                    cruiseFraction = s.pace,
                    thrustFactor = s.thrust,
                    aggression = s.aggression,
                    mistakeRate = s.mistakes,
                    orbAppetite = s.appetite,
                    bounty = stakes.bounty,
                    ramHull = stakes.ramHull,
                });
            }
        }

        static GameObject CreateOrLoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                // A prefab made before M4 gets its kill cam added in place.
                if (prefab.GetComponent<RivalKillCam>() == null)
                {
                    GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                    try
                    {
                        contents.AddComponent<RivalKillCam>();
                        PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                }
                return prefab;
            }

            var go = new GameObject("PF_RaceSystem");
            go.AddComponent<RaceSystem>();
            go.AddComponent<RivalKillCam>();
            prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Nests the system in PF_Systems (as "RaceSystem") and wires its GameManager. False when both were already done.</summary>
        static bool PlaceInSystems(GameObject prefab)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SystemsPath);
            try
            {
                bool changed = false;
                RaceSystem system = root.GetComponentInChildren<RaceSystem>(true);
                if (system == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    instance.name = "RaceSystem";
                    system = instance.GetComponent<RaceSystem>();
                    changed = true;
                }

                GameManager manager = root.GetComponentInChildren<GameManager>(true);
                if (manager == null)
                {
                    Debug.LogError("RaceInstaller: PF_Systems has no GameManager — wire GameManager.race by hand.");
                }
                else
                {
                    var so = new SerializedObject(manager);
                    SerializedProperty field = so.FindProperty("race");
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

        // A race level to test with: the default race, a 30 km track, no
        // Reach Speed objective (D5), no traffic, and a Top 3 Race Position
        // goal.
        static RunnerLevelDefinition CreateOrLoadLevel(RaceDefinition race)
        {
            var level = AssetDatabase.LoadAssetAtPath<RunnerLevelDefinition>(LevelPath);
            if (level != null) return level;

            level = ScriptableObject.CreateInstance<RunnerLevelDefinition>();
            level.levelName = "Race";
            level.trackLengthMeters = 30000f;
            level.race = race;
            level.objectives.Add(new RunnerObjective
            {
                type = RunnerObjectiveType.RacePosition,
                raceMode = RaceGoalMode.TopX,
                racePosition = 3,
                reward = 500,
            });
            AssetDatabase.CreateAsset(level, LevelPath);
            return level;
        }
    }
}
