using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Rigs a Kenney vehicle model into a drivable WheelCollider car at
    /// runtime — the traffic system builds civilians straight from the FBX
    /// assets, no prefab per vehicle type. Mirrors the editor prefab builder:
    /// model scaled by the kit factor, wheel meshes re-pivoted onto centered
    /// pivots (the kit's pivots sit at the axle attach), wheel colliders at
    /// the true wheel centers with radii off the mesh bounds, chassis box
    /// fitted to the body. Build order matters: the root is posed before any
    /// Rigidbody exists (no interpolation snap-back), and the driver
    /// component is added before the CarController so its Awake finds the
    /// ICarInput.
    /// </summary>
    public static class VehicleRigBuilder
    {
        static readonly string[] WheelNames =
        {
            "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right",
        };

        /// <summary>Build a rig at the given pose. Returns (null, null) with a warning if the model lacks the kit's four named wheels.</summary>
        public static (CarController controller, TDriver driver) Build<TDriver>(
            GameObject modelPrefab, CarConfig config, float scale, Vector3 position, Quaternion rotation)
            where TDriver : Component, ICarInput
        {
            var root = new GameObject(modelPrefab.name + "-rig");
            root.transform.SetPositionAndRotation(position, rotation);

            var model = Object.Instantiate(modelPrefab, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * scale;

            // Wheel poses measured before any physics component exists.
            var wheelCenters = new Vector3[WheelNames.Length];   // root-local
            var wheelRadii = new float[WheelNames.Length];
            var wheelMeshes = new Transform[WheelNames.Length];
            for (int i = 0; i < WheelNames.Length; i++)
            {
                Transform mesh = model.transform.Find(WheelNames[i]);
                Renderer renderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
                if (renderer == null)
                {
                    Debug.LogWarning($"VehicleRigBuilder: '{modelPrefab.name}' has no '{WheelNames[i]}' — not riggable, skipped.");
                    Object.Destroy(root);
                    return (null, null);
                }
                wheelMeshes[i] = mesh;
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

            // Chassis box from what's left of the model (wheels re-parented out).
            // Spawn yaws are 90° multiples, so the world AABB converts exactly.
            Bounds bodyBounds = CombinedBounds(model.transform);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bodyBounds.center);
            Vector3 localSize = root.transform.InverseTransformVector(bodyBounds.size);
            box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

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

            // Driver before controller, so CarController.Awake finds the ICarInput.
            var driver = root.AddComponent<TDriver>();
            var controller = root.AddComponent<CarController>();
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
