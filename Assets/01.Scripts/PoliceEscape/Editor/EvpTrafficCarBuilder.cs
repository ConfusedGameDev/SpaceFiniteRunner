using System.Collections.Generic;
using System.Linq;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using EVP;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Turns the EVP5 demo vehicles (Assets/00.Plugins/EVP5/Prefabs/Vehicle —
    /// bus, L200 pickups, sport coupes, each a complete Edy's car with its own
    /// hand-tuned VehicleController) into traffic NPC prefabs, IN PLACE, and
    /// lists them in the traffic pool. The conversion keeps what makes each
    /// car itself — rigidbody mass, wheel colliders, the authored
    /// VehicleController and its wheel list (EvpCarBackend reuses it in
    /// "authored" mode instead of stamping the L200 baseline) — and strips
    /// what an NPC must not carry: the demo's player/random input (it would
    /// overwrite the AI's inputs every physics step), audio (AI cars stay
    /// silent), damage/tire/visual add-ons, the driver ragdoll pivot (a second
    /// rigidbody) and the body mesh colliders, replaced by the root chassis
    /// BoxCollider every game system reads (CarHealth's emitter anchor, the
    /// escape arrow, IsCellClear). CarController is wired to the four EVP
    /// wheel colliders and wheel transforms — classified by position, not by
    /// array order — and TrafficCarInput makes it a civilian. Re-running is
    /// safe: a prefab that already carries a CarController only has its
    /// identity and config refreshed. The "Vehicle Original" sibling folder
    /// is the pristine backup of the stock prefabs.
    /// </summary>
    public static class EvpTrafficCarBuilder
    {
        public const string VehicleFolder = "Assets/00.Plugins/EVP5/Prefabs/Vehicle";

        /// <summary>One demo car: its prefab name, what it is, and how often traffic should pick it.</summary>
        readonly struct Entry
        {
            public readonly string Name;
            public readonly VehicleIdentity Identity;
            public readonly float Weight;

            public Entry(string name, VehicleKind kind, VehiclePaint paint, string hex, float weight)
            {
                Name = name;
                ColorUtility.TryParseHtmlString(hex, out Color color);
                Identity = new VehicleIdentity(kind, paint, color);
                Weight = weight;
            }

            public string Path => $"{VehicleFolder}/{Name}.prefab";
        }

        // Paint swatches read off each prefab's paint material (the L200s are
        // textured, so theirs are representative picks). The "Blue" coupe is
        // really a steel silver-blue — named after its prefab regardless.
        static readonly Entry[] Table =
        {
            new("Sport Coupe-Red", VehicleKind.SportCoupe, VehiclePaint.Red, "#C45555", 1f),
            new("Sport Coupe-Blue", VehicleKind.SportCoupe, VehiclePaint.Blue, "#AFBBC4", 1f),
            new("Sport Coupe Drift-Blue", VehicleKind.SportCoupeDrift, VehiclePaint.Blue, "#0088B4", 1f),
            new("L200-Red", VehicleKind.L200, VehiclePaint.Red, "#B8342A", 1f),
            new("L200-Blue", VehicleKind.L200, VehiclePaint.Blue, "#2E5FB5", 1f),
            new("L200-Green", VehicleKind.L200, VehiclePaint.Green, "#3E8F45", 1f),
            new("Bus-Green", VehicleKind.Bus, VehiclePaint.Green, "#6CD57E", 0.4f),
        };

        /// <summary>
        /// EVP add-ons an NPC must not carry (the VehicleController itself
        /// stays). The authored VehicleDamage goes too: its collider list
        /// points at the body MeshColliders removed below, and the runtime
        /// <see cref="CarDeformation"/> re-adds one wired to the surviving
        /// body meshes when the EVP backend installs.
        /// </summary>
        static readonly System.Type[] StrippedComponents =
        {
            typeof(VehicleStandardInput), typeof(VehicleRandomInput), typeof(VehicleAudio),
            typeof(VehicleTireEffects), typeof(VehicleDamage), typeof(VehicleViewConfig),
            typeof(VehicleVisualEffects), typeof(RigidbodyPause),
        };

        /// <summary>Demo-only children: the audio source rig, the driver ragdoll pivot (its own rigidbody + joint), the first-person view anchor.</summary>
        static readonly string[] StrippedChildren = { "Audio", "DriverFrontPivot", "FPView" };

        [MenuItem("Tools/Police Escape/Build EVP Traffic Prefabs")]
        public static void BuildPrefabs()
        {
            CarConfig config = LoadTrafficConfig();
            if (config == null)
            {
                Debug.LogError("EvpTrafficCarBuilder: no CarConfig found — run Create Car Test Scene first.");
                return;
            }

            int converted = 0;
            foreach (Entry entry in Table)
                if (Convert(entry, config)) converted++;
            AssetDatabase.SaveAssets();
            Debug.Log($"EvpTrafficCarBuilder: {converted}/{Table.Length} EVP vehicles are traffic NPC prefabs. Next: Tools → Police Escape → Traffic Adds EVP Vehicles.");
        }

        /// <summary>
        /// Append every converted demo car to the traffic pool (the existing
        /// entries — the taxi, the Kenney toys — stay; already-listed prefabs
        /// are skipped), and name any model entry that still has no identity.
        /// </summary>
        [MenuItem("Tools/Police Escape/Traffic Adds EVP Vehicles")]
        public static void TrafficAddsEvpVehicles()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TrafficSettings>(CarTestSceneBuilder.TrafficSettingsPath);
            if (settings == null)
            {
                Debug.LogError($"EvpTrafficCarBuilder: traffic settings missing at {CarTestSceneBuilder.TrafficSettingsPath} — run Create Car Test Scene first.");
                return;
            }
            settings.vehicles ??= new List<TrafficVehicleDefinition>();

            int added = 0;
            foreach (Entry entry in Table)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
                if (prefab == null)
                {
                    Debug.LogWarning($"EvpTrafficCarBuilder: {entry.Path} not found — skipped.");
                    continue;
                }
                if (prefab.GetComponent<CarController>() == null || prefab.GetComponent<TrafficCarInput>() == null)
                {
                    Debug.LogWarning($"EvpTrafficCarBuilder: {entry.Name} is not converted yet — run Build EVP Traffic Prefabs first. Skipped.");
                    continue;
                }
                if (settings.vehicles.Any(v => v != null && v.prefab == prefab)) continue;
                settings.vehicles.Add(new TrafficVehicleDefinition
                {
                    prefab = prefab,
                    weight = entry.Weight,
                    stopsRandomly = false,
                    kind = entry.Identity.kind,
                    paint = entry.Identity.paint,
                    color = entry.Identity.color,
                });
                added++;
            }

            // Model entries authored before identities existed (the taxi,
            // the Kenney pool) get theirs from the model name.
            int named = 0;
            foreach (TrafficVehicleDefinition definition in settings.vehicles)
            {
                if (definition == null || definition.prefab != null || definition.model == null || definition.Identity.IsSet) continue;
                if (!CarTestSceneBuilder.TryIdentifyModel(definition.model, out VehicleIdentity identity)) continue;
                definition.kind = identity.kind;
                definition.paint = identity.paint;
                definition.color = identity.color;
                named++;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"EvpTrafficCarBuilder: added {added} EVP vehicles to the traffic pool ({settings.vehicles.Count} entries), named {named} model entries.", settings);
        }

        // ----------------------------------------------------------- convert

        static bool Convert(Entry entry, CarConfig config)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path) == null)
            {
                Debug.LogWarning($"EvpTrafficCarBuilder: {entry.Path} not found — skipped.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(entry.Path);
            try
            {
                var vehicle = root.GetComponent<VehicleController>();
                if (vehicle == null)
                {
                    Debug.LogError($"EvpTrafficCarBuilder: {entry.Name} has no EVP VehicleController on its root — skipped.");
                    return false;
                }

                var controller = root.GetComponent<CarController>();
                if (controller != null)
                {
                    // Already converted: refresh the data that may have
                    // changed and leave the surgery alone.
                    controller.config = config;
                    controller.identity = entry.Identity;
                    if (root.GetComponent<TrafficCarInput>() == null) root.AddComponent<TrafficCarInput>();
                    PrefabUtility.SaveAsPrefabAsset(root, entry.Path);
                    Debug.Log($"EvpTrafficCarBuilder: {entry.Name} was already converted — identity/config refreshed.");
                    return true;
                }

                // Wheels off the authored list, told apart by position in car
                // space (the contents root sits at the origin, unrotated).
                if (!TryClassifyWheels(vehicle, root.transform, out Wheel fl, out Wheel fr, out Wheel rl, out Wheel rr))
                {
                    Debug.LogError($"EvpTrafficCarBuilder: could not tell {entry.Name}'s four wheels apart by position — skipped.");
                    return false;
                }

                foreach (System.Type type in StrippedComponents)
                    foreach (Component component in root.GetComponentsInChildren(type, true))
                        Object.DestroyImmediate(component);
                foreach (string name in StrippedChildren)
                {
                    Transform child = root.transform.Find(name);
                    if (child != null) Object.DestroyImmediate(child.gameObject);
                }
                // Stray rigidbodies/joints under the root would ride along as
                // a second body; only the root's is the car.
                foreach (Joint joint in root.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
                foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                    if (body.gameObject != root) Object.DestroyImmediate(body);

                // The body mesh colliders give way to the chassis box.
                foreach (MeshCollider mesh in root.GetComponentsInChildren<MeshCollider>(true))
                    Object.DestroyImmediate(mesh);

                // Chassis box around the body — wheels and calipers excluded —
                // with its underside no lower than the wheel centres, so a ramp
                // is met by the wheels and never by the box's front edge.
                var excluded = new List<Transform>();
                foreach (Wheel wheel in new[] { fl, fr, rl, rr })
                {
                    if (wheel.wheelTransform != null) excluded.Add(wheel.wheelTransform);
                    if (wheel.caliperTransform != null) excluded.Add(wheel.caliperTransform);
                }
                Bounds bodyBounds = BodyBounds(root.transform, excluded);
                float axleY = Mathf.Max(fl.wheelTransform.position.y, rl.wheelTransform.position.y);
                float bottom = Mathf.Max(bodyBounds.min.y, axleY);
                float top = bodyBounds.max.y;
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(bodyBounds.center.x, (top + bottom) * 0.5f, bodyBounds.center.z);
                box.size = new Vector3(bodyBounds.size.x, top - bottom, bodyBounds.size.z);

                var rigidbody = root.GetComponent<Rigidbody>();
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

                // Controller before driver: the driver's [RequireComponent] is
                // then satisfied by this one instead of a bare auto-added twin.
                controller = root.AddComponent<CarController>();
                controller.config = config;
                controller.frontLeft = fl.wheelCollider;
                controller.frontRight = fr.wheelCollider;
                controller.rearLeft = rl.wheelCollider;
                controller.rearRight = rr.wheelCollider;
                controller.frontLeftVisual = fl.wheelTransform;
                controller.frontRightVisual = fr.wheelTransform;
                controller.rearLeftVisual = rl.wheelTransform;
                controller.rearRightVisual = rr.wheelTransform;
                controller.identity = entry.Identity;
                root.AddComponent<TrafficCarInput>();

                PrefabUtility.SaveAsPrefabAsset(root, entry.Path);
                Debug.Log($"EvpTrafficCarBuilder: {entry.Name} → {entry.Identity} traffic NPC " +
                          $"({bodyBounds.size.z:F2} m long, {rigidbody.mass:F0} kg, wheel radius {fl.wheelCollider.radius:F2}/{rl.wheelCollider.radius:F2} m, " +
                          $"chassis box underside at {bottom:F2} m, EVP top speed {vehicle.maxSpeedForward * 3.6f:F0} km/h kept).");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The four authored wheels by position in car space (+Z front, -X
        /// left) — the CyberpunkCarKit rule applied to wheel colliders. False
        /// when two slots pick the same wheel or the list isn't four long.
        /// </summary>
        static bool TryClassifyWheels(VehicleController vehicle, Transform car,
            out Wheel frontLeft, out Wheel frontRight, out Wheel rearLeft, out Wheel rearRight)
        {
            frontLeft = frontRight = rearLeft = rearRight = null;
            Wheel[] wheels = vehicle.wheels;
            if (wheels == null || wheels.Length != 4 || wheels.Any(w => w == null || w.wheelCollider == null || w.wheelTransform == null))
                return false;

            Wheel Pick(bool front, bool left) => wheels
                .OrderByDescending(w =>
                {
                    Vector3 local = car.InverseTransformPoint(w.wheelCollider.transform.position);
                    return (front ? 1f : -1f) * local.z + (left ? -1f : 1f) * local.x;
                })
                .First();
            frontLeft = Pick(true, true);
            frontRight = Pick(true, false);
            rearLeft = Pick(false, true);
            rearRight = Pick(false, false);
            return new[] { frontLeft, frontRight, rearLeft, rearRight }.Distinct().Count() == 4;
        }

        /// <summary>World bounds of every renderer under <paramref name="root"/> that is not under one of the <paramref name="excluded"/> subtrees.</summary>
        static Bounds BodyBounds(Transform root, List<Transform> excluded)
        {
            bool first = true;
            Bounds bounds = new(root.position, Vector3.one);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (excluded.Any(e => renderer.transform == e || renderer.transform.IsChildOf(e))) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        /// <summary>The civilians' shared CarConfig — off the traffic settings, else the scene builder's test config.</summary>
        static CarConfig LoadTrafficConfig()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TrafficSettings>(CarTestSceneBuilder.TrafficSettingsPath);
            if (settings != null && settings.carConfig != null) return settings.carConfig;
            return AssetDatabase.LoadAssetAtPath<CarConfig>(CarTestSceneBuilder.CarConfigPath);
        }
    }
}
