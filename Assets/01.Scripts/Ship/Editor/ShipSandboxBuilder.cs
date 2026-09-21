using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Ship.Sandbox;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Builds <c>Assets/05.Scenes/ShipSandbox.unity</c>, the standalone ship's
    /// test scene, from nothing: the course (<see cref="ShipSandboxCourse"/>,
    /// which makes its own geometry at play), a ship wired exactly the way the
    /// prefab will be, the chase camera's rig and the debug overlay — all
    /// hand-placed at edit time under the project's scene headers, so nothing
    /// is spawned as a system at runtime. The scene is built ADDITIVELY and
    /// closed again, so whatever scene is open (and its unsaved changes) is
    /// left alone. Re-running rebuilds the scene from scratch; the
    /// <see cref="ShipSettings"/> asset it creates on first run is kept.
    /// </summary>
    public static class ShipSandboxBuilder
    {
        const string ScenePath = "Assets/05.Scenes/ShipSandbox.unity";
        const string SettingsPath = "Assets/04.Data/Ship/HoverShip_Settings.asset";
        const string DefinitionPath = "Assets/04.Data/FiniteRunner/Fighter_ShipDefinition.asset";
        const string CameraSettingsPath = "Assets/04.Data/FiniteRunner/Fighter_CameraSettings.asset";
        const string ModelPath = "Assets/99.Test/Diego/3DModels/nabucodonosor.fbx";
        const float ModelScale = 0.57f;
        static readonly Vector3 HullSize = new(5.02f, 4.61f, 12.3f);

        [MenuItem("Tools/FiniteRunner/Ship/Build Sandbox Scene")]
        public static void Build()
        {
            if (!ShipLayers.Installed)
            {
                Debug.LogError("ShipSandboxBuilder: run Tools → FiniteRunner → Ship → Install Ship Layers first.");
                return;
            }

            var definition = AssetDatabase.LoadAssetAtPath<ShipDefinition>(DefinitionPath);
            var cameraSettings = AssetDatabase.LoadAssetAtPath<OrbitCameraSettings>(CameraSettingsPath);
            if (definition == null)
            {
                Debug.LogError($"ShipSandboxBuilder: no ship definition at {DefinitionPath}.");
                return;
            }
            ShipSettings settings = LoadOrCreateSettings();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                Transform systems = Header("===SYSTEMS===");
                Transform level = Header("===LEVEL===");
                Transform player = Header("===PLAYER===");

                var light = new GameObject("Directional Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.SetParent(level, false);
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                var camera = new GameObject("Main Camera") { tag = "MainCamera" };
                camera.transform.SetParent(systems, false);
                camera.transform.position = new Vector3(0f, 12f, -30f);
                camera.AddComponent<Camera>().farClipPlane = 6000f;
                camera.AddComponent<AudioListener>();

                // The rig and its two sibling vcams: empty objects are all the rig needs, it fills them in when first targeted.
                new GameObject("OrbitCameraRig").AddComponent<OrbitCameraRig>().transform.SetParent(systems, false);
                new GameObject(OrbitCameraRig.FirstPersonName).transform.SetParent(systems, false);
                new GameObject(OrbitCameraRig.CinematicName).transform.SetParent(systems, false);

                var course = new GameObject("SandboxCourse").AddComponent<ShipSandboxCourse>();
                course.transform.SetParent(level, false);

                HoverShip ship = BuildShip(player, definition, settings, cameraSettings);

                var overlay = new GameObject("ShipDebugOverlay").AddComponent<ShipDebugOverlay>();
                overlay.transform.SetParent(systems, false);
                var overlayFields = new SerializedObject(overlay);
                overlayFields.FindProperty("ship").objectReferenceValue = ship;
                overlayFields.FindProperty("course").objectReferenceValue = course;
                overlayFields.ApplyModifiedPropertiesWithoutUndo();

                var autoTest = new GameObject("ShipSandboxAutoTest").AddComponent<ShipSandboxAutoTest>();
                autoTest.transform.SetParent(systems, false);
                var testFields = new SerializedObject(autoTest);
                testFields.FindProperty("ship").objectReferenceValue = ship;
                testFields.FindProperty("course").objectReferenceValue = course;
                testFields.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log($"ShipSandboxBuilder: built {ScenePath}.");
        }

        static HoverShip BuildShip(Transform parent, ShipDefinition definition, ShipSettings settings, OrbitCameraSettings cameraSettings)
        {
            var root = new GameObject("HoverShip") { layer = ShipLayers.Ship };
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, 4f, 20f);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            root.AddComponent<BoxCollider>().size = HullSize;
            root.AddComponent<SteeringInput>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
                instance.transform.localScale = Vector3.one * ModelScale;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ShipLayers.Ship;
            }
            else
            {
                Debug.LogWarning($"ShipSandboxBuilder: no model at {ModelPath} — the ship gets a placeholder box.");
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(box.GetComponent<Collider>());
                box.transform.SetParent(visual.transform, false);
                box.transform.localScale = HullSize;
            }

            var ship = root.AddComponent<HoverShip>();
            var shipFields = new SerializedObject(ship);
            shipFields.FindProperty("definition").objectReferenceValue = definition;
            shipFields.FindProperty("settings").objectReferenceValue = settings;
            shipFields.FindProperty("visual").objectReferenceValue = visual.transform;
            shipFields.ApplyModifiedPropertiesWithoutUndo();

            var attach = root.AddComponent<ShipCameraAttach>();
            var attachFields = new SerializedObject(attach);
            attachFields.FindProperty("cameraSettings").objectReferenceValue = cameraSettings;
            attachFields.ApplyModifiedPropertiesWithoutUndo();
            return ship;
        }

        static ShipSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ShipSettings>(SettingsPath);
            if (settings != null) return settings;
            if (!AssetDatabase.IsValidFolder("Assets/04.Data/Ship")) AssetDatabase.CreateFolder("Assets/04.Data", "Ship");
            settings = ScriptableObject.CreateInstance<ShipSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        static Transform Header(string name)
        {
            var header = new GameObject(name);
            header.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return header.transform;
        }
    }
}
