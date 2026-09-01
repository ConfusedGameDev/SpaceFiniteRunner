using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.Debugging;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// The civilian driver: an ICarInput like the police, so traffic runs
    /// the same CarController physics — but it only ever wanders the
    /// RoadGraph at its personal cruise speed, brakes to a queue behind
    /// whatever car is ahead, and (for work vehicles like the garbage
    /// truck) randomly pulls to a stop for a few seconds before rolling on.
    /// No perception, no pathfinding — that keeps a whole fleet cheap; the
    /// wander/drive core intentionally mirrors PoliceCarInput's Patrol
    /// behavior. All knobs live on TrafficSettings.
    ///
    /// Any civilian can be promoted into the ESCAPING CAR of a Chase Car
    /// objective (<see cref="BecomeEscapeCar"/>): it registers under the
    /// objective's id, drives at the PLAYER'S top speed, picks wander nodes
    /// away from the player instead of at random, and — because the city only
    /// exists around the player — parks and waits whenever it gets more than
    /// the flee hold distance ahead, so it can never flee off the edge of the
    /// streamed world. Everything else (queues, stuck recovery, health) is the
    /// same civilian core, which is the point: the escapee is just a scared
    /// citizen, not a second police AI.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class TrafficCarInput : MonoBehaviour, ICarInput, IAiDebugDriver
    {
        [Required, InlineEditor]
        [Tooltip("All traffic tunables live on this asset — assigned by the TrafficManager at spawn.")]
        public TrafficSettings settings;

        // The escaping cars of Chase Car objectives, by objective id — same
        // registry idiom as TargetObject, so the LevelManager and both maps
        // resolve the car without holding references across its destruction.
        static readonly Dictionary<string, TrafficCarInput> EscapeRegistry = new();

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public bool Stopped { get; private set; }

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public bool Fleeing { get; private set; }

        /// <summary>The Chase Car objective id this car escapes under (null for ordinary traffic).</summary>
        public string EscapeId { get; private set; }

        /// <summary>What kind of car this civilian drives and what colour it is — the CarController's identity, surfaced on the NPC.</summary>
        public VehicleIdentity Identity => car != null ? car.identity : default;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake => Stopped || (health != null && health.IsDead);
        public bool RespawnPressed => false;

        CarController car;
        CarHealth health;
        CityManager city;
        bool stopsRandomly;
        bool offRoad;
        float cruiseSpeedKmh = 30f;
        readonly List<Vector3> waypoints = new();
        RoadNode wanderFrom;
        // Graph node of the LAST queued waypoint — an overpass deck shares its
        // XZ with the street below, so the level travels with the plan.
        RoadNode? planHead;
        Vector3 previousWaypoint;
        // Grid direction the plan is travelling (0..3 = N,E,S,W, -1 unknown).
        // The wander rule re-derives it from the plan (or the car's nose on a
        // fresh plan), so this is the debug/recovery record, not the authority.
        int travelDirection = -1;
        float stopTimer, stoppedTimer, stuckTimer, reverseTimer, noProgressTime;
        Vector3 progressAnchor;
        float lastForwardSteer;
        float reverseSteer;
        float queuedPatienceFactor = 3f;
        float recentContactTimer;
        float obstacleHitSide;
        ObstacleKind lastObstacle;

        // Debug mirror: what the driver actually decided this frame, kept so
        // the overlay can show the real decision instead of re-deriving one.
        readonly AiProbeLog probeLog = new();
        Vector3 steerAim;

        CarController playerCar;
        float playerRefreshTimer;
        Transform escapeArrow;
        Vector3 escapeArrowAnchor;

        static readonly Collider[] OverlapBuffer = new Collider[8];

        // Shared by every escape arrow in the run — same caching rule as the
        // smoke plume materials.
        static Mesh arrowHeadMesh;
        static Material arrowMaterial;

        /// <summary>The live escaping car registered under a Chase Car objective id, if any.</summary>
        public static bool TryFindEscaping(string id, out TrafficCarInput car)
        {
            car = null;
            if (string.IsNullOrEmpty(id)) return false;
            return EscapeRegistry.TryGetValue(id.Trim(), out car) && car != null;
        }

        /// <summary>Copy every live escaping car into the buffer — how the minimap and the city map draw their yellow markers.</summary>
        public static void GetEscaping(List<TrafficCarInput> into)
        {
            into.Clear();
            foreach (TrafficCarInput car in EscapeRegistry.Values)
                if (car != null) into.Add(car);
        }

        /// <summary>
        /// Promote this civilian into the escaping car of a Chase Car
        /// objective: register under the id, flee at the player's own top
        /// speed (the chase is winnable on driving skill, never on raw pace),
        /// and drop any work-vehicle stop habit — nobody pulls over mid-getaway.
        /// </summary>
        public void BecomeEscapeCar(string id, float topSpeedKmh)
        {
            if (string.IsNullOrEmpty(id)) return;
            EscapeId = id.Trim();
            if (EscapeRegistry.TryGetValue(EscapeId, out var other) && other != null && other != this)
                Debug.LogWarning($"Two escaping cars under id '{EscapeId}' — the newest wins.", this);
            EscapeRegistry[EscapeId] = this;
            Fleeing = true;
            stopsRandomly = false;
            Stopped = false;
            cruiseSpeedKmh = topSpeedKmh;
            ClearPlan(); // the wander plan was aimless — replan away from the player right away
            previousWaypoint = transform.position;
            BuildEscapeArrow();
        }

        void OnDestroy()
        {
            if (EscapeId != null && EscapeRegistry.TryGetValue(EscapeId, out var current) && current == this)
                EscapeRegistry.Remove(EscapeId);
            // The wreck strips this driver but keeps the hull — the arrow must
            // go with the driver, because a dead car is no longer the mark.
            if (escapeArrow != null) Destroy(escapeArrow.gameObject);
        }

        /// <summary>
        /// The over-head marker: a red arrow hovering above the roof, tip
        /// down — built from code like the police cruiser's visual, and
        /// collider-free so it can never trip a trigger or a spawn check.
        /// Rides the car as a child but is kept upright and spun in WORLD
        /// space by the animator, so the car's roll and pitch never tilt it.
        /// </summary>
        void BuildEscapeArrow()
        {
            if (escapeArrow != null) return;
            escapeArrow = new GameObject("EscapeArrow").transform;
            escapeArrow.SetParent(transform, false);

            // Anchor off the chassis box, so trucks carry it above their taller roof.
            var box = GetComponent<BoxCollider>();
            float roof = box != null ? box.center.y + box.size.y * 0.5f : 1.6f;
            escapeArrowAnchor = new Vector3(0f, roof + 1.4f, 0f);
            escapeArrow.localPosition = escapeArrowAnchor;

            // Head: a pyramid with the tip at the group's origin, pointing down.
            var head = new GameObject("Head");
            head.transform.SetParent(escapeArrow, false);
            head.AddComponent<MeshFilter>().sharedMesh = ArrowHeadMesh();
            SetupArrowRenderer(head.AddComponent<MeshRenderer>());

            // Shaft: a slim cube sitting on the head's base.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(escapeArrow, false);
            shaft.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            shaft.transform.localScale = new Vector3(0.28f, 0.7f, 0.28f);
            SetupArrowRenderer(shaft.GetComponent<MeshRenderer>());
        }

        /// <summary>Bob on the car, spin upright in world space — a tilted arrow would stop reading as "down".</summary>
        void AnimateEscapeArrow()
        {
            float bob = Mathf.Sin(Time.time * 3.2f) * 0.25f;
            escapeArrow.localPosition = escapeArrowAnchor + Vector3.up * bob;
            escapeArrow.rotation = Quaternion.Euler(0f, Time.time * 140f % 360f, 0f);
        }

        static void SetupArrowRenderer(MeshRenderer meshRenderer)
        {
            meshRenderer.sharedMaterial = ArrowMaterial();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>Unlit red, so the marker reads at any time of day — same shader idiom as TargetObject's beam.</summary>
        static Material ArrowMaterial()
        {
            if (arrowMaterial != null) return arrowMaterial;
            var color = new Color(1f, 0.12f, 0.1f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            arrowMaterial = new Material(shader) { color = color };
            if (arrowMaterial.HasProperty("_BaseColor")) arrowMaterial.SetColor("_BaseColor", color);
            return arrowMaterial;
        }

        /// <summary>Four-sided pyramid, apex at the origin pointing down, base square at the top.</summary>
        static Mesh ArrowHeadMesh()
        {
            if (arrowHeadMesh != null) return arrowHeadMesh;
            const float w = 0.5f;  // base half-width
            const float h = 1.0f;  // apex-to-base height
            var vertices = new[]
            {
                Vector3.zero,             // 0 apex (the tip, pointing down)
                new Vector3(-w, h, -w),   // 1
                new Vector3( w, h, -w),   // 2
                new Vector3( w, h,  w),   // 3
                new Vector3(-w, h,  w),   // 4
            };
            var triangles = new[]
            {
                0, 1, 2,   0, 2, 3,   0, 3, 4,   0, 4, 1,   // sides, outward-facing
                1, 4, 3,   1, 3, 2,                          // base cap, facing up
            };
            arrowHeadMesh = new Mesh { name = "EscapeArrowHead", vertices = vertices, triangles = triangles };
            arrowHeadMesh.RecalculateNormals();
            arrowHeadMesh.RecalculateBounds();
            return arrowHeadMesh;
        }

        float CellSize => city != null && city.settings != null ? city.settings.cellSize : 20f;

        public void Initialize(TrafficSettings trafficSettings, CityManager cityManager, bool stops)
        {
            settings = trafficSettings;
            city = cityManager;
            stopsRandomly = stops;
            health = GetComponent<CarHealth>(); // attached by the manager before Initialize
            cruiseSpeedKmh = Random.Range(settings.CruiseMin, settings.CruiseMax);
            stopTimer = Random.Range(settings.StopEveryMin, settings.StopEveryMax);
        }

        void Awake()
        {
            car = GetComponent<CarController>();
            previousWaypoint = transform.position;
            progressAnchor = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (settings == null || city == null || city.Graph == null || city.Graph.Count == 0)
            {
                Steer = 0f;
                Throttle = 0f;
                return;
            }

            if (recentContactTimer > 0f) recentContactTimer -= dt;
            if (escapeArrow != null) AnimateEscapeArrow();

            // A dead car is a wreck burning its fuse: brake to a stop and hold.
            // Before the self-heal on purpose — a wreck must never be teleported
            // back onto the road, and must not reverse out of anything either.
            if (health != null && health.IsDead)
            {
                Steer = 0f;
                Throttle = car.SpeedKmh > 3f ? -0.5f : 0f;
                return; // Handbrake holds the wreck
            }

            // Last-resort self-heal: NO NET PROGRESS for too long → snap onto
            // the road. Displacement, not speed: a reverse-crash-reverse loop
            // keeps momentary speed (each reverse resets a low-speed timer)
            // while going nowhere. Deliberate stops excluded; a failed recover
            // (target cell occupied) retries next frame.
            if (Stopped || FlatDistance(transform.position, progressAnchor) > CellSize * 0.6f)
            {
                progressAnchor = transform.position;
                noProgressTime = 0f;
            }
            // Politely queued behind another car (no collision involved) is
            // patience, not stuckness — don't teleport a car out of a queue.
            else if (lastObstacle != ObstacleKind.Vehicle || recentContactTimer > 0f)
                noProgressTime += dt;
            if (noProgressTime >= settings.hardRecoverSeconds && HardRecover())
                return;

            if (Fleeing) UpdateFleeHold(dt);
            else if (stopsRandomly) UpdateStopCycle(dt);
            if (Stopped)
            {
                Steer = 0f;
                Throttle = 0f;
                return; // Handbrake holds the vehicle
            }

            // Roads only: if we're off the grid (clipped a corner, got shoved
            // into a lot), drop the plan and creep straight back to the
            // nearest road cell instead of wandering onward through buildings.
            RoadGraph graph = city.Graph;
            offRoad = !graph.TryGetNodeAt(transform.position, out _);
            if (offRoad)
            {
                // Replan unless we're already creeping to that exact node —
                // a stale single-waypoint plan must not block recovery.
                if (graph.TryGetNearestNode(transform.position, out RoadNode nearest)
                    && (waypoints.Count != 1 || planHead != nearest))
                {
                    ClearPlan();
                    waypoints.Add(graph.Center(nearest));
                    planHead = nearest;
                    previousWaypoint = transform.position;
                }
            }
            else
            {
                // Keep a few cells queued so the corner slowdown can look ahead.
                for (int i = 0; waypoints.Count < 3 && i < 4; i++) ExtendWander();
            }

            Drive(dt);
        }

        /// <summary>Drop the current plan — waypoints and the node they ended on go together.</summary>
        void ClearPlan()
        {
            waypoints.Clear();
            planHead = null;
        }

        /// <summary>
        /// Flee-mode leash: the city only exists around the player, so an
        /// escapee that gets more than the hold distance ahead parks (the
        /// handbrake hold of Stopped) and waits for the chase to catch up
        /// rather than driving off the edge of the streamed world.
        /// </summary>
        void UpdateFleeHold(float dt)
        {
            playerRefreshTimer -= dt;
            if (playerRefreshTimer <= 0f || playerCar == null)
            {
                playerRefreshTimer = 1f;
                playerCar = PatrolManager.FindPlayerCar();
            }
            Stopped = playerCar != null
                && FlatDistance(transform.position, playerCar.transform.position) > settings.fleeHoldDistance;
        }

        /// <summary>Work-vehicle rhythm: drive a while, pull to a stop for a few seconds, repeat — each interval rolled fresh.</summary>
        void UpdateStopCycle(float dt)
        {
            if (Stopped)
            {
                stoppedTimer -= dt;
                if (stoppedTimer > 0f) return;
                Stopped = false;
                stopTimer = Random.Range(settings.StopEveryMin, settings.StopEveryMax);
                return;
            }

            stopTimer -= dt;
            if (stopTimer > 0f) return;
            Stopped = true;
            stoppedTimer = Random.Range(settings.StopDurationMin, settings.StopDurationMax);
        }

        void Drive(float dt)
        {
            if (reverseTimer > 0f)
            {
                reverseTimer -= dt;
                Throttle = -0.6f;
                Steer = reverseSteer;
                return;
            }

            // Tight tracking: pop when genuinely close OR just passed — a
            // missed waypoint must never become an orbit center. Measured
            // against the SAME lane-offset point we steer at: popping against
            // the raw cell center while aiming beside it is exactly how a
            // never-satisfied waypoint becomes an orbit center.
            float reach = Mathf.Min(settings.waypointReachDistance, CellSize * 0.4f);
            while (waypoints.Count > 0)
            {
                Vector3? next = waypoints.Count >= 2 ? waypoints[1] : (Vector3?)null;
                Vector3 to = SteerTarget(waypoints[0], previousWaypoint, next) - transform.position;
                to.y = 0f;
                bool reached = to.magnitude < reach;
                bool passed = Vector3.Dot(transform.forward, to) < 0f && to.magnitude < reach * 2.5f;
                if (!reached && !passed) break;
                previousWaypoint = waypoints[0];
                waypoints.RemoveAt(0);
            }

            if (waypoints.Count == 0)
            {
                Steer = 0f;
                Throttle = car.SpeedKmh > 5f ? -0.3f : 0f;
                steerAim = transform.position;
                return;
            }

            Vector3 target = SteerTarget(waypoints[0], previousWaypoint, waypoints.Count >= 2 ? waypoints[1] : (Vector3?)null);
            steerAim = target;
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float maxSteerAngle = car.config != null ? car.config.maxSteerAngle : 35f;
            Steer = Mathf.Clamp(angle / maxSteerAngle, -1f, 1f);
            lastForwardSteer = Steer;

            // Slow BEFORE the corner, not just in it: when the next leg bends,
            // the closer we get to the corner cell the harder we brake.
            float steerFactor = Mathf.Clamp01(1f - Mathf.Abs(angle) / 90f);
            float approachFactor = 1f;
            if (waypoints.Count >= 2)
            {
                float turnAhead = Vector3.Angle(Flat(waypoints[1] - waypoints[0]), Flat(waypoints[0] - transform.position)); // XZ only — a ramp is a climb, not a corner
                if (turnAhead > 30f)
                    approachFactor = Mathf.Clamp01(FlatDistance(transform.position, waypoints[0]) / (CellSize * 1.2f));
            }
            float cornerSpeed = Fleeing ? settings.fleeCornerSpeedKmh : settings.cornerSpeedKmh;
            float desired = Mathf.Lerp(cornerSpeed, cruiseSpeedKmh, Mathf.Min(steerFactor, approachFactor));
            if (offRoad) desired = Mathf.Min(desired, cornerSpeed); // creep back onto the road
            ObstacleKind obstacle = ObstacleAhead();
            lastObstacle = obstacle;
            if (obstacle != ObstacleKind.None) desired = 0f; // queue politely / don't wedge into the wall
            if (health != null) desired *= health.SpeedFactor; // a wounded engine can't hold cruise speed
            Throttle = Mathf.Clamp((desired - car.SpeedKmh) * settings.throttleGain, -1f, 1f);

            // Stuck escalation while standing still: walls escalate fast (we're
            // wedged, back out), a vehicle ahead is waited on patiently (it's
            // probably a queue), and plain wheelspin counts as wedged too.
            // Patience is rolled per episode and cut after a real collision —
            // two crashed cars on identical timers reverse in lockstep and
            // re-collide forever.
            bool standing = car.SpeedKmh < 3f;
            bool wedged = obstacle == ObstacleKind.Wall || (obstacle == ObstacleKind.None && Mathf.Abs(Throttle) > 0.2f && desired > 1f);
            bool queued = obstacle == ObstacleKind.Vehicle;
            bool wasStuck = stuckTimer > 0f;
            stuckTimer = standing && (wedged || queued) ? stuckTimer + dt : 0f;
            if (stuckTimer > 0f && !wasStuck) queuedPatienceFactor = Random.Range(1.5f, 3f);
            float patience = recentContactTimer > 0f ? 1f : queuedPatienceFactor;
            float escalation = queued ? settings.stuckSeconds * patience : settings.stuckSeconds;
            if (stuckTimer >= escalation)
            {
                if (settings.logTrafficEvents)
                    Debug.Log($"[Traffic] stuck escalation ({(queued ? "queued" : "wedged")}) at {transform.position}", this);
                stuckTimer = 0f;
                reverseTimer = settings.reverseSeconds * Random.Range(0.7f, 1.8f);
                // Reverse steering TOWARD the obstacle side swings the nose
                // away from it (front-steer kinematics reverse the yaw).
                reverseSteer = obstacleHitSide != 0f
                    ? obstacleHitSide * 0.7f
                    : -Mathf.Sign(lastForwardSteer) * 0.7f;
                ClearPlan();
                previousWaypoint = transform.position;
                if (city.Graph.TryGetNodeAt(transform.position, out RoadNode here)) wanderFrom = here; // stale value could allow an instant U-turn into the wreck
            }
        }

        /// <summary>Lane offset in metres: the designer fraction of a cell under an absolute cap, so wide cells can't push the lane onto the sidewalk.</summary>
        float LaneOffset => Mathf.Min(CellSize * settings.laneOffsetFraction, settings.laneOffsetMaxMeters);

        /// <summary>
        /// The point we actually steer at: the waypoint pushed into the
        /// right-hand lane (a miter join when the next waypoint is known, so
        /// corner arrivals land in the OUTGOING leg's lane — see
        /// <see cref="LaneRules.LaneTarget"/>). Anchored to the SEGMENT
        /// direction (previous → current waypoint) so the target is a fixed
        /// point in space — offsetting by the live approach vector rotates the
        /// target with the car and creates merry-go-rounds. Fork seams and
        /// roundabout footprints collapse to the centre line.
        /// </summary>
        Vector3 SteerTarget(Vector3 waypoint, Vector3 previous, Vector3? next)
        {
            float lane = city.Graph.IsCenterLineOnlyAt(waypoint) ? 0f : LaneOffset;
            return LaneRules.LaneTarget(previous, waypoint, next, lane);
        }

        void OnCollisionEnter(Collision collision)
        {
            // A car we just hit is a wreck to back out of, not a queue to
            // wait in — short patience for a while after any vehicle contact.
            if (collision.rigidbody == null) return;
            recentContactTimer = 3f;
            if (settings != null && settings.logTrafficEvents)
                Debug.Log($"[Traffic] vehicle contact with {collision.gameObject.name}", this);
        }

        /// <summary>
        /// Snap onto the nearest road cell, aligned with the road — the
        /// ambient-traffic answer to a hopeless wedge. Refuses (returns false,
        /// caller retries next frame) while another car occupies the cell, so
        /// recovery never materializes one wreck inside another.
        /// </summary>
        bool HardRecover()
        {
            RoadGraph graph = city.Graph;
            if (!graph.TryGetNearestNode(transform.position, out RoadNode node)) return false;
            Vector3 center = graph.Center(node);
            if (!CellClearOfOtherCars(center)) return false;

            if (settings.logTrafficEvents)
                Debug.Log($"[Traffic] HardRecover to {node}", this);
            noProgressTime = 0f;
            stuckTimer = 0f;
            reverseTimer = 0f;
            ClearPlan();
            // Re-enter traffic legally: resume the nearest connected direction
            // to the old heading, standing in that direction's lane — a random
            // yaw here points a recovered car into oncoming traffic half the
            // time.
            int dir = LaneRules.NearestConnectedDirection(graph.Connections(node), transform.forward);
            float lane = graph.IsCenterLineOnly(node) ? 0f : LaneOffset;
            Vector3 pose = center + LaneRules.RightOf(dir) * lane;
            progressAnchor = pose;
            car.Body.linearVelocity = Vector3.zero;
            car.Body.angularVelocity = Vector3.zero;
            CarFactory.Teleport(car.Body, pose + Vector3.up * 0.5f, Quaternion.Euler(0f, dir * 90f, 0f));
            previousWaypoint = transform.position;
            travelDirection = dir;
            return true;
        }

        /// <summary>No FOREIGN rigidbody in the cell — own colliders don't count, so a car can always recover onto the cell it already stands on.</summary>
        bool CellClearOfOtherCars(Vector3 center)
        {
            Vector3 halfExtents = new(CellSize * 0.45f, 1f, CellSize * 0.45f);
            int count = Physics.OverlapBoxNonAlloc(center + Vector3.up, halfExtents, OverlapBuffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody other = OverlapBuffer[i].attachedRigidbody;
                if (other != null && other != car.Body) return false;
            }
            return true;
        }

        /// <summary>
        /// The avoidance fan. Every cast is recorded into <see cref="probeLog"/>
        /// while the debug overlay is on, verdict included: the visualizer must
        /// show the decision that was made, not one it re-derives a frame later.
        /// </summary>
        ObstacleKind ObstacleAhead()
        {
            obstacleHitSide = 0f;
            bool log = DebugManager.ShowCollisionProbes;
            if (log) probeLog.Begin();
            Vector3 origin = transform.position + Vector3.up * 0.6f + transform.forward * 2.6f;

            // Three forward rays — center plus both fenders. A single center
            // ray lets a building corner slip past and catch the fender, which
            // is exactly how cars wedge nose-first on junction corners.
            for (int i = 0; i < 3; i++)
            {
                Vector3 rayOrigin = origin + transform.right * ((i - 1) * 0.85f);
                bool blocked = Physics.Raycast(rayOrigin, transform.forward, out RaycastHit hit,
                                   settings.forwardBrakeDistance, ~0, QueryTriggerInteraction.Ignore)
                               && !hit.transform.IsChildOf(transform);
                ObstacleKind verdict = ObstacleKind.None;
                if (blocked)
                {
                    obstacleHitSide = Mathf.Sign(transform.InverseTransformPoint(hit.point).x);
                    if (hit.rigidbody != null && hit.rigidbody != car.Body) verdict = ObstacleKind.Vehicle;
                    // Static geometry: a genuinely-close hit is an imminent wedge
                    // regardless of steering; farther hits only count when head-on
                    // and not mid-turn — a hard-steering car's ray sweeps off a
                    // corner facade by itself, and grazing hits are normal there.
                    else if (hit.rigidbody == null)
                    {
                        // A drivable slope — a ramp surface, or the flat street seen
                        // from the top of a down-ramp — is not a wall to stop for.
                        bool headOn = Vector3.Dot(hit.normal, transform.forward) < -0.5f;
                        if (hit.normal.y > 0.35f) verdict = ObstacleKind.None;
                        else if (hit.distance < 1.2f) verdict = ObstacleKind.Wall;
                        else if (hit.distance < settings.wallBrakeDistance && headOn && Mathf.Abs(Steer) < 0.5f)
                            verdict = ObstacleKind.Wall;
                    }
                    if (verdict == ObstacleKind.None) obstacleHitSide = 0f;
                }
                if (log)
                    probeLog.Add(i == 1 ? AiProbeRole.Forward : AiProbeRole.Fender, rayOrigin, transform.forward,
                        settings.forwardBrakeDistance, blocked, hit.point, verdict);
                if (verdict != ObstacleKind.None) return verdict;
            }

            // Junction yield: a right-hand whisker sees crossing traffic the
            // forward ray can't. Vehicles only — buildings on the whisker are
            // normal at corners. Right-only is the tiebreak (priority to the
            // right): of two converging cars, exactly one yields.
            Vector3 whisker = Quaternion.AngleAxis(settings.yieldWhiskerAngle, Vector3.up) * transform.forward;
            bool sideHit = Physics.Raycast(origin, whisker, out RaycastHit side,
                               settings.yieldWhiskerDistance, ~0, QueryTriggerInteraction.Ignore)
                           && !side.transform.IsChildOf(transform);
            bool yields = sideHit && side.rigidbody != null && side.rigidbody != car.Body;
            if (log)
                probeLog.Add(AiProbeRole.Whisker, origin, whisker, settings.yieldWhiskerDistance, sideHit, side.point,
                    yields ? ObstacleKind.Vehicle : ObstacleKind.None);
            if (yields)
            {
                obstacleHitSide = 1f;
                return ObstacleKind.Vehicle;
            }

            return ObstacleKind.None;
        }

        /// <summary>
        /// Append one more wander cell under the traffic rules: never the
        /// reverse of the direction of travel (a U-turn puts the car in the
        /// oncoming lane — dead ends stay the one legal flip), a straight
        /// bias so junctions read as through-traffic, otherwise a random
        /// connected neighbour. The direction of travel is re-derived from
        /// the plan itself — or from the car's own nose on a fresh plan, so a
        /// crash-spun car legally resumes in whichever direction it now
        /// faces. A fleeing car swaps the random pick for the neighbour
        /// farthest from the player (with a little jitter so a grid-perfect
        /// player can't predict every turn) — flight as greedy node choice,
        /// no pathfinding, so it costs what a civilian costs.
        /// </summary>
        void ExtendWander()
        {
            RoadGraph graph = city.Graph;
            RoadNode from;
            if (waypoints.Count > 0 && planHead.HasValue) from = planHead.Value;
            else if (waypoints.Count > 0 && graph.TryGetNodeAt(waypoints[^1], out from)) { }
            else if (!TryGetNodeOn(graph, transform.position, out from)) return;

            // The direction we arrive at 'from' with: the plan's last segment
            // when there is one (seam twins can share a centre — fall through),
            // else the car's own heading.
            int incoming = waypoints.Count > 0
                ? LaneRules.SegmentDirection(waypoints.Count >= 2 ? waypoints[^2] : previousWaypoint, waypoints[^1])
                : -1;
            if (incoming < 0) incoming = LaneRules.HeadingToDirection(transform.forward);
            int banned = LaneRules.ReverseOf(incoming);

            bool flee = Fleeing && playerCar != null;
            Vector3 threat = flee ? playerCar.transform.position : Vector3.zero;

            RoadNode pick = default;
            RoadNode straightPick = default;
            bool straightSeen = false;
            int seen = 0;
            float bestScore = float.MinValue;
            for (int dir = 0; dir < 4; dir++)
            {
                if (dir == banned) continue; // wrong-way turn — never, outside a dead end
                if (!graph.TryGetNeighbour(from, dir, out RoadNode neighbour) || neighbour == wanderFrom) continue;
                // Civilians keep to the allowed blocks; the escaping car is
                // exempt — gating its greedy flight could force a U-turn into
                // the player, and its flee-hold leash bounds it anyway.
                if (!flee && !city.IsNpcPositionAllowed(graph.Center(neighbour))) continue;
                seen++;
                if (dir == incoming)
                {
                    straightSeen = true;
                    straightPick = neighbour;
                }
                if (flee)
                {
                    float score = FlatDistance(graph.Center(neighbour), threat) + Random.Range(0f, CellSize * 0.5f);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    pick = neighbour;
                }
                else if (Random.Range(0, seen) == 0) pick = neighbour;
            }
            if (!flee && straightSeen && Random.value < settings.straightBias) pick = straightPick;
            if (seen == 0)
            {
                // Dead end (or everything else filtered): the U-turn is the
                // one legal direction flip left.
                if (!graph.TryGetNeighbour(from, banned, out pick))
                {
                    if (!graph.Contains(wanderFrom)) return;
                    pick = wanderFrom;
                }
            }

            wanderFrom = from;
            waypoints.Add(graph.Center(pick));
            planHead = pick;
            travelDirection = LaneRules.SegmentDirection(graph.Center(from), graph.Center(pick));
        }

        // --------------------------------------------------------- debug view

        string IAiDebugDriver.StateLabel => Fleeing ? (Stopped ? "FLEE HOLD" : "FLEE") : Stopped ? "STOPPED" : "WANDER";

        Color IAiDebugDriver.StateColor => Fleeing
            ? new Color(1f, 0.4f, 0.9f)
            : Stopped
                ? new Color(0.6f, 0.6f, 0.6f)
                : new Color(0.4f, 0.95f, 0.5f);

        IReadOnlyList<Vector3> IAiDebugDriver.Waypoints => waypoints;
        Vector3 IAiDebugDriver.PreviousWaypoint => previousWaypoint;
        Vector3 IAiDebugDriver.SteerAim => steerAim;

        /// <summary>The current leg's direction while driving one; the last recorded one otherwise.</summary>
        int IAiDebugDriver.TravelDirection => waypoints.Count > 0
            ? LaneRules.SegmentDirection(previousWaypoint, waypoints[0])
            : travelDirection;
        bool IAiDebugDriver.OffRoad => offRoad;
        bool IAiDebugDriver.Reversing => reverseTimer > 0f;
        float IAiDebugDriver.StuckTime => stuckTimer;
        ObstacleKind IAiDebugDriver.Obstacle => lastObstacle;
        IReadOnlyList<AiProbe> IAiDebugDriver.Probes => probeLog.Probes;

        /// <summary>Civilians have no perception system — nothing to draw.</summary>
        bool IAiDebugDriver.TryGetSightLine(out Vector3 from, out Vector3 to, out bool clear)
        {
            from = to = Vector3.zero;
            clear = false;
            return false;
        }

        /// <summary>The node a position stands on (level chosen by height), else the nearest one.</summary>
        static bool TryGetNodeOn(RoadGraph graph, Vector3 position, out RoadNode node) =>
            graph.TryGetNodeAt(position, out node) || graph.TryGetNearestNode(position, out node);

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
