using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Re-skins the car prefabs with the Cyberpunk Megapolis cars
    /// (Models/Car, taken through the pack's own prefabs so the LODGroup
    /// bodies and emissive materials come along): the player car and the
    /// CarTest car wear the Quadron, the police car the Minivan, and the
    /// traffic pool is repointed at the Taxi FBX (rigged at runtime by
    /// VehicleRigBuilder, so no prefab). Only the VISUAL RIG of a prefab is
    /// rebuilt — the Model / Wheels / WheelPivots children the Kenney builder
    /// made: the prefab is edited in place through LoadPrefabContents, so the
    /// root components (rigidbody, CarController, chassis box, input or AI
    /// driver, respawner, PoliceLights) keep their identity, every scene
    /// reference to the asset stays valid, and hand-placed extras such as the
    /// PF_ROB driver survive (re-seated by the change in roof height). The
    /// police light bar is the one child rebuilt rather than re-seated: its
    /// boxes are sized off the chassis, so it is dropped and stamped again on
    /// the new roof through CarTestSceneBuilder.AddPoliceLightBar. What the
    /// kit demands (real metres, the -90 yaw, wheels by position, LOD and
    /// collider cleanup, the body lift) lives in <see cref="CyberpunkCarKit"/>,
    /// shared with the runtime rig so a traffic taxi is built the same way.
    /// </summary>
    public static class CyberpunkCarBuilder
    {
        const string PlayerCarPrefabPath = "Assets/03.Prefabs/PoliceEscape/PlayerCar.prefab";
        const string TestCarPrefabPath = "Assets/03.Prefabs/PoliceEscape/TestCar.prefab";
        const string PoliceCarPrefabPath = "Assets/03.Prefabs/PoliceEscape/TestPoliceCar.prefab";
        const string KitPrefabFolder = "Assets/Cyberpunk_Megapolis/Prefabs/Car";
        const string KitModelFolder = "Assets/Cyberpunk_Megapolis/Models/Car";

        /// <summary>One car of the kit: the pack prefab (LODGroup body + emissive material), with the raw FBX as the fallback.</summary>
        public readonly struct KitCar
        {
            public readonly string Name;
            public KitCar(string name) { Name = name; }
            public string PrefabPath => $"{KitPrefabFolder}/{Name}.prefab";
            public string ModelPath => $"{KitModelFolder}/{Name}.fbx";
            public GameObject Load() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                                        ?? AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        public static readonly KitCar Quadron = new("CP_Quadron");
        public static readonly KitCar Minivan = new("CP_Minivan");
        public static readonly KitCar Taxi = new("CP_Taxi");

        /// <summary>The children the vehicle builders own; everything else on the root is a hand-placed extra.</summary>
        static readonly string[] RigChildren = { "Model", "Wheels", "WheelPivots" };

        [MenuItem("Tools/Police Escape/Player Car Uses Quadron")]
        public static void PlayerCarUsesQuadron() => SwapModel(PlayerCarPrefabPath, Quadron);

        /// <summary>
        /// The CarTest scene's car. Note that "Rebuild Vehicle Prefabs" /
        /// "Create Car Test Scene" rebuild this prefab (and the police one)
        /// from the Kenney models — re-run these items after them.
        /// </summary>
        [MenuItem("Tools/Police Escape/Test Car Uses Quadron")]
        public static void TestCarUsesQuadron() => SwapModel(TestCarPrefabPath, Quadron);

        /// <summary>The police cruiser both chase scenes spawn: the Minivan under the same flashing bar, AI driver untouched.</summary>
        [MenuItem("Tools/Police Escape/Police Car Uses Minivan")]
        public static void PoliceCarUsesMinivan() => SwapModel(PoliceCarPrefabPath, Minivan);

        /// <summary>
        /// Point the traffic pool at the Taxi FBX alone — every civilian on
        /// the road becomes a cyberpunk cab, rigged at spawn with the kit's
        /// yaw and real-metre scale. "Traffic Uses Kenney Vehicles" is the
        /// way back; weights on the asset are yours to retune afterwards.
        /// </summary>
        [MenuItem("Tools/Police Escape/Traffic Uses Taxi")]
        public static void TrafficUsesTaxi()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TrafficSettings>(CarTestSceneBuilder.TrafficSettingsPath);
            var taxi = AssetDatabase.LoadAssetAtPath<GameObject>(Taxi.ModelPath);
            if (settings == null || taxi == null)
            {
                Debug.LogError($"CyberpunkCarBuilder: needs {CarTestSceneBuilder.TrafficSettingsPath} (run Create Car Test Scene) and {Taxi.ModelPath}.");
                return;
            }
            settings.vehicles ??= new List<TrafficVehicleDefinition>();
            settings.vehicles.Clear();
            CarTestSceneBuilder.TryIdentifyModel(taxi, out VehicleIdentity identity);
            settings.vehicles.Add(new TrafficVehicleDefinition
            {
                model = taxi,
                weight = 1f,
                stopsRandomly = false,
                modelYaw = CyberpunkCarKit.ModelYaw,
                scaleOverride = CyberpunkCarKit.ModelScale,
                kind = identity.kind,
                paint = identity.paint,
                color = identity.color,
            });
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"CyberpunkCarBuilder: traffic pool is now the {Taxi.Name} (yaw {CyberpunkCarKit.ModelYaw}, scale {CyberpunkCarKit.ModelScale}).", settings);
        }

        /// <summary>Restore the Kenney civilian pool (sedan, taxi, van … with their authored weights and stop flags).</summary>
        [MenuItem("Tools/Police Escape/Traffic Uses Kenney Vehicles")]
        public static void TrafficUsesKenneyVehicles()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TrafficSettings>(CarTestSceneBuilder.TrafficSettingsPath);
            if (settings == null)
            {
                Debug.LogError($"CyberpunkCarBuilder: traffic settings missing at {CarTestSceneBuilder.TrafficSettingsPath} — run Create Car Test Scene first.");
                return;
            }
            CarTestSceneBuilder.FillKenneyTrafficPool(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("CyberpunkCarBuilder: traffic pool is the Kenney vehicles again.", settings);
        }

        /// <summary>Rebuild the visual rig of the car prefab at <paramref name="prefabPath"/> around <paramref name="car"/>, in place.</summary>
        public static void SwapModel(string prefabPath, KitCar car)
        {
            GameObject source = car.Load();
            if (source == null)
            {
                Debug.LogError($"CyberpunkCarBuilder: {car.Name} missing at {car.PrefabPath} / {car.ModelPath}.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError($"CyberpunkCarBuilder: car prefab missing at {prefabPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var controller = root.GetComponent<CarController>();
                var box = root.GetComponent<BoxCollider>();
                if (controller == null || controller.config == null || box == null)
                {
                    Debug.LogError($"CyberpunkCarBuilder: {prefabPath} needs a CarController with a config and a chassis BoxCollider on its root.");
                    return;
                }
                CarConfig config = controller.config;

                // The light bar is rebuilt on the new roof, not re-seated: its
                // boxes are placed off the chassis, which is about to change.
                var lights = root.GetComponent<PoliceLights>();
                var lightObjects = new List<GameObject>();
                if (lights != null)
                {
                    if (lights.redLight != null) lightObjects.Add(lights.redLight.gameObject);
                    if (lights.blueLight != null) lightObjects.Add(lights.blueLight.gameObject);
                }

                // Extras (the PF_ROB driver) sit relative to the old roof —
                // remembered so they can be re-seated on the new one.
                float oldRoof = box.center.y + box.size.y * 0.5f;
                var extras = new List<Transform>();
                foreach (Transform child in root.transform)
                    if (System.Array.IndexOf(RigChildren, child.name) < 0 && !lightObjects.Contains(child.gameObject))
                        extras.Add(child);

                foreach (string name in RigChildren)
                {
                    Transform old = root.transform.Find(name);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }
                foreach (GameObject light in lightObjects) Object.DestroyImmediate(light);

                // The contents root lives at the origin, unrotated, in its own
                // preview scene: world space == root-local space, and every
                // new object has to be created into that scene.
                Scene scene = root.scene;
                Transform model = NewChild("Model", root.transform, scene);
                model.localRotation = Quaternion.Euler(0f, CyberpunkCarKit.ModelYaw, 0f);
                model.localScale = Vector3.one * CyberpunkCarKit.ModelScale;

                var kit = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                // Unpack: the wheels get re-parented out below, which a
                // connected prefab instance would refuse (meshes/materials survive).
                PrefabUtility.UnpackPrefabInstance(kit, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                kit.name = source.name;
                kit.transform.SetParent(model, false);
                kit.transform.localPosition = Vector3.zero;
                kit.transform.localRotation = Quaternion.identity;
                kit.transform.localScale = Vector3.one;

                CyberpunkCarKit.StripColliders(kit);
                List<Transform> wheelMeshes = CyberpunkCarKit.FindWheels(kit.transform);
                if (wheelMeshes.Count != 4)
                    throw new System.InvalidOperationException($"CyberpunkCarBuilder: expected 4 wheel objects on {source.name}, found {wheelMeshes.Count}.");
                CyberpunkCarKit.FlattenWheels(wheelMeshes);
                if (!CyberpunkCarKit.TryClassifyWheels(wheelMeshes, root.transform,
                        out Transform frontLeft, out Transform frontRight, out Transform rearLeft, out Transform rearRight))
                    throw new System.InvalidOperationException($"CyberpunkCarBuilder: could not tell {source.name}'s four wheels apart by position — is CyberpunkCarKit.ModelYaw right?");

                Transform wheels = NewChild("Wheels", root.transform, scene);
                Transform pivots = NewChild("WheelPivots", root.transform, scene);
                (controller.frontLeft, controller.frontLeftVisual) = CarTestSceneBuilder.BuildWheelFromMesh(frontLeft, "wheel-front-left", wheels, pivots, config);
                (controller.frontRight, controller.frontRightVisual) = CarTestSceneBuilder.BuildWheelFromMesh(frontRight, "wheel-front-right", wheels, pivots, config);
                (controller.rearLeft, controller.rearLeftVisual) = CarTestSceneBuilder.BuildWheelFromMesh(rearLeft, "wheel-back-left", wheels, pivots, config);
                (controller.rearRight, controller.rearRightVisual) = CarTestSceneBuilder.BuildWheelFromMesh(rearRight, "wheel-back-right", wheels, pivots, config);
                CyberpunkCarKit.PruneLodGroup(kit);

                // Lift the body now that the wheels have left it: they hang
                // off the pivots the controller places, so only the shell rises.
                model.localPosition = Vector3.up * CyberpunkCarKit.BodyLift;

                // Chassis box from what's left under Model — the body and its
                // LODs — with its underside no lower than the wheel centres so
                // a ramp is met by the wheels, never by the box's front edge.
                Bounds bodyBounds = CarTestSceneBuilder.CombinedBounds(model);
                float axleY = Mathf.Max(controller.frontLeftVisual.position.y, controller.rearLeftVisual.position.y);
                float bottom = Mathf.Max(bodyBounds.min.y, axleY);
                float top = bodyBounds.max.y;
                box.center = new Vector3(bodyBounds.center.x, (top + bottom) * 0.5f, bodyBounds.center.z);
                box.size = new Vector3(bodyBounds.size.x, top - bottom, bodyBounds.size.z);

                if (lights != null) CarTestSceneBuilder.AddPoliceLightBar(root, box, lights);

                // The kit car names the prefab's identity; an authored paint
                // on the prefab is kept, the kind follows the model.
                if (CarTestSceneBuilder.TryIdentifyModel(source, out VehicleIdentity identity))
                    controller.identity = controller.identity.paint != VehiclePaint.Unknown
                        ? new VehicleIdentity(identity.kind, controller.identity.paint, controller.identity.color)
                        : identity;

                float roofShift = box.center.y + box.size.y * 0.5f - oldRoof;
                foreach (Transform extra in extras)
                {
                    extra.localPosition += Vector3.up * roofShift;
                    Debug.Log($"CyberpunkCarBuilder: re-seated '{extra.name}' by {roofShift:F2} m to follow the new roof — check it by eye.", source);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"CyberpunkCarBuilder: {prefabPath} now wears the {source.name} " +
                          $"({bodyBounds.size.z:F2} m long, wheel radius {controller.frontLeft.radius:F2}/{controller.rearLeft.radius:F2} m, " +
                          $"body lifted {CyberpunkCarKit.BodyLift:F2} m, chassis box underside at {bottom:F2} m" +
                          (lights != null ? ", light bar rebuilt on the new roof" : "") + ").", source);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
