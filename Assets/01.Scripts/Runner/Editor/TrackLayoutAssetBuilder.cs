using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Layout;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates the runner's default track layout the way the level asset is
    /// created: create-or-load at a fixed path, never overwriting an authored
    /// one, and wired into the open scene's <see cref="TrackGenerator"/> when
    /// its layout slot is empty. The asset starts EMPTY — GENERATE LAYOUT on
    /// the Track fills it, because the builder needs Unity Splines to run.
    /// </summary>
    public static class TrackLayoutAssetBuilder
    {
        const string DataFolder = "Assets/04.Data/FiniteRunner";
        const string TracksFolder = DataFolder + "/Tracks";
        const string AssetPath = TracksFolder + "/Default_TrackLayout.asset";

        [MenuItem("Tools/FiniteRunner/Create Track Layout")]
        public static void Create()
        {
            var layout = AssetDatabase.LoadAssetAtPath<TrackLayout>(AssetPath);
            bool existed = layout != null;
            if (!existed)
            {
                if (!AssetDatabase.IsValidFolder(DataFolder))
                    AssetDatabase.CreateFolder("Assets/04.Data", "FiniteRunner");
                if (!AssetDatabase.IsValidFolder(TracksFolder))
                    AssetDatabase.CreateFolder(DataFolder, "Tracks");
                layout = ScriptableObject.CreateInstance<TrackLayout>();
                AssetDatabase.CreateAsset(layout, AssetPath);
                AssetDatabase.SaveAssets();
            }

            // Wire it where the scene has a generator with an empty slot —
            // through the serialized property, so the scene is dirtied like a hand edit.
            var generator = Object.FindFirstObjectByType<TrackGenerator>(FindObjectsInactive.Include);
            if (generator != null)
            {
                var so = new SerializedObject(generator);
                var slot = so.FindProperty("layout");
                if (slot != null && slot.objectReferenceValue == null)
                {
                    slot.objectReferenceValue = layout;
                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
                }
            }

            Selection.activeObject = layout;
            Debug.Log(existed ? $"Track layout already exists: {AssetPath}" : $"Track layout created: {AssetPath} — select the Track and click GENERATE LAYOUT.", layout);
        }
    }
}
