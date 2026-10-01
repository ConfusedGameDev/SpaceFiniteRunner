using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;

namespace ConfusedGameDev.FiniteRunner.Traffic
{
    /// <summary>
    /// The runner's oncoming traffic (OncomingTrafficPRD.md): a pool of hover
    /// cars that drive the track toward the ship. A car spawns far enough
    /// ahead that the fog hides it, on road that is already built, and goes
    /// back to the pool once it is behind the ship; a fresh one then spawns
    /// far ahead again. Hand-placed under ===SYSTEMS=== (PF_TrafficSystem);
    /// it finds nothing in the scene — the composition root
    /// (<see cref="GameManager"/>) binds it to the ship, the track and the
    /// level's <see cref="TrafficDefinition"/>, and unbound it idles. The
    /// whole pool is built at bind: nothing is instantiated or destroyed
    /// mid-run.
    /// </summary>
    public class TrafficSystem : MonoBehaviour
    {
        IChaseTarget ship;
        ITrafficBody shipBody;
        ITrafficVictim patrol; // null when the run has no chase
        TrackManager track;
        TrackGenerator generator;
        IRunState run;
        GameSettings rules;    // the explosion's look is the ship's own

        // Each body's distance and lateral at the end of the last tick: the
        // contact test sweeps from there (a ship at Light Speed covers ~36 m a
        // step, so a car and the ship can pass through each other between two).
        bool haveShipPrev, havePatrolPrev;
        float shipPrevDistance, shipPrevLateral, patrolPrevDistance, patrolPrevLateral;
        const float TeleportMeters = 400f; // a jump this long is a launch, respawn or redeploy, not a sweep

        /// <summary>
        /// A car ran into the ship. The car has already exploded and gone back
        /// to the pool; the listener (the <see cref="GameManager"/>) lands the
        /// hit on the ship — the laser's (OncomingTrafficPRD.md D3, D4).
        /// </summary>
        public event System.Action ShipStruck;
        TrafficDefinition source;
        TrafficDefinition def; // the runtime clone gameplay reads

        // Pool: every shell ever built, and the free ones per vehicle entry.
        readonly List<TrafficCar> all = new();
        readonly List<TrafficCar> active = new();
        List<TrafficCar>[] free = System.Array.Empty<List<TrafficCar>>();
        Transform poolRoot;

        System.Random rng = new();
        float nextSpacing;
        float lastTickTime;

        /// <summary>True while bound to a level with traffic.</summary>
        public bool Bound => def != null && ship != null && track != null;
        /// <summary>The live clone of the level's definition (the debug menu mirrors onto it); null unbound.</summary>
        public TrafficDefinition Definition => def;
        /// <summary>The authored asset the clone was made from — what the debug page writes.</summary>
        public TrafficDefinition DefinitionAsset => source;

        /// <summary>The system bound to the running level, for the debug page; null when no level has traffic.</summary>
        public static TrafficSystem Live { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() => Live = null; // domain reload is off
        /// <summary>Cars on the road right now.</summary>
        public IReadOnlyList<TrafficCar> ActiveCars => active;

        [ShowInInspector, ReadOnly] int ActiveCount => active.Count;

        bool suspended;
        /// <summary>
        /// True from the HIT HYPERSPACE chord to the next run (D5): the frame it
        /// turns on, every car leaves the road — the ones within
        /// <see cref="TrafficDefinition.visibleRange"/> ahead explode
        /// harmlessly, the rest just vanish — and nothing spawns while it holds.
        /// <see cref="ResetForRun"/> turns it off.
        /// </summary>
        public bool Suspended
        {
            get => suspended;
            set
            {
                if (value == suspended) return;
                suspended = value;
                if (suspended && Bound) ClearRoad();
            }
        }

        void ClearRoad()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var car = active[i];
                float ahead = car.Distance - ship.Distance;
                if (ahead > -def.despawnBehind && ahead < def.visibleRange) Explode(car, i);
                else
                {
                    Recycle(car);
                    active.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Binds the system to a run. A null <paramref name="definition"/> (the
        /// level has no traffic) or one with no spawnable vehicle unbinds it,
        /// so it idles. Rebuilds the pool when the definition changes.
        /// </summary>
        public void Bind(IChaseTarget ship, ITrafficBody shipBody, ITrafficVictim patrol, TrackManager track,
                         TrackGenerator generator, IRunState run, GameSettings rules, TrafficDefinition definition)
        {
            Unsubscribe();
            if (definition == null || !definition.HasVehicles || ship == null || shipBody == null || track == null)
            {
                Unbind();
                return;
            }

            this.ship = ship;
            this.shipBody = shipBody;
            this.patrol = patrol;
            this.track = track;
            this.generator = generator;
            this.run = run;
            this.rules = rules;
            if (generator != null) generator.Regenerated += RecycleAll;

            if (definition != source || def == null)
            {
                ClearPool();
                source = definition;
                def = Instantiate(definition);
                BuildPool();
            }
            Live = this;
            ResetForRun();
        }

        /// <summary>Drops the run: every car back to the pool, the pool torn down.</summary>
        public void Unbind()
        {
            if (Live == this) Live = null;
            Unsubscribe();
            ClearPool();
            ship = null;
            shipBody = null;
            patrol = null;
            track = null;
            generator = null;
            run = null;
            rules = null;
            source = null;
            if (def != null) Destroy(def);
            def = null;
        }

        /// <summary>A fresh run: an empty road and a new random stream.</summary>
        public void ResetForRun()
        {
            RecycleAll();
            suspended = false;
            haveShipPrev = havePatrolPrev = false;
            rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            RollSpacing();
        }

        void OnDestroy() => Unbind();

        void Unsubscribe()
        {
            if (generator != null) generator.Regenerated -= RecycleAll;
        }

        // ---- pool ----

        void BuildPool()
        {
            if (poolRoot == null)
            {
                poolRoot = new GameObject("Pool").transform;
                poolRoot.SetParent(transform, false);
            }
            var vehicles = def.vehicles;
            free = new List<TrafficCar>[vehicles.Count];
            for (int v = 0; v < vehicles.Count; v++)
            {
                free[v] = new List<TrafficCar>();
                var vehicle = vehicles[v];
                if (vehicle == null || !vehicle.IsSpawnable) continue;
                for (int k = 0; k < def.maxActive; k++)
                {
                    var shell = new GameObject($"Traffic_{vehicle.prefab.name}_{k}");
                    shell.transform.SetParent(poolRoot, false);
                    var car = shell.AddComponent<TrafficCar>();
                    car.Build(v, vehicle);
                    all.Add(car);
                    free[v].Add(car);
                }
            }
        }

        void ClearPool()
        {
            foreach (var car in all)
                if (car != null) Destroy(car.gameObject);
            all.Clear();
            active.Clear();
            free = System.Array.Empty<List<TrafficCar>>();
        }

        void Recycle(TrafficCar car)
        {
            car.gameObject.SetActive(false);
            free[car.VehicleIndex].Add(car);
        }

        void RecycleAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) Recycle(active[i]);
            active.Clear();
        }

        /// <summary>Weighted pick among the vehicle entries that still have a free shell; null when none do.</summary>
        TrafficCar TakeFree()
        {
            float total = 0f;
            for (int v = 0; v < free.Length; v++)
                if (free[v].Count > 0) total += def.vehicles[v].weight;
            if (total <= 0f) return null;

            float roll = (float)rng.NextDouble() * total;
            for (int v = 0; v < free.Length; v++)
            {
                if (free[v].Count == 0) continue;
                roll -= def.vehicles[v].weight;
                if (roll <= 0f || v == free.Length - 1) return Pop(v);
            }
            for (int v = free.Length - 1; v >= 0; v--)
                if (free[v].Count > 0) return Pop(v);
            return null;
        }

        TrafficCar Pop(int v)
        {
            var list = free[v];
            var car = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return car;
        }

        // ---- the run ----

        void FixedUpdate()
        {
            if (!Bound || ship.Paused) return;
            float dt = Time.fixedDeltaTime;

            foreach (var car in active)
            {
                Steer(car);
                car.Step(dt, def.lateralSpeed);
            }

            // Contact BEFORE the recycle: at Light Speed a car can go from ahead
            // of the ship to past the despawn line in one tick, and the sweep
            // must still see the pass.
            Contacts();

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var car = active[i];
                // Behind the ship (or before the start line): back to the pool.
                if (car.Distance < ship.Distance - def.despawnBehind || car.Distance < 0f)
                {
                    Recycle(car);
                    active.RemoveAt(i);
                }
            }

            TrySpawn();
            lastTickTime = Time.fixedTime;
        }

        // ---- contact (R4, R5): analytic and swept, no colliders ----

        void Contacts()
        {
            bool live = run == null || (!run.IsEnding && !run.RunOver);

            float shipNow = shipBody.Distance, shipLat = shipBody.Lateral;
            if (!haveShipPrev || Mathf.Abs(shipNow - shipPrevDistance) > TeleportMeters)
            {
                shipPrevDistance = shipNow;
                shipPrevLateral = shipLat;
            }
            bool patrolSolid = patrol != null && (Object)patrol != null && patrol.TrafficSolid;
            float patrolNow = patrolSolid ? patrol.Distance : 0f, patrolLat = patrolSolid ? patrol.Lateral : 0f;
            if (patrolSolid && (!havePatrolPrev || Mathf.Abs(patrolNow - patrolPrevDistance) > TeleportMeters))
            {
                patrolPrevDistance = patrolNow;
                patrolPrevLateral = patrolLat;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var car = active[i];
                if (live && shipBody.TrafficSolid &&
                    Touches(car, shipPrevDistance, shipNow, shipPrevLateral, shipLat, shipBody.TrafficHeight, shipBody.TrafficReach))
                {
                    // D4: the car goes whether or not the blink shields the ship.
                    Explode(car, i);
                    ShipStruck?.Invoke();
                    continue;
                }
                if (patrolSolid &&
                    Touches(car, patrolPrevDistance, patrolNow, patrolPrevLateral, patrolLat, patrol.TrafficHeight, patrol.TrafficReach))
                {
                    Explode(car, i);
                    patrol.HitByTraffic();
                    patrolSolid = false; // it is hidden and redeployed now
                }
            }

            shipPrevDistance = shipNow;
            shipPrevLateral = shipLat;
            haveShipPrev = true;
            if (patrol != null && (Object)patrol != null && patrol.TrafficSolid)
            {
                patrolPrevDistance = patrol.Distance;
                patrolPrevLateral = patrol.Lateral;
                havePatrolPrev = true;
            }
            else havePatrolPrev = false;
        }

        /// <summary>
        /// True when the car and a body overlapped at any moment of the tick:
        /// the gap along the track between them, swept from last tick to this
        /// one, came within both half lengths; across the track at the
        /// crossing they were within both half widths; and the body was not
        /// flying over the car's roof.
        /// </summary>
        static bool Touches(TrafficCar car, float bodyPrev, float bodyNow, float latPrev, float latNow,
                            float bodyHeight, Vector3 reach)
        {
            TrafficVehicle v = car.Vehicle;
            float gapPrev = car.PrevDistance - bodyPrev;
            float gapNow = car.Distance - bodyNow;
            float along = v.halfLength + reach.z;
            if (Mathf.Min(gapPrev, gapNow) > along || Mathf.Max(gapPrev, gapNow) < -along) return false;

            // Where in the tick the two were level (or the end of it, if they never were).
            float t = Mathf.Abs(gapPrev - gapNow) > 1e-4f ? Mathf.Clamp01(gapPrev / (gapPrev - gapNow)) : 1f;
            float across = Mathf.Lerp(car.PrevLateral, car.Lateral, t) - Mathf.Lerp(latPrev, latNow, t);
            if (Mathf.Abs(across) > v.halfWidth + reach.x) return false;

            return bodyHeight - reach.y < v.hoverHeight + v.height;
        }

        /// <summary>Blows the car up where it is and returns it to the pool.</summary>
        void Explode(TrafficCar car, int index)
        {
            if (rules != null && rules.explosionTextures != null && rules.explosionTextures.Count > 0)
            {
                track.GetPoseAtDistance(Mathf.Max(0f, car.Distance), car.Lateral, out Vector3 pos, out Quaternion rot);
                pos += rot * (Vector3.up * (car.Vehicle.hoverHeight + car.Vehicle.height * 0.5f));
                FX.ExplosionVfx.SpawnFireball(pos, rules.explosionTextures, rules.explosionScale * def.explosionScale,
                                              rules.explosionLifetime, rules.explosionParticles);
            }
            Recycle(car);
            active.RemoveAt(index);
        }

        void Update()
        {
            if (!Bound || active.Count == 0) return;
            float alpha = ship.Paused ? 1f : Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f));
            foreach (var car in active) car.ApplyPose(track, alpha);
        }

        /// <summary>At most one car per tick, far ahead, on built road, clear of the others.</summary>
        void TrySpawn()
        {
            if (suspended || active.Count >= def.maxActive) return;
            if (!ship.Steady) return;
            if (run != null && (run.RunOver || run.IsEnding)) return;
            // Close to the end the run is all but decided: the road stays empty (D6).
            if (run != null && run.HasTrackEnd && run.DistanceRemaining <= def.noSpawnNearEnd) return;

            float ahead = Mathf.Max(def.minSpawnAhead, ship.Speed * def.spawnAheadSeconds);
            float d = ship.Distance + ahead;
            // Only on road that is already built: the art, colliders and decoration are there.
            if (generator != null) d = Mathf.Min(d, generator.SettledDistance);
            // Never on the final run-up — nothing drives the end ramps but the ship.
            if (track.EndZoneStart >= 0f) d = Mathf.Min(d, track.EndZoneStart - 1f);
            else if (track.HasEnd) d = Mathf.Min(d, track.EndDistance);
            if (d - ship.Distance < def.minSpawnAhead) return;

            foreach (var other in active)
                if (Mathf.Abs(other.Distance - d) < nextSpacing) return;

            // Open road only (D1): no loop or tube between the car and the
            // ship, so it never has to drive one backwards. The ship only moves
            // on and the car only comes back, so a clear path stays clear.
            foreach (var section in track.Sections)
                if (section != null && section.StartDistance < d && section.EndDistance > ship.Distance) return;

            var car = TakeFree();
            if (car == null) return;

            // Every ramp and gate on the way must leave this car a gap (R3.3),
            // and the spawn spot itself must have a free lane.
            float pad = car.Vehicle.halfWidth + def.avoidMargin;
            if (!PathPassable(ship.Distance, d, car.Vehicle.halfWidth, pad) ||
                !FreeLanes(d, d - def.avoidLookahead, car.Vehicle.halfWidth, pad))
            {
                free[car.VehicleIndex].Add(car);
                return;
            }

            Vector2 lane = freeLanes[rng.Next(freeLanes.Count)];
            float lateral = Mathf.Lerp(lane.x, lane.y, (float)rng.NextDouble());
            float speed = Mathf.Lerp(def.MinSpeed, def.MaxSpeed, (float)rng.NextDouble());

            car.Place(d, lateral, speed);
            car.ApplyPose(track, 1f);
            active.Add(car);
            RollSpacing();
        }

        // ---- avoidance (R3.2, R3.3) ----

        readonly List<TrackObstacles.Obstacle> obstacles = new();
        readonly List<TrackObstacles.Obstacle> single = new(1);
        readonly List<Vector2> freeLanes = new();

        /// <summary>Points the car's lane at the free stretch of road nearest its home lane, over the road it is about to drive.</summary>
        void Steer(TrafficCar car)
        {
            float pad = car.Vehicle.halfWidth + def.avoidMargin;
            if (FreeLanes(car.Distance, car.Distance - def.avoidLookahead, car.Vehicle.halfWidth, pad) &&
                TrackObstacles.Nearest(freeLanes, car.HomeLateral, out float target))
                car.TargetLateral = target;
            // No free lane at all (a bad tune): hold the line it has.
        }

        /// <summary>
        /// The lanes a car's CENTRE may take at <paramref name="at"/> while the
        /// road from <paramref name="at"/> back to <paramref name="to"/> holds
        /// every obstacle it must clear — into <see cref="freeLanes"/>. False
        /// when there is none.
        /// </summary>
        bool FreeLanes(float at, float to, float halfWidth, float pad)
        {
            Band(at, halfWidth, out float lo, out float hi);
            // An open edge coming up pulls the lane in before the car reaches it.
            if (track.IsEdgeOpen(to, -1)) lo = Mathf.Max(lo, -track.HalfWidth + halfWidth + def.edgeMargin);
            if (track.IsEdgeOpen(to, 1)) hi = Mathf.Min(hi, track.HalfWidth - halfWidth - def.edgeMargin);
            obstacles.Clear();
            TrackObstacles.Collect(Mathf.Min(at, to), Mathf.Max(at, to), pad, obstacles);
            TrackObstacles.FreeIntervals(lo, hi, obstacles, freeLanes);
            return freeLanes.Count > 0;
        }

        /// <summary>True when every obstacle between the two distances leaves a car of this size a gap of its own.</summary>
        bool PathPassable(float from, float to, float halfWidth, float pad)
        {
            obstacles.Clear();
            TrackObstacles.Collect(from, to, pad, obstacles);
            foreach (var o in obstacles)
            {
                Band((o.start + o.end) * 0.5f, halfWidth, out float lo, out float hi);
                single.Clear();
                single.Add(o);
                TrackObstacles.FreeIntervals(lo, hi, single, freeLanes);
                if (freeLanes.Count == 0) return false;
            }
            return true;
        }

        /// <summary>Where a car's centre may be at a distance: the lane, less its half width and a margin off any open edge.</summary>
        void Band(float distance, float halfWidth, out float lo, out float hi)
        {
            track.GetLateralBand(distance, out float min, out float max);
            lo = min + halfWidth + (track.IsEdgeOpen(distance, -1) ? def.edgeMargin : 0f);
            hi = max - halfWidth - (track.IsEdgeOpen(distance, 1) ? def.edgeMargin : 0f);
        }

        void RollSpacing()
        {
            nextSpacing = def != null ? Mathf.Lerp(def.MinSpawnSpacing, def.MaxSpawnSpacing, (float)rng.NextDouble()) : 0f;
        }
    }
}
