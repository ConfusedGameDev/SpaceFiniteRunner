using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Population;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click test harness for the road generator: builds five primitive
    /// road pieces (straight/corner/T/cross/dead-end) whose socket masks are
    /// correct *by construction* — each connected edge gets a visible strip —
    /// plus a settings asset wired to them and a saved test scene with a
    /// CityManager. Because the test pieces can't have wrong orientations,
    /// any mismatch on screen is a generator bug, not an asset guess. Colors
    /// match the CityManager gizmo palette (cyan straight, yellow corner,
    /// magenta T, white cross, red dead end).
    /// </summary>
    public static class CityTestSceneBuilder
    {
        const string SceneFolder = "Assets/05.Scenes";
        const string DataFolder = "Assets/04.Data/InfiniteCity";
        const string PrefabFolder = "Assets/03.Prefabs/PoliceEscape";
        const string MaterialFolder = "Assets/02.Art/02.Materials/InfiniteCity/Test";
        const string ScenePath = SceneFolder + "/CityTest.unity";
        const string SettingsPath = DataFolder + "/CityTestSettings.asset";
        const string BuildingSetPath = DataFolder + "/CityTestBuildingSet.asset";
        // Weather is shared by both games, so it lives with the other Resources
        // singletons rather than in this scene's data folder.
        const string RainSettingsPath = "Assets/04.Data/Resources/FiniteRunner_Rain.asset";

        [MenuItem("Tools/Police Escape/Create City Test Scene")]
        public static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(SceneFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            var pieces = new List<RoadPieceDefinition>
            {
                BuildPiece("TestRoad_Straight", EdgeMask.North | EdgeMask.South, new Color(0.3f, 0.9f, 1f)),
                BuildPiece("TestRoad_Corner", EdgeMask.North | EdgeMask.East, Color.yellow),
                BuildPiece("TestRoad_Tee", EdgeMask.North | EdgeMask.East | EdgeMask.West, Color.magenta),
                BuildPiece("TestRoad_Cross", EdgeMask.All, Color.white),
                BuildPiece("TestRoad_End", EdgeMask.North, Color.red),
            };

            var buildings = new List<BuildingDefinition>
            {
                BuildBuilding("TestBuilding_1x1", 1, 1, 1.5f, 3f, new Color(0.55f, 0.65f, 0.8f)),
                BuildBuilding("TestBuilding_1x1_Tall", 1, 1, 4f, 1f, new Color(0.4f, 0.5f, 0.9f)),
                BuildBuilding("TestBuilding_2x1", 2, 1, 2f, 2f, new Color(0.8f, 0.6f, 0.45f)),
                BuildBuilding("TestBuilding_2x2", 2, 2, 3f, 1f, new Color(0.6f, 0.8f, 0.55f)),
            };

            CityGenerationSettings settings = CreateOrUpdateSettings(pieces);
            settings.buildingSet = CreateOrUpdateBuildingSet(buildings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            CreateScene(settings);

            Debug.Log("CityTestSceneBuilder: test scene ready — press Recalculate on the CityManager (or just look: it already ran once).");
        }

        // ------------------------------------------------------------- prefabs

        static RoadPieceDefinition BuildPiece(string name, EdgeMask mask, Color roadColor)
        {
            Material asphalt = CreateOrUpdateMaterial("TestRoad_Base", new Color(0.18f, 0.18f, 0.2f));
            Material road = CreateOrUpdateMaterial(name + "_Road", roadColor);

            var root = new GameObject(name);
            try
            {
                // Full-cell base slab, top surface at y = 0.
                AddCube(root.transform, "Base", asphalt,
                    new Vector3(0f, -0.05f, 0f), new Vector3(1f, 0.1f, 1f));

                // Center pad + one strip per connected edge — the mask made visible.
                AddCube(root.transform, "Center", road,
                    new Vector3(0f, 0.01f, 0f), new Vector3(0.5f, 0.04f, 0.5f));
                for (int dir = 0; dir < 4; dir++)
                {
                    if ((mask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                    Vector2Int o = EdgeMaskUtility.Offset(dir);
                    AddCube(root.transform, $"Stub_{(EdgeMask)(1 << dir)}", road,
                        new Vector3(o.x * 0.375f, 0.01f, o.y * 0.375f),
                        new Vector3(o.x == 0 ? 0.5f : 0.25f, 0.04f, o.y == 0 ? 0.5f : 0.25f));
                }

                string path = $"{PrefabFolder}/{name}.prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return new RoadPieceDefinition { prefab = prefab, connectionMask = mask, weight = 1f };
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void AddCube(Transform parent, string name, Material material, Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
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

        // ------------------------------------------------------- test buildings

        /// <summary>
        /// A colored box on a footprint-sized slab, with a small dark "door"
        /// block on its +Z front face — so which way a building faces is
        /// obvious from above. Built on a 1 m-per-cell footprint like the
        /// road pieces.
        /// </summary>
        static BuildingDefinition BuildBuilding(string name, int w, int h, float height, float weight, Color color)
        {
            Material wall = CreateOrUpdateMaterial(name + "_Wall", color);
            Material door = CreateOrUpdateMaterial("TestBuilding_Door", new Color(0.1f, 0.1f, 0.1f));

            var root = new GameObject(name);
            try
            {
                // Body fills ~80% of the footprint so neighbours never touch.
                AddCube(root.transform, "Body", wall,
                    new Vector3(0f, height * 0.5f, 0f), new Vector3(w * 0.8f, height, h * 0.8f));
                AddCube(root.transform, "Door", door,
                    new Vector3(0f, 0.2f, h * 0.4f + 0.02f), new Vector3(0.25f, 0.4f, 0.06f));

                string path = $"{PrefabFolder}/{name}.prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return new BuildingDefinition
                {
                    prefab = prefab,
                    weight = weight,
                    footprintInCells = new Vector2Int(w, h),
                    positionJitter = 0.05f,
                    scaleJitter = 0.1f,
                    heightJitter = 0.25f,
                };
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static BuildingSet CreateOrUpdateBuildingSet(List<BuildingDefinition> buildings)
        {
            var set = AssetDatabase.LoadAssetAtPath<BuildingSet>(BuildingSetPath);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<BuildingSet>();

            set.buildings = buildings;
            set.nativeCellSize = 1f; // test boxes are built on a 1 m-per-cell footprint
            set.density = 0.9f;

            if (isNew) AssetDatabase.CreateAsset(set, BuildingSetPath);
            else EditorUtility.SetDirty(set);
            return set;
        }

        // ------------------------------------------------------------ settings

        static CityGenerationSettings CreateOrUpdateSettings(List<RoadPieceDefinition> pieces)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(SettingsPath);
            bool isNew = settings == null;
            if (isNew) settings = ScriptableObject.CreateInstance<CityGenerationSettings>();

            // Only a fresh asset gets the primitive pieces and the 20 m cell:
            // re-running the builder must not clobber a Kenney road set or a
            // tuned cell size on the live settings (Tools → Police Escape →
            // Use Box Road Pieces switches back on purpose).
            if (isNew)
            {
                settings.roadPieces = pieces;
                settings.cellSize = 20f;
                settings.pieceNativeSize = 1f; // test cubes are built on a 1 m footprint
                settings.scaleToCellSize = true;
            }

            if (isNew) AssetDatabase.CreateAsset(settings, SettingsPath);
            else EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        // --------------------------------------------------------------- scene

        static void CreateScene(CityGenerationSettings settings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // The baked city prefab — baked on demand from the definition.
            CityDefinition definition = CityBaker.EnsureDefinition(DataFolder + "/CityDefinition.asset", settings);
            GameObject cityPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CityBaker.DefaultPrefabPath);
            if (cityPrefab == null) cityPrefab = CityBaker.BakeCity(definition);
            CityRoot cityRoot = null;
            if (cityPrefab != null)
            {
                var cityInstance = (GameObject)PrefabUtility.InstantiatePrefab(cityPrefab);
                cityInstance.transform.position = Vector3.zero;
                cityRoot = cityInstance.GetComponent<CityRoot>();
            }

            var managerGo = new GameObject("CityManager");
            var manager = managerGo.AddComponent<CityManager>();
            manager.settings = settings;
            manager.cityRoot = cityRoot;

            // Wire the car test assets when they exist, so the Create Car
            // button and the police fleet work in this scene too (assets are
            // built by CarTestSceneBuilder).
            manager.carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/TestCar.prefab");
            manager.orbitCameraSettings = AssetDatabase.LoadAssetAtPath<Vehicles.OrbitCameraSettings>(DataFolder + "/TestOrbitCameraSettings.asset");
            manager.policeCarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/TestPoliceCar.prefab");
            manager.pursuitSettings = AssetDatabase.LoadAssetAtPath<AI.PursuitSettings>(DataFolder + "/TestPursuitSettings.asset");
            manager.minimapSettings = AssetDatabase.LoadAssetAtPath<UI.MinimapSettings>(DataFolder + "/TestMinimapSettings.asset");
            manager.speedometerSettings = AssetDatabase.LoadAssetAtPath<UI.SpeedometerSettings>(DataFolder + "/TestSpeedometerSettings.asset");
            manager.trafficSettings = AssetDatabase.LoadAssetAtPath<AI.TrafficSettings>(DataFolder + "/TestTrafficSettings.asset");
            manager.rainSettings = AssetDatabase.LoadAssetAtPath<FX.RainSettings>(RainSettingsPath);

            // Weather as a real scene object, so the downpour can be tuned (and
            // previewed) before pressing play — the CityManager only switches
            // it on and hands it an override asset.
            var rain = new GameObject("RainSystem").AddComponent<FX.RainSystem>();
            rain.settings = AssetDatabase.LoadAssetAtPath<FX.RainSettings>(RainSettingsPath);

            // Overhead vantage so one glance shows the whole city.
            var camera = Camera.main;
            if (camera != null)
            {
                float side = cityRoot != null ? Mathf.Max(cityRoot.CitySizeX, cityRoot.CitySizeZ) : 500f;
                camera.transform.position = new Vector3(side * 0.5f, side * 1.2f, -side * 0.25f);
                camera.transform.LookAt(new Vector3(side * 0.5f, 0f, side * 0.5f));
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, side * 4f);
            }

            Selection.activeGameObject = managerGo;
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ------------------------------------------------------------- helpers

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
