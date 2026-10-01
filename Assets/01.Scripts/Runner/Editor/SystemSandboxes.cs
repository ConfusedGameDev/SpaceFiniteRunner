using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// The independence proof (refactor Step 11): one sandbox scene per
    /// system, holding that system's prefab and nothing else but a camera and
    /// a light, and a smoke test that plays every sandbox for a few seconds and
    /// reports any error or exception. "It must be possible to drop any single
    /// system into a new level on its own without anything breaking" — a
    /// system alone must boot and idle, hide or fall back, never throw or log
    /// an error. Composition roots (the GameManager, the LevelManager) and the
    /// runner's ship adapter are deliberately absent: wiring the others is
    /// their job, so alone they have nothing to do. Tools → Refactor → Build
    /// System Sandboxes writes the scenes to <c>05.Scenes/Sandboxes/</c>;
    /// Tools → Refactor → Run Sandbox Smoke Test plays them one by one and
    /// writes <c>Temp/SandboxSmoke.txt</c>.
    /// </summary>
    public static class SystemSandboxes
    {
        const string SceneFolder = "Assets/05.Scenes/Sandboxes";
        const string ReportPath = "Temp/SandboxSmoke.txt";
        const float PlaySeconds = 4f;

        /// <summary>(sandbox name, the one prefab it holds).</summary>
        public static readonly (string name, string prefab)[] Systems =
        {
            // runner
            ("Track", "Assets/03.Prefabs/FiniteRunner/PF_Track.prefab"),
            ("PolicePatrol", "Assets/03.Prefabs/Runner/PolicePatrol.prefab"),
            ("RaceHud", "Assets/03.Prefabs/FiniteRunner/PF_RaceHUD.prefab"),
            ("ChaseMinimap", "Assets/03.Prefabs/Runner/ChaseMinimap.prefab"),
            ("DashPrompt", "Assets/03.Prefabs/Runner/DashPrompt.prefab"),
            ("HyperspacePrompt", "Assets/03.Prefabs/FiniteRunner/PF_HyperspacePrompt.prefab"),
            ("LaserSystem", "Assets/03.Prefabs/Runner/PF_LaserSystem.prefab"),
            ("OrbFx", "Assets/03.Prefabs/FiniteRunner/PF_OrbFx.prefab"),
            ("Music", "Assets/03.Prefabs/FiniteRunner/PF_Music.prefab"),
            ("PauseMenu", "Assets/03.Prefabs/Runner/PauseMenu.prefab"),
            ("OncomingTraffic", "Assets/03.Prefabs/FiniteRunner/PF_TrafficSystem.prefab"),
            // the standalone ship
            ("HoverShip", "Assets/03.Prefabs/Runner/HoverShip.prefab"),
            // shared
            ("DistanceFog", "Assets/03.Prefabs/FiniteRunner/PF_DistanceFog.prefab"),
            ("SpeedLines", "Assets/03.Prefabs/FiniteRunner/PF_SpeedLines.prefab"),
            ("HyperspaceSky", "Assets/03.Prefabs/FiniteRunner/PF_HyperspaceSky.prefab"),
            ("VhsTape", "Assets/03.Prefabs/FiniteRunner/PF_VhsTape.prefab"),
            ("PsxLook", "Assets/03.Prefabs/FiniteRunner/PF_PsxLook.prefab"),
            ("CrtScreen", "Assets/03.Prefabs/FiniteRunner/PF_CrtScreen.prefab"),
            ("GlitchController", "Assets/03.Prefabs/FiniteRunner/PF_GlitchController.prefab"),
            ("CameraController", "Assets/03.Prefabs/FiniteRunner/PF_CameraController.prefab"),
            ("CollectibleManager", "Assets/03.Prefabs/FiniteRunner/PF_CollectibleManager.prefab"),
            ("MoneyHud", "Assets/03.Prefabs/FiniteRunner/PF_MoneyHud.prefab"),
            ("FloatingText", "Assets/03.Prefabs/Runner/FloatingTextSystem.prefab"),
            ("RpgMessages", "Assets/03.Prefabs/Runner/RpgMessageSystem.prefab"),
            ("Haptics", "Assets/03.Prefabs/Shared/HapticsSystem.prefab"),
            ("CheatManager", "Assets/03.Prefabs/Shared/CheatManager.prefab"),
            // city
            ("PatrolManager", "Assets/03.Prefabs/PoliceEscape/PatrolManager.prefab"),
            ("TrafficManager", "Assets/03.Prefabs/PoliceEscape/TrafficManager.prefab"),
            ("Minimap", "Assets/03.Prefabs/PoliceEscape/Minimap.prefab"),
            ("Speedometer", "Assets/03.Prefabs/PoliceEscape/Speedometer.prefab"),
            ("CityMap", "Assets/03.Prefabs/PoliceEscape/CityMap.prefab"),
            ("ObjectiveHud", "Assets/03.Prefabs/PoliceEscape/ObjectiveHud.prefab"),
            ("PlayerCar", "Assets/03.Prefabs/PoliceEscape/PlayerCar.prefab"),
            ("PoliceCar", "Assets/03.Prefabs/PoliceEscape/TestPoliceCar.prefab"),
            ("ExplosiveBarrel", "Assets/03.Prefabs/PoliceEscape/ExplosiveBarrel.prefab"),
        };

        static string ScenePath(string name) => $"{SceneFolder}/Sandbox_{name}.unity";

        // Only a scene with unsaved changes needs the save prompt (and a
        // scripted run must not stop on a dialog nobody will answer).
        static bool AnySceneDirty()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }

        [MenuItem("Tools/Refactor/Build System Sandboxes")]
        public static void Build()
        {
            if (Application.isPlaying) { Debug.LogWarning("SystemSandboxes: build in edit mode."); return; }
            if (AnySceneDirty() && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(SceneFolder);
            int built = 0;
            foreach (var (name, prefabPath) in Systems)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) { Debug.LogWarning($"SystemSandboxes: no prefab at {prefabPath} — {name} skipped."); continue; }
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                PrefabUtility.InstantiatePrefab(prefab, scene);
                EditorSceneManager.SaveScene(scene, ScenePath(name));
                built++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"SystemSandboxes: {built} sandbox scene(s) written to {SceneFolder}.");
        }

        // ------------------------------------------------------------ smoke test
        // Domain reload is off on entering play mode, so this state survives the
        // play/edit transitions the run steps through.

        static readonly List<string> queue = new();
        static readonly StringBuilder report = new();
        static readonly List<string> currentErrors = new();
        static int index = -1;
        static double playStartedAt;
        static bool running, stopping;
        static int failed;

        /// <summary>True while the smoke test is stepping through the sandboxes.</summary>
        public static bool Running => running;

        [MenuItem("Tools/Refactor/Run Sandbox Smoke Test")]
        public static void RunSmokeTest()
        {
            if (running || Application.isPlaying) { Debug.LogWarning("SystemSandboxes: already running, or in play mode."); return; }
            if (AnySceneDirty() && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            queue.Clear();
            foreach (var (name, _) in Systems)
                if (File.Exists(ScenePath(name))) queue.Add(ScenePath(name));
            if (queue.Count == 0) { Debug.LogWarning("SystemSandboxes: no sandbox scenes — run Build System Sandboxes first."); return; }
            report.Clear();
            report.AppendLine($"Sandbox smoke test — {queue.Count} systems, {PlaySeconds:0} s each.");
            index = -1;
            failed = 0;
            running = true;
            stopping = false;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Next();
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (!running || !Application.isPlaying) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                currentErrors.Add(message.Split('\n')[0]);
        }

        static void Next()
        {
            index++;
            if (index >= queue.Count) { Finish(); return; }
            currentErrors.Clear();
            EditorSceneManager.OpenScene(queue[index], OpenSceneMode.Single);
            playStartedAt = -1;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!running) return;
            if (Application.isPlaying)
            {
                if (playStartedAt < 0) playStartedAt = EditorApplication.timeSinceStartup;
                if (!stopping && EditorApplication.timeSinceStartup - playStartedAt >= PlaySeconds)
                {
                    stopping = true;
                    EditorApplication.isPlaying = false;
                }
                return;
            }
            if (!stopping || EditorApplication.isPlayingOrWillChangePlaymode) return;
            stopping = false;
            string name = Path.GetFileNameWithoutExtension(queue[index]).Replace("Sandbox_", "");
            if (currentErrors.Count == 0) report.AppendLine($"  PASS  {name}");
            else
            {
                failed++;
                report.AppendLine($"  FAIL  {name} ({currentErrors.Count} error(s))");
                foreach (string e in currentErrors.GetRange(0, Mathf.Min(5, currentErrors.Count)))
                    report.AppendLine($"          {e}");
            }
            Next();
        }

        static void Finish()
        {
            running = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            report.AppendLine($"{queue.Count - failed} passed, {failed} failed.");
            File.WriteAllText(ReportPath, report.ToString());
            if (failed == 0) Debug.Log(report.ToString());
            else Debug.LogWarning(report.ToString());
        }
    }
}
