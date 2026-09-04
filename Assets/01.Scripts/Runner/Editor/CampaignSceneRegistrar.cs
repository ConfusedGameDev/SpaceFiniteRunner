using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Store;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Register Campaign Scenes</c>: rewrites the
    /// build settings' scene list from the campaign catalog — MainMenu at
    /// index 0 (the one scene ever loaded by index), the Store, every
    /// world's city scene, the runner scene and the Coming Soon scene, in
    /// that order, each found by name under <c>Assets/05.Scenes</c>. Every
    /// other scene trip in the project loads BY NAME, so a scene missing
    /// from this list fails silently at play; run this after adding a world
    /// or a scene and commit <c>ProjectSettings/EditorBuildSettings.asset</c>.
    /// </summary>
    public static class CampaignSceneRegistrar
    {
        const string ScenesFolder = "Assets/05.Scenes";
        const string MainMenuScene = "MainMenu";
        const string CatalogPath = "Assets/04.Data/Resources/" + CampaignCatalog.ResourcePath + ".asset";

        [MenuItem("Tools/FiniteRunner/Register Campaign Scenes")]
        public static void Register()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CampaignCatalog>(CatalogPath);
            if (catalog == null)
            {
                string[] guids = AssetDatabase.FindAssets($"t:{nameof(CampaignCatalog)}");
                if (guids.Length > 0) catalog = AssetDatabase.LoadAssetAtPath<CampaignCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (catalog == null)
                Debug.LogWarning("Register Campaign Scenes: no CampaignCatalog found — run Tools → FiniteRunner → Create Campaign Assets first. Registering the fixed scenes only.");

            var names = new List<string> { MainMenuScene, StoreSettings.SceneName };
            if (catalog != null)
            {
                foreach (WorldDefinition world in catalog.worlds)
                    if (world != null) Add(names, world.sceneName);
                Add(names, catalog.runnerSceneName);
                Add(names, catalog.comingSoonSceneName);
            }
            else
            {
                Add(names, StoreSettings.Load() != null ? StoreSettings.Load().nextMissionScene : "CarTest");
                Add(names, "FiniteRunner_Test");
                Add(names, ComingSoonScreen.SceneName);
            }

            var scenes = new List<EditorBuildSettingsScene>();
            var missing = new List<string>();
            foreach (string name in names)
            {
                string path = FindScenePath(name);
                if (path == null) { missing.Add(name); continue; }
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            string list = string.Join(", ", scenes.ConvertAll(s => Path.GetFileNameWithoutExtension(s.path)));
            if (missing.Count > 0)
                Debug.LogWarning($"Register Campaign Scenes: {scenes.Count} scene(s) registered ({list}); NOT FOUND under {ScenesFolder}: {string.Join(", ", missing)}.");
            else
                Debug.Log($"Register Campaign Scenes: {scenes.Count} scene(s) registered in build order: {list}. Commit ProjectSettings/EditorBuildSettings.asset.");
        }

        static void Add(List<string> names, string name)
        {
            if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
        }

        static string FindScenePath(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:Scene {name}", new[] { ScenesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return path;
            }
            return null;
        }
    }
}
