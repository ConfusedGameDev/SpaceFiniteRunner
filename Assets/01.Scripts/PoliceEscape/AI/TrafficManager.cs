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
    /// spawn distance and the active radius, inside an allowed city block
    /// (the player's block plus edge-close neighbours — see CityBounds), and
    /// are removed once they fall beyond activeRadius + despawnPadding
    /// (hysteresis so boundary cars don't churn) or their block stops being
    /// allowed. Hand-placed DefaultVehicles are exempt by construction: this
    /// manager only ever culls cars it spawned itself. Vehicles are rigged straight from the Kenney FBX
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

            // Cull: beyond the active radius (+ padding) or fallen out of the
            // world. An escaping car is exempt from the distance cull — it IS
            // an objective, and despawning it would silently complete the
            // chase; its own flee-hold leash keeps it near the player anyway.
            float despawnDistance = settings.activeRadius + settings.despawnPadding;
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                Vector3 position = vehicles[i].transform.position;
                if (position.y > -25f
                    && (vehicles[i].Fleeing
                        || (Vector3.Distance(position, player.transform.position) <= despawnDistance
                            && city.IsNpcPositionAllowed(position))))
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

            // Reservoir pick among clear flat ground nodes inside the active
            // band — never on a ramp or an overpass deck.
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

            TrafficVehicleDefinition definition = PickVehicle();
            if (definition == null) return false;

            // Face along a random direction the cell actually connects to —
            // and stand in that direction's right-hand lane, not on the centre
            // line, so the car is born already obeying the traffic rules its
            // driver enforces.
            int count = 0, direction = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((pickedMask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                count++;
                if (Random.Range(0, count) == 0) direction = dir;
            }
            float cellSize = city.settings != null ? city.settings.cellSize : 20f;
            float lane = graph.IsCenterLineOnly(pickedNode)
                ? 0f
                : Mathf.Min(cellSize * settings.laneOffsetFraction, settings.laneOffsetMaxMeters);

            (CarController controller, TrafficCarInput driver) = VehicleRigBuilder.Build<TrafficCarInput>(
                definition.model, settings.carConfig, definition.Scale(settings.modelScale),
                graph.Center(pickedNode) + LaneRules.RightOf(direction) * lane + Vector3.up * 0.5f,
                Quaternion.Euler(0f, direction * 90f, 0f), definition.modelYaw);
            if (controller == null) return false; // model not riggable — warned by the builder
            SceneHierarchy.Adopt(controller.gameObject, SceneHierarchy.Traffic(controller.gameObject.scene));

            // Health before Initialize, so the driver's fetch finds it.
            controller.gameObject.AddComponent<CarHealth>();
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
