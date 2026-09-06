using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Runtime entry point for the drivable car, and the one chokepoint both
    /// the scene's start and the in-place retry (<c>LevelManager.RestartLevel</c>)
    /// go through. Where the car appears is decided in this order: the
    /// <see cref="PlayerSpawnPoint"/> named by the level's
    /// <see cref="LevelDefinition.spawnPointId"/>; a random registered point
    /// (an unknown id warns and falls through to this); and, only when the
    /// scene has no points at all, the pre-spawn-point rule — the nearest
    /// straight runway to this component's own transform, or the nearest
    /// road cell, or the transform itself without a city. It also wires the
    /// Cinemachine orbit rig onto the main camera through the factory.
    /// </summary>
    public class PlayerCarSpawner : MonoBehaviour
    {
        [Required, AssetsOnly]
        [Tooltip("Car prefab with CarController + an ICarInput on the root.")]
        public GameObject carPrefab;

        [Required, InlineEditor]
        [Tooltip("Camera-feel tunables for the Cinemachine orbit rig this spawner sets up.")]
        public OrbitCameraSettings cameraSettings;

        [ShowInInspector, System.NonSerialized, PropertyOrder(10)]
        [Tooltip("Debug: a PlayerSpawnPoint id for the button below. Empty = random.")]
        [LabelText("Debug Spawn Point Id")]
        string debugSpawnPointId = "";

        public CarController SpawnedCar { get; private set; }

        void Start() => SpawnCar();

        /// <summary>Spawn at the point the current level asks for (random when it names none).</summary>
        [Button("Respawn Car", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
        public void SpawnCar() => SpawnCar(FindAnyObjectByType<LevelManager>()?.Level?.spawnPointId);

        [Button("Respawn At Id"), EnableIf("@UnityEngine.Application.isPlaying"), PropertyOrder(11)]
        void RespawnAtId() => SpawnCar(debugSpawnPointId);

        /// <summary>
        /// Spawn at the <see cref="PlayerSpawnPoint"/> with this id, a random
        /// one when the id is empty or unknown, or the nearest straight runway
        /// when the scene has no points.
        /// </summary>
        public void SpawnCar(string spawnPointId)
        {
            if (carPrefab == null)
            {
                Debug.LogWarning("PlayerCarSpawner: assign a car prefab first.");
                return;
            }

            var city = FindAnyObjectByType<CityManager>();
            ResolveSpawnPose(city, spawnPointId, out Vector3 position, out float yaw);

            // The factory enforces the single-car rule and wires the camera.
            SpawnedCar = CarFactory.Spawn(carPrefab, cameraSettings, position, yaw);
        }

        void ResolveSpawnPose(CityManager city, string spawnPointId, out Vector3 position, out float yaw)
        {
            string wanted = string.IsNullOrWhiteSpace(spawnPointId) ? null : spawnPointId.Trim();

            if (PlayerSpawnPoint.Count > 0)
            {
                if (wanted != null)
                {
                    if (PlayerSpawnPoint.TryFind(wanted, out PlayerSpawnPoint named))
                    {
                        named.GetPose(city, out position, out yaw);
                        return;
                    }
                    Debug.LogWarning($"PlayerCarSpawner: no {nameof(PlayerSpawnPoint)} with id '{wanted}' — picking a random one.", this);
                }
                if (PlayerSpawnPoint.TryPickRandom(out PlayerSpawnPoint random))
                {
                    random.GetPose(city, out position, out yaw);
                    return;
                }
            }
            else if (wanted != null)
            {
                Debug.LogWarning($"PlayerCarSpawner: spawn point id '{wanted}' is set but the scene has no {nameof(PlayerSpawnPoint)} — using the nearest runway.", this);
            }

            // No spawn points: the runway rule off this transform.
            position = transform.position;
            yaw = transform.eulerAngles.y;
            if (city == null) return;

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
    }
}
