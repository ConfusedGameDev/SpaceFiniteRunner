using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Makes <c>FiniteRunner_Physics</c>: a COPY of the runner's test scene in
    /// which the standalone ship does the flying. The original scene is never
    /// touched — the track-space ship keeps running the runner there until the
    /// physics one is signed off. In the copy, two things are hand-placed
    /// (edit time, like every scene-lifetime system): the ship object gains a
    /// <see cref="HoverShip"/> with its recovery and pickup components beside
    /// the <see cref="ShipMotor"/> that is already there — which switches the
    /// motor into physics mode, so the GameManager, HUD, generator and patrol
    /// need no rewiring — and the track object gains the
    /// <see cref="TrackColliderBuilder"/> and the <see cref="TrackGuide"/> the
    /// ship flies on. Re-running re-copies the original and wires it again, so
    /// changes made to the test scene carry over; the ship's settings asset is
    /// created once and kept — <b>and so is whatever was tuned by hand on the
    /// five wired components in the old copy</b> (the guide's assist, the
    /// collider builder's chunking…): their values are read out of the scene
    /// about to be replaced and written back onto the fresh components, the
    /// scene references re-wired after.
    /// </summary>
    public static class PhysicsRunnerSceneBuilder
    {
        const string SourcePath = "Assets/05.Scenes/FiniteRunner_Test.unity";
        const string ScenePath = "Assets/05.Scenes/FiniteRunner_Physics.unity";
        const string SettingsPath = "Assets/04.Data/Ship/Runner_ShipSettings.asset";

        [MenuItem("Tools/FiniteRunner/Ship/Create Physics Runner Scene")]
        public static void Build()
        {
            if (!ShipLayers.Installed)
            {
                Debug.LogError("PhysicsRunnerSceneBuilder: run Tools → FiniteRunner → Ship → Install Ship Layers first.");
                return;
            }
            if (SceneManager.GetSceneByPath(ScenePath).isLoaded || SceneManager.GetSceneByPath(SourcePath).isDirty)
            {
                Debug.LogWarning($"PhysicsRunnerSceneBuilder: close {ScenePath} (and save {SourcePath} if it is open with changes), then re-run.");
                return;
            }

            CaptureTuning();
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourcePath, ScenePath))
            {
                Debug.LogError($"PhysicsRunnerSceneBuilder: could not copy {SourcePath}.");
                return;
            }

            ShipSettings settings = LoadOrCreateSettings();
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                ShipMotor motor = Find<ShipMotor>(scene);
                TrackManager track = Find<TrackManager>(scene);
                TrackGenerator generator = Find<TrackGenerator>(scene);
                if (motor == null || track == null || generator == null)
                {
                    Debug.LogError("PhysicsRunnerSceneBuilder: the scene needs a ShipMotor, a TrackManager and a TrackGenerator.");
                    return;
                }

                WireShip(motor, settings);
                WireTrack(track, generator);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previous.IsValid() && previous != scene) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log($"PhysicsRunnerSceneBuilder: built {ScenePath}.");
        }

        /// <summary>
        /// The cutover, in place: wires the ACTIVE scene the same way, for the scene the game really loads (the
        /// campaign catalog and the city's level definitions name <c>FiniteRunner_Test</c>, so the runner cannot
        /// simply switch scenes). Nothing is saved — the scene is left dirty to be looked at first — and taking the
        /// <see cref="HoverShip"/> off the ship again is the whole way back: the motor flies track-space without one.
        /// </summary>
        [MenuItem("Tools/FiniteRunner/Ship/Wire Physics Ship Into Open Scene")]
        public static void WireOpenScene()
        {
            if (Application.isPlaying) return;
            if (!ShipLayers.Installed)
            {
                Debug.LogError("PhysicsRunnerSceneBuilder: run Tools → FiniteRunner → Ship → Install Ship Layers first.");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            ShipMotor motor = Find<ShipMotor>(scene);
            TrackManager track = Find<TrackManager>(scene);
            TrackGenerator generator = Find<TrackGenerator>(scene);
            if (motor == null || track == null || generator == null)
            {
                Debug.LogError($"PhysicsRunnerSceneBuilder: {scene.name} needs a ShipMotor, a TrackManager and a TrackGenerator.");
                return;
            }

            Tuning.Clear(); // nothing to carry over: the scene keeps whatever its components already hold
            WireShip(motor, LoadOrCreateSettings());
            WireTrack(track, generator);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"PhysicsRunnerSceneBuilder: {scene.name} now flies the physics ship. Not saved — check it, then save.", motor);
        }

        // The HoverShip goes on the SAME object as the motor: that is what puts the motor in physics mode.
        static void WireShip(ShipMotor motor, ShipSettings settings)
        {
            GameObject ship = motor.gameObject;
            var motorFields = new SerializedObject(motor);

            var rb = ship.GetComponent<Rigidbody>();
            if (rb == null) rb = ship.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var hover = ship.GetComponent<HoverShip>();
            if (hover == null) hover = ship.AddComponent<HoverShip>();
            RestoreTuning(hover);
            var hoverFields = new SerializedObject(hover);
            hoverFields.FindProperty("definition").objectReferenceValue = motorFields.FindProperty("definition").objectReferenceValue;
            hoverFields.FindProperty("settings").objectReferenceValue = settings;
            Object visual = motorFields.FindProperty("visual").objectReferenceValue;
            hoverFields.FindProperty("visual").objectReferenceValue = visual != null ? visual : ship.transform;
            hoverFields.FindProperty("launchOnStart").boolValue = false; // the motor launches it from the start line
            hoverFields.ApplyModifiedPropertiesWithoutUndo();

            var recovery = ship.GetComponent<ShipRecovery>();
            if (recovery == null) recovery = ship.AddComponent<ShipRecovery>();
            RestoreTuning(recovery);
            var sweeper = ship.GetComponent<ShipPickupSweeper>();
            if (sweeper == null) sweeper = ship.AddComponent<ShipPickupSweeper>();
            RestoreTuning(sweeper);
            // No ShipCameraAttach: the GameManager attaches the camera, as it always has.
        }

        static void WireTrack(TrackManager track, TrackGenerator generator)
        {
            GameObject host = track.gameObject;

            var colliders = host.GetComponent<TrackColliderBuilder>();
            if (colliders == null) colliders = host.AddComponent<TrackColliderBuilder>();
            RestoreTuning(colliders);
            var colliderFields = new SerializedObject(colliders);
            colliderFields.FindProperty("track").objectReferenceValue = track;
            colliderFields.FindProperty("generator").objectReferenceValue = generator;
            colliderFields.ApplyModifiedPropertiesWithoutUndo();

            var guide = host.GetComponent<TrackGuide>();
            if (guide == null) guide = host.AddComponent<TrackGuide>();
            RestoreTuning(guide);
            var guideFields = new SerializedObject(guide);
            guideFields.FindProperty("track").objectReferenceValue = track;
            guideFields.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------- tuning
        static readonly Dictionary<System.Type, string> Tuning = new();

        // The values of the wired components in the copy that is about to be thrown away. Scene references inside them
        // die with that scene; WireShip / WireTrack set them again right after the restore.
        static void CaptureTuning()
        {
            Tuning.Clear();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
            Scene old = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Keep(Find<HoverShip>(old));
                Keep(Find<ShipRecovery>(old));
                Keep(Find<ShipPickupSweeper>(old));
                Keep(Find<TrackColliderBuilder>(old));
                Keep(Find<TrackGuide>(old));
            }
            finally { EditorSceneManager.CloseScene(old, true); }
        }

        static void Keep(Component component)
        {
            if (component != null) Tuning[component.GetType()] = EditorJsonUtility.ToJson(component);
        }

        static void RestoreTuning(Component component)
        {
            if (component != null && Tuning.TryGetValue(component.GetType(), out string json))
                EditorJsonUtility.FromJsonOverwrite(json, component);
        }

        // The runner's physics scene can share its physics world with the city during the handoff: the ship reads its own layers only.
        static ShipSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ShipSettings>(SettingsPath);
            if (settings != null) return settings;
            if (!AssetDatabase.IsValidFolder("Assets/04.Data/Ship")) AssetDatabase.CreateFolder("Assets/04.Data", "Ship");
            settings = ScriptableObject.CreateInstance<ShipSettings>();
            settings.groundLayers = ShipLayers.GroundMask | ShipLayers.SurfaceMask;
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        static T Find<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }
    }
}
