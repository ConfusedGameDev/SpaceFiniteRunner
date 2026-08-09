using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
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
        const string TestFolder = "Assets/99.Test/Jorge/InfiniteCity/Scripts/City/Test";
        const string ScenePath = TestFolder + "/CityTest.unity";
        const string SettingsPath = TestFolder + "/CityTestSettings.asset";

        [MenuItem("Tools/Police Escape/Create City Test Scene")]
        public static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(TestFolder);

            var pieces = new List<RoadPieceDefinition>
            {
                BuildPiece("TestRoad_Straight", EdgeMask.North | EdgeMask.South, new Color(0.3f, 0.9f, 1f)),
                BuildPiece("TestRoad_Corner", EdgeMask.North | EdgeMask.East, Color.yellow),
                BuildPiece("TestRoad_Tee", EdgeMask.North | EdgeMask.East | EdgeMask.West, Color.magenta),
                BuildPiece("TestRoad_Cross", EdgeMask.All, Color.white),
                BuildPiece("TestRoad_End", EdgeMask.North, Color.red),
            };

            CityGenerationSettings settings = CreateOrUpdateSettings(pieces);
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

                string path = $"{TestFolder}/{name}.prefab";
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

        // ------------------------------------------------------------ settings

        static CityGenerationSettings CreateOrUpdateSettings(List<RoadPieceDefinition> pieces)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(SettingsPath);
            bool isNew = settings == null;
            if (isNew) settings = ScriptableObject.CreateInstance<CityGenerationSettings>();

            settings.roadPieces = pieces;
            settings.cellSize = 20f;
            settings.pieceNativeSize = 1f; // test cubes are built on a 1 m footprint
            settings.scaleToCellSize = true;

            if (isNew) AssetDatabase.CreateAsset(settings, SettingsPath);
            else EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        // --------------------------------------------------------------- scene

        static void CreateScene(CityGenerationSettings settings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var managerGo = new GameObject("CityManager");
            var manager = managerGo.AddComponent<CityManager>();
            manager.settings = settings;

            // Overhead vantage so one glance shows the whole first chunk.
            var camera = Camera.main;
            if (camera != null)
            {
                float side = settings.chunkSizeInCells * settings.cellSize;
                camera.transform.position = new Vector3(side * 0.5f, side * 1.2f, -side * 0.25f);
                camera.transform.LookAt(new Vector3(side * 0.5f, 0f, side * 0.5f));
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, side * 4f);
            }

            manager.Recalculate();
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
