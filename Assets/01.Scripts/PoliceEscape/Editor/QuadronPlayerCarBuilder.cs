using System.Collections.Generic;
using System.Linq;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Re-skins the player car prefab with the Cyberpunk Megapolis Quadron
    /// (Models/Car/CP_Quadron.fbx, taken through the pack's own prefab so the
    /// LODGroup body and the emissive CP_Quadron material come along). Only
    /// the VISUAL RIG is rebuilt — the Model / Wheels / WheelPivots children
    /// the Kenney builder made: the prefab is edited in place through
    /// LoadPrefabContents, so the root components (rigidbody, CarController,
    /// chassis box, input, respawner, motion blur, mega-scale cheat) keep
    /// their identity, every scene reference to the asset stays valid, and
    /// hand-placed extras such as the PF_ROB driver survive (re-seated by the
    /// change in roof height). Kit facts the rig has to absorb: the Quadron is
    /// authored in REAL METRES (5.3 m long, so it stays at scale 1 — the
    /// Kenney models were stretched to 4.4 m), its length runs along X after
    /// Unity's FBX X-mirror with the long low bonnet at +X and the larger
    /// wheel pair at the tail, so the whole model is yawed by
    /// <see cref="ModelYaw"/> to face +Z; its four wheel objects are already
    /// pivoted on their axle (no re-centring needed) but carry LOD1/LOD2
    /// children inside the body's LODGroup, which are dropped so a wheel is
    /// one mesh the controller can spin; and its convex body MeshCollider is
    /// stripped because the root BoxCollider is the chassis every consumer
    /// (CarHealth, the police builder) reads. Wheels are classified by their
    /// position in car space rather than by the kit's 01–04 names.
    /// </summary>
    public static class QuadronPlayerCarBuilder
    {
        const string PlayerCarPrefabPath = "Assets/03.Prefabs/PoliceEscape/PlayerCar.prefab";
        const string TestCarPrefabPath = "Assets/03.Prefabs/PoliceEscape/TestCar.prefab";
        const string QuadronPrefabPath = "Assets/Cyberpunk_Megapolis/Prefabs/Car/CP_Quadron.prefab";
        const string QuadronModelPath = "Assets/Cyberpunk_Megapolis/Models/Car/CP_Quadron.fbx";

        /// <summary>
        /// Yaw that turns the kit's forward (+X once imported) into the
        /// controller's +Z. Flip to +90 if a re-exported model ever drives
        /// tail-first.
        /// </summary>
        const float ModelYaw = -90f;

        /// <summary>The kit is real metres — 1 keeps the Quadron its authored 5.3 m.</summary>
        const float ModelScale = 1f;

        /// <summary>
        /// Ride height added to the BODY (model + chassis box) over the
        /// wheels, which stay on their axles. Authored, the Quadron's floor is
        /// 0.14 m off the road with a metre of bonnet ahead of the front axle,
        /// so the chassis box's front edge met the bridge ramps before the
        /// wheels (raycasts, they climb anything) did — the Kenney sedan's box
        /// sat at 0.26 m. On top of the lift the box's underside is clamped
        /// to the axle line (<see cref="SwapModel"/>), so the car climbs on
        /// its wheels and only the visual floor stays low.
        /// </summary>
        const float BodyLift = 0.15f;

        /// <summary>The children the vehicle builders own; everything else on the root is a hand-placed extra.</summary>
        static readonly string[] RigChildren = { "Model", "Wheels", "WheelPivots" };

        [MenuItem("Tools/Police Escape/Player Car Uses Quadron")]
        public static void PlayerCarUsesQuadron() => SwapModel(PlayerCarPrefabPath);

        /// <summary>
        /// The CarTest scene's car. Note that "Rebuild Vehicle Prefabs" /
        /// "Create Car Test Scene" rebuild this prefab from the Kenney sedan —
        /// re-run this item after them.
        /// </summary>
        [MenuItem("Tools/Police Escape/Test Car Uses Quadron")]
        public static void TestCarUsesQuadron() => SwapModel(TestCarPrefabPath);

        /// <summary>Rebuild the visual rig of the car prefab at <paramref name="prefabPath"/> around the Quadron, in place.</summary>
        public static void SwapModel(string prefabPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(QuadronPrefabPath)
                         ?? AssetDatabase.LoadAssetAtPath<GameObject>(QuadronModelPath);
            if (source == null)
            {
                Debug.LogError($"QuadronPlayerCarBuilder: Quadron model missing at {QuadronPrefabPath} / {QuadronModelPath}.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError($"QuadronPlayerCarBuilder: car prefab missing at {prefabPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var controller = root.GetComponent<CarController>();
                var box = root.GetComponent<BoxCollider>();
                if (controller == null || controller.config == null || box == null)
                {
                    Debug.LogError($"QuadronPlayerCarBuilder: {prefabPath} needs a CarController with a config and a chassis BoxCollider on its root.");
                    return;
                }
                CarConfig config = controller.config;

                // Extras (the PF_ROB driver) sit relative to the old roof —
                // remembered so they can be re-seated on the new one.
                float oldRoof = box.center.y + box.size.y * 0.5f;
                var extras = new List<Transform>();
                foreach (Transform child in root.transform)
                    if (System.Array.IndexOf(RigChildren, child.name) < 0) extras.Add(child);

                foreach (string name in RigChildren)
                {
                    Transform old = root.transform.Find(name);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }

                // The contents root lives at the origin, unrotated, in its own
                // preview scene: world space == root-local space, and every
                // new object has to be created into that scene.
                Scene scene = root.scene;
                Transform model = NewChild("Model", root.transform, scene);
                model.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
                model.localScale = Vector3.one * ModelScale;

                var kit = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                // Unpack: the wheels get re-parented out below, which a
                // connected prefab instance would refuse (meshes/materials survive).
                PrefabUtility.UnpackPrefabInstance(kit, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                kit.name = source.name;
                kit.transform.SetParent(model, false);
                kit.transform.localPosition = Vector3.zero;
                kit.transform.localRotation = Quaternion.identity;
                kit.transform.localScale = Vector3.one;

                // The pack prefab carries a convex MeshCollider on the body; the
                // root box is the chassis, so nothing else may collide.
                foreach (Collider collider in kit.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);

                Transform wheels = NewChild("Wheels", root.transform, scene);
                Transform pivots = NewChild("WheelPivots", root.transform, scene);

                List<Transform> wheelMeshes = FindWheels(kit.transform);
                if (wheelMeshes.Count != 4)
                    throw new System.InvalidOperationException($"QuadronPlayerCarBuilder: expected 4 wheel objects on {source.name}, found {wheelMeshes.Count}.");

                // One mesh per wheel: drop the LOD1/LOD2 children so the
                // pivot spins a single renderer and the bounds are LOD0's.
                foreach (Transform wheel in wheelMeshes)
                    for (int i = wheel.childCount - 1; i >= 0; i--)
                        Object.DestroyImmediate(wheel.GetChild(i).gameObject);

                // Classify by car-space position now that the model faces +Z.
                Transform Pick(bool front, bool left) => wheelMeshes
                    .OrderByDescending(w => (front ? 1f : -1f) * w.GetComponent<Renderer>().bounds.center.z
                                          + (left ? -1f : 1f) * w.GetComponent<Renderer>().bounds.center.x)
                    .First();
                Transform frontLeft = Pick(true, true), frontRight = Pick(true, false);
                Transform rearLeft = Pick(false, true), rearRight = Pick(false, false);
                if (new[] { frontLeft, frontRight, rearLeft, rearRight }.Distinct().Count() != 4)
                    throw new System.InvalidOperationException("QuadronPlayerCarBuilder: could not tell the four wheels apart by position — is ModelYaw right?");

                (controller.frontLeft, controller.frontLeftVisual) = CarTestSceneBuilder.BuildWheelFromMesh(frontLeft, "wheel-front-left", wheels, pivots, config);
                (controller.frontRight, controller.frontRightVisual) = CarTestSceneBuilder.BuildWheelFromMesh(frontRight, "wheel-front-right", wheels, pivots, config);
                (controller.rearLeft, controller.rearLeftVisual) = CarTestSceneBuilder.BuildWheelFromMesh(rearLeft, "wheel-back-left", wheels, pivots, config);
                (controller.rearRight, controller.rearRightVisual) = CarTestSceneBuilder.BuildWheelFromMesh(rearRight, "wheel-back-right", wheels, pivots, config);

                // The body's LODGroup still lists the wheel renderers that just
                // left its subtree (and the destroyed LOD children as nulls).
                var lodGroup = kit.GetComponentInChildren<LODGroup>();
                if (lodGroup != null)
                {
                    LOD[] lods = lodGroup.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                        lods[i].renderers = lods[i].renderers
                            .Where(r => r != null && r.transform.IsChildOf(lodGroup.transform))
                            .ToArray();
                    lodGroup.SetLODs(lods);
                    lodGroup.RecalculateBounds();
                }

                // Lift the body now that the wheels have left it: they hang
                // off the pivots the controller places, so only the shell rises.
                model.localPosition = Vector3.up * BodyLift;

                // Chassis box from what's left under Model — the body and its
                // LODs — with its underside no lower than the wheel centres so
                // a ramp is met by the wheels, never by the box's front edge.
                Bounds bodyBounds = CarTestSceneBuilder.CombinedBounds(model);
                float axleY = Mathf.Max(controller.frontLeftVisual.position.y, controller.rearLeftVisual.position.y);
                float bottom = Mathf.Max(bodyBounds.min.y, axleY);
                float top = bodyBounds.max.y;
                box.center = new Vector3(bodyBounds.center.x, (top + bottom) * 0.5f, bodyBounds.center.z);
                box.size = new Vector3(bodyBounds.size.x, top - bottom, bodyBounds.size.z);

                float roofShift = box.center.y + box.size.y * 0.5f - oldRoof;
                foreach (Transform extra in extras)
                {
                    extra.localPosition += Vector3.up * roofShift;
                    Debug.Log($"QuadronPlayerCarBuilder: re-seated '{extra.name}' by {roofShift:F2} m to follow the new roof — check it by eye.", source);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"QuadronPlayerCarBuilder: {prefabPath} now wears the {source.name} " +
                          $"({bodyBounds.size.z:F2} m long, wheel radius {controller.frontLeft.radius:F2}/{controller.rearLeft.radius:F2} m, " +
                          $"body lifted {BodyLift:F2} m, chassis box underside at {bottom:F2} m).", source);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>The kit's wheel objects: direct children of the model root named *Wheel* that carry a renderer.</summary>
        static List<Transform> FindWheels(Transform kit)
        {
            var found = new List<Transform>();
            foreach (Transform child in kit)
                if (child.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) >= 0 && child.GetComponent<Renderer>() != null)
                    found.Add(child);
            return found;
        }

        /// <summary>An empty child created INTO the prefab-contents scene — a plain new GameObject lands in the active scene.</summary>
        static Transform NewChild(string name, Transform parent, Scene scene)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
