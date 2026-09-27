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

        // Each system owns its own settings and prefabs (refactor Step 9.4):
        // the PatrolManager its pursuit settings and police prefab, the
        // TrafficManager its traffic settings, the PlayerCarSpawner the
        // player's car, the OrbitCameraRig its camera settings, each HUD piece
        // its own asset. This manager held a second copy of every one of them
        // to spawn missing systems at play — they are hand-placed now
        // (SceneSystemsPlacer), so it holds only the city.

        [TitleGroup("Sandbox")]
        [AssetsOnly]
        [Tooltip("Car prefab dropped by the Create Car button (a sandbox tool — the level's own car comes from the PlayerCarSpawner). Needs a CarController and an ICarInput on its root.")]
        public GameObject carPrefab;

        [ToggleGroup("rain", "Weather")]
        [Tooltip("Spawn the rain over the chase. The downpour's own knobs live on the RainSettings asset below — this is only the on/off for this scene.")]
        public bool rain = true;

        [ToggleGroup("vhs", "VHS tape")]
        [Tooltip("Play the chase back as a worn VHS tape: chroma bleed, row jitter, a crawling tracking band, grain and scanlines over the finished picture. The look lives on the asset below — this is only the on/off for this scene.")]
        public bool vhs = true;

        [ToggleGroup("psx", "PSX look")]
        [Tooltip("Show the chase as a PlayStation-1 console would: a 240-row picture with square pixels, vertex wobble and texture swim per polygon-sized block, 15-bit colour under a Bayer dither. The look lives on the asset below — this is only the on/off for this scene.")]
        public bool psx = true;

        [ToggleGroup("crt", "CRT screen")]
        [Tooltip("Show the chase on a curved CRT tube: barrel curvature with rounded corners, phosphor bleed and colour convergence that grow toward the edges, scanlines, an aperture grille and refresh flicker over the finished picture — the last pass, the display the console and the tape play on. The look lives on the asset below — this is only the on/off for this scene; the player has their own dial on the VIDEO settings page.")]
        public bool crt = true;

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

        /// <summary>
        /// The city's grid cell, metres — THE runtime cell size (refactor Step
        /// 9.3). The baked root's value is the truth for the city that exists;
        /// the generation settings only when there is no baked city (the road
        /// kit sandbox). It replaced five "settings, else 20 m" copies in the
        /// AI, the fleet managers and the debug overlay.
        /// </summary>
        public float CellSize
        {
            get
            {
                CityRoot root = Root;
                if (root != null && root.cellSize > 0f) return root.cellSize;
                return settings != null ? settings.cellSize : CityRoot.DefaultCellSize;
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

            // Scene-lifetime systems are HAND-PLACED (the scene builders run
            // SceneSystemsPlacer; so does Tools -> Police Escape -> Place Scene
            // Systems): the fleets, the HUD, the motion blur and the stats
            // recorder are never spawned here. The full-screen drivers below
            // are found and parked, never spawned.

            // Weather: a camera-sized volume, so it needs neither the city nor
            // the car — it just has to exist before the first frame is drawn.
            // The scene's own RainSystem wins; switching this off parks it.
            RainSystem.Apply(rain);

            // VHS tape: same rule — the scene's hand-placed VhsTape driver,
            // found and parked, never spawned.
            VhsTape.Apply(vhs);

            // PSX look: the console the tape records — same rule.
            PsxLook.Apply(psx);

            // CRT screen: the tube it is all shown on — same rule.
            CrtScreen.Apply(crt);
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

            Vehicles.CarFactory.Spawn(carPrefab, center, yaw);
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
