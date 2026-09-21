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
    /// created once and kept.
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
            var hoverFields = new SerializedObject(hover);
            hoverFields.FindProperty("definition").objectReferenceValue = motorFields.FindProperty("definition").objectReferenceValue;
            hoverFields.FindProperty("settings").objectReferenceValue = settings;
            Object visual = motorFields.FindProperty("visual").objectReferenceValue;
            hoverFields.FindProperty("visual").objectReferenceValue = visual != null ? visual : ship.transform;
            hoverFields.FindProperty("launchOnStart").boolValue = false; // the motor launches it from the start line
            hoverFields.ApplyModifiedPropertiesWithoutUndo();

            if (ship.GetComponent<ShipRecovery>() == null) ship.AddComponent<ShipRecovery>();
            if (ship.GetComponent<ShipPickupSweeper>() == null) ship.AddComponent<ShipPickupSweeper>();
            // No ShipCameraAttach: the GameManager attaches the camera, as it always has.
        }

        static void WireTrack(TrackManager track, TrackGenerator generator)
        {
            GameObject host = track.gameObject;

            var colliders = host.GetComponent<TrackColliderBuilder>();
            if (colliders == null) colliders = host.AddComponent<TrackColliderBuilder>();
            var colliderFields = new SerializedObject(colliders);
            colliderFields.FindProperty("track").objectReferenceValue = track;
            colliderFields.FindProperty("generator").objectReferenceValue = generator;
            colliderFields.ApplyModifiedPropertiesWithoutUndo();

            var guide = host.GetComponent<TrackGuide>();
            if (guide == null) guide = host.AddComponent<TrackGuide>();
            var guideFields = new SerializedObject(guide);
            guideFields.FindProperty("track").objectReferenceValue = track;
            guideFields.ApplyModifiedPropertiesWithoutUndo();
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
