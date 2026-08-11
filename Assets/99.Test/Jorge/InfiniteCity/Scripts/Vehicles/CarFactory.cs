using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The one runtime spawn routine for the player car, used by both
    /// PlayerCarSpawner and CityManager's Create Car button. Enforces the
    /// single-car rule (any CarController already in the scene is destroyed
    /// first), gives the car its rolling start (CarConfig.spawnSpeedKmh, so
    /// the player takes the wheel mid-motion) and attaches/retargets the
    /// ChaseCamera on the main camera.
    /// </summary>
    public static class CarFactory
    {
        /// <summary>Spawn the car on a road cell, facing <paramref name="yaw"/> degrees (0 = +Z), already rolling.</summary>
        public static CarController Spawn(GameObject carPrefab, ChaseCameraSettings cameraSettings, Vector3 roadCenter, float yaw)
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
            var car = go.GetComponent<CarController>();

            // Rolling start — hand the player a car that's already moving.
            var body = go.GetComponent<Rigidbody>();
            if (body != null && config != null)
                body.linearVelocity = rotation * Vector3.forward * (config.spawnSpeedKmh / 3.6f);

            AttachChaseCamera(car, cameraSettings);
            return car;
        }

        /// <summary>
        /// Teleport a live car's rigidbody. Moves the physics body and the
        /// transform together with interpolation suspended for the jump —
        /// moving only the transform of an interpolating rigidbody gets
        /// overwritten by its interpolation history on the next frame.
        /// </summary>
        public static void Teleport(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            RigidbodyInterpolation mode = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.interpolation = mode;
        }

        /// <summary>Get-or-add a ChaseCamera on the main camera and snap it behind the car.</summary>
        public static void AttachChaseCamera(CarController car, ChaseCameraSettings cameraSettings)
        {
            if (car == null) return;
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("CarFactory: no main camera found to attach the ChaseCamera to.");
                return;
            }

            var chase = camera.GetComponent<ChaseCamera>();
            if (chase == null) chase = camera.gameObject.AddComponent<ChaseCamera>();
            if (cameraSettings != null) chase.settings = cameraSettings;
            chase.target = car;
            chase.SnapBehindTarget();
        }
    }
}
