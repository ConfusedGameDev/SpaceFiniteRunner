using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Installs the boost-orb sparks on the ship, never overwriting: Read/Write
    /// on the ship model's FBX (the sparks sample its triangles), the
    /// designer-owned <c>PF_ShipBoostSparks</c> VFX prefab
    /// (<see cref="MeshSurfaceSparks"/> + <see cref="BoostOrbSparks"/>, with
    /// RevolverBallad's KakoSparks look and a spark size scaled to the model),
    /// and its instance NESTED in <c>PF_Ship</c> under the ROOT with its
    /// <c>source</c> on the ship's visual — never under the visual, whose
    /// meshes and renderers the dash ghosts, the respawn blink and the livery
    /// walk. The material (<c>ShipBoostSparks_Mat</c>, the KakoSparks sheet on
    /// URP Particles/Unlit, additive) is an authored asset this only loads.
    /// </summary>
    public static class ShipBoostSparksBuilder
    {
        public const string ShipPrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_Ship.prefab";
        public const string PrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_ShipBoostSparks.prefab";
        public const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/ShipBoostSparks_Mat.mat";
        const string InstanceName = "BoostSparks";

        [MenuItem("Tools/FiniteRunner/Install Ship Boost Sparks")]
        public static void Install()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError($"ShipBoostSparksBuilder: no material at {MaterialPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            if (root == null)
            {
                Debug.LogError($"ShipBoostSparksBuilder: no prefab at {ShipPrefabPath}.");
                return;
            }

            try
            {
                Transform model = ShipVisual(root);
                if (model == null)
                {
                    Debug.LogError("ShipBoostSparksBuilder: PF_Ship's ShipMotor has no visual to put the sparks on.");
                    return;
                }
                EnableReadWrite(model);

                if (root.GetComponentInChildren<BoostOrbSparks>(true) != null)
                {
                    Debug.Log("ShipBoostSparksBuilder: PF_Ship already carries the boost sparks.");
                    return;
                }

                GameObject prefab = CreateOrLoadPrefab(material, ModelSize(model));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.name = InstanceName;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                var so = new SerializedObject(instance.GetComponent<MeshSurfaceSparks>());
                so.FindProperty("source").objectReferenceValue = model;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
                Debug.Log($"ShipBoostSparksBuilder: {PrefabPath} nested in {ShipPrefabPath}, crawling over '{model.name}'.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform ShipVisual(GameObject root)
        {
            var motor = root.GetComponent<ShipMotor>();
            if (motor == null) return null;
            return new SerializedObject(motor).FindProperty("visual").objectReferenceValue as Transform;
        }

        // The sparks read the model's triangles on the CPU.
        static void EnableReadWrite(Transform model)
        {
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.sharedMesh.isReadable) continue;
                string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    Debug.Log($"ShipBoostSparksBuilder: Read/Write enabled on {path}.");
                }
            }
        }

        // The longest side of the model's bounds, metres.
        static float ModelSize(Transform model)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            return any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 10f;
        }

        /// <summary>The spark prefab — loaded when it exists, otherwise made with the KakoSparks look, sized for a model <paramref name="modelSize"/> metres long.</summary>
        public static GameObject CreateOrLoadPrefab(Material material, float modelSize)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            var go = new GameObject("PF_ShipBoostSparks");
            try
            {
                var sparks = go.AddComponent<MeshSurfaceSparks>();
                go.AddComponent<BoostOrbSparks>();
                var so = new SerializedObject(sparks);
                so.FindProperty("material").objectReferenceValue = material;
                // KakoSparks sized 0.9–1.5 m on a turret seen close up; behind a ~20 m
                // ship at chase distance they vanish, so they are scaled to the model
                // (8–13 m on the shipped one), turned toward the camera and brightened.
                so.FindProperty("size").vector2Value = new Vector2(0.4f, 0.65f) * modelSize;
                so.FindProperty("surfaceOffset").floatValue = 0.01f * modelSize;
                so.FindProperty("cameraFacing").floatValue = 0.85f;
                so.FindProperty("tint").colorValue = new Color(6f, 9f, 13.5f, 1f);
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
