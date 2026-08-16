using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// Keeps the civilian fleet alive around the player — and ONLY around
    /// the player: vehicles spawn on clear road cells between the minimum
    /// spawn distance and the active radius, and are removed once they fall
    /// beyond activeRadius + despawnPadding (hysteresis so boundary cars
    /// don't churn). Vehicles are rigged straight from the Kenney FBX
    /// assets at spawn time by VehicleRigBuilder — no per-type prefabs.
    /// Spawned by CityManager when its traffic settings field is wired;
    /// maintenance runs on a 1 s tick, not per-frame.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("All traffic tunables live on this asset — add new knobs there, not here.")]
        public TrafficSettings settings;

        readonly List<TrafficCarInput> vehicles = new();
        CityManager city;
        float maintenanceTimer;

        void Update()
        {
            maintenanceTimer -= Time.deltaTime;
            if (maintenanceTimer > 0f) return;
            maintenanceTimer = 1f;

            if (settings == null || settings.carConfig == null || settings.vehicles.Count == 0) return;
            if (city == null) city = FindAnyObjectByType<CityManager>();
            if (city == null || city.Graph == null || city.Graph.Count == 0) return;

            CarController player = PatrolManager.FindPlayerCar();
            if (player == null) return; // traffic waits for the player spawn

            vehicles.RemoveAll(vehicle => vehicle == null);

            // Cull: beyond the active radius (+ padding) or fallen out of the world.
            float despawnDistance = settings.activeRadius + settings.despawnPadding;
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                Vector3 position = vehicles[i].transform.position;
                if (position.y > -25f
                    && Vector3.Distance(position, player.transform.position) <= despawnDistance)
                    continue;
                Destroy(vehicles[i].gameObject);
                vehicles.RemoveAt(i);
            }

            // Ramp the fleet in over a few ticks — spawning the whole lot at
            // once is a rig-building hitch frame.
            int spawnedThisTick = 0;
            while (vehicles.Count < settings.targetVehicleCount && spawnedThisTick < settings.spawnsPerTick)
            {
                if (!TrySpawnVehicle(player)) break; // no valid spot this tick — try again next tick
                spawnedThisTick++;
            }
        }

        bool TrySpawnVehicle(CarController player)
        {
            RoadGraph graph = city.Graph;
            Vector3 anchor = player.transform.position;
            float minSqr = settings.minSpawnDistance * settings.minSpawnDistance;
            float maxSqr = settings.activeRadius * settings.activeRadius;

            // Reservoir pick among clear road cells inside the active band.
            Vector2Int pickedCell = default;
            EdgeMask pickedMask = EdgeMask.None;
            int seen = 0;
            foreach (var pair in graph.Cells)
            {
                Vector3 center = graph.CellCenter(pair.Key);
                float sqr = (center - anchor).sqrMagnitude;
                if (sqr < minSqr || sqr > maxSqr) continue;
                if (!city.IsCellClear(center)) continue;
                seen++;
                if (Random.Range(0, seen) != 0) continue;
                pickedCell = pair.Key;
                pickedMask = pair.Value;
            }
            if (seen == 0) return false;

            TrafficVehicleDefinition definition = PickVehicle();
            if (definition == null) return false;

            // Face along a random direction the cell actually connects to.
            int count = 0, direction = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((pickedMask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                count++;
                if (Random.Range(0, count) == 0) direction = dir;
            }

            (CarController controller, TrafficCarInput driver) = VehicleRigBuilder.Build<TrafficCarInput>(
                definition.model, settings.carConfig, settings.modelScale,
                graph.CellCenter(pickedCell) + Vector3.up * 0.5f,
                Quaternion.Euler(0f, direction * 90f, 0f));
            if (controller == null) return false; // model not riggable — warned by the builder

            driver.Initialize(settings, city, definition.stopsRandomly);
            vehicles.Add(driver);
            // Push the fresh rig into the physics world now, so the next
            // IsCellClear check in this same tick can see it — without this,
            // same-tick spawns can land on top of each other.
            Physics.SyncTransforms();
            return true;
        }

        TrafficVehicleDefinition PickVehicle()
        {
            TrafficVehicleDefinition picked = null;
            float totalWeight = 0f;
            foreach (TrafficVehicleDefinition definition in settings.vehicles)
            {
                if (definition?.model == null) continue;
                totalWeight += definition.weight;
                if (Random.value * totalWeight <= definition.weight) picked = definition;
            }
            return picked;
        }
    }
}
