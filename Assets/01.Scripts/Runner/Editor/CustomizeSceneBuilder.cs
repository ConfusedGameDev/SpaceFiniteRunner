using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.Customize;
using ConfusedGameDev.FiniteRunner.Livery;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Create Customize Vehicle Scene</c>: the
    /// customize test scene and its two assets, idempotently — the assets are
    /// create-or-load (an authored vehicle list is never stomped) and an
    /// existing scene is opened rather than rebuilt. A fresh
    /// <see cref="CustomizeSettings"/> lists the nabucodonosor ship (with the
    /// Store's six RealToon parts as slot overrides, so each part is its own
    /// slider) and the Quadron, each seated by measuring its bounds. The
    /// scene uses the runner's header layout: ===SYSTEMS=== (EventSystem,
    /// haptics), ===ENV=== (the <see cref="CustomizeStage"/> right of centre),
    /// ===CAMERAS===, ===UI=== (the <see cref="CustomizeScreen"/>) and
    /// ===LIGHTING===.
    /// </summary>
    public static class CustomizeSceneBuilder
    {
        const string DataFolder = "Assets/04.Data/Customize";
        const string SettingsPath = DataFolder + "/CustomizeSettings.asset";
        const string ProfilePath = DataFolder + "/VehicleColorProfile.asset";
        const string ScenesFolder = "Assets/05.Scenes";
        const string ScenePath = ScenesFolder + "/CustomizeVehicle.unity";

        const string ShipModel = "Assets/99.Test/Diego/3DModels/nabucodonosor.fbx";
        const string CarPrefab = "Assets/Cyberpunk_Megapolis/Prefabs/Car/CP_Quadron.prefab";
        // The Store scene's material overrides on the ship's one renderer, in slot order.
        static readonly string[] ShipSlotMaterials =
        {
            "Assets/99.Test/Diego/3DModels/PUNTAmat.mat",
            "Assets/99.Test/Diego/3DModels/PROPULSORmat.mat",
            "Assets/99.Test/Diego/3DModels/PiezasUNOmat.mat",
            "Assets/99.Test/Diego/3DModels/PRINCIPALmat.mat",
            "Assets/99.Test/Diego/3DModels/vidrio.mat",
            "Assets/99.Test/Diego/3DModels/ESCAPES.mat",
        };
        static readonly string[] SharedPrefabs =
        {
            "Assets/03.Prefabs/Shared/EventSystem.prefab",
            "Assets/03.Prefabs/Shared/HapticsSystem.prefab",
        };

        // The vehicle sits in the right half of the frame; the camera looks at
        // the screen centre, so the UI column on the left stays clear.
        static readonly Vector3 StagePosition = new(1.7f, 0f, 0f);
        static readonly Vector3 CameraPosition = new(0f, 1.4f, -7.5f);
        static readonly Vector3 CameraTarget = new(0f, 0.9f, 0f);
        const float CameraFov = 32f;
        const float FitExtent = 3.6f; // metres the longest side is scaled to — clears the right edge while it turns

        [MenuItem("Tools/FiniteRunner/Create Customize Vehicle Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(DataFolder);
            EnsureFolder(ScenesFolder);

            CreateSettings();
            CreateOrLoad<VehicleColorProfile>(ProfilePath, out _);
            AssetDatabase.SaveAssets();
            // Re-load by path: saving can re-import a just-created asset, which
            // leaves the instance in hand destroyed — wired, it serializes as null.
            var settings = AssetDatabase.LoadAssetAtPath<CustomizeSettings>(SettingsPath);
            var profile = AssetDatabase.LoadAssetAtPath<VehicleColorProfile>(ProfilePath);

            if (File.Exists(ScenePath))
            {
                Scene existing = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (RepairWiring(settings, profile)) EditorSceneManager.SaveScene(existing);
                Debug.Log($"Customize scene already exists — opened it: {ScenePath}. Delete the file to rebuild it from scratch.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform systems = Header("===SYSTEMS===");
            Header("===PLAYER==="); // empty: the stage spawns the vehicle under itself
            Transform env = Header("===ENV===");
            Transform cameras = Header("===CAMERAS===");
            Transform uiHeader = Header("===UI===");
            Transform lighting = Header("===LIGHTING===");

            foreach (string path in SharedPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) PrefabUtility.InstantiatePrefab(prefab, systems);
                else Debug.LogWarning($"Customize scene: shared prefab missing, skipped: {path}");
            }

            Camera camera = BuildCamera(cameras);
            BuildLight(lighting);

            var stageGo = new GameObject("CustomizeStage");
            stageGo.transform.SetParent(env, false);
            stageGo.transform.position = StagePosition;
            var stage = stageGo.AddComponent<CustomizeStage>();
            stage.Configure(settings, camera);
            BuildFloor(stageGo.transform);

            var screenGo = new GameObject("CustomizeScreen", typeof(RectTransform)) { layer = 5 };
            screenGo.transform.SetParent(uiHeader, false);
            var screen = screenGo.AddComponent<CustomizeScreen>();
            var so = new SerializedObject(screen);
            so.FindProperty("stage").objectReferenceValue = stage;
            so.FindProperty("profile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = screenGo;
            Debug.Log($"Customize scene created: {ScenePath}. Press Play with a gamepad connected.");
        }

        // Fills any empty asset slot on an existing scene's stage and screen
        // (a scene built before the re-load fix saved its profile as null).
        static bool RepairWiring(CustomizeSettings settings, VehicleColorProfile profile)
        {
            bool changed = false;
            foreach (CustomizeStage stage in Object.FindObjectsByType<CustomizeStage>(FindObjectsInactive.Include))
                changed |= FillIfEmpty(stage, "settings", settings);
            foreach (CustomizeScreen screen in Object.FindObjectsByType<CustomizeScreen>(FindObjectsInactive.Include))
                changed |= FillIfEmpty(screen, "profile", profile);
            if (changed) Debug.Log("Customize scene: re-wired its missing asset references.");
            return changed;
        }

        static bool FillIfEmpty(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null || property.objectReferenceValue != null || value == null) return false;
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        // -------------------------------------------------------------- assets

        static CustomizeSettings CreateSettings()
        {
            var settings = CreateOrLoad<CustomizeSettings>(SettingsPath, out bool fresh);
            if (!fresh && settings.vehicles.Count > 0) return settings;

            settings.vehicles.Clear();
            var ship = new CustomizeVehicleEntry
            {
                model = new StoreModel
                {
                    modelId = UpgradeIds.ShipNabucodonosor,
                    displayName = "NABUCODONOSOR",
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModel),
                },
            };
            foreach (string path in ShipSlotMaterials)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) Debug.LogWarning($"Customize scene: ship material missing: {path}");
                ship.slotOverrides.Add(mat);
            }
            var car = new CustomizeVehicleEntry
            {
                model = new StoreModel
                {
                    modelId = UpgradeIds.CarQuadron,
                    displayName = "QUADRON",
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab),
                },
            };
            Measure(ship.model);
            Measure(car.model);
            settings.vehicles.Add(ship);
            settings.vehicles.Add(car);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        // Instances the prefab at the origin, then writes the preview scale /
        // seat back onto the entry: longest side fitted to FitExtent, bounds
        // centre parked at the camera's target height.
        static void Measure(StoreModel model)
        {
            if (model.prefab == null)
            {
                Debug.LogWarning($"Customize scene: {model.displayName} has no prefab.");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model.prefab);
            try
            {
                StoreStage.PrepareInstance(instance, new StoreModel { previewScale = 1f }); // LODs stripped before measuring
                if (!TryBounds(instance, out Bounds bounds)) return;
                float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float scale = longest > 0.001f ? FitExtent / longest : 1f;
                model.previewScale = scale;
                model.previewOffset = new Vector3(0f, CameraTarget.y, 0f) - bounds.center * scale;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static bool TryBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        static T CreateOrLoad<T>(string path, out bool created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        // --------------------------------------------------------------- scene

        static Transform Header(string name)
        {
            var go = new GameObject(name);
            go.transform.position = Vector3.zero;
            return go.transform;
        }

        static Camera BuildCamera(Transform parent)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.position = CameraPosition;
            go.transform.LookAt(CameraTarget);
            var camera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            Color backdrop = MenuTheme.Load().Backdrop;
            camera.backgroundColor = new Color(backdrop.r, backdrop.g, backdrop.b, 1f);
            return camera;
        }

        static void BuildLight(Transform parent)
        {
            var go = new GameObject("Directional Light");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
        }

        // A thin dark disc under the vehicle so it reads as standing somewhere.
        static void BuildFloor(Transform stage)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.SetParent(stage, false);
            floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            floor.transform.localScale = new Vector3(4.2f, 0.02f, 4.2f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
