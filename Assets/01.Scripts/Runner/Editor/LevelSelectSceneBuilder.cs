using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Create Level Select Scene</c>: the SELECT
    /// COURSE scene from code — a camera clearing to the menu backdrop, the
    /// hand-placed <see cref="LevelSelectScreen"/>, the menu soundtrack under
    /// <c>===SYSTEMS===</c> and the shared EventSystem / Haptics / Cheat
    /// prefabs. Syncs the track catalog first so the screen has courses on
    /// its first play. An existing scene is opened rather than rebuilt (the
    /// <c>StoreSceneBuilder</c> rule). Registering it in the build list is
    /// <c>Register Campaign Scenes</c>' job, which this runs too.
    /// </summary>
    public static class LevelSelectSceneBuilder
    {
        const string ScenesFolder = "Assets/05.Scenes";
        const string ScenePath = ScenesFolder + "/" + LevelSelectScreen.SceneName + ".unity";

        static readonly string[] SharedPrefabs =
        {
            "Assets/03.Prefabs/Shared/EventSystem.prefab",
            "Assets/03.Prefabs/Shared/HapticsSystem.prefab",
            "Assets/03.Prefabs/Shared/CheatManager.prefab",
        };

        [MenuItem("Tools/FiniteRunner/Create Level Select Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(ScenesFolder);
            TrackCatalogSync.Sync();

            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log($"Level Select scene already exists — opened it: {ScenePath}. Delete the file to rebuild it from scratch.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                Color backdrop = MenuTheme.Load().Backdrop;
                camera.backgroundColor = new Color(backdrop.r, backdrop.g, backdrop.b, 1f);
            }

            var go = new GameObject("LevelSelectScreen", typeof(RectTransform)) { layer = 5 };
            var screen = go.AddComponent<LevelSelectScreen>();

            foreach (string path in SharedPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) PrefabUtility.InstantiatePrefab(prefab);
                else Debug.LogWarning($"Level Select scene: shared prefab missing, skipped: {path}");
            }

            // The menu loop under ===SYSTEMS===, like the main menu's.
            RunnerSceneSystemsPlacer.PlaceInMainMenuScene();

            EditorSceneManager.SaveScene(scene, ScenePath);
            CampaignSceneRegistrar.Register();
            Selection.activeGameObject = screen.gameObject;
            Debug.Log($"Level Select scene created: {ScenePath}. Select LevelSelectScreen and press Rebuild Preview to see it in edit mode.");
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
