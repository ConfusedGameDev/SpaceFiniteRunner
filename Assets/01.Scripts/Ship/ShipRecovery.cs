using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Falling off the world, for a ship with no track to be "off" of. It
    /// decides the ship is lost — it has flown with <b>no ground anywhere
    /// below</b> for a moment, it has been airborne longer than any jump
    /// lasts, it dropped under the level's kill height, or its path crossed a
    /// <see cref="KillVolume"/> (found by a sweep along the tick's path, so a
    /// 5 m curtain is caught at Light Speed) — then plays the runner's fall
    /// and respawn, ported one-to-one: the body goes
    /// <see cref="ShipState.OffTrack"/> and this component flies it as plain
    /// ballistics with a cosmetic tumble; after the fall time it is seated
    /// back on the road, stands still for the wait while
    /// <see cref="RespawnBlink"/> flickers it, and relaunches with the speed
    /// it fell with minus the penalty. Falling costs time, never the run.
    /// With <see cref="ShipSettings.respawnRollingStart"/> the relaunch comes
    /// first: the ship is seated already flying at that speed, under control,
    /// and the wait is spent <see cref="RespawnShielded"/> — blinking and
    /// untouchable — with <c>Respawned</c> raised only as the window closes,
    /// so whatever a game holds for the wait (the runner's patrol) holds for it.
    ///
    /// <b>Where it comes back</b>: with a guide in reach, where the guide says
    /// (<see cref="IShipGuide.FindRespawn"/> — at or past the fall point, so
    /// time is lost, never distance). With none, on the newest of its own
    /// breadcrumbs — poses dropped every few metres while it was safely
    /// grounded — that lies far enough back to be clear of whatever it fell
    /// off. A breadcrumb only says where the ship WAS, and a ship on its way
    /// off an edge was already pointing at it; so the spot is then read off
    /// the colliders (<see cref="AimAtRoad"/>): the ship is put in the middle
    /// of the road and faced down the direction with the most road ahead.
    ///
    /// Both phases are timers in the fixed tick, not coroutines, so the ship's
    /// <c>Paused</c> and a menu's timeScale freeze them.
    /// </summary>
    [DefaultExecutionOrder(10)] // after the ship's own tick, so the fall's pose is the last word of the step
    [RequireComponent(typeof(HoverShip))]
    public sealed class ShipRecovery : MonoBehaviour
    {
        const float GroundSearch = 5000f;
        const int MaxCrumbs = 32;

        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static readonly Collider[] Overlaps = new Collider[8];
        static readonly float[] FanReach = new float[91];

        struct Crumb
        {
            public Vector3 position;
            public Quaternion rotation;
            public float odometer;
        }

        readonly List<Crumb> crumbs = new(MaxCrumbs);
        HoverShip ship;
        float odometer, lastCrumbAt;
        float groundlessTimer, phaseTimer, speedAtFall;
        float shieldLeft; // a rolling start's blinking window, seconds
        Vector3 lastPosition, fallVelocity, fallPosition;
        Vector3 fallOrigin; // where it left from: what the respawn reasons about, not where the tumble ended
        Quaternion fallRotation;
        Vector3 tumbleAxis;
        bool hasLastPosition;
        IShipGuide lastGuide;        // the guide the ship was last ON, and how far along it —
        float lastGuideDistance;     // where it left the road, which is not where it was declared lost

        /// <summary>A rolling start is flying out its respawn wait: blinking, and untouchable for a game that asks.</summary>
        public bool RespawnShielded => shieldLeft > 0f;

        /// <summary>Why the ship last fell — a debug readout.</summary>
        public string LastFallReason { get; private set; } = "";

        void Awake() => ship = GetComponent<HoverShip>();

        void OnEnable() => ship.Launched += OnLaunched;
        void OnDisable() => ship.Launched -= OnLaunched;

        // A launch is a teleport: the trail behind it belongs to somewhere else.
        void OnLaunched()
        {
            crumbs.Clear();
            odometer = lastCrumbAt = 0f;
            groundlessTimer = 0f;
            hasLastPosition = false;
            lastGuide = null;
            shieldLeft = 0f;
        }

        void FixedUpdate()
        {
            ShipSettings settings = ship.Settings;
            if (ship.Paused || settings == null || !settings.recoveryEnabled) return;
            float dt = Time.fixedDeltaTime;
            HoverBody body = ship.Body;
            StepShield(dt);

            switch (body.State)
            {
                case ShipState.OffTrack: StepFall(settings, dt); return;
                case ShipState.Respawning: StepRespawnWait(settings, dt); return;
                case ShipState.Falling: hasLastPosition = false; return; // a game's own scripted fall (the runner's failed loop): not ours to judge
            }

            // On the ground the ship either rides a guide or it does not; in the air the last answer stands — but the
            // DISTANCE keeps counting while the line is still in reach: a game streams its world off that number (the
            // runner culls road and colliders 300 m behind it), so coming back short of it is coming back onto nothing.
            if (body.State == ShipState.Grounded) lastGuide = body.HasGuideSample ? body.Guide : null;
            if (body.HasGuideSample && ReferenceEquals(body.Guide, lastGuide)) lastGuideDistance = body.Guided.distance;

            Vector3 position = body.Position;
            if (hasLastPosition)
            {
                odometer += Vector3.Distance(lastPosition, position);
                if (CrossedKillVolume(lastPosition, position, out KillVolume volume))
                {
                    BeginFall("crossed " + volume.name, volume.SkipFall);
                    return;
                }
            }
            lastPosition = position;
            hasLastPosition = true;

            if (settings.useKillHeight && position.y < settings.killHeight) { BeginFall("under the kill height"); return; }

            if (body.State == ShipState.Airborne)
            {
                if (body.AirTime > settings.maxAirSeconds) { BeginFall("airborne too long"); return; }
                bool groundBelow = Physics.Raycast(position, Vector3.down, GroundSearch, settings.groundLayers, QueryTriggerInteraction.Ignore);
                groundlessTimer = groundBelow ? 0f : groundlessTimer + dt;
                if (groundlessTimer > settings.groundlessSeconds) { BeginFall("no ground below"); return; }
            }
            else
            {
                groundlessTimer = 0f;
                DropCrumb(settings, body);
            }
        }

        // A breadcrumb only where the ship could be put back: grounded, upright enough, moving, not sliding.
        void DropCrumb(ShipSettings settings, HoverBody body)
        {
            if (body.State != ShipState.Grounded || body.IsSliding) return;
            if (odometer - lastCrumbAt < settings.crumbSpacingMeters && crumbs.Count > 0) return;
            if (Vector3.Angle(body.Up, Vector3.up) > settings.maxCrumbTilt) return;
            if (crumbs.Count == MaxCrumbs) crumbs.RemoveAt(0);
            crumbs.Add(new Crumb { position = body.Position, rotation = body.Rotation, odometer = odometer });
            lastCrumbAt = odometer;
        }

        bool CrossedKillVolume(Vector3 from, Vector3 to, out KillVolume volume)
        {
            volume = null;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f) return false;
            int count = Physics.SphereCastNonAlloc(from, ship.Settings.hullRadius, delta / distance, Hits, distance,
                                                   ShipLayers.VolumeMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.TryGetComponent(out volume)) return true;
            // A sweep does not report what it starts inside of (a respawn inside a volume, a volume switched on around the ship).
            count = Physics.OverlapSphereNonAlloc(to, ship.Settings.hullRadius, Overlaps, ShipLayers.VolumeMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (Overlaps[i].TryGetComponent(out volume)) return true;
            return false;
        }

        // ---------------------------------------------------------- road reading
        /// <summary>
        /// Reads the road around a respawn spot off the colliders, with no
        /// guide to ask: <b>centre</b> — the ground's edges are searched for to
        /// the left and right (an edge is where the ground ends or a wall
        /// stands) and the spot moves to the middle of them; <b>heading</b> — a
        /// fan of directions around the way the ship had been travelling is
        /// walked along the ground, and the ship faces the one with the most
        /// road ahead (the travel direction wins a tie). Centred first with the
        /// travel direction, then aimed, then centred again across the final
        /// heading. A few thousand raycasts, once per respawn (~1 ms).
        /// </summary>
        void AimAtRoad(ref Vector3 position, ref Quaternion rotation, Vector3 travel, float ride)
        {
            ShipSettings settings = ship.Settings;
            Vector3 up = rotation * Vector3.up;
            Vector3 heading = Vector3.ProjectOnPlane(travel, up);
            if (heading.sqrMagnitude < 1e-4f) heading = rotation * Vector3.forward;
            heading.Normalize();

            position = Centre(position, heading, up, ride, settings);

            // A fan of headings, −90°..+90° around the travel direction. On a road many of
            // them reach the full look-ahead — a whole wedge is clear — so the answer is the
            // MIDDLE of the clear wedge nearest the travel direction, not its first ray:
            // that is what points the ship down the road instead of at its far edge.
            const int Rays = 91;
            const float Spread = 2f;
            float longest = 0f;
            for (int i = 0; i < Rays; i++)
            {
                FanReach[i] = RoadAhead(position, Quaternion.AngleAxis((i - Rays / 2) * Spread, up) * heading, up, ride, settings);
                longest = Mathf.Max(longest, FanReach[i]);
            }

            float bestAngle = 0f, bestOff = float.MaxValue;
            for (int i = 0; i < Rays; i++)
            {
                if (FanReach[i] < longest - 1f) continue;
                int last = i;
                while (last + 1 < Rays && FanReach[last + 1] >= longest - 1f) last++;
                float middle = ((i + last) * 0.5f - Rays / 2) * Spread;
                if (Mathf.Abs(middle) < bestOff) { bestOff = Mathf.Abs(middle); bestAngle = middle; }
                i = last;
            }
            Vector3 best = Quaternion.AngleAxis(bestAngle, up) * heading;

            position = Centre(position, best, up, ride, settings);
            rotation = Quaternion.LookRotation(best, up);
        }

        /// <summary>Metres of unbroken, unwalled ground along a direction.</summary>
        float RoadAhead(Vector3 from, Vector3 direction, Vector3 up, float ride, ShipSettings settings)
        {
            const float Stride = 10f;
            float reach = 0f;
            Vector3 at = from;
            while (reach < settings.respawnLookAheadMeters)
            {
                Vector3 next = at + direction * Stride;
                if (Physics.Linecast(at, next, settings.groundLayers, QueryTriggerInteraction.Ignore)) break; // a wall
                if (!GroundUnder(next, up, ride, settings, out Vector3 seated)) break;                       // the edge
                at = seated;
                reach += Stride;
            }
            return reach;
        }

        /// <summary>The spot moved to the middle of the road across a heading. A side with no edge in range is open ground and pulls nothing.</summary>
        Vector3 Centre(Vector3 position, Vector3 heading, Vector3 up, float ride, ShipSettings settings)
        {
            Vector3 side = Vector3.Cross(up, heading).normalized;
            bool rightEdge = EdgeDistance(position, side, up, ride, settings, out float right);
            bool leftEdge = EdgeDistance(position, -side, up, ride, settings, out float left);
            if (!rightEdge || !leftEdge) return position; // open to a side: nothing to be the middle of
            Vector3 centred = position + side * ((right - left) * 0.5f);
            return GroundUnder(centred, up, ride, settings, out Vector3 seated) ? seated : position;
        }

        bool EdgeDistance(Vector3 from, Vector3 direction, Vector3 up, float ride, ShipSettings settings, out float distance)
        {
            const float Stride = 2f;
            distance = 0f;
            Vector3 at = from;
            while (distance < settings.respawnCentreSearchMeters)
            {
                Vector3 next = at + direction * Stride;
                if (Physics.Linecast(at, next, out RaycastHit wall, settings.groundLayers, QueryTriggerInteraction.Ignore))
                {
                    distance += wall.distance;
                    return true;
                }
                if (!GroundUnder(next, up, ride, settings, out Vector3 seated)) return true;
                at = seated;
                distance += Stride;
            }
            return false;
        }

        /// <summary>Is there floor under a point at ride height? <paramref name="seated"/> is the point re-seated on it, so a walk follows slopes.</summary>
        bool GroundUnder(Vector3 point, Vector3 up, float ride, ShipSettings settings, out Vector3 seated)
        {
            seated = point;
            float lift = 2f;
            if (!Physics.Raycast(point + up * lift, -up, out RaycastHit hit, lift + ride + settings.attachRange, settings.groundLayers, QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Angle(hit.normal, up) > settings.climbAngle) return false;
            seated = hit.point + up * ride;
            return true;
        }

        // --------------------------------------------------------------- fall
        /// <summary>Takes the ship out of play now — what a game calls for its own reasons (a hazard, a rule).</summary>
        public void BeginFall(string reason, bool skipFall = false)
        {
            HoverBody body = ship.Body;
            if (body.State == ShipState.OffTrack || body.State == ShipState.Respawning) return;
            LastFallReason = reason;
            shieldLeft = 0f; // fell again inside the window: its Respawned waits for the next one
            speedAtFall = body.ForwardSpeed;
            fallVelocity = body.Velocity;
            fallPosition = fallOrigin = body.Position;
            fallRotation = body.Rotation;
            tumbleAxis = (body.Right + body.Forward * 0.35f).normalized;
            phaseTimer = 0f;
            groundlessTimer = 0f;
            hasLastPosition = false;
            body.StopLateralMotion();
            body.SetState(ShipState.OffTrack);
            ship.RaiseFellOff();
            if (skipFall) BeginRespawnWait();
        }

        // Plain ballistics from the velocity it left with, plus a cosmetic tumble.
        void StepFall(ShipSettings settings, float dt)
        {
            phaseTimer += dt;
            fallVelocity += Vector3.down * (settings.fallGravity * dt);
            fallPosition += fallVelocity * dt;
            fallRotation = Quaternion.AngleAxis(settings.fallTumbleDegreesPerSecond * dt, tumbleAxis) * fallRotation;
            ship.Body.MoveOutOfPlay(fallPosition, fallRotation);
            if (phaseTimer >= settings.fallDurationSeconds) BeginRespawnWait();
        }

        void BeginRespawnWait()
        {
            HoverBody body = ship.Body;
            FindRespawnPose(out Vector3 position, out Quaternion rotation);
            Vector3 from = body.Position;
            phaseTimer = 0f;
            ShipSettings settings = ship.Settings;
            if (settings.respawnRollingStart)
            {
                // Relaunched on the spot: the wait is flown, not stood, and only the blink says it is still going.
                body.Reset(position, rotation, RelaunchSpeed(settings), ShipState.Grounded);
                hasLastPosition = false;
                shieldLeft = settings.respawnWaitSeconds;
                ship.RaiseRespawnStarted(body.Position - from);
                if (shieldLeft <= 0f) ship.RaiseRespawned();
                return;
            }
            body.Reset(position, rotation, 0f, ShipState.Respawning);
            ship.RaiseRespawnStarted(body.Position - from);
        }

        void StepRespawnWait(ShipSettings settings, float dt)
        {
            phaseTimer += dt;
            if (phaseTimer < settings.respawnWaitSeconds) return;
            HoverBody body = ship.Body;
            body.ForwardSpeed = RelaunchSpeed(settings);
            body.SetState(ShipState.Grounded);
            hasLastPosition = false;
            ship.RaiseRespawned();
        }

        // A rolling start's window runs in the fixed tick like the wait it replaces, so a pause freezes it too.
        void StepShield(float dt)
        {
            if (shieldLeft <= 0f) return;
            shieldLeft -= dt;
            if (shieldLeft <= 0f) ship.RaiseRespawned();
        }

        float RelaunchSpeed(ShipSettings settings) => speedAtFall * (1f - Mathf.Clamp01(settings.respawnSpeedPenalty));

        void FindRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            ShipSettings settings = ship.Settings;
            float ride = ship.Definition != null ? ship.Definition.hoverHeight : 0f;

            // Guided: the guide knows the road — at or past the point the ship LEFT it, never back. That point is
            // remembered, not re-projected: a ship is declared lost after flying on for most of a second, by then
            // hundreds of metres from the line, and an unhinted projection from out there lands anywhere — on the
            // runner it came back past the last built collider, with no road under the respawn, and fell again, and again.
            IShipGuide guide = lastGuide as Object != null ? lastGuide : ship.Body.Guide;
            if (guide as Object != null)
            {
                float from = lastGuideDistance;
                bool known = lastGuide as Object != null;
                if (!known)
                {
                    float hint = float.NaN;
                    known = guide.TryProject(fallOrigin, ref hint, out GuideSample at);
                    from = at.distance;
                }
                if (known)
                {
                    guide.SampleAt(guide.FindRespawn(from, settings.respawnClearance), out GuideSample spot);
                    position = spot.position + spot.up * ride;
                    rotation = Quaternion.LookRotation(spot.forward, spot.up);
                    return;
                }
            }

            // Free: the newest breadcrumb far enough back to be clear of what it fell off.
            for (int i = crumbs.Count - 1; i >= 0; i--)
            {
                if (odometer - crumbs[i].odometer < settings.respawnBackMeters && i > 0) continue;
                position = crumbs[i].position;
                rotation = crumbs[i].rotation;
                // The way the ship was TRAVELLING over the stretch before the crumb says more about the road than the way it happened to face.
                Vector3 travel = i > 0 ? crumbs[i].position - crumbs[Mathf.Max(0, i - 4)].position : rotation * Vector3.forward;
                crumbs.RemoveRange(i + 1, crumbs.Count - i - 1); // the trail past it led to the fall
                lastCrumbAt = crumbs[i].odometer;
                odometer = crumbs[i].odometer;
                AimAtRoad(ref position, ref rotation, travel, ride);
                return;
            }

            // Nothing known: where it fell from, upright.
            position = fallOrigin;
            Vector3 flat = Vector3.ProjectOnPlane(fallRotation * Vector3.forward, Vector3.up);
            rotation = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward, Vector3.up);
        }
    }
}
