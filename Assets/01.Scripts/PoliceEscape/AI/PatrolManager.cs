using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// Keeps the police fleet alive: maintains PursuitSettings.targetPatrolCount
    /// cars, spawning each on a random physically-clear road cell inside the
    /// spawn distance band around the player (far enough to not pop into
    /// view, close enough to matter) AND inside an allowed city block (the
    /// player's block plus edge-close neighbours — see CityBounds), and
    /// despawning any that fall too far behind. A patrol outside the allowed
    /// blocks is retired too, but ONLY while it is in the Patrol state:
    /// LevelManager treats "every patrol is patrolling" as escaped, so
    /// despawning a chasing or searching car would silently complete an
    /// EscapePolice objective the player never earned. Spawned by CityManager
    /// at play start when its police fields are wired — no scene object
    /// needed. Maintenance runs on a 1 s tick, not per-frame.
    /// </summary>
    public class PatrolManager : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("All pursuit tunables live on this asset — add new knobs there, not here.")]
        public PursuitSettings settings;

        [Required, AssetsOnly]
        [Tooltip("Police car prefab — CarController + PoliceCarInput on the root.")]
        public GameObject policeCarPrefab;

        readonly List<PoliceCarInput> patrols = new();
        CityManager city;
        float maintenanceTimer;
        int spawnedTotal;

        void Update()
        {
            maintenanceTimer -= Time.deltaTime;
            if (maintenanceTimer > 0f) return;
            maintenanceTimer = 1f;

            if (settings == null || policeCarPrefab == null) return;
            if (city == null) city = FindAnyObjectByType<CityManager>();
            if (city == null || city.Graph == null || city.Graph.Count == 0) return;

            CarController player = FindPlayerCar();
            if (player == null) return; // nothing to hunt yet — fleet waits for the player spawn

            patrols.RemoveAll(patrol => patrol == null);

            for (int i = patrols.Count - 1; i >= 0; i--)
            {
                Vector3 position = patrols[i].transform.position;
                bool tooFar = Vector3.Distance(position, player.transform.position) > settings.despawnDistance;
                // Block rule only for patrolling cars — never break a live chase
                // from the outside (see the class comment).
                bool outOfBlock = patrols[i].State == PoliceCarInput.AiState.Patrol
                    && !city.IsNpcPositionAllowed(position);
                if (!tooFar && !outOfBlock) continue;
                Destroy(patrols[i].gameObject);
                patrols.RemoveAt(i);
            }

            // Fleet shrank (the debug slider, or a rebalanced asset): retire
            // the extras now rather than waiting for them to drift out of
            // despawn range — a lowered count has to mean fewer cars.
            for (int i = patrols.Count - 1; i >= settings.targetPatrolCount; i--)
            {
                Destroy(patrols[i].gameObject);
                patrols.RemoveAt(i);
            }

            while (patrols.Count < settings.targetPatrolCount)
            {
                if (!TrySpawnPatrol(player)) break; // no valid cell this tick — try again next tick
            }
        }

        /// <summary>The player's car: the one driven by a CarInput. Null while no player car exists.</summary>
        public static CarController FindPlayerCar()
        {
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (car.GetComponent<CarInput>() != null)
                    return car;
            return null;
        }

        bool TrySpawnPatrol(CarController player)
        {
            RoadGraph graph = city.Graph;
            Vector3 anchor = player.transform.position;
            float minSqr = settings.SpawnDistanceMin * settings.SpawnDistanceMin;
            float maxSqr = settings.SpawnDistanceMax * settings.SpawnDistanceMax;

            // Reservoir pick among flat ground nodes inside the band with clear
            // airspace — never on a ramp or an overpass deck.
            RoadNode pickedNode = default;
            EdgeMask pickedMask = EdgeMask.None;
            int seen = 0;
            foreach (var pair in graph.Nodes)
            {
                if (pair.Key.Level != 0 || pair.Value.IsRamp) continue;
                Vector3 center = pair.Value.Center;
                float sqr = (center - anchor).sqrMagnitude;
                if (sqr < minSqr || sqr > maxSqr) continue;
                if (!city.IsNpcPositionAllowed(center)) continue;
                if (!city.IsCellClear(center)) continue;
                seen++;
                if (Random.Range(0, seen) != 0) continue;
                pickedNode = pair.Key;
                pickedMask = pair.Value.Mask;
            }
            if (seen == 0) return false;

            // Face along a random direction the cell actually connects to.
            int count = 0, direction = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((pickedMask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                count++;
                if (Random.Range(0, count) == 0) direction = dir;
            }

            // Instantiate at the spawn pose — never move it afterwards (see CarFactory).
            var go = Instantiate(policeCarPrefab,
                graph.Center(pickedNode) + Vector3.up * 0.6f,
                Quaternion.Euler(0f, direction * 90f, 0f));
            go.name = $"PoliceCar_{++spawnedTotal}";

            var driver = go.GetComponent<PoliceCarInput>();
            if (driver == null)
            {
                Debug.LogError("PatrolManager: police prefab has no PoliceCarInput on its root — destroying it.", policeCarPrefab);
                Destroy(go);
                return false;
            }
            // Health before Initialize, so the driver's fetch finds it — the
            // prefab stays untouched, attachment in code same as traffic.
            go.AddComponent<CarHealth>();
            driver.Initialize(settings, city);
            patrols.Add(driver);
            return true;
        }
    }
}
