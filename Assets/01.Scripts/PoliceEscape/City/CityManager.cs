using ConfusedGameDev.FiniteRunner.FX;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Play-mode entry point of the city chase — and nothing more. The city
    /// itself is a BAKED PREFAB now (see <see cref="CityRoot"/> and the City
    /// Designer window): no generation, no streaming, no seeds are resolved
    /// here. What remains is the boot sequence (spawn the patrol/traffic
    /// managers, HUD pieces and weather when their settings are wired) and a
    /// thin facade over <see cref="CityRoot"/>'s road graph and spatial
    /// queries, kept because a dozen consumers (spawners, AI managers,
    /// respawner, map, minimap, debug overlays) already hold a CityManager
    /// reference — they keep working unchanged, whichever component actually
    /// owns the data.
    /// </summary>
    public class CityManager : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("City-wide generation settings — needed at runtime only for geometry facts (cell size, piece scale). Blocks are baked; edit the city in Tools → Police Escape → City Designer.")]
        public CityGenerationSettings settings;

        [Tooltip("The baked city this scene runs on. Empty = found in the scene at Awake.")]
        public CityRoot cityRoot;

        [TitleGroup("Player car")]
        [AssetsOnly]
        [Tooltip("Car prefab dropped by Create Car — needs a CarController and an ICarInput on its root.")]
        public GameObject carPrefab;

        [TitleGroup("Player car")]
        [Tooltip("Camera-feel settings for the Cinemachine orbit rig set up when the car spawns.")]
        public Vehicles.OrbitCameraSettings orbitCameraSettings;

        [TitleGroup("Police")]
        [AssetsOnly]
        [Tooltip("Police car prefab — needs a CarController and a PoliceCarInput on its root. When both police fields are wired, a PatrolManager is spawned at play start.")]
        public GameObject policeCarPrefab;

        [TitleGroup("Police")]
        [Tooltip("All pursuit tunables (fleet size, detection, driving) live on this asset.")]
        public AI.PursuitSettings pursuitSettings;

        [TitleGroup("Traffic")]
        [Tooltip("Civilian traffic tunables — when assigned, a TrafficManager is spawned at play start (vehicles exist only within its active radius of the player).")]
        public AI.TrafficSettings trafficSettings;

        [TitleGroup("UI")]
        [Tooltip("Circular radar settings — when assigned, a minimap is spawned at play start (bottom-right, GTA-style).")]
        public UI.MinimapSettings minimapSettings;

        [TitleGroup("UI")]
        [Tooltip("Speedometer settings — when assigned, an analog gauge is spawned at play start (bottom-left).")]
        public UI.SpeedometerSettings speedometerSettings;

        [TitleGroup("UI")]
        [Tooltip("Full-screen city map settings — when assigned, the Tab/Back map screen is spawned at play start.")]
        public UI.CityMapSettings mapSettings;

        [TitleGroup("Camera FX")]
        [Tooltip("Speed-driven motion blur: fades in past 100 km/h by default. Tuning (speed band, intensity) lives on the spawned SpeedMotionBlur — hand-place one in the scene to change it.")]
        public bool speedMotionBlur = true;

        [ToggleGroup("rain", "Weather")]
        [Tooltip("Spawn the rain over the chase. The downpour's own knobs live on the RainSettings asset below — this is only the on/off for this scene.")]
        public bool rain = true;

        [ToggleGroup("rain"), InlineEditor]
        [Tooltip("Override asset pushed onto the scene's RainSystem on boot. Empty = leave that system with the asset it was authored with (the shipped FiniteRunner_Rain from Resources).")]
        public RainSettings rainSettings;

        /// <summary>Waypoint graph over the baked roads — the AI's navigation source. Null until a CityRoot exists.</summary>
        public RoadGraph Graph => Root != null ? Root.Graph : null;

        /// <summary>The baked city this manager fronts. Found lazily so wiring order never matters.</summary>
        public CityRoot Root
        {
            get
            {
                if (cityRoot == null) cityRoot = FindAnyObjectByType<CityRoot>();
                return cityRoot;
            }
        }

        /// <summary>Uniform scale applied to every baked road piece: cell fit (cellSize ÷ native footprint) × the extra multiplier.</summary>
        public float PieceScale => settings != null ? settings.PieceScale : 1f;

        /// <summary>
        /// World height of an overpass deck's LANE above the drivable ground
        /// plane. Measured from the sunk city, so it tracks both the piece
        /// scale and the surface offset and lands on the deck's asphalt.
        /// </summary>
        public float DeckWorldHeight =>
            settings != null ? (settings.DeckNativeHeight - settings.RoadSurfaceNativeHeight) * PieceScale : 0f;

        /// <summary>How far every baked piece was sunk so its driving lane lands on the block ground slab at y = 0.</summary>
        public float RoadSurfaceHeight => settings != null ? settings.RoadSurfaceNativeHeight * PieceScale : 0f;

        void Awake()
        {
            if (!Application.isPlaying) return;

            if (Root == null)
                Debug.LogWarning("CityManager: no CityRoot in the scene — drop the baked city prefab in (Tools → Police Escape → City Designer bakes one).", this);

            // Police fleet: prefer a scene-placed (prefab) manager so it is
            // visible before play; spawn one only when the scene has none.
            if (FindAnyObjectByType<AI.PatrolManager>() == null && policeCarPrefab != null && pursuitSettings != null)
            {
                var managerGo = new GameObject("PatrolManager");
                var patrolManager = managerGo.AddComponent<AI.PatrolManager>();
                patrolManager.settings = pursuitSettings;
                patrolManager.policeCarPrefab = policeCarPrefab;
            }

            // Civilian traffic: same scene-first pattern as the police.
            if (FindAnyObjectByType<AI.TrafficManager>() == null && trafficSettings != null)
            {
                var trafficGo = new GameObject("TrafficManager");
                trafficGo.AddComponent<AI.TrafficManager>().settings = trafficSettings;
            }

            // HUD pieces: same deal — spawned when wired, each builds its own canvas.
            if (FindAnyObjectByType<UI.Minimap>() == null && minimapSettings != null)
            {
                var minimapGo = new GameObject("Minimap");
                minimapGo.AddComponent<UI.Minimap>().settings = minimapSettings;
            }
            if (FindAnyObjectByType<UI.Speedometer>() == null && speedometerSettings != null)
            {
                var speedometerGo = new GameObject("Speedometer");
                speedometerGo.AddComponent<UI.Speedometer>().settings = speedometerSettings;
            }
            if (FindAnyObjectByType<UI.CityMapScreen>() == null && mapSettings != null) UI.CityMapScreen.Spawn(this, mapSettings);

            // Camera FX: scene-first like everything above — a hand-placed
            // SpeedMotionBlur keeps its tuning, this only fills the gap.
            if (speedMotionBlur && FindAnyObjectByType<Vehicles.SpeedMotionBlur>() == null)
                new GameObject("SpeedMotionBlur").AddComponent<Vehicles.SpeedMotionBlur>();

            // Weather: a camera-sized volume, so it needs neither the city nor
            // the car — it just has to exist before the first frame is drawn.
            // The scene's own RainSystem wins; switching this off parks it.
            RainSystem.Apply(rain, rainSettings);
        }

        // ------------------------------------------------------------- buttons

        /// <summary>
        /// Drop the player car on a random road cell, already rolling
        /// (CarConfig.spawnSpeedKmh) and facing along the road, with the chase
        /// camera retargeted. The factory removes any existing car first, so
        /// pressing this repeatedly always leaves exactly one car.
        /// </summary>
        [TitleGroup("Actions")]
        [Button("Create Car", ButtonSizes.Large), GUIColor(0.6f, 0.8f, 1f)]
        [EnableIf("@UnityEngine.Application.isPlaying")]
        public void CreateCar()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("CityManager: Create Car works in play mode — the car is physics-driven.");
                return;
            }
            if (carPrefab == null)
            {
                Debug.LogWarning("CityManager: assign a car prefab first.");
                return;
            }
            // Preferred: a straight piece with a clear runway ahead, so the
            // rolling start never launches into a corner or junction.
            if (!TryGetRandomStraightSpawn(SpawnRunwayCells(), out Vector3 center, out float yaw))
            {
                if (!TryGetRandomRoadCell(out center, out EdgeMask connections))
                {
                    Debug.LogWarning("CityManager: no road cells found — is the baked city prefab in the scene?");
                    return;
                }
                yaw = RandomConnectedYaw(connections);
                Debug.LogWarning($"CityManager: no straight stretch with {SpawnRunwayCells()} clear cells ahead — spawning on a random road cell instead.");
            }

            Vehicles.CarFactory.Spawn(carPrefab, orbitCameraSettings, center, yaw);
        }

        /// <summary>Runway length demanded by the car prefab's config (CarConfig.spawnRunwayCells), with a safe default when unwired.</summary>
        int SpawnRunwayCells()
        {
            var controller = carPrefab != null ? carPrefab.GetComponent<Vehicles.CarController>() : null;
            return controller != null && controller.config != null ? controller.config.spawnRunwayCells : 4;
        }

        // ------------------------------------------------------------- queries
        // Thin delegations to the baked CityRoot — kept so every existing
        // consumer of these signatures works untouched.

        public bool TryFindNearestRoadCell(Vector3 from, out Vector3 center, out EdgeMask connections, bool groundOnly = false)
        {
            center = default;
            connections = EdgeMask.None;
            return Root != null && Root.TryFindNearestRoadCell(from, out center, out connections, groundOnly);
        }

        public bool TryGetRandomRoadCell(out Vector3 center, out EdgeMask connections, bool groundOnly = false)
        {
            center = default;
            connections = EdgeMask.None;
            return Root != null && Root.TryGetRandomRoadCell(out center, out connections, groundOnly);
        }

        public bool IsCellClear(Vector3 cellCenter) => Root == null || Root.IsCellClear(cellCenter);

        public bool TryGetRandomStraightSpawn(int runwayCells, out Vector3 center, out float yaw)
        {
            center = default;
            yaw = 0f;
            return Root != null && Root.TryGetRandomStraightSpawn(runwayCells, out center, out yaw);
        }

        public bool TryFindNearestStraightSpawn(Vector3 from, int runwayCells, out Vector3 center, out float yaw)
        {
            center = default;
            yaw = 0f;
            return Root != null && Root.TryFindNearestStraightSpawn(from, runwayCells, out center, out yaw);
        }

        /// <summary>
        /// May NPCs (police on patrol, civilian traffic) occupy this world
        /// position right now? True unless the baked city's block scoping says
        /// otherwise — the player's block plus edge-close neighbours.
        /// </summary>
        public bool IsNpcPositionAllowed(Vector3 worldPosition) =>
            Root == null || Root.Bounds.IsAllowed(worldPosition);

        /// <summary>Yaw (degrees, 0 = +Z) of a random direction the cell actually connects to, so a spawned (or recovered) car launches along the road.</summary>
        public static float RandomConnectedYaw(EdgeMask connections)
        {
            int count = 0;
            int picked = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((connections & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                count++;
                if (Random.Range(0, count) == 0) picked = dir;
            }
            return count > 0 ? picked * 90f : 0f;
        }
    }
}
