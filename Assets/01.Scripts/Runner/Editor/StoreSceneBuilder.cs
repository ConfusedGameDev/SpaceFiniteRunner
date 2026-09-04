using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Create Store Scene</c>: the Store's data
    /// assets and its scene from code, idempotently — every asset is
    /// create-or-load (an authored table is never stomped), and an existing
    /// scene is opened rather than rebuilt. Fresh definitions are seeded
    /// with the agreed curves (cost <c>500 × 1.5^level</c>, multiplier
    /// <c>1 + 0.05 × level</c>); the three sections get their default model
    /// (Quadron / nabucodonosor / ROB) whose preview scale and seat are
    /// measured off the instanced model's bounds so each fills the stage.
    /// The scene is the hand-placed kind the project insists on: camera,
    /// light, the <see cref="StoreStage"/> with its models, the
    /// <see cref="StoreScreen"/>, its <see cref="MoneyHud"/> wallet and the
    /// shared EventSystem / Haptics / Cheat prefabs — and it is appended to
    /// the build list, which the main menu's START relies on.
    /// </summary>
    public static class StoreSceneBuilder
    {
        const string DataFolder = "Assets/04.Data/Resources/Store";
        const string SettingsPath = DataFolder + "/StoreSettings.asset";
        const string ScenesFolder = "Assets/05.Scenes";
        const string ScenePath = ScenesFolder + "/" + StoreSettings.SceneName + ".unity";

        const string CarPrefab = "Assets/Cyberpunk_Megapolis/Prefabs/Car/CP_Quadron.prefab";
        const string ShipModel = "Assets/99.Test/Diego/3DModels/nabucodonosor.fbx";
        const string CharacterPrefab = "Assets/03.Prefabs/Characters/PF_ROB.prefab";
        static readonly string[] SharedPrefabs =
        {
            "Assets/03.Prefabs/Shared/EventSystem.prefab",
            "Assets/03.Prefabs/Shared/HapticsSystem.prefab",
            "Assets/03.Prefabs/Shared/CheatManager.prefab",
        };

        const float BaseCost = 1500f;
        const float CostGrowth = 1.5f;
        const float MultiplierStep = 0.05f;

        // Where the stage sits: right of the screen centre, in the gap between
        // the row column and the media plate (see StoreScreen's layout).
        static readonly Vector3 StagePosition = new(0.55f, 0f, 0f);
        static readonly Vector3 CameraPosition = new(0f, 1.4f, -7.5f);
        static readonly Vector3 CameraTarget = new(0f, 0.9f, 0f);
        const float CameraFov = 32f;

        struct CategorySpec
        {
            public string file, id;
            public MenuTextId label;
            public CategorySpec(string file, string id, MenuTextId label) { this.file = file; this.id = id; this.label = label; }
        }

        struct SectionSpec
        {
            public string file, modelId, displayName, prefabPath;
            public StoreSectionKind kind;
            public MenuTextId title;
            public float fitExtent; // metres the model's longest side is scaled to
            public CategorySpec[] categories;
        }

        static readonly SectionSpec[] Sections =
        {
            new()
            {
                file = "Section_Car", kind = StoreSectionKind.Car, title = MenuTextId.StoreSectionCar,
                modelId = UpgradeIds.CarQuadron, displayName = "QUADRON", prefabPath = CarPrefab, fitExtent = 4.5f,
                categories = new[]
                {
                    new CategorySpec("Upgrade_Car_Speed", UpgradeIds.CarSpeed, MenuTextId.UpgradeSpeed),
                    new CategorySpec("Upgrade_Car_Acceleration", UpgradeIds.CarAcceleration, MenuTextId.UpgradeAcceleration),
                    new CategorySpec("Upgrade_Car_Weight", UpgradeIds.CarWeight, MenuTextId.UpgradeWeight),
                    new CategorySpec("Upgrade_Car_Resistance", UpgradeIds.CarResistance, MenuTextId.UpgradeResistance),
                    new CategorySpec("Upgrade_Car_Handling", UpgradeIds.CarHandling, MenuTextId.UpgradeHandling),
                }
            },
            new()
            {
                file = "Section_Ship", kind = StoreSectionKind.Ship, title = MenuTextId.StoreSectionShip,
                modelId = UpgradeIds.ShipNabucodonosor, displayName = "NABUCODONOSOR", prefabPath = ShipModel, fitExtent = 4.5f,
                categories = new[]
                {
                    new CategorySpec("Upgrade_Ship_Handling", UpgradeIds.ShipHandling, MenuTextId.UpgradeHandling),
                    new CategorySpec("Upgrade_Ship_DashPower", UpgradeIds.ShipDashPower, MenuTextId.UpgradeDashPower),
                    new CategorySpec("Upgrade_Ship_SpeedMultiplier", UpgradeIds.ShipSpeedMultiplier, MenuTextId.UpgradeSpeedMultiplier),
                    new CategorySpec("Upgrade_Ship_JumpStrength", UpgradeIds.ShipJumpStrength, MenuTextId.UpgradeJumpStrength),
                }
            },
            new()
            {
                file = "Section_Character", kind = StoreSectionKind.Character, title = MenuTextId.StoreSectionCharacter,
                modelId = UpgradeIds.CharRob, displayName = "ROB", prefabPath = CharacterPrefab, fitExtent = 1.9f,
                categories = new[]
                {
                    new CategorySpec("Upgrade_Char_HackingSpeed", UpgradeIds.CharHackingSpeed, MenuTextId.UpgradeHackingSpeed),
                    new CategorySpec("Upgrade_Char_HackValue", UpgradeIds.CharHackValue, MenuTextId.UpgradeHackValue),
                    new CategorySpec("Upgrade_Char_Strength", UpgradeIds.CharStrength, MenuTextId.UpgradeStrength),
                    new CategorySpec("Upgrade_Char_Range", UpgradeIds.CharRange, MenuTextId.UpgradeRange),
                    new CategorySpec("Upgrade_Char_Accuracy", UpgradeIds.CharAccuracy, MenuTextId.UpgradeAccuracy),
                }
            },
        };

        [MenuItem("Tools/FiniteRunner/Create Store Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(DataFolder);
            EnsureFolder(ScenesFolder);

            StoreSettings settings = CreateAssets();

            if (File.Exists(ScenePath))
            {
                EnsureInBuildSettings();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log($"Store scene already exists — opened it: {ScenePath}. Delete the file to rebuild it from scratch.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetUpCameraAndLight();
            StoreStage stage = BuildStage(settings);
            StoreScreen screen = BuildScreen(settings, stage);
            foreach (string path in SharedPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) PrefabUtility.InstantiatePrefab(prefab);
                else Debug.LogWarning($"Store scene: shared prefab missing, skipped: {path}");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings();
            Selection.activeGameObject = screen.gameObject;
            Debug.Log($"Store scene created: {ScenePath} (build index {SceneUtility.GetBuildIndexByScenePath(ScenePath)}). " +
                      "Main menu START now lands here; set the runner level's nextSceneName to Store to close the loop.");
        }

        // -------------------------------------------------------------- assets

        static StoreSettings CreateAssets()
        {
            var settings = CreateOrLoad<StoreSettings>(SettingsPath, out bool freshSettings);
            var sections = new StoreSection[Sections.Length];

            for (int s = 0; s < Sections.Length; s++)
            {
                SectionSpec spec = Sections[s];
                var section = CreateOrLoad<StoreSection>($"{DataFolder}/{spec.file}.asset", out bool freshSection);
                if (freshSection)
                {
                    section.kind = spec.kind;
                    section.title = spec.title;
                    section.models.Add(new StoreModel
                    {
                        modelId = spec.modelId,
                        displayName = spec.displayName,
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.prefabPath),
                        previewScale = 1f,
                    });
                    foreach (CategorySpec c in spec.categories)
                    {
                        var def = CreateOrLoad<UpgradeDefinition>($"{DataFolder}/{c.file}.asset", out bool freshDef);
                        if (freshDef) Seed(def, c.id, c.label);
                        section.categories.Add(def);
                    }
                    EditorUtility.SetDirty(section);
                }
                else
                {
                    // Categories the table lists but the authored section lost get re-created, never re-seeded over.
                    foreach (CategorySpec c in spec.categories)
                    {
                        var def = CreateOrLoad<UpgradeDefinition>($"{DataFolder}/{c.file}.asset", out bool freshDef);
                        if (freshDef) Seed(def, c.id, c.label);
                    }
                }
                sections[s] = section;
            }

            if (freshSettings || settings.car == null) settings.car = sections[0];
            if (freshSettings || settings.ship == null) settings.ship = sections[1];
            if (freshSettings || settings.character == null) settings.character = sections[2];
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        static void Seed(UpgradeDefinition def, string id, MenuTextId label)
        {
            def.id = id;
            def.label = label;
            def.levels.Clear();
            for (int level = 1; level <= UpgradeIds.MaxLevel; level++)
            {
                def.levels.Add(new UpgradeDefinition.UpgradeLevel
                {
                    cost = Mathf.RoundToInt(BaseCost * Mathf.Pow(CostGrowth, level - 1)),
                    multiplier = 1f + MultiplierStep * level,
                });
            }
            EditorUtility.SetDirty(def);
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

        static void SetUpCameraAndLight()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = CameraPosition;
                camera.transform.LookAt(CameraTarget);
                camera.fieldOfView = CameraFov;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 60f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                Color backdrop = MenuTheme.Load().Backdrop;
                camera.backgroundColor = new Color(backdrop.r, backdrop.g, backdrop.b, 1f);
            }

            var light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
                light.intensity = 1.2f;
            }
        }

        static StoreStage BuildStage(StoreSettings settings)
        {
            var root = new GameObject("StoreStage");
            root.transform.position = StagePosition;
            var stage = root.AddComponent<StoreStage>();

            Transform car = Slot(root.transform, "CarSlot");
            Transform ship = Slot(root.transform, "ShipSlot");
            Transform character = Slot(root.transform, "CharacterSlot");
            stage.Configure(settings, car, ship, character);

            Seat(settings.car, car, Sections[0].fitExtent);
            Seat(settings.ship, ship, Sections[1].fitExtent);
            Seat(settings.character, character, Sections[2].fitExtent);

            ship.gameObject.SetActive(false);
            character.gameObject.SetActive(false);
            return stage;
        }

        static Transform Slot(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // Instances the section's default model under its slot, then measures
        // it to write the preview scale / seat back onto the StoreModel entry:
        // longest side fitted to fitExtent, bounds centre parked at the
        // camera's target height.
        static void Seat(StoreSection section, Transform slot, float fitExtent)
        {
            StoreModel model = section != null ? section.DefaultModel : null;
            if (model == null || model.prefab == null)
            {
                Debug.LogWarning($"Store scene: {slot.name} has no model prefab to show.");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(model.prefab, slot) as GameObject;
            if (instance == null) return;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var unit = new StoreModel { previewScale = 1f, previewOffset = Vector3.zero };
            StoreStage.PrepareInstance(instance, unit); // LODs stripped before measuring
            if (TryBounds(instance, out Bounds bounds))
            {
                float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float scale = longest > 0.001f ? fitExtent / longest : 1f;
                model.previewScale = scale;
                model.previewOffset = new Vector3(0f, CameraTarget.y, 0f) - (bounds.center - slot.position) * scale;
                EditorUtility.SetDirty(section);
            }
            StoreStage.PrepareInstance(instance, model);
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

        static StoreScreen BuildScreen(StoreSettings settings, StoreStage stage)
        {
            var walletGo = new GameObject("MoneyHud");
            var wallet = walletGo.AddComponent<MoneyHud>();

            var go = new GameObject("StoreScreen", typeof(RectTransform)) { layer = 5 };
            var screen = go.AddComponent<StoreScreen>();
            var so = new SerializedObject(screen);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("stage").objectReferenceValue = stage;
            so.FindProperty("wallet").objectReferenceValue = wallet;
            so.ApplyModifiedPropertiesWithoutUndo();
            return screen;
        }

        static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in scenes)
                if (s.path == ScenePath)
                {
                    if (!s.enabled)
                    {
                        s.enabled = true;
                        EditorBuildSettings.scenes = scenes.ToArray();
                    }
                    return;
                }
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
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
