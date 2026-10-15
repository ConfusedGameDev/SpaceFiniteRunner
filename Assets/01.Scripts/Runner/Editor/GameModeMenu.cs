using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Set Game Mode: points the open scene's
    /// <see cref="GameManager"/> at one of the four runner game types' levels —
    /// Standard (the escape run), Race, Beat the Clock or Arcade — so Play runs
    /// that mode. Each level carries its own saved track (Track_Standard,
    /// Track_Race, Track_BeatTheClock, Track_Arcade). Only the scene's level
    /// field changes (an undoable scene edit); the levels themselves are
    /// never written. The mode in use is ticked.
    /// </summary>
    public static class GameModeMenu
    {
        const string Root = "Tools/FiniteRunner/Set Game Mode/";
        const string Folder = "Assets/04.Data/FiniteRunner/Levels/";
        const string StandardLevel = Folder + "FiniteRunner_LevelDefinition.asset";
        const string RaceLevel = Folder + "FiniteRunner_LevelDefinition_Race.asset";
        const string BeatTheClockLevel = Folder + "FiniteRunner_LevelDefinition_BeatTheClock.asset";
        const string ArcadeLevel = Folder + "FiniteRunner_LevelDefinition_Arcade.asset";

        [MenuItem(Root + "Standard")] static void Standard() => Set(StandardLevel);
        [MenuItem(Root + "Race")] static void Race() => Set(RaceLevel);
        [MenuItem(Root + "Beat the Clock")] static void BeatTheClock() => Set(BeatTheClockLevel);
        [MenuItem(Root + "Arcade")] static void Arcade() => Set(ArcadeLevel);

        [MenuItem(Root + "Standard", true)] static bool StandardCheck() => Check(Root + "Standard", StandardLevel);
        [MenuItem(Root + "Race", true)] static bool RaceCheck() => Check(Root + "Race", RaceLevel);
        [MenuItem(Root + "Beat the Clock", true)] static bool BeatTheClockCheck() => Check(Root + "Beat the Clock", BeatTheClockLevel);
        [MenuItem(Root + "Arcade", true)] static bool ArcadeCheck() => Check(Root + "Arcade", ArcadeLevel);

        static void Set(string levelPath)
        {
            var level = AssetDatabase.LoadAssetAtPath<RunnerLevelDefinition>(levelPath);
            if (level == null)
            {
                Debug.LogWarning($"[GameMode] no level at '{levelPath}'. Run Tools → FiniteRunner → Install Time Trials for the time trial levels.");
                return;
            }
            GameManager game = Object.FindFirstObjectByType<GameManager>();
            if (game == null)
            {
                Debug.LogWarning("[GameMode] no GameManager in the open scene. Open a runner scene (FiniteRunner_Test) first.");
                return;
            }

            var serialized = new SerializedObject(game);
            serialized.FindProperty("level").objectReferenceValue = level;
            serialized.ApplyModifiedProperties(); // undoable, and marks the scene dirty
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);

            string track = level.track != null ? level.track.name : "a generated track";
            Debug.Log($"[GameMode] '{game.gameObject.scene.name}' now plays '{level.name}' ({level.levelName}) on {track}. Save the scene to keep it.", level);
        }

        // Ticks the mode in use; every mode is offered whenever a GameManager is open in edit mode.
        static bool Check(string item, string levelPath)
        {
            GameManager game = Object.FindFirstObjectByType<GameManager>();
            bool current = game != null && game.Level != null && AssetDatabase.GetAssetPath(game.Level) == levelPath;
            Menu.SetChecked(item, current);
            return game != null && !Application.isPlaying;
        }
    }
}
