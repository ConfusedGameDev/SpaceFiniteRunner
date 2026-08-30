using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Rigs a vehicle model into a drivable WheelCollider car at runtime —
    /// the traffic system builds civilians straight from the FBX assets, no
    /// prefab per vehicle type. Mirrors the editor prefab builder: model
    /// scaled by the kit factor and yawed to face +Z, wheel meshes
    /// re-pivoted onto centered pivots (the Kenney pivots sit at the axle
    /// attach), wheel colliders at the true wheel centers with radii off the
    /// mesh bounds, chassis box fitted to the body. Two kits are understood:
    /// Kenney models by their four named wheels (wheel-front-left …), and
    /// Cyberpunk Megapolis cars by shape through <see cref="CyberpunkCarKit"/>
    /// — wheels told apart by position, LOD children and the body collider
    /// dropped, the shell lifted and the box underside clamped to the axle
    /// line exactly as the player and police prefabs get. Build order
    /// matters: the root is posed before any Rigidbody exists (no
    /// interpolation snap-back), and the driver component is added before
    /// the CarController so its Awake finds the ICarInput.
    /// </summary>
    public static class VehicleRigBuilder
    {
        static readonly string[] WheelNames =
        {
            "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right",
        };

        /// <summary>
        /// Build a rig at the given pose. <paramref name="modelYaw"/> turns the
        /// model to face the rig's +Z (0 for Kenney, <see cref="CyberpunkCarKit.ModelYaw"/>
        /// for the cyberpunk cars). Returns (null, null) with a warning if the
        /// model lacks the kit's four named wheels and isn't a cyberpunk car either.
        /// </summary>
        public static (CarController controller, TDriver driver) Build<TDriver>(
            GameObject modelPrefab, CarConfig config, float scale, Vector3 position, Quaternion rotation, float modelYaw = 0f)
            where TDriver : Component, ICarInput
        {
            var root = new GameObject(modelPrefab.name + "-rig");
            root.transform.SetPositionAndRotation(position, rotation);

            var model = Object.Instantiate(modelPrefab, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
            model.transform.localScale = Vector3.one * scale;

            Transform[] wheelMeshes = ResolveWheels(model.transform, root.transform, modelPrefab.name, out bool kitCar);
            if (wheelMeshes == null)
            {
                Object.Destroy(root);
                return (null, null);
            }

            // Wheel poses measured before any physics component exists.
            var wheelCenters = new Vector3[WheelNames.Length];   // root-local
            var wheelRadii = new float[WheelNames.Length];
            for (int i = 0; i < WheelNames.Length; i++)
            {
                Renderer renderer = wheelMeshes[i].GetComponent<Renderer>();
                wheelCenters[i] = root.transform.InverseTransformPoint(renderer.bounds.center);
                wheelRadii[i] = renderer.bounds.extents.y;
            }

            var pivots = new GameObject("WheelPivots");
            pivots.transform.SetParent(root.transform, false);
            var wheelRoot = new GameObject("Wheels");
            wheelRoot.transform.SetParent(root.transform, false);

            var pivotTransforms = new Transform[WheelNames.Length];
            for (int i = 0; i < WheelNames.Length; i++)
            {
                var pivot = new GameObject(WheelNames[i] + "-pivot").transform;
                pivot.SetParent(pivots.transform, false);
                pivot.localPosition = wheelCenters[i];
                wheelMeshes[i].SetParent(pivot, true); // keep pose — spins around its true center now
                pivotTransforms[i] = pivot;
            }

            if (kitCar)
            {
                // The body's LODGroup still lists the wheels that just left it;
                // then lift the shell over the wheels, which stay on their axles.
                CyberpunkCarKit.PruneLodGroup(model);
                model.transform.localPosition = Vector3.up * CyberpunkCarKit.BodyLift;
            }

            // Chassis box from what's left of the model (wheels re-parented out).
            // Spawn yaws are 90° multiples, so the world AABB converts exactly.
            Bounds bodyBounds = CombinedBounds(model.transform);
            Vector3 center = root.transform.InverseTransformPoint(bodyBounds.center);
            Vector3 localSize = root.transform.InverseTransformVector(bodyBounds.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            if (kitCar)
            {
                // Underside no lower than the wheel centres, so a ramp is met
                // by the wheels and never by the box's front edge.
                float axleY = Mathf.Max(wheelCenters[0].y, wheelCenters[2].y);
                float bottom = Mathf.Max(center.y - localSize.y * 0.5f, axleY);
                float top = center.y + localSize.y * 0.5f;
                center.y = (top + bottom) * 0.5f;
                localSize.y = top - bottom;
            }
            var box = root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = localSize;

            // Physics components last, at the final pose.
            var body = root.AddComponent<Rigidbody>();
            body.mass = config != null ? config.mass : 1200f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var colliders = new WheelCollider[WheelNames.Length];
            for (int i = 0; i < WheelNames.Length; i++)
            {
                var colliderGo = new GameObject(WheelNames[i] + "-collider");
                colliderGo.transform.SetParent(wheelRoot.transform, false);
                float travel = config != null ? config.suspensionDistance : 0.25f;
                colliderGo.transform.localPosition = wheelCenters[i] + Vector3.up * (travel * 0.5f);
                var wheel = colliderGo.AddComponent<WheelCollider>();
                wheel.radius = wheelRadii[i];
                wheel.mass = 25f;
                wheel.suspensionDistance = travel;
                colliders[i] = wheel;
            }

            // Controller before driver: the driver's [RequireComponent] is then
            // satisfied by this one instead of auto-adding a bare duplicate.
            // CarController binds its ICarInput in Start, so order is safe.
            var controller = root.AddComponent<CarController>();
            var driver = root.AddComponent<TDriver>();
            controller.config = config;
            controller.frontLeft = colliders[0];
            controller.frontRight = colliders[1];
            controller.rearLeft = colliders[2];
            controller.rearRight = colliders[3];
            controller.frontLeftVisual = pivotTransforms[0];
            controller.frontRightVisual = pivotTransforms[1];
            controller.rearLeftVisual = pivotTransforms[2];
            controller.rearRightVisual = pivotTransforms[3];
            return (controller, driver);
        }

        /// <summary>
        /// The four wheel meshes in WheelNames order (FL, FR, RL, RR): the
        /// Kenney names when the model carries them, else the cyberpunk kit's
        /// wheels classified by position — that path also strips the pack's
        /// body collider and the wheels' LOD children, which have to go before
        /// anything is measured. Null (warned) when neither fits.
        /// </summary>
        static Transform[] ResolveWheels(Transform model, Transform car, string modelName, out bool kitCar)
        {
            kitCar = false;
            var meshes = new Transform[WheelNames.Length];
            bool named = true;
            for (int i = 0; i < WheelNames.Length && named; i++)
            {
                meshes[i] = model.Find(WheelNames[i]);
                named = meshes[i] != null && meshes[i].GetComponent<Renderer>() != null;
            }
            if (named) return meshes;

            if (!CyberpunkCarKit.IsKitModel(model))
            {
                Debug.LogWarning($"VehicleRigBuilder: '{modelName}' has neither the Kenney wheel names nor a cyberpunk kit's four *Wheel* children — not riggable, skipped.");
                return null;
            }

            CyberpunkCarKit.StripColliders(model.gameObject);
            var wheels = CyberpunkCarKit.FindWheels(model);
            CyberpunkCarKit.FlattenWheels(wheels);
            if (!CyberpunkCarKit.TryClassifyWheels(wheels, car, out meshes[0], out meshes[1], out meshes[2], out meshes[3]))
            {
                Debug.LogWarning($"VehicleRigBuilder: could not tell '{modelName}'s four wheels apart by position — is its model yaw right ({CyberpunkCarKit.ModelYaw} for the cyberpunk kit)?");
                return null;
            }
            kitCar = true;
            return meshes;
        }

        static Bounds CombinedBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
