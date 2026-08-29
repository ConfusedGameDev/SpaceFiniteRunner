using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Bakes the open scene's occlusion map with parameters sized for the
    /// city kit. Occlusion data is a SCENE artifact keyed to the static
    /// renderers that existed at bake time: a city rebake (or a single-block
    /// rebuild) replaces those renderers and orphans the PVS, so this must
    /// run LAST, after the city bake and the static-flag pass, with every
    /// block active (edit mode, where the streamer never runs). The
    /// parameters: the smallest occluder is a building footprint, not a
    /// traffic cone (5 m, the default, explodes the cell count over a 7 km
    /// city); the smallest hole is car-sized, nothing needs to see through a
    /// 25 cm gap; the back-face threshold stays at 100 because the Kenney
    /// boxes are single-sided and lowering it culls through their open backs.
    /// </summary>
    public static class CityOcclusionBaker
    {
        const float SmallestOccluder = 8f;
        const float SmallestHole = 1.5f;
        const float BackfaceThreshold = 100f;

        [MenuItem("Tools/Police Escape/Bake Occlusion Culling")]
        public static void Bake()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("CityOcclusionBaker: save the scene first. Occlusion data is stored next to the scene file.");
                return;
            }
            if (Object.FindAnyObjectByType<CityRoot>() == null)
            {
                Debug.LogError("CityOcclusionBaker: the open scene has no city instance to bake occlusion for.");
                return;
            }
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("CityOcclusionBaker: the scene could not be saved. Bake aborted.");
                return;
            }

            StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
            StaticOcclusionCulling.smallestHole = SmallestHole;
            StaticOcclusionCulling.backfaceThreshold = BackfaceThreshold;

            double started = EditorApplication.timeSinceStartup;
            bool ok = StaticOcclusionCulling.Compute();
            double seconds = EditorApplication.timeSinceStartup - started;
            if (!ok)
            {
                Debug.LogError("CityOcclusionBaker: occlusion bake failed or was cancelled.");
                return;
            }
            Debug.Log($"CityOcclusionBaker: baked {scene.name} in {seconds:0.0} s, {StaticOcclusionCulling.umbraDataSize / 1024f / 1024f:0.0} MB of occlusion data " +
                      $"(occluder {SmallestOccluder} m, hole {SmallestHole} m). Re-run after EVERY city bake or block rebuild: the data refers to bake-time renderers.");
        }

        [MenuItem("Tools/Police Escape/Clear Occlusion Culling")]
        public static void Clear()
        {
            StaticOcclusionCulling.Clear();
            Debug.Log("CityOcclusionBaker: occlusion data cleared for the open scene.");
        }
    }
}
