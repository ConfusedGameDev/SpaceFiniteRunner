using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click test harness for M3+: builds the player and police car
    /// prefabs around the Kenney vehicle models (sedan-sports / police) —
    /// scaled to real-car length, wheel colliders placed at the models'
    /// wheel positions, wheel meshes re-pivoted so they spin cleanly — plus
    /// the config assets and a saved scene with the city test layout (if its
    /// settings asset exists), a safety-net ground collider and a
    /// PlayerCarSpawner. The car is spawned at runtime, not baked in — every
    /// play regenerates the city with a fresh seed. Re-running rebuilds
    /// prefabs and scene but leaves existing config assets untouched, so
    /// handling tuning survives; 'Rebuild Vehicle Prefabs' refreshes just the
    /// prefabs in place (same assets, scene wiring keeps working). Drive with
    /// WASD or gamepad; Space/South = handbrake, R/North = respawn; mouse /
    /// right stick / arrows pan the camera.
    /// </summary>
    public static class CarTestSceneBuilder
    {
        const string SceneFolder = "Assets/05.Scenes";
        const string DataFolder = "Assets/04.Data/InfiniteCity";
        const string PrefabFolder = "Assets/03.Prefabs/InfiniteCity";
        const string MaterialFolder = "Assets/02.Art/02.Materials/InfiniteCity/Test";
        const string ScenePath = SceneFolder + "/CarTest.unity";
        const string CarPrefabPath = PrefabFolder + "/TestCar.prefab";
        const string PoliceCarPrefabPath = PrefabFolder + "/TestPoliceCar.prefab";
        const string CarConfigPath = DataFolder + "/TestCarConfig.asset";
        const string CameraSettingsPath = DataFolder + "/TestOrbitCameraSettings.asset";
        const string PursuitSettingsPath = DataFolder + "/TestPursuitSettings.asset";
        const string MinimapSettingsPath = DataFolder + "/TestMinimapSettings.asset";
        const string SpeedometerSettingsPath = DataFolder + "/TestSpeedometerSettings.asset";
        const string TrafficSettingsPath = DataFolder + "/TestTrafficSettings.asset";
        const string LevelDefinitionPath = DataFolder + "/TestLevelDefinition.asset";
        const string VehiclesFolder = "Assets/02.Art/01.Models/InfiniteCity/Vehicles";
        const string CitySettingsPath = DataFolder + "/CityTestSettings.asset";
        const string GlitchMaterialPath = "Assets/02.Art/02.Materials/InfiniteCity/GlitchPost.mat";
        const string PlayerModelPath = VehiclesFolder + "/sedan-sports.fbx";
        const string PoliceModelPath = VehiclesFolder + "/police.fbx";
        const float TargetCarLength = 4.4f; // the Kenney models are ~2.6-3.1 units long — scale them to real-car meters

        [MenuItem("Tools/Police Escape/Create Car Test Scene")]
        public static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(SceneFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            // Existing config assets are kept as-is — only created when missing —
            // so re-running the builder never wipes handling tuning.
            CarConfig carConfig = CreateOrLoad<CarConfig>(CarConfigPath);
            OrbitCameraSettings cameraSettings = CreateOrLoad<OrbitCameraSettings>(CameraSettingsPath);
            AI.PursuitSettings pursuitSettings = CreateOrLoad<AI.PursuitSettings>(PursuitSettingsPath);
            UI.MinimapSettings minimapSettings = CreateOrLoad<UI.MinimapSettings>(MinimapSettingsPath);
            UI.SpeedometerSettings speedometerSettings = CreateOrLoad<UI.SpeedometerSettings>(SpeedometerSettingsPath);
            AI.TrafficSettings trafficSettings = CreateOrLoadTrafficSettings(carConfig);
            LevelDefinition levelDefinition = CreateOrLoadLevelDefinition();
            GameObject carPrefab = BuildCarPrefab(carConfig);
            GameObject policeCarPrefab = BuildPoliceCarPrefab(carConfig, pursuitSettings);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // City roads + buildings, if the city test assets have been built.
            var citySettings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(CitySettingsPath);
            if (citySettings != null)
            {
                var cityGo = new GameObject("CityManager");
                var city = cityGo.AddComponent<CityManager>();
                city.settings = citySettings;
                city.carPrefab = carPrefab;              // enables the Create Car button
                city.orbitCameraSettings = cameraSettings;
                city.policeCarPrefab = policeCarPrefab;  // wired police fields spawn the PatrolManager at play
                city.pursuitSettings = pursuitSettings;
                city.minimapSettings = minimapSettings;  // wired minimap settings spawn the radar at play
                city.speedometerSettings = speedometerSettings;
                city.trafficSettings = trafficSettings;  // wired traffic settings spawn the TrafficManager at play
                city.Recalculate();
            }
            else
            {
                Debug.LogWarning("CarTestSceneBuilder: no city test settings found — run 'Tools/Police Escape/Create City Test Scene' first for roads. Spawning the car on bare ground.");
            }

            // Chunks now carry their own ground colliders; this big slab is a
            // safety net (and visual floor) beyond the city edge.
            Vector3 cityCenter = CityCenter(citySettings);
            BuildGround(cityCenter);

            // Car + chase camera arrive at runtime: every play rolls a fresh
            // city, so the spawner picks the road cell nearest this position.
            var spawnerGo = new GameObject("PlayerCarSpawner");
            spawnerGo.transform.position = cityCenter;
            var spawner = spawnerGo.AddComponent<PlayerCarSpawner>();
            spawner.carPrefab = carPrefab;
            spawner.cameraSettings = cameraSettings;

            // Objective flow, as data: the level asset lists the steps (by
            // default reach the hack speed, then shake the police) and the
            // scene handed over to. Messages come from the shared
            // RpgMessageSystem, which builds its own canvas at runtime.
            var levelManager = new GameObject("LevelManager").AddComponent<LevelManager>();
            levelManager.level = levelDefinition;

            // Pause menu shared with the runner — hand-placed here, it builds
            // itself in Start with no ship references and offers the city's
            // debug pages (car, camera, police, level).
            new GameObject("PauseMenu") { layer = 5 }.AddComponent<global::FiniteRunner.PauseMenu>();

            // Fullscreen glitch dial — the renderer's GlitchPost feature runs
            // the shader; this controller is what gameplay events talk to.
            // The slow base fade doubles as crash-damage recovery.
            var glitchMaterial = AssetDatabase.LoadAssetAtPath<Material>(GlitchMaterialPath);
            if (glitchMaterial != null)
            {
                var glitch = new GameObject("GlitchController").AddComponent<FX.GlitchController>();
                glitch.glitchMaterial = glitchMaterial;
                glitch.baseFadePerSecond = 0.05f;
            }

            // Overhead vantage for edit mode; the ChaseCamera takes over in play.
            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = cityCenter + new Vector3(0f, 120f, -80f);
                camera.transform.LookAt(cityCenter);
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 4000f);
            }

            Selection.activeGameObject = spawnerGo;
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("CarTestSceneBuilder: car test scene ready — press Play and drive (WASD/arrows, Space handbrake, R respawn). Tune TestCarConfig live from the PlayerCar inspector.");
        }

        /// <summary>World center of the initial chunk grid, matching CityManager's -half .. size-half chunk loop.</summary>
        static Vector3 CityCenter(CityGenerationSettings settings)
        {
            if (settings == null) return Vector3.zero;
            float side = settings.chunkSizeInCells * settings.cellSize;
            int size = settings.initialCitySizeInChunks;
            int half = size / 2;
            float min = -half * side;
            float max = (size - half) * side;
            float mid = (min + max) * 0.5f;
            return new Vector3(mid, 0f, mid);
        }

        // ------------------------------------------------------------- prefab

        /// <summary>Refresh just the two vehicle prefabs in place — same assets, so every scene and CityManager wiring keeps working.</summary>
        [MenuItem("Tools/Police Escape/Rebuild Vehicle Prefabs")]
        public static void RebuildVehiclePrefabs()
        {
            EnsureFolder(SceneFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            CarConfig carConfig = CreateOrLoad<CarConfig>(CarConfigPath);
            AI.PursuitSettings pursuitSettings = CreateOrLoad<AI.PursuitSettings>(PursuitSettingsPath);
            BuildCarPrefab(carConfig);
            BuildPoliceCarPrefab(carConfig, pursuitSettings);
            AssetDatabase.SaveAssets();
            Debug.Log("CarTestSceneBuilder: vehicle prefabs rebuilt from the Kenney models.");
        }

        static GameObject BuildCarPrefab(CarConfig config)
        {
            var root = new GameObject("TestCar");
            try
            {
                BuildVehicleBase(root, config, PlayerModelPath);
                root.AddComponent<CarInput>();
                root.AddComponent<CarRespawner>();
                return PrefabUtility.SaveAsPrefabAsset(root, CarPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Kenney police cruiser with flashing red/blue toppers on the
        /// modeled light bar, driven by PoliceCarInput — same chassis logic
        /// and CarConfig as the player car, only the driver differs.
        /// </summary>
        static GameObject BuildPoliceCarPrefab(CarConfig config, AI.PursuitSettings pursuitSettings)
        {
            Material redMat = CreateOrUpdateMaterial("TestPolice_Red", new Color(1f, 0.1f, 0.1f));
            Material blueMat = CreateOrUpdateMaterial("TestPolice_Blue", new Color(0.15f, 0.35f, 1f));

            var root = new GameObject("TestPoliceCar");
            try
            {
                BuildVehicleBase(root, config, PoliceModelPath);

                // Flashing toppers sitting on the model's roof, over the cabin.
                var chassis = root.GetComponent<BoxCollider>();
                float roofY = chassis.center.y + chassis.size.y * 0.5f;
                float barZ = chassis.center.z - chassis.size.z * 0.12f;
                GameObject red = AddBox(root.transform, "LightRed", redMat, new Vector3(-0.2f, roofY + 0.05f, barZ), new Vector3(0.38f, 0.12f, 0.3f), withCollider: false);
                GameObject blue = AddBox(root.transform, "LightBlue", blueMat, new Vector3(0.2f, roofY + 0.05f, barZ), new Vector3(0.38f, 0.12f, 0.3f), withCollider: false);
                var lights = root.AddComponent<AI.PoliceLights>();
                lights.redLight = red.GetComponent<Renderer>();
                lights.blueLight = blue.GetComponent<Renderer>();

                var driver = root.AddComponent<AI.PoliceCarInput>();
                driver.settings = pursuitSettings; // PatrolManager re-assigns at spawn; wired here so a hand-placed car works too

                return PrefabUtility.SaveAsPrefabAsset(root, PoliceCarPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Shared chassis built around a Kenney vehicle model: rigidbody, the
        /// model scaled to real-car length, wheel colliders placed at the
        /// model's actual wheel positions, and a chassis box fitted to the
        /// body. Each wheel mesh is re-pivoted onto a centered pivot — the
        /// kit's wheel pivots sit at the axle attach point, not the wheel
        /// center, and GetWorldPose spins the pivot.
        /// </summary>
        static CarController BuildVehicleBase(GameObject root, CarConfig config, string modelPath)
        {
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null)
                throw new System.InvalidOperationException("CarTestSceneBuilder: vehicle model missing at " + modelPath);

            var body = root.AddComponent<Rigidbody>();
            body.mass = config.mass;
            body.interpolation = RigidbodyInterpolation.Interpolate; // the camera reads the pose in LateUpdate
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Model, scaled so its length equals a real car's. The build root
            // sits at the origin unrotated, so world space == root-local space.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            // Unpack: the wheels get re-parented out below, which a connected
            // prefab instance would refuse (mesh/material references survive).
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            float scale = TargetCarLength / Mathf.Max(0.01f, CombinedBounds(model.transform).size.z);
            model.transform.localScale = Vector3.one * scale;

            var wheels = new GameObject("Wheels");
            wheels.transform.SetParent(root.transform, false);
            var pivots = new GameObject("WheelPivots");
            pivots.transform.SetParent(root.transform, false);

            var controller = root.AddComponent<CarController>();
            controller.config = config;
            (controller.frontLeft, controller.frontLeftVisual) = BuildModelWheel(model.transform, "wheel-front-left", wheels.transform, pivots.transform, config);
            (controller.frontRight, controller.frontRightVisual) = BuildModelWheel(model.transform, "wheel-front-right", wheels.transform, pivots.transform, config);
            (controller.rearLeft, controller.rearLeftVisual) = BuildModelWheel(model.transform, "wheel-back-left", wheels.transform, pivots.transform, config);
            (controller.rearRight, controller.rearRightVisual) = BuildModelWheel(model.transform, "wheel-back-right", wheels.transform, pivots.transform, config);

            // Chassis box from what's left of the model — the wheels were just
            // re-parented out, so this is the body (plus spoiler/grill details).
            Bounds bodyBounds = CombinedBounds(model.transform);
            var box = root.AddComponent<BoxCollider>();
            box.center = bodyBounds.center;
            box.size = bodyBounds.size;
            return controller;
        }

        /// <summary>
        /// Wire one of the model's wheels: a centered pivot (the visual the
        /// controller spins via GetWorldPose) wrapping the original mesh, and
        /// a WheelCollider at the wheel's true center with its radius read
        /// off the mesh bounds.
        /// </summary>
        static (WheelCollider collider, Transform pivot) BuildModelWheel(
            Transform model, string wheelName, Transform collidersRoot, Transform pivotsRoot, CarConfig config)
        {
            Transform mesh = model.Find(wheelName);
            if (mesh == null)
                throw new System.InvalidOperationException("CarTestSceneBuilder: '" + wheelName + "' not found on " + model.name);

            Bounds worldBounds = mesh.GetComponent<Renderer>().bounds;
            float radius = worldBounds.extents.y;
            Vector3 center = worldBounds.center;

            var pivot = new GameObject(wheelName + "-pivot").transform;
            pivot.SetParent(pivotsRoot, false);
            pivot.position = center;
            mesh.SetParent(pivot, true); // keep pose — the mesh now spins around its true center

            var colliderGo = new GameObject(wheelName + "-collider");
            colliderGo.transform.SetParent(collidersRoot, false);
            // Attach half a suspension travel above the resting wheel center.
            colliderGo.transform.position = center + Vector3.up * (config.suspensionDistance * 0.5f);
            var wheel = colliderGo.AddComponent<WheelCollider>();
            wheel.radius = radius;
            wheel.mass = 25f;
            wheel.suspensionDistance = config.suspensionDistance;
            return (wheel, pivot);
        }

        static Bounds CombinedBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        // -------------------------------------------------------------- scene

        static void BuildGround(Vector3 center)
        {
            Material groundMat = CreateOrUpdateMaterial("TestCar_Ground", new Color(0.14f, 0.15f, 0.14f));
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            // Top face 1 cm below road level: the chunk ground colliders carry the
            // city itself, and the tiny drop stops z-fighting with road meshes.
            ground.transform.position = new Vector3(center.x, -0.51f, center.z);
            ground.transform.localScale = new Vector3(6000f, 1f, 6000f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Traffic settings with a starter vehicle pool: filled only when the
        /// list is empty, so weight/flag tuning survives re-runs. Work
        /// vehicles (delivery, garbage truck) get the random-stop flag.
        /// </summary>
        static AI.TrafficSettings CreateOrLoadTrafficSettings(CarConfig carConfig)
        {
            var settings = CreateOrLoad<AI.TrafficSettings>(TrafficSettingsPath);
            if (settings.carConfig == null) settings.carConfig = carConfig;
            if (settings.vehicles == null || settings.vehicles.Count == 0)
            {
                (string file, float weight, bool stops)[] pool =
                {
                    ("sedan", 1.5f, false),
                    ("taxi", 1.2f, false),
                    ("van", 1f, false),
                    ("suv", 1f, false),
                    ("suv-luxury", 0.7f, false),
                    ("hatchback-sports", 1f, false),
                    ("truck", 0.6f, false),
                    ("truck-flat", 0.4f, false),
                    ("delivery", 0.8f, true),
                    ("garbage-truck", 0.5f, true),
                };
                foreach ((string file, float weight, bool stops) in pool)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehiclesFolder}/{file}.fbx");
                    if (model == null)
                    {
                        Debug.LogWarning($"CarTestSceneBuilder: traffic vehicle '{file}.fbx' not found — skipped.");
                        continue;
                    }
                    settings.vehicles.Add(new AI.TrafficVehicleDefinition { model = model, weight = weight, stopsRandomly = stops });
                }
            }
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>The level asset, seeded with the default two steps only when freshly created — an authored list is never touched.</summary>
        static LevelDefinition CreateOrLoadLevelDefinition()
        {
            bool existed = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath) != null;
            var level = CreateOrLoad<LevelDefinition>(LevelDefinitionPath);
            if (existed) return level;
            LevelDefinition.SeedDefaultObjectives(level);
            EditorUtility.SetDirty(level);
            return level;
        }

        static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static GameObject AddBox(Transform parent, string name, Material material, Vector3 localPosition, Vector3 localScale, bool withCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!withCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Material CreateOrUpdateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
