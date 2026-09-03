using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates the runner's level asset the way the city's scene builder
    /// creates its <c>LevelDefinition</c>: create-or-load at a fixed path,
    /// seeded with the default run ONLY when freshly created, so an authored
    /// list is never stomped by re-running it. It also wires the asset into
    /// the open scene's <see cref="GameManager"/> when its slot is empty.
    /// </summary>
    public static class RunnerLevelAssetBuilder
    {
        const string DataFolder = "Assets/04.Data/FiniteRunner";
        const string AssetPath = DataFolder + "/FiniteRunner_LevelDefinition.asset";

        [MenuItem("Tools/FiniteRunner/Create Runner Level Definition")]
        public static void Create()
        {
            var level = AssetDatabase.LoadAssetAtPath<RunnerLevelDefinition>(AssetPath);
            bool existed = level != null;
            if (!existed)
            {
                if (!AssetDatabase.IsValidFolder(DataFolder))
                    AssetDatabase.CreateFolder("Assets/04.Data", "FiniteRunner");
                level = ScriptableObject.CreateInstance<RunnerLevelDefinition>();
                RunnerLevelDefinition.SeedDefaultObjectives(level);
                AssetDatabase.CreateAsset(level, AssetPath);
                AssetDatabase.SaveAssets();
            }

            // Wire it where the scene has a manager with an empty slot — through
            // the serialized property, so the scene is dirtied like a hand edit.
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                var so = new SerializedObject(manager);
                var slot = so.FindProperty("level");
                if (slot != null && slot.objectReferenceValue == null)
                {
                    slot.objectReferenceValue = level;
                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                }
            }

            Selection.activeObject = level;
            Debug.Log(existed ? $"Runner level definition already exists: {AssetPath}" : $"Runner level definition created: {AssetPath}", level);
        }
    }
}
