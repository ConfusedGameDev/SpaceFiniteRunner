using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click test harness for M3: builds a primitive WheelCollider car
    /// prefab (box body, cylinder wheels), a CarConfig and ChaseCameraSettings
    /// asset, and a saved scene with the city test layout (if its settings
    /// asset exists), a safety-net ground collider and a PlayerCarSpawner.
    /// The car is spawned at runtime, not baked in — every play regenerates
    /// the city with a fresh seed, so the spawner picks a road cell then and
    /// attaches the chase camera. Re-running rebuilds prefab and scene but
    /// leaves existing config assets untouched, so handling tuning survives.
    /// Drive with WASD/arrows or gamepad; Space/South = handbrake, R/North =
    /// respawn.
    /// </summary>
    public static class CarTestSceneBuilder
    {
        const string TestFolder = "Assets/99.Test/Jorge/InfiniteCity/Scripts/Vehicles/Test";
        const string ScenePath = TestFolder + "/CarTest.unity";
        const string CarPrefabPath = TestFolder + "/TestCar.prefab";
        const string PoliceCarPrefabPath = TestFolder + "/TestPoliceCar.prefab";
        const string CarConfigPath = TestFolder + "/TestCarConfig.asset";
        const string CameraSettingsPath = TestFolder + "/TestChaseCameraSettings.asset";
        const string PursuitSettingsPath = TestFolder + "/TestPursuitSettings.asset";
        const string MinimapSettingsPath = TestFolder + "/TestMinimapSettings.asset";
        const string CitySettingsPath = "Assets/99.Test/Jorge/InfiniteCity/Scripts/City/Test/CityTestSettings.asset";

        [MenuItem("Tools/Police Escape/Create Car Test Scene")]
        public static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(TestFolder);

            // Existing config assets are kept as-is — only created when missing —
            // so re-running the builder never wipes handling tuning.
            CarConfig carConfig = CreateOrLoad<CarConfig>(CarConfigPath);
            ChaseCameraSettings cameraSettings = CreateOrLoad<ChaseCameraSettings>(CameraSettingsPath);
            AI.PursuitSettings pursuitSettings = CreateOrLoad<AI.PursuitSettings>(PursuitSettingsPath);
            UI.MinimapSettings minimapSettings = CreateOrLoad<UI.MinimapSettings>(MinimapSettingsPath);
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
                city.chaseCameraSettings = cameraSettings;
                city.policeCarPrefab = policeCarPrefab;  // wired police fields spawn the PatrolManager at play
                city.pursuitSettings = pursuitSettings;
                city.minimapSettings = minimapSettings;  // wired minimap settings spawn the radar at play
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

        static GameObject BuildCarPrefab(CarConfig config)
        {
            Material bodyMat = CreateOrUpdateMaterial("TestCar_Body", new Color(0.85f, 0.25f, 0.2f));
            Material cabinMat = CreateOrUpdateMaterial("TestCar_Cabin", new Color(0.2f, 0.3f, 0.4f));
            Material wheelMat = CreateOrUpdateMaterial("TestCar_Wheel", new Color(0.08f, 0.08f, 0.08f));

            var root = new GameObject("TestCar");
            try
            {
                BuildVehicleBase(root, config, bodyMat, cabinMat, wheelMat);
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
        /// White-and-dark cruiser with a flashing red/blue light bar, driven
        /// by PoliceCarInput — same chassis and CarConfig as the player car,
        /// only the driver differs.
        /// </summary>
        static GameObject BuildPoliceCarPrefab(CarConfig config, AI.PursuitSettings pursuitSettings)
        {
            Material bodyMat = CreateOrUpdateMaterial("TestPolice_Body", new Color(0.92f, 0.92f, 0.95f));
            Material cabinMat = CreateOrUpdateMaterial("TestPolice_Cabin", new Color(0.1f, 0.1f, 0.14f));
            Material wheelMat = CreateOrUpdateMaterial("TestCar_Wheel", new Color(0.08f, 0.08f, 0.08f));
            Material redMat = CreateOrUpdateMaterial("TestPolice_Red", new Color(1f, 0.1f, 0.1f));
            Material blueMat = CreateOrUpdateMaterial("TestPolice_Blue", new Color(0.15f, 0.35f, 1f));

            var root = new GameObject("TestPoliceCar");
            try
            {
                BuildVehicleBase(root, config, bodyMat, cabinMat, wheelMat);

                // Light bar on the cabin roof.
                AddBox(root.transform, "LightBarBase", cabinMat, new Vector3(0f, 1.26f, -0.35f), new Vector3(0.9f, 0.12f, 0.4f), withCollider: false);
                GameObject red = AddBox(root.transform, "LightRed", redMat, new Vector3(-0.22f, 1.4f, -0.35f), new Vector3(0.4f, 0.16f, 0.34f), withCollider: false);
                GameObject blue = AddBox(root.transform, "LightBlue", blueMat, new Vector3(0.22f, 1.4f, -0.35f), new Vector3(0.4f, 0.16f, 0.34f), withCollider: false);
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

        /// <summary>Shared chassis: rigidbody, body/cabin boxes, four wheels + visuals, CarController wired to the config.</summary>
        static CarController BuildVehicleBase(GameObject root, CarConfig config, Material bodyMat, Material cabinMat, Material wheelMat)
        {
            var body = root.AddComponent<Rigidbody>();
            body.mass = config.mass;
            body.interpolation = RigidbodyInterpolation.Interpolate; // ChaseCamera reads the pose in LateUpdate
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Chassis: one box collider on the body, cabin is visual only.
            // Body is narrower than the wheel track so the wheels show.
            AddBox(root.transform, "Body", bodyMat, new Vector3(0f, 0.55f, 0f), new Vector3(1.6f, 0.6f, 4.2f), withCollider: true);
            AddBox(root.transform, "Cabin", cabinMat, new Vector3(0f, 1f, -0.35f), new Vector3(1.4f, 0.4f, 1.9f), withCollider: false);

            var wheels = new GameObject("Wheels");
            wheels.transform.SetParent(root.transform, false);

            WheelCollider fl = BuildWheel(wheels.transform, "FL", new Vector3(-0.85f, 0.45f, 1.35f));
            WheelCollider fr = BuildWheel(wheels.transform, "FR", new Vector3(0.85f, 0.45f, 1.35f));
            WheelCollider rl = BuildWheel(wheels.transform, "RL", new Vector3(-0.85f, 0.45f, -1.35f));
            WheelCollider rr = BuildWheel(wheels.transform, "RR", new Vector3(0.85f, 0.45f, -1.35f));

            var visuals = new GameObject("WheelVisuals");
            visuals.transform.SetParent(root.transform, false);

            var controller = root.AddComponent<CarController>();
            controller.config = config;
            controller.frontLeft = fl;
            controller.frontRight = fr;
            controller.rearLeft = rl;
            controller.rearRight = rr;
            controller.frontLeftVisual = BuildWheelVisual(visuals.transform, "FL_Visual", fl.transform.localPosition, wheelMat);
            controller.frontRightVisual = BuildWheelVisual(visuals.transform, "FR_Visual", fr.transform.localPosition, wheelMat);
            controller.rearLeftVisual = BuildWheelVisual(visuals.transform, "RL_Visual", rl.transform.localPosition, wheelMat);
            controller.rearRightVisual = BuildWheelVisual(visuals.transform, "RR_Visual", rr.transform.localPosition, wheelMat);
            return controller;
        }

        static WheelCollider BuildWheel(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject("Wheel_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var wheel = go.AddComponent<WheelCollider>();
            wheel.radius = 0.35f;
            wheel.mass = 25f;
            wheel.suspensionDistance = 0.25f; // baseline; CarController re-applies from config each step
            return wheel;
        }

        /// <summary>
        /// Pivot synced by CarController via GetWorldPose, with the cylinder
        /// mesh as a rotated child so its axis points along the axle.
        /// </summary>
        static Transform BuildWheelVisual(Transform parent, string name, Vector3 localPosition, Material material)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPosition;

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mesh.name = "Mesh";
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.SetParent(pivot.transform, false);
            mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            mesh.transform.localScale = new Vector3(0.7f, 0.125f, 0.7f); // radius 0.35, width 0.25
            mesh.GetComponent<MeshRenderer>().sharedMaterial = material;
            return pivot.transform;
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
            string path = $"{TestFolder}/{name}.mat";
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
