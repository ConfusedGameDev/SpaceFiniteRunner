using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Runtime entry point for the drivable car. Because the city regenerates
    /// with a fresh seed on every play, the car can't be baked into the scene
    /// at a fixed spot — this component waits for the city (CityManager
    /// regenerates in Awake, this spawns in Start), drops the car prefab on
    /// the road cell nearest its own position aligned with the road, and
    /// wires a ChaseCamera onto the main camera. Place it roughly where the
    /// run should begin; without a city it spawns at its own transform.
    /// </summary>
    public class PlayerCarSpawner : MonoBehaviour
    {
        [Required, AssetsOnly]
        [Tooltip("Car prefab with CarController + an ICarInput on the root.")]
        public GameObject carPrefab;

        [Required, InlineEditor]
        [Tooltip("Camera-feel tunables for the Cinemachine orbit rig this spawner sets up.")]
        public OrbitCameraSettings cameraSettings;

        public CarController SpawnedCar { get; private set; }

        void Start() => SpawnCar();

        [Button("Respawn Car", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
        public void SpawnCar()
        {
            if (carPrefab == null)
            {
                Debug.LogWarning("PlayerCarSpawner: assign a car prefab first.");
                return;
            }

            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;
            var city = FindAnyObjectByType<CityManager>();
            if (city != null)
            {
                // Preferred: nearest straight piece with a clear runway ahead;
                // fall back to any road cell if the layout has no such stretch.
                var controller = carPrefab.GetComponent<CarController>();
                int runway = controller != null && controller.config != null ? controller.config.spawnRunwayCells : 4;
                if (city.TryFindNearestStraightSpawn(transform.position, runway, out Vector3 straightCenter, out float straightYaw))
                {
                    position = straightCenter;
                    yaw = straightYaw;
                }
                else if (city.TryFindNearestRoadCell(transform.position, out Vector3 roadCenter, out EdgeMask connections, groundOnly: true))
                {
                    position = roadCenter;
                    // Face along a connected road direction: north/south = +Z, else +X.
                    yaw = (connections & (EdgeMask.North | EdgeMask.South)) != 0 ? 0f : 90f;
                }
            }

            // The factory enforces the single-car rule and wires the camera.
            SpawnedCar = CarFactory.Spawn(carPrefab, cameraSettings, position, yaw);
        }
    }
}
