using System.IO;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Create Campaign Assets</c>: seeds the
    /// campaign's data from code, idempotently (create-or-load, an authored
    /// asset is never stomped — the <c>StoreSceneBuilder</c> rule):
    /// <list type="bullet">
    /// <item>the <see cref="CampaignCatalog"/> in Resources,</item>
    /// <item><c>World_01</c> on the <c>CarTest</c> scene,</item>
    /// <item><c>Mission_01</c> — the existing pair, <c>TestLevelDefinition</c> +
    /// <c>FiniteRunner_LevelDefinition</c>, no requirements,</item>
    /// <item><c>Mission_02</c> — a new small city level (destroy three cars,
    /// then shake the police) and its OWN runner level with a higher Light
    /// Speed, so a session that failed to inject the right assets is
    /// visible at once. It auto-unlocks on Mission 1; a requirement is one
    /// inspector edit away.</item>
    /// </list>
    /// Lives in the city's editor assembly because it is the one that sees
    /// both level types. Run <c>Register Campaign Scenes</c> after it.
    /// </summary>
    public static class CampaignAssetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources/Campaign";
        const string CatalogPath = ResourcesFolder + "/CampaignCatalog.asset";
        const string DataFolder = "Assets/04.Data/Campaign";
        const string WorldPath = DataFolder + "/World_01.asset";
        const string Mission1Path = DataFolder + "/Mission_01.asset";
        const string Mission2Path = DataFolder + "/Mission_02.asset";
        const string CityLevel2Path = DataFolder + "/Level_02_City.asset";
        const string RunnerLevel2Path = DataFolder + "/FiniteRunner_Level_02.asset";

        const string CityLevel1Path = "Assets/04.Data/InfiniteCity/TestLevelDefinition.asset";
        const string RunnerLevel1Path = "Assets/04.Data/FiniteRunner/FiniteRunner_LevelDefinition.asset";
        const string WorldScene = "CarTest";

        [MenuItem("Tools/FiniteRunner/Create Campaign Assets")]
        public static void Create()
        {
            EnsureFolder(ResourcesFolder);
            EnsureFolder(DataFolder);

            // Mission 1: the pair that already exists.
            var cityLevel1 = AssetDatabase.LoadAssetAtPath<PoliceEscape.LevelDefinition>(CityLevel1Path);
            var runnerLevel1 = AssetDatabase.LoadAssetAtPath<RunnerLevelDefinition>(RunnerLevel1Path);
            if (cityLevel1 == null) Debug.LogWarning($"Campaign: no city level at {CityLevel1Path} — Mission 1's city slot is left empty (run the CarTest scene builder).");
            if (runnerLevel1 == null) Debug.LogWarning($"Campaign: no runner level at {RunnerLevel1Path} — Mission 1's runner slot is left empty (run Create Runner Level Definition).");

            // Mission 2: its own levels, seeded only when fresh.
            var cityLevel2 = CreateOrLoad<PoliceEscape.LevelDefinition>(CityLevel2Path, out bool freshCity2);
            if (freshCity2)
            {
                cityLevel2.levelName = "Level 2";
                cityLevel2.baseReward = 1500;
                cityLevel2.objectives.Add(new PoliceEscape.LevelObjective
                {
                    type = PoliceEscape.ObjectiveType.DestroyCars,
                    destroyCount = 3,
                    reward = 600,
                    briefing = "They know your face now. Make a mess — total {0} cars and they will send everyone.",
                });
                cityLevel2.objectives.Add(new PoliceEscape.LevelObjective
                {
                    type = PoliceEscape.ObjectiveType.EscapePolice,
                    mustBeHuntedFirst = true,
                    reward = 900,
                    accent = PoliceEscape.LevelObjective.DefaultAccent(PoliceEscape.ObjectiveType.EscapePolice),
                    briefing = "Now lose them. All of them.",
                });
                EditorUtility.SetDirty(cityLevel2);
            }

            var runnerLevel2 = CreateOrLoad<RunnerLevelDefinition>(RunnerLevel2Path, out bool freshRunner2);
            if (freshRunner2)
            {
                runnerLevel2.levelName = "Escape Run 2";
                runnerLevel2.nextSceneName = "Store";
                runnerLevel2.objectives.Add(new RunnerObjective { type = RunnerObjectiveType.ReachSpeed, targetSpeedKmh = 7500f, reward = 750 });
                EditorUtility.SetDirty(runnerLevel2);
            }

            var mission1 = CreateOrLoad<MissionDefinition>(Mission1Path, out bool freshMission1);
            if (freshMission1)
            {
                mission1.id = "m1_downtown";
                mission1.displayName = "FIRST RUN";
                mission1.cityLevel = cityLevel1;
                mission1.runnerLevel = runnerLevel1;
                EditorUtility.SetDirty(mission1);
            }

            var mission2 = CreateOrLoad<MissionDefinition>(Mission2Path, out bool freshMission2);
            if (freshMission2)
            {
                mission2.id = "m2_downtown";
                mission2.displayName = "HOT PURSUIT";
                mission2.cityLevel = cityLevel2;
                mission2.runnerLevel = runnerLevel2;
                EditorUtility.SetDirty(mission2);
            }

            var world = CreateOrLoad<WorldDefinition>(WorldPath, out bool freshWorld);
            if (freshWorld)
            {
                world.displayName = "DOWNTOWN";
                world.sceneName = WorldScene;
                world.missions.Add(mission1);
                world.missions.Add(mission2);
                EditorUtility.SetDirty(world);
            }

            var catalog = CreateOrLoad<CampaignCatalog>(CatalogPath, out bool freshCatalog);
            if (freshCatalog || catalog.worlds.Count == 0)
            {
                catalog.worlds.Add(world);
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = catalog;
            Debug.Log($"Campaign assets ready: {CatalogPath} → {world.name} [{mission1.name}, {mission2.name}]. " +
                      "Run Tools → FiniteRunner → Register Campaign Scenes and commit the build settings.", catalog);
        }

        static T CreateOrLoad<T>(string path, out bool created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
