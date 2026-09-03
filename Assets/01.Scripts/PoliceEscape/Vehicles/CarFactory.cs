using ConfusedGameDev.FiniteRunner.Cameras;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The one runtime spawn routine for the player car, used by both
    /// PlayerCarSpawner and CityManager's Create Car button. Enforces the
    /// single-car rule (any CarController already in the scene is destroyed
    /// first), gives the car its rolling start (CarConfig.spawnSpeedKmh, so
    /// the player takes the wheel mid-motion) and retargets the shared
    /// Cinemachine orbit rig through <see cref="CameraRigInstaller"/>.
    /// </summary>
    public static class CarFactory
    {
        /// <summary>Spawn the car on a road cell, facing <paramref name="yaw"/> degrees (0 = +Z), already rolling.</summary>
        public static CarController Spawn(GameObject carPrefab, OrbitCameraSettings cameraSettings, Vector3 roadCenter, float yaw)
        {
            if (carPrefab == null)
            {
                Debug.LogWarning("CarFactory: no car prefab assigned.");
                return null;
            }

            // Single-PLAYER-car rule: only cars with a player driver (CarInput)
            // are replaced — AI cars carry their own driver component and are
            // owned by the PatrolManager.
            foreach (var existing in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (existing.GetComponent<CarInput>() != null)
                    Object.Destroy(existing.gameObject);

            // Read the config off the prefab so the car can be instantiated
            // directly at its spawn pose. Never teleport it after Instantiate:
            // the rigidbody interpolates, and a post-hoc transform move gets
            // stomped by the interpolation history — the car snaps back to the
            // prefab pose at the world origin (the "spawned inside a building"
            // bug, found the hard way).
            var prefabController = carPrefab.GetComponent<CarController>();
            CarConfig config = prefabController != null ? prefabController.config : null;
            if (prefabController == null)
                Debug.LogError("CarFactory: car prefab has no CarController on its root — it will not drive.", carPrefab);

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            float dropHeight = config != null ? config.respawnHeight : 0.6f;
            var go = Object.Instantiate(carPrefab, roadCenter + Vector3.up * dropHeight, rotation);
            go.name = "PlayerCar";
            SceneHierarchy.Adopt(go, SceneHierarchy.Player(go.scene)); // world pose kept: the header sits at the origin
            var car = go.GetComponent<CarController>();

            // Blasts reach the player through the same IDamageable interface
            // as every NPC car and barrel — this is the player's half of it.
            if (go.GetComponent<PlayerDamageReceiver>() == null)
                go.AddComponent<PlayerDamageReceiver>();

            // The PlayerCar layer feeds the glitch silhouette render feature
            // (car visible through buildings). Added by Tools → Police Escape →
            // Install Glitch Silhouette Feature; harmless while it doesn't exist.
            int glitchLayer = LayerMask.NameToLayer("PlayerCar");
            if (glitchLayer >= 0) SetLayerRecursively(go.transform, glitchLayer);

            // Rolling start — hand the player a car that's already moving.
            var body = go.GetComponent<Rigidbody>();
            if (body != null && config != null)
                body.linearVelocity = rotation * Vector3.forward * (config.spawnSpeedKmh / 3.6f);

            AttachOrbitCamera(car, cameraSettings);
            return car;
        }

        /// <summary>
        /// Teleport a live car's rigidbody. Moves the physics body and the
        /// transform together with interpolation suspended for the jump —
        /// moving only the transform of an interpolating rigidbody gets
        /// overwritten by its interpolation history on the next frame.
        /// Cinemachine is told about the warp so the camera cuts along
        /// instead of swooping across the city.
        /// </summary>
        public static void Teleport(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            Vector3 delta = position - body.position;
            RigidbodyInterpolation mode = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.interpolation = mode;
            CameraRigInstaller.Warp(body.transform, delta);
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        /// <summary>Attach the shared chase rig to the car (brain on the main camera, rig found or created).</summary>
        public static void AttachOrbitCamera(CarController car, OrbitCameraSettings cameraSettings)
        {
            if (car != null) CameraRigInstaller.Attach(car, cameraSettings);
        }
    }
}
