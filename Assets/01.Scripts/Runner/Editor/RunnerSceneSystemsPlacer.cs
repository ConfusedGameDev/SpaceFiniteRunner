using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.HUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Place Scene Systems: puts the runner's
    /// hand-placed scene-lifetime systems that need no wiring into the OPEN
    /// scene as root objects — the <see cref="CollectibleManager"/> (the one
    /// pickup recorder) and the <see cref="MoneyHud"/> (the top-right money
    /// counter). The project rule: systems are hand-placed so they can be
    /// tuned before play, nothing creates one at play time, and the
    /// runtime only finds them (with an error when missing). Idempotent —
    /// a scene that already has one (even disabled) is left alone. The
    /// city's counterpart is Tools → Police Escape → Place Scene Systems,
    /// which places the same two under its <c>===SYSTEMS===</c> header.
    /// </summary>
    public static class RunnerSceneSystemsPlacer
    {
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
            Debug.Log($"RunnerSceneSystemsPlacer: {placed} object(s) placed in '{EditorSceneManager.GetActiveScene().name}' — save the scene to keep them.");
        }

        static int Place<T>(string name) where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null) return 0;
            var go = new GameObject(name);
            go.AddComponent<T>();
            Undo.RegisterCreatedObjectUndo(go, $"Place {name}");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return 1;
        }
    }
}
