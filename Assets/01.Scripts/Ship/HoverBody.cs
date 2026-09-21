using System;
using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Simulation;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The feel numbers a <see cref="HoverBody"/> steps with — a plain struct
    /// the owner refills from its <see cref="ShipDefinition"/> clone before
    /// every tick, so the body never holds the asset (the clone is swapped and
    /// edited live, and the patrol feeds its own). Same meaning, same units as
    /// the track-space body's, which is what keeps one definition valid for both.
    /// </summary>
    public struct HoverParams
    {
        /// <summary>Rate queued speed changes (pads, boosts) blend into the speed, m/s per s.</summary>
        public float impulseBlendRate;
        /// <summary>Top speed the throttle alone reaches, m/s.</summary>
        public float cruiseSpeed;
        /// <summary>Acceleration at full throttle below cruise, m/s per s.</summary>
        public float thrust;
        /// <summary>Deceleration at full brake, m/s per s.</summary>
        public float brakeDecel;
        /// <summary>Deceleration below cruise with the throttle released, m/s per s.</summary>
        public float coastDrag;
        /// <summary>Over-cruise bleed, m/s per s.</summary>
        public float passiveDeceleration;
        /// <summary>Lateral speed full steer settles at, m/s.</summary>
        public float lateralSpeed;
        /// <summary>Lateral drag, 1/s.</summary>
        public float handlingResponse;
        /// <summary>Lateral acceleration the ship holds before it slides, at rest, m/s².</summary>
        public float gripBase;
        /// <summary>Extra grip per m/s of forward speed.</summary>
        public float gripPerSpeed;
        /// <summary>Outward lateral speed past which slipping counts as a slide, m/s.</summary>
        public float slideThreshold;
        /// <summary>Fraction of the forward speed a slide scrubs off per second.</summary>
        public float slideSpeedLoss;
        /// <summary>Scales a flight's length.</summary>
        public float jumpStrength;
        /// <summary>Physical hover height above the surface, metres (the definition's hover height).</summary>
        public float rideHeight;
    }

    /// <summary>
    /// A body simulated in WORLD space over whatever colliders the level is
    /// made of — the standalone counterpart of the runner's track-space body,
    /// shared the same way by the ship and (later) the patrol, and driven by
    /// the same <see cref="BodyControls"/>. Plain C#, no MonoBehaviour: the
    /// owner calls <see cref="Tick"/> once per fixed step, moves its kinematic
    /// rigidbody to the result and renders along <see cref="PoseAt"/>.
    ///
    /// It is a <b>cast-based surface follower</b>, not a force integrator, and
    /// the reason is the speed: at Light Speed (~1800 m/s) a body covers 36 m
    /// per physics step, a 60 m ramp lasts under two ticks and a loop asks for
    /// 10⁴ m/s² of centripetal load — no hover spring survives that. So a tick
    /// is split by DISTANCE (<see cref="ShipSettings.maxStepMeters"/>; the
    /// count simply falls when slow-mo shrinks the tick), and every substep:
    /// runs the speed model and the lateral force-vs-drag model ported
    /// verbatim from the track-space body (so one definition feels the same on
    /// both, and a dash still carries exactly its distance at any substep
    /// count); turns or strafes; <b>sweeps the hull</b> for walls
    /// (collide-and-slide — a surface within the climb angle of the ship's up
    /// is floor, not wall); and <b>re-seats the body on the probed
    /// surface</b>: five rays along −up, the plane through their hits is the
    /// new up, the centre hit + ride height is the new position, and the
    /// heading is carried across by projection. Position control is what makes
    /// loops, tubes and banks fall out of plain geometry with no markup and no
    /// speed cost. The up is never smoothed here — a lagging up would miss a
    /// loop's surface; only the visual eases.
    ///
    /// <b>Letting go</b>: probes that find nothing for the coyote distance, or
    /// a crest sharper than the magnet can hold, become a flight — a constant
    /// gravity picked per flight so that, at the take-off speed, it comes down
    /// the authored air distance on (the track-space rule; real gravity at
    /// these speeds is a 65 km jump), integrated exactly. A flight ends on the
    /// first landable surface the body meets.
    /// </summary>
    public sealed class HoverBody
    {
        const float ThrottleDeadzone = 0.01f;
        const float ShoveEpsilon = 0.05f;
        const float Skin = 0.05f;
        const float SlimHull = 0.6f;
        const float ProbeLift = 1f;
        const float GroundSearch = 5000f;
        const int MaxSlides = 3;

        // One scratch buffer for every body: queries are main-thread and consumed at once.
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        readonly List<Pose> path = new(32);
        float wallHitCooldown;
        float airGap;
        float airGravity;
        float airScale = 1f;
        Vector3 airForward = Vector3.forward;
        float guideHint = float.NaN;

        // -------------------------------------------------------------- state
        public Vector3 Position { get; private set; }
        /// <summary>Heading, unit, always in the plane of <see cref="Up"/> while attached.</summary>
        public Vector3 Forward { get; private set; } = Vector3.forward;
        /// <summary>The PHYSICAL up: the probed surface normal while attached, perpendicular to the velocity in flight. Unsmoothed.</summary>
        public Vector3 Up { get; private set; } = Vector3.up;
        public Vector3 Right => Vector3.Cross(Up, Forward).normalized;
        public Quaternion Rotation => Quaternion.LookRotation(Forward, Up);
        /// <summary>World velocity over the last substep, m/s.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>The scalar speed the speed model owns, m/s: along the surface while attached, along the ground track in flight.</summary>
        public float ForwardSpeed { get; set; }
        /// <summary>Speed the body is backing up at, m/s (0 while it flies forward). Only ever non-zero at a forward standstill.</summary>
        public float ReverseSpeed { get; private set; }
        public float LateralVelocity { get; private set; }
        public float ShoveVelocity { get; private set; }
        public float TotalLateralVelocity { get; private set; }
        public float VerticalVelocity { get; private set; }
        public float PendingSpeedChange { get; private set; }
        /// <summary>Normalised lateral demand (steer + slip + dash), what the visual banks by.</summary>
        public float BankDemand { get; private set; }
        public bool IsSliding { get; private set; }
        /// <summary>True for the tick a wall stopped the lateral motion — the owner ends its dash on it.</summary>
        public bool LateralBlocked { get; private set; }
        public float AirTime { get; private set; }
        public ShipState State { get; private set; } = ShipState.Grounded;
        /// <summary>1 on the ground, the flight's reduced authority in the air.</summary>
        public float ControlFactor => State == ShipState.Airborne && Settings != null ? Settings.airControlFactor : 1f;

        /// <summary>The guide the owner found in reach, or null for free flight.</summary>
        public IShipGuide Guide;
        /// <summary>The ship's own share of the guide's assist, 0..1.</summary>
        public float GuideAssist = 1f;
        /// <summary>True while the last substep was guided; <see cref="Guided"/> is then where the body sits on the guide.</summary>
        public bool HasGuideSample { get; private set; }
        public GuideSample Guided { get; private set; }

        /// <summary>Refilled by the owner before every tick.</summary>
        public HoverParams Params;
        /// <summary>The owner's runtime clone; read live.</summary>
        public ShipSettings Settings;

        // ------------------------------------------------------------ readout
        /// <summary>Substeps the last tick ran.</summary>
        public int Substeps { get; private set; }
        /// <summary>How far the last substep's surface correction moved the body along its up, metres — the follower's tracking error.</summary>
        public float SurfaceError { get; private set; }
        /// <summary>Physics queries the last tick made.</summary>
        public int Queries { get; private set; }
        /// <summary>Why the body last let go of the surface — a debug readout.</summary>
        public string LastTakeOffReason { get; private set; } = "";

        public event Action<ShipState> StateChanged;
        public event Action TookOff;
        public event Action Landed;
        /// <summary>A slam into a wall: the speed it was hit at, m/s.</summary>
        public event Action<float> WallHit;
        /// <summary>A slide began: the lateral acceleration beyond the grip, m/s².</summary>
        public event Action<float> Sliding;

        // -------------------------------------------------------------- setup
        /// <summary>Teleports the body: pose, speed, everything else cleared. Attached states are seated on the surface found under the pose.</summary>
        public void Reset(Vector3 position, Quaternion rotation, float forwardSpeed, ShipState state = ShipState.Grounded)
        {
            Position = position;
            Up = rotation * Vector3.up;
            Forward = rotation * Vector3.forward;
            ForwardSpeed = forwardSpeed;
            LateralVelocity = ShoveVelocity = TotalLateralVelocity = VerticalVelocity = 0f;
            ReverseSpeed = 0f;
            PendingSpeedChange = 0f;
            BankDemand = 0f;
            IsSliding = false;
            LateralBlocked = false;
            AirTime = 0f;
            wallHitCooldown = 0f;
            airGap = 0f;
            guideHint = float.NaN; // a teleport: find the line again from scratch
            HasGuideSample = false;
            Velocity = Forward * forwardSpeed;
            SetState(state);
            if (state == ShipState.Grounded && Settings != null) FollowSurface(0f);
            SnapInterpolation();
        }

        /// <summary>Queues a speed change (already scaled by the owner's weight) to blend in.</summary>
        public void AddSpeedChange(float delta) => PendingSpeedChange += delta;

        /// <summary>A dash: a sideways shove in m/s (right positive) at the current control authority; the lateral drag eats it, so it carries velocity / drag metres.</summary>
        public void AddLateralImpulse(float velocity) => ShoveVelocity += velocity * ControlFactor;

        public void StopLateralMotion()
        {
            LateralVelocity = 0f;
            ShoveVelocity = 0f;
            TotalLateralVelocity = 0f;
        }

        /// <summary>For the states the owner decides (a fall, a respawn wait).</summary>
        public void SetState(ShipState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }

        /// <summary>Lets go of the surface now, whatever the magnet says (a failed loop). <paramref name="gravity"/> ≤ 0 solves the flight as a jump.</summary>
        public void ForceDetach(float gravity = 0f)
        {
            if (State == ShipState.Airborne) return;
            TakeOff("forced");
            if (gravity > 0f) airGravity = gravity;
        }

        // ----------------------------------------------------- interpolation
        /// <summary>After a teleport: nothing to interpolate from.</summary>
        public void SnapInterpolation()
        {
            path.Clear();
            path.Add(new Pose(Position, Rotation));
        }

        /// <summary>
        /// The pose <paramref name="alpha"/> (0..1) of the way through the last
        /// tick, along the substep polyline — exact on a loop, where a straight
        /// lerp between the tick's ends would cut the chord (1.6 m at Light Speed).
        /// </summary>
        public Pose PoseAt(float alpha)
        {
            int last = path.Count - 1;
            if (last <= 0) return new Pose(Position, Rotation);
            float f = Mathf.Clamp01(alpha) * last;
            int i = Mathf.Min((int)f, last - 1);
            float t = f - i;
            return new Pose(Vector3.LerpUnclamped(path[i].position, path[i + 1].position, t),
                            Quaternion.SlerpUnclamped(path[i].rotation, path[i + 1].rotation, t));
        }

        // --------------------------------------------------------------- tick
        public void Tick(float dt, in BodyControls controls)
        {
            path.Clear();
            path.Add(new Pose(Position, Rotation));
            Queries = 0;
            Substeps = 0;
            LateralBlocked = false;
            if (Settings == null || dt <= 0f) return;
            if (State == ShipState.OffTrack || State == ShipState.Respawning) return;

            wallHitCooldown -= dt;
            float reach = (Mathf.Max(ForwardSpeed, Mathf.Abs(VerticalVelocity)) + Mathf.Abs(TotalLateralVelocity)) * dt;
            int n = Mathf.Clamp(Mathf.CeilToInt(reach / Mathf.Max(Settings.maxStepMeters, 0.1f)), 1, Mathf.Max(1, Settings.maxSubsteps));
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Substep(h, controls);
                Substeps++;
                path.Add(new Pose(Position, Rotation));
                if (State == ShipState.OffTrack) break;
            }
        }

        void Substep(float h, in BodyControls controls)
        {
            StepSpeed(h, controls);
            StepReverse(h, controls);
            ProjectOnGuide();
            StepSteering(h, controls);

            Vector3 from = Position;
            Vector3 delta;
            if (State == ShipState.Airborne)
            {
                AirTime += h;
                Vector3 side = Vector3.Cross(Vector3.up, airForward).normalized;
                delta = airForward * (ForwardSpeed * airScale * h)
                      + side * (TotalLateralVelocity * h)
                      + Vector3.up * (VerticalVelocity * h - 0.5f * airGravity * h * h);
                VerticalVelocity -= airGravity * h;
            }
            else
            {
                delta = Forward * ((ForwardSpeed - ReverseSpeed) * h) + Right * (TotalLateralVelocity * h);
            }

            float stepLength = delta.magnitude;
            Sweep(delta);

            if (State == ShipState.Airborne)
            {
                FaceVelocity();
                TryLand(h);
            }
            else
            {
                FollowSurface(stepLength);
            }
            KeepClear();
            Velocity = (Position - from) / h;
        }

        // -------------------------------------------------------------- speed
        /// <summary>The track-space body's speed model, verbatim: queued changes blend in first and are the only way past cruise; above it the bleed pulls back down to it; below it the throttle climbs to it; a released throttle coasts; the brake works everywhere.</summary>
        void StepSpeed(float dt, in BodyControls controls)
        {
            if (!Mathf.Approximately(PendingSpeedChange, 0f))
            {
                float step = Mathf.Sign(PendingSpeedChange) *
                             Mathf.Min(Mathf.Abs(PendingSpeedChange), Params.impulseBlendRate * dt);
                ForwardSpeed += step;
                PendingSpeedChange -= step;
            }

            float cruise = Params.cruiseSpeed;
            float throttle = Mathf.Clamp01(controls.throttle);
            if (ForwardSpeed > cruise)
                ForwardSpeed = Mathf.Max(cruise, ForwardSpeed - Params.passiveDeceleration * dt);
            else if (throttle > ThrottleDeadzone)
                ForwardSpeed = Mathf.Min(cruise, ForwardSpeed + Params.thrust * throttle * dt);
            else
                ForwardSpeed -= Params.coastDrag * dt;

            ForwardSpeed = Mathf.Max(0f, ForwardSpeed - Params.brakeDecel * Mathf.Clamp01(controls.brake) * dt);
        }

        /// <summary>
        /// The way out of a wall: at a forward standstill on the ground, the
        /// brake backs the body up. It is its own small speed so the forward
        /// model stays the runner's, untouched (there the brake only stops);
        /// the throttle, or letting go, ends it at the brake's rate.
        /// </summary>
        void StepReverse(float dt, in BodyControls controls)
        {
            bool backing = Settings.reverseSpeed > 0f && State != ShipState.Airborne && ForwardSpeed <= 0.01f
                           && controls.brake > 0.1f && controls.throttle <= ThrottleDeadzone;
            float target = backing ? Settings.reverseSpeed * Mathf.Clamp01(controls.brake) : 0f;
            float rate = backing ? Settings.reverseAcceleration : Mathf.Max(Params.brakeDecel, Settings.reverseAcceleration);
            ReverseSpeed = Mathf.MoveTowards(ReverseSpeed, target, rate * dt);
        }

        // ----------------------------------------------------------- steering
        /// <summary>
        /// Free steering: with no spline to supply a heading the stick TURNS
        /// the ship — at a rate capped by what its grip can hold at this speed
        /// (grip ÷ speed), so the same definition that slid on the runner's
        /// flat sweeps slides here when full lock asks for more than it has —
        /// and also strafes it by a share of the runner's lateral model, which
        /// is ported whole: a force against an implicitly integrated drag, the
        /// dash a shove the same drag eats.
        /// </summary>
        void StepSteering(float dt, in BodyControls controls)
        {
            float control = ControlFactor;
            float steer = Mathf.Clamp(controls.steer, -1f, 1f);
            float speed = Mathf.Max(ForwardSpeed, 1f);
            float grip = Params.gripBase + Params.gripPerSpeed * ForwardSpeed;
            float assist = HasGuideSample ? Mathf.Clamp01(GuideAssist * Guide.Assist) : 0f;

            // Turn — the share of it the guide has not taken over.
            float maxYaw = Settings.maxYawRate * Mathf.Deg2Rad;
            float yawRate = steer * control * (1f - assist) * Mathf.Min(maxYaw, Settings.turnAuthority * grip / speed);
            if (!Mathf.Approximately(yawRate, 0f))
            {
                Quaternion turn = Quaternion.AngleAxis(yawRate * Mathf.Rad2Deg * dt, State == ShipState.Airborne ? Vector3.up : Up);
                if (State == ShipState.Airborne) airForward = (turn * airForward).normalized;
                else Forward = (turn * Forward).normalized;
            }

            FollowGuideHeading(assist, dt);

            // What the turn asks of the grip; the excess pushes the ship to the outside of it.
            float slip = 0f, excess = 0f;
            bool sliding = false;
            if (State != ShipState.Airborne)
            {
                // A guided road that tests grip asks v²κ of it (the runner's flat sweep); the player's own turning asks v·yaw.
                bool roadTurn = assist > 0f && Guided.gripTested;
                float turnRate = roadTurn ? ForwardSpeed * Guided.curvature * assist + yawRate : yawRate;
                float demand = ForwardSpeed * Mathf.Abs(turnRate);
                excess = Mathf.Max(0f, demand - grip);
                float outward = -Mathf.Sign(turnRate);
                slip = outward * excess;
                // World-gravity mode: a bank pulls the ship downhill.
                if (!Settings.magnetic) slip += Vector3.Dot(Physics.gravity, Right);
                sliding = excess > 0f && outward * LateralVelocity > Params.slideThreshold;
            }
            if (sliding)
            {
                ForwardSpeed *= 1f - Mathf.Clamp01(Params.slideSpeedLoss * dt);
                if (!IsSliding) Sliding?.Invoke(excess);
            }
            IsSliding = sliding;

            // Strafe: force against drag, integrated implicitly so any step is stable.
            float drag = Mathf.Max(Params.handlingResponse, 0.01f);
            float fullSteerSpeed = Mathf.Max(Params.lateralSpeed, 0.01f);
            float steerForce = fullSteerSpeed * drag;
            // Guided, the stick strafes like the runner's; free, it mostly turns.
            float drive = steer * steerForce * control * Mathf.Lerp(Settings.freeStrafeShare, 1f, assist);
            LateralVelocity = (LateralVelocity + (drive + slip) * dt) / (1f + drag * dt);
            ShoveVelocity /= 1f + drag * dt;
            if (Mathf.Abs(ShoveVelocity) < ShoveEpsilon) ShoveVelocity = 0f;
            TotalLateralVelocity = LateralVelocity + ShoveVelocity;
            BankDemand = steer * control + slip / steerForce + ShoveVelocity / fullSteerSpeed;
        }

        // -------------------------------------------------------------- guide
        /// <summary>Where the body sits on the guide this substep, if it has one in reach.</summary>
        void ProjectOnGuide()
        {
            HasGuideSample = false;
            if (Guide as UnityEngine.Object == null || GuideAssist <= 0f || !Settings.useGuide) return;
            if (!Guide.TryProject(Position, ref guideHint, out GuideSample sample)) { guideHint = float.NaN; return; }
            if (new Vector2(sample.lateral, sample.height).magnitude > Guide.CaptureRange) { guideHint = float.NaN; return; }
            Guided = sample;
            HasGuideSample = true;
        }

        /// <summary>
        /// The guide's help: the heading is eased onto the line — locked to it
        /// at full assist, where the road supplies the heading exactly as the
        /// runner's spline did. The tangent is taken half a step AHEAD
        /// (midpoint rule): stepping along the tangent at the start of each
        /// substep would walk off the outside of every curve, a few
        /// millimetres at a time. A ship flying the line backwards is helped
        /// backwards. The guide never moves the body: it still rides whatever
        /// surface is under it.
        /// </summary>
        void FollowGuideHeading(float assist, float dt)
        {
            if (assist <= 0f) return;
            bool airborne = State == ShipState.Airborne;
            Vector3 up = airborne ? Vector3.up : Up;
            Vector3 heading = airborne ? airForward : Forward;

            float direction = Vector3.Dot(heading, Guided.forward) >= 0f ? 1f : -1f;
            Guide.SampleAt(Guided.distance + direction * 0.5f * ForwardSpeed * dt, out GuideSample ahead);
            Vector3 target = Vector3.ProjectOnPlane(ahead.forward * direction, up);
            if (target.sqrMagnitude < 1e-6f) return;
            target.Normalize();

            Vector3 eased = assist >= 0.999f
                ? target
                : Vector3.Slerp(heading, target, 1f - Mathf.Exp(-Settings.guideHeadingResponse * assist * dt)).normalized;
            if (airborne) airForward = eased;
            else Forward = eased;
        }

        // -------------------------------------------------------------- walls
        /// <summary>Moves the body by <paramref name="delta"/>, colliding and sliding along anything that is not floor.</summary>
        void Sweep(Vector3 delta)
        {
            float radius = Mathf.Min(Settings.hullRadius, Mathf.Max(0.1f, Params.rideHeight - 0.25f));
            for (int slide = 0; slide < MaxSlides; slide++)
            {
                float distance = delta.magnitude;
                if (distance < 1e-5f) return;
                Vector3 direction = delta / distance;

                // A cast never reports what it starts inside of, so a hull that has
                // crept into a wall (a graze along a curving one) would sail through
                // it. When that happens the cast is repeated with a slimmer hull,
                // which is still outside the wall and meets it properly.
                int nearest = NearestWall(radius, direction, distance, out bool overlapped);
                if (nearest < 0 && overlapped) nearest = NearestWall(radius * SlimHull, direction, distance, out _);
                if (nearest < 0)
                {
                    Position += delta;
                    return;
                }

                RaycastHit wall = Hits[nearest];
                float travelled = Mathf.Max(0f, wall.distance - Skin);
                Position += direction * travelled;

                // Met in the air, a landable surface is the ground.
                if (State == ShipState.Airborne && IsFloor(wall.normal, Vector3.up, Settings.maxLandAngle))
                {
                    Land(wall.point, wall.normal);
                    return;
                }

                delta -= direction * travelled;
                delta -= wall.normal * Mathf.Min(0f, Vector3.Dot(delta, wall.normal));
                HitWall(wall.normal);
            }
        }

        /// <summary>
        /// A wall's two answers. Sideways into it: the lateral motion stops,
        /// and only a dash carried into it is a slam (gentle steering
        /// saturates silently — the runner's rule). Nose into it: the heading
        /// swings onto the wall and the speed keeps only its share along it,
        /// so a graze costs almost nothing and a square hit stops the ship.
        /// </summary>
        void HitWall(Vector3 normal)
        {
            Vector3 up = State == ShipState.Airborne ? Vector3.up : Up;
            Vector3 flat = Vector3.ProjectOnPlane(normal, up);
            if (flat.sqrMagnitude < 1e-6f) return; // a ceiling: the slide already dealt with it
            flat.Normalize();

            Vector3 heading = State == ShipState.Airborne ? airForward : Forward;
            Vector3 side = Vector3.Cross(up, heading).normalized;
            float hitSpeed = 0f;

            if (TotalLateralVelocity * Vector3.Dot(flat, side) < 0f)
            {
                LateralBlocked = true;
                if (Mathf.Abs(ShoveVelocity) > ShoveEpsilon) hitSpeed = Mathf.Abs(TotalLateralVelocity);
                StopLateralMotion();
            }

            // Backed into it: the reverse just ends.
            if (ReverseSpeed > 0f && Vector3.Dot(heading, flat) > 0.02f) ReverseSpeed = 0f;

            float into = -Vector3.Dot(heading, flat);
            if (ReverseSpeed <= 0f && into > 0.02f)
            {
                Vector3 along = heading + flat * into; // heading with its into-wall part removed
                float keep = along.magnitude;
                hitSpeed = Mathf.Max(hitSpeed, ForwardSpeed * into);
                ForwardSpeed *= keep;
                if (keep > 1e-3f)
                {
                    along /= keep;
                    if (State == ShipState.Airborne) airForward = Vector3.ProjectOnPlane(along, Vector3.up).normalized;
                    else Forward = along;
                }
            }

            if (hitSpeed > 0f && wallHitCooldown <= 0f)
            {
                wallHitCooldown = Settings.wallHitCooldownSeconds;
                WallHit?.Invoke(hitSpeed);
            }
        }

        /// <summary>
        /// The wall constraint that does not depend on a sweep: short whisker
        /// rays from the hull's centre, in the ship's own plane, push it back
        /// out to the hull radius from anything that is not floor. A sphere
        /// sweep is blind to what it starts touching, and a ship grinding
        /// along a curved wall at a grazing angle starts every sweep touching
        /// it — a straight line on a banked curve climbs the bank, so the
        /// outer wall of a sweep is exactly where that happens. A ray starts
        /// from a point well inside the road and has no such blind spot.
        /// </summary>
        void KeepClear()
        {
            Vector3 up = State == ShipState.Airborne ? Vector3.up : Up;
            Vector3 heading = State == ShipState.Airborne ? airForward : Forward;
            Vector3 side = Vector3.Cross(up, heading).normalized;
            float radius = Mathf.Min(Settings.hullRadius, Mathf.Max(0.1f, Params.rideHeight - 0.25f));

            for (int i = 0; i < 5; i++)
            {
                Vector3 travel = ReverseSpeed > 0f ? -heading : heading; // the fan faces the way the body is going
                Vector3 direction = i switch
                {
                    0 => side,
                    1 => -side,
                    2 => (travel + side).normalized,
                    3 => (travel - side).normalized,
                    _ => travel,
                };
                Queries++;
                if (!Physics.Raycast(Position, direction, out RaycastHit hit, radius, Settings.groundLayers, QueryTriggerInteraction.Ignore)) continue;
                if (IsFloor(hit.normal, up, State == ShipState.Airborne ? Settings.maxLandAngle : Settings.climbAngle)) continue;
                Position += hit.normal * (radius - hit.distance) * Mathf.Max(0f, -Vector3.Dot(direction, hit.normal));
                HitWall(hit.normal);
            }
        }

        /// <summary>Index into <see cref="Hits"/> of the first thing in the way that is not floor, or −1. <paramref name="overlapped"/>: the cast started inside a collider.</summary>
        int NearestWall(float radius, Vector3 direction, float distance, out bool overlapped)
        {
            Queries++;
            overlapped = false;
            int count = Physics.SphereCastNonAlloc(Position, radius, direction, Hits, distance + Skin,
                                                   Settings.groundLayers, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.distance <= 0f && hit.point == Vector3.zero) { overlapped = true; continue; }
                if (Vector3.Dot(direction, hit.normal) >= 0f) continue; // moving away
                if (State != ShipState.Airborne && IsFloor(hit.normal, Up, Settings.climbAngle)) continue;
                if (nearest < 0 || hit.distance < Hits[nearest].distance) nearest = i;
            }
            return nearest;
        }

        static bool IsFloor(Vector3 normal, Vector3 up, float maxAngle) => Vector3.Angle(normal, up) <= maxAngle;

        // ------------------------------------------------------------ surface
        /// <summary>
        /// Re-seats the body on the surface under it: the centre probe gives
        /// the point, the plane through the four outer probes the up (their
        /// own hit normals when fewer than four land). No surface for the
        /// coyote distance, or a crest the magnet cannot hold, is a take-off.
        /// </summary>
        void FollowSurface(float stepLength)
        {
            float ride = Params.rideHeight;
            float length = ProbeLift + ride + Settings.attachRange;
            Vector3 up = Up, fwd = Forward, right = Right;
            Vector3 origin = Position + up * ProbeLift;
            Vector2 half = Settings.probeHalfExtents;

            bool centre = Probe(origin, up, length, out RaycastHit mid);
            bool f = Probe(origin + fwd * half.y, up, length, out RaycastHit front);
            bool b = Probe(origin - fwd * half.y, up, length, out RaycastHit back);
            bool l = Probe(origin - right * half.x, up, length, out RaycastHit left);
            bool r = Probe(origin + right * half.x, up, length, out RaycastHit rightHit);

            if (!centre)
            {
                SurfaceError = 0f;
                airGap += stepLength;
                if (airGap > Settings.coyoteMeters) TakeOff("no ground under the probes");
                return;
            }
            airGap = 0f;

            Vector3 normal;
            if (f && b && l && r)
            {
                normal = Vector3.Cross(front.point - back.point, rightHit.point - left.point).normalized;
                if (Vector3.Dot(normal, up) < 0f) normal = -normal;
            }
            else
            {
                normal = mid.normal;
                if (f) normal += front.normal;
                if (b) normal += back.normal;
                if (l) normal += left.normal;
                if (r) normal += rightHit.normal;
                normal.Normalize();
            }

            // World-gravity mode: a surface too steep to stand on only holds under centripetal load (the inside of a loop).
            if (!Settings.magnetic && Vector3.Angle(normal, Vector3.up) > Settings.maxGroundAngle)
            {
                float bend = Vector3.Dot(normal - up, fwd); // < 0: concave, the surface rises to meet the ship
                float load = stepLength > 1e-4f ? ForwardSpeed * ForwardSpeed * Mathf.Max(0f, -bend) / stepLength : 0f;
                if (load < Physics.gravity.magnitude) { TakeOff("too steep to hold without the magnet"); return; }
            }

            // A crest sharper than the magnet holds launches the ship.
            if (Settings.magnetStrength > 0f && stepLength > 1e-4f)
            {
                float bend = Vector3.Dot(normal - up, fwd); // > 0: convex, the surface falls away
                if (bend > 0f && ForwardSpeed * ForwardSpeed * bend / stepLength > Settings.magnetStrength)
                {
                    TakeOff("crest sharper than the magnet holds");
                    return;
                }
            }

            // The re-seat is a move like any other: on a bank that is still
            // rolling it shifts the body sideways, and it must not carry the hull
            // into a wall it is sliding along.
            Vector3 seated = mid.point + normal * ride;
            SurfaceError = Vector3.Dot(seated - Position, up);
            Vector3 shift = seated - Position;
            float shiftLength = shift.magnitude;
            if (shiftLength > 1e-4f)
            {
                float radius = Mathf.Min(Settings.hullRadius, Mathf.Max(0.1f, ride - 0.25f));
                Vector3 direction = shift / shiftLength;
                int wall = NearestWall(radius, direction, shiftLength, out _);
                if (wall < 0)
                {
                    Position += shift;
                }
                else
                {
                    // Up to the wall, then the rest of the way ALONG it: only the
                    // part that pushes into the wall is dropped, never the height
                    // correction (a body that stopped dead here fell behind a bank
                    // as it unwound, until the probes lost the road).
                    Vector3 wallNormal = Hits[wall].normal;
                    float travelled = Mathf.Max(0f, Hits[wall].distance - Skin);
                    Vector3 rest = shift - direction * travelled;
                    rest -= wallNormal * Mathf.Min(0f, Vector3.Dot(rest, wallNormal));
                    Position += direction * travelled + rest;
                }
            }
            Up = normal;
            Vector3 carried = Vector3.ProjectOnPlane(fwd, normal);
            if (carried.sqrMagnitude > 1e-8f) Forward = carried.normalized;
        }

        /// <summary>One hover ray along −up. A hit that is not floor (a wall's top edge) does not count.</summary>
        bool Probe(Vector3 origin, Vector3 up, float length, out RaycastHit hit)
        {
            Queries++;
            return Physics.Raycast(origin, -up, out hit, length, Settings.groundLayers, QueryTriggerInteraction.Ignore)
                   && IsFloor(hit.normal, up, Settings.climbAngle);
        }

        // ------------------------------------------------------------- flight
        /// <summary>
        /// Leaves the surface with the velocity it was riding it at. The
        /// gravity is then the one number that lands this flight, at this
        /// speed, the authored air distance on (0 = h0 + vy·T − ½·g·T² with
        /// T = distance / ground speed, h0 the height over the ground below).
        /// </summary>
        void TakeOff(string reason)
        {
            LastTakeOffReason = reason;
            Vector3 heading = Forward;
            float climb = Mathf.Clamp(heading.y, -1f, 1f);
            Vector3 flat = Vector3.ProjectOnPlane(heading, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(Up, Vector3.up); // going straight up a wall: over its top
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            airForward = flat.normalized;
            airScale = Mathf.Sqrt(Mathf.Max(0f, 1f - climb * climb));
            VerticalVelocity = ForwardSpeed * climb;

            float groundSpeed = Mathf.Max(ForwardSpeed * airScale, 1f);
            float airLength = Mathf.Clamp(ForwardSpeed * Settings.airDistancePerSpeed, Settings.airDistanceRange.x, Settings.airDistanceRange.y)
                              * Mathf.Max(Params.jumpStrength, 0.01f);
            float airSeconds = Mathf.Max(airLength / groundSpeed, 0.05f);

            Queries++;
            airGravity = Settings.minAirGravity;
            if (Physics.Raycast(Position, Vector3.down, out RaycastHit below, GroundSearch, Settings.groundLayers, QueryTriggerInteraction.Ignore))
            {
                float height = Mathf.Max(0f, below.distance - Params.rideHeight);
                float solved = 2f * (height + VerticalVelocity * airSeconds) / (airSeconds * airSeconds);
                airGravity = Mathf.Max(solved, Settings.minAirGravity);
            }

            AirTime = 0f;
            airGap = 0f;
            SurfaceError = 0f;
            SetState(ShipState.Airborne);
            FaceVelocity();
            TookOff?.Invoke();
        }

        /// <summary>In flight the body points down its velocity, so the pose is continuous with the slope it left and the one it lands on.</summary>
        void FaceVelocity()
        {
            Vector3 velocity = airForward * (ForwardSpeed * airScale) + Vector3.up * VerticalVelocity;
            if (velocity.sqrMagnitude < 1f) return;
            Vector3 heading = velocity.normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, heading);
            if (up.sqrMagnitude < 1e-4f) return; // straight up or down: keep the last frame
            Forward = heading;
            Up = up.normalized;
        }

        /// <summary>Coming down within the ride height of a landable surface is a landing — found by a probe, so the body settles AT its hover height instead of bouncing the hull off the ground.</summary>
        void TryLand(float h)
        {
            if (State != ShipState.Airborne) return;
            float reach = ProbeLift + Params.rideHeight + Mathf.Max(0f, -VerticalVelocity) * h;
            Queries++;
            if (!Physics.Raycast(Position + Vector3.up * ProbeLift, Vector3.down, out RaycastHit hit, reach,
                                 Settings.groundLayers, QueryTriggerInteraction.Ignore)) return;
            if (!IsFloor(hit.normal, Vector3.up, Settings.maxLandAngle)) return;
            Vector3 velocity = airForward * (ForwardSpeed * airScale) + Vector3.up * VerticalVelocity;
            if (Vector3.Dot(velocity, hit.normal) >= 0f) return; // still rising away from it
            Land(hit.point, hit.normal);
        }

        void Land(Vector3 point, Vector3 normal)
        {
            Position = point + normal * Params.rideHeight;
            Up = normal;
            Vector3 carried = Vector3.ProjectOnPlane(airForward, normal);
            Forward = carried.sqrMagnitude > 1e-8f ? carried.normalized : Vector3.ProjectOnPlane(Forward, normal).normalized;
            VerticalVelocity = 0f;
            airGap = 0f;
            SetState(ShipState.Grounded);
            Landed?.Invoke();
        }
    }
}
