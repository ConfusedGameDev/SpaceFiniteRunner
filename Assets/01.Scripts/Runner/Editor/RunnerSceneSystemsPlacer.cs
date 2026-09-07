using ConfusedGameDev.FiniteRunner.Audio;
using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.HUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Place Scene Systems: puts the runner's
    /// hand-placed scene-lifetime systems into the OPEN scene — the
    /// <see cref="CollectibleManager"/> (the one pickup recorder), the
    /// <see cref="MoneyHud"/> (the top-right money counter) and the
    /// <see cref="RunnerMusic"/> soundtrack (as <c>Music</c>, wired to the
    /// FiniteRunner_Music asset, created on the spot when missing). A new
    /// object goes under the scene's <c>===SYSTEMS===</c> header when it has
    /// one, at the root otherwise. The project rule: systems are hand-placed
    /// so they can be tuned before play, nothing creates one at play time, and
    /// the runtime only finds them (with an error when missing). Idempotent —
    /// a scene that already has one (even disabled) is left alone. The city's
    /// counterpart is Tools → Police Escape → Place Scene Systems, which
    /// places the collectible pair under its own header.
    /// </summary>
    public static class RunnerSceneSystemsPlacer
    {
        const string SystemsHeaderName = "===SYSTEMS===";

        [MenuItem("Tools/FiniteRunner/Place Scene Systems")]
        public static void PlaceInOpenScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("RunnerSceneSystemsPlacer: place the systems in edit mode.");
                return;
            }

            int placed = 0;
            placed += Place<CollectibleManager>("CollectibleManager");
            placed += Place<MoneyHud>("MoneyHud");
            placed += Place<RunnerMusic>("Music", music => music.settings = MusicAssetBuilder.CreateOrLoad());
            Debug.Log($"RunnerSceneSystemsPlacer: {placed} object(s) placed in '{EditorSceneManager.GetActiveScene().name}' — save the scene to keep them.");
        }

        static int Place<T>(string name, System.Action<T> configure = null) where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null) return 0;
            var go = new GameObject(name);
            Transform header = FindSystemsHeader();
            if (header != null) go.transform.SetParent(header, false);
            T component = go.AddComponent<T>();
            configure?.Invoke(component);
            Undo.RegisterCreatedObjectUndo(go, $"Place {name}");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return 1;
        }

        /// <summary>The open scene's root <c>===SYSTEMS===</c> header, null when it has none.</summary>
        static Transform FindSystemsHeader()
        {
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == SystemsHeaderName) return root.transform;
            return null;
        }
    }
}
