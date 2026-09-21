using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Simulation
{
    /// <summary>
    /// The feel numbers a <see cref="TrackBody"/> steps with. A plain struct
    /// the driver refills from its definition before every tick, so the body
    /// never holds an asset: the ship's definition is a runtime clone that is
    /// swapped and edited live, and the patrol will feed its own.
    /// </summary>
    public struct BodyParams
    {
        /// <summary>Rate queued speed changes (pads, takeoff boosts) blend into the speed, m/s per s.</summary>
        public float impulseBlendRate;
        /// <summary>Top speed the throttle alone reaches, m/s. Only queued speed changes (orbs, pads, takeoffs) go past it.</summary>
        public float cruiseSpeed;
        /// <summary>Acceleration at full throttle below cruise, m/s per s.</summary>
        public float thrust;
        /// <summary>Deceleration at full brake, m/s per s.</summary>
        public float brakeDecel;
        /// <summary>Deceleration below cruise with the throttle released, m/s per s.</summary>
        public float coastDrag;
        /// <summary>Over-cruise bleed: deceleration above cruise, down to it, m/s per s.</summary>
        public float passiveDeceleration;
        /// <summary>Lateral speed full steer settles at, m/s — the steering force over the lateral drag.</summary>
        public float lateralSpeed;
        /// <summary>Lateral drag, 1/s: how fast the lateral velocity settles (and a slide dies). The steering force is lateralSpeed × this.</summary>
        public float handlingResponse;
        /// <summary>Lateral acceleration a flat sweep may demand before the body slides, at rest, m/s².</summary>
        public float gripBase;
        /// <summary>Extra grip per m/s of forward speed, m/s² per m/s. The demand grows with v², the grip only with v: faster is always more dangerous.</summary>
        public float gripPerSpeed;
        /// <summary>Outward lateral speed past which slipping counts as a slide, m/s.</summary>
        public float slideThreshold;
        /// <summary>Fraction of the forward speed a slide scrubs off per second.</summary>
        public float slideSpeedLoss;
        /// <summary>Scales a ramp takeoff: the arc's length and height, and the boost at the lip.</summary>
        public float jumpStrength;
        /// <summary>Seconds before another wall hit can cost speed / fire feedback.</summary>
        public float wallHitCooldownSeconds;
        /// <summary>Half width and half height of the body's own pickup volume, metres — added to every pickup's.</summary>
        public Vector2 pickupReach;
        /// <summary>How far past an OPEN edge the body may hang before the fall clock starts, metres.</summary>
        public float edgeOverhang;
        /// <summary>Seconds the body must stay past the overhang to fall: counter-steer inside them pulls it back.</summary>
        public float edgeGraceSeconds;
    }

    /// <summary>
    /// A body simulated in TRACK SPACE: its state is distance along the
    /// track, lateral offset across it and height above the flight line, plus
    /// their velocities — never a world position. The world pose is always
    /// looked up from that state through the <see cref="TrackManager"/>, so
    /// the body cannot tunnel through or drift off the track however far it
    /// travels in a step (~36 m at Light Speed), it is deterministic, and the
    /// ship and the patrol can run the very same rules. Plain C#, no
    /// MonoBehaviour: the owner ticks it (<see cref="BeginTick"/> once per
    /// fixed step, then <see cref="Step"/> per substep) and renders the pose
    /// interpolated between the last two ticks (<see cref="DistanceAt"/> and
    /// friends), which is exact because it interpolates the track-space state
    /// and not the pose.
    ///
    /// It owns the rules every body shares: the speed model, lateral movement
    /// inside the lane (the edge, a ramp's rails and side wall, a tube's
    /// wrap-around and assisted return) and the jump state machine (commit to
    /// a ramp, ride the slope, fly the arc, land). What only the ship has —
    /// the dash meter, the barrel roll, the loop verdict and its fall — stays
    /// in <see cref="ShipMotor"/>, which forces <see cref="State"/> for those.
    /// <b>Speed</b> is a throttle model (<see cref="StepSpeed"/>): thrust up
    /// to a cruise cap, a brake, coast drag, and a bleed that only acts above
    /// cruise — so boosts are the one way past it. <b>Steering is a lateral
    /// force</b> against a lateral drag, and <b>flat sweeps test grip</b>
    /// (<see cref="SlipAcceleration"/>): where the track says the road is a
    /// flat sweep, the centripetal demand v²κ beyond the body's grip pushes
    /// it outward — braking is the answer. Banked sweeps, straights and every
    /// section always hold. <b>An open edge has no wall</b>
    /// (<see cref="TrackManager.IsEdgeOpen"/>): the body may run past it, and
    /// hanging beyond the overhang for the grace time takes it
    /// <see cref="ShipState.OffTrack"/> (<see cref="LeftTrack"/>) — from there
    /// the owner flies it in world space and the body stands still until it
    /// is <see cref="Reset"/> back onto the track. <b>A jump is a real
    /// flight</b>: the lip hands the body a vertical velocity (ramp slope ×
    /// speed) and a gravity picked per jump so that, at the takeoff speed, it
    /// comes down exactly <see cref="JumpDefinition.AirDistanceFor"/> metres
    /// on — the authored feel — while speed changes in the air (a boost, the
    /// brake) now really lengthen or shorten it. <b>A dash is a shove</b>
    /// (<see cref="AddLateralImpulse"/>): a lateral velocity the same drag
    /// eats, so it carries impulse / drag metres and steering can fight it.
    /// </summary>
    public sealed class TrackBody
    {
        readonly TrackManager track;

        // ------------------------------------------------------------- state
        /// <summary>Metres from the track start — the authoritative coordinate.</summary>
        public float Distance { get; set; }
        /// <summary>Metres across the track from the centre line, right positive (an arc length on a tube).</summary>
        public float Lateral { get; set; }
        /// <summary>Lift of the body above the flight line, along the track's up.</summary>
        public float Height { get; private set; }
        /// <summary>Speed along the track, m/s.</summary>
        public float ForwardSpeed { get; set; }
        /// <summary>Steering's (and a slide's) lateral velocity, m/s. A dash's shove is kept apart (<see cref="ShoveVelocity"/>) only so a wall can tell a slam from steering; both ride the same drag.</summary>
        public float LateralVelocity { get; set; }
        /// <summary>What is left of a dash's lateral shove, m/s.</summary>
        public float ShoveVelocity { get; private set; }
        /// <summary>Rate of change of <see cref="Height"/>, m/s: the ramp's slope × speed on a run-up, integrated under the jump's gravity in the air.</summary>
        public float VerticalVelocity { get; private set; }

        /// <summary>The feel numbers; the driver refreshes them before each tick.</summary>
        public BodyParams Params;

        public ShipState State { get; private set; }

        /// <summary>The ramp the body is committed to (riding its run-up), or null.</summary>
        public JumpRamp Ramp { get; private set; }

        /// <summary>Seconds since takeoff while airborne; the last flight's length otherwise.</summary>
        public float AirTime { get; private set; }

        /// <summary>Nose pitch the slope or the arc asks for, degrees (negative = nose up). Visual only.</summary>
        public float PitchDegrees { get; private set; }

        /// <summary>Speed change still queued to blend in.</summary>
        public float PendingSpeedChange { get; private set; }

        /// <summary>Steering and dash authority right now: the jump's reduced factor in the air, 1 elsewhere.</summary>
        public float ControlFactor => State == ShipState.Airborne && airDefinition != null ? airDefinition.airControlFactor : 1f;

        /// <summary>
        /// What the visual bank leans on, normalised so full steer is ±1: the
        /// lateral ACCELERATION the last step applied (steering force plus any
        /// slip, over the full steering force) plus the dash burst (over the
        /// full-steer lateral speed). Zero once something stopped the move.
        /// </summary>
        public float BankDemand { get; private set; }

        /// <summary>Steering plus the dash burst: the lateral velocity the last step moved with, m/s.</summary>
        public float TotalLateralVelocity { get; private set; }

        /// <summary>
        /// The owner's guarantee that nothing can go wrong from here (the win
        /// has latched): every edge is a wall again, grip is never tested. A
        /// body already hanging past an open edge is simply pulled back in.
        /// </summary>
        public bool HoldOnTrack { get; set; }

        /// <summary>True while the body is slipping outward on a flat sweep faster than the slide threshold: it is scrubbing speed.</summary>
        public bool IsSliding { get; private set; }

        /// <summary>True when the last step's lateral move was stopped or taken over (the edge, a ramp's side, a tube's return): a dash burst ends there.</summary>
        public bool LateralBlocked { get; private set; }

        // ------------------------------------------------------------ events
        /// <summary>Raised on every <see cref="State"/> change, after the new state is set.</summary>
        public event System.Action<ShipState> StateChanged;
        /// <summary>Raised at a ramp's lip, already <see cref="ShipState.Airborne"/>. Argument: the raw takeoff boost (× jump strength) for the owner to apply its own way.</summary>
        public event System.Action<float> TookOff;
        /// <summary>Raised when the arc returns to the flight line.</summary>
        public event System.Action Landed;
        /// <summary>Raised when a dash slams the lane's edge, or the body hits a ramp from the side. Argument: lateral impact speed in m/s.</summary>
        public event System.Action<float> WallHit;
        /// <summary>Raised on the step the body leaves the track over an open edge, already <see cref="ShipState.OffTrack"/>. Argument: the side it left over (−1 left, +1 right).</summary>
        public event System.Action<int> LeftTrack;
        /// <summary>Raised for every pickup the body touched over the distance a step covered (<see cref="PickupRegistry"/>). The owner decides what taking it means.</summary>
        public event System.Action<ITrackPickup> PickedUp;
        /// <summary>Raised on the step a slide begins (see <see cref="IsSliding"/>). Argument: the lateral acceleration beyond the grip, m/s².</summary>
        public event System.Action<float> Sliding;

        // Jump state.
        JumpRamp blockingRamp;  // beside a ramp: its edge is a wall this step
        int blockSide;          // which side of the blocking ramp the body is on (+1 right)
        JumpDefinition airDefinition; // the jump in flight (control authority)
        float airGravity;       // this flight's gravity, m/s² — see TakeOff

        // A shove below this is over (m/s): what tells a dash slam from steering at a wall.
        const float ShoveEpsilon = 1f;

        // Tube return: before a tube unrolls, the lateral eases back to the
        // band's centre from wherever the driver left it.
        bool returnLocked;
        float returnFromLateral;

        readonly List<ITrackPickup> touched = new(); // scratch for the pickup sweep
        float wallHitCooldown;
        float edgeTimer; // seconds spent hanging past an open edge's overhang

        // Interpolation: the state at the start of the current tick.
        float prevDistance, prevLateral, prevHeight;

        public TrackBody(TrackManager track) => this.track = track;

        /// <summary>Puts the body on the flight line at a distance, at rest across the track, silently (no events but the state change).</summary>
        public void Reset(float distance, float forwardSpeed, ShipState state = ShipState.Grounded)
        {
            Distance = distance;
            Lateral = 0f;
            Height = 0f;
            ForwardSpeed = forwardSpeed;
            LateralVelocity = 0f;
            ShoveVelocity = 0f;
            VerticalVelocity = 0f;
            PendingSpeedChange = 0f;
            BankDemand = 0f;
            IsSliding = false;
            LateralBlocked = false;
            wallHitCooldown = 0f;
            Ramp = null;
            blockingRamp = null;
            airDefinition = null;
            returnLocked = false;
            AirTime = 0f;
            PitchDegrees = 0f;
            TotalLateralVelocity = 0f;
            edgeTimer = 0f;
            SetState(state);
            SnapInterpolation();
        }

        /// <summary>Queues a speed change (already scaled by the owner's weight) to blend in at <see cref="BodyParams.impulseBlendRate"/>.</summary>
        public void AddSpeedChange(float delta) => PendingSpeedChange += delta;

        /// <summary>
        /// A dash: a sideways shove of <paramref name="velocity"/> m/s (signed,
        /// right positive), at the body's current control authority. The
        /// lateral drag eats it, so it carries velocity / drag metres.
        /// </summary>
        public void AddLateralImpulse(float velocity) => ShoveVelocity += velocity * ControlFactor;

        /// <summary>Kills all lateral motion (steering, slide and shove) — for the owner's own set pieces.</summary>
        public void StopLateralMotion()
        {
            LateralVelocity = 0f;
            ShoveVelocity = 0f;
        }

        /// <summary>For the states the owner decides (a loop, its fall) — and back to Grounded after them.</summary>
        public void SetState(ShipState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }

        // ----------------------------------------------------- interpolation
        /// <summary>Call once at the start of every tick, before its substeps: remembers the state the render interpolates FROM.</summary>
        public void BeginTick()
        {
            prevDistance = Distance;
            prevLateral = Lateral;
            prevHeight = Height;
        }

        /// <summary>After a teleport: nothing to interpolate from, the render sits on the current state.</summary>
        public void SnapInterpolation() => BeginTick();

        public float DistanceAt(float alpha) => Mathf.Lerp(prevDistance, Distance, alpha);
        public float LateralAt(float alpha) => Mathf.Lerp(prevLateral, Lateral, alpha);
        public float HeightAt(float alpha) => Mathf.Lerp(prevHeight, Height, alpha);

        // -------------------------------------------------------------- step
        /// <summary>Advances the body by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt, in BodyControls controls)
        {
            // Off the track (or waiting to relaunch) the body is the owner's.
            if (State == ShipState.OffTrack || State == ShipState.Respawning) return;

            wallHitCooldown = Mathf.Max(0f, wallHitCooldown - dt);

            StepSpeed(dt, controls);
            StepLateral(dt, controls);
            if (State == ShipState.OffTrack) return; // this step took it over an open edge
            // A loop fall is off the track: distance waits at the exit.
            float from = Distance;
            if (State != ShipState.Falling) Distance += ForwardSpeed * dt;
            StepJump(dt);
            StepTubeState();
            if (State != ShipState.Falling) SweepPickups(from);
        }

        // Everything lying on the stretch this step covered, at the lateral
        // and height the body ended it with. Swept, so nothing is tunnelled at
        // any speed; the height test is what makes a jump clear the ground
        // lane. Round a full tube laterals compare modulo its circumference.
        void SweepPickups(float from)
        {
            if (PickedUp == null || PickupRegistry.All.Count == 0) return;
            float wrap = track.SectionAt(Distance) is TubeSection { Unbounded: true } tube ? tube.Circumference : 0f;
            touched.Clear();
            PickupRegistry.Sweep(from, Distance, Lateral, Height, Params.pickupReach.x, Params.pickupReach.y, wrap, touched);
            for (int i = 0; i < touched.Count; i++) PickedUp?.Invoke(touched[i]);
        }

        // Throttle released below this reads as "coasting".
        const float ThrottleDeadzone = 0.01f;

        /// <summary>
        /// The speed model. Queued changes (orbs, pads, a takeoff boost) blend
        /// in first and are the ONLY thing that lifts the speed past cruise.
        /// Above cruise the passive bleed pulls it back down to cruise, never
        /// through it; at or below cruise the throttle accelerates up to
        /// cruise and no further, and a released throttle coasts down. The
        /// brake works everywhere.
        /// </summary>
        void StepSpeed(float dt, in BodyControls controls)
        {
            // Blend queued pad effects in at the body's acceleration rate.
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

        void StepLateral(float dt, in BodyControls controls)
        {
            LateralBlocked = false;

            // The lane at this distance: ±half width on the road, the arc round
            // the pipe on a tube (its edge behaves exactly like the road's).
            track.GetLateralBand(Distance, out float bandMin, out float bandMax);

            // In the air the stick and the dash both work at the jump's reduced
            // authority; on a tube the stick alone gets the pipe's extra reach.
            float control = ControlFactor;
            TubeSection tube = State == ShipState.OnTube ? track.SectionAt(Distance) as TubeSection : null;
            float steerFactor = tube != null ? tube.SteeringFactor : 1f;

            // Steering is a FORCE against a lateral drag: full steer settles
            // at lateralSpeed (force / drag) within about 1 / drag seconds. A
            // flat sweep taken too fast adds its slip on top — the same drag
            // is what a slide has to overcome, and what ends it. The drag is
            // integrated implicitly, so any step size is stable.
            float drag = Mathf.Max(Params.handlingResponse, 0.01f);
            float fullSteerSpeed = Params.lateralSpeed * steerFactor;
            float steerForce = fullSteerSpeed * drag;
            float drive = controls.steer * steerForce * control;
            float slip = SlipAcceleration(dt);
            LateralVelocity = (LateralVelocity + (drive + slip) * dt) / (1f + drag * dt);

            // A dash's shove decays under the very same drag (kept as its
            // own term only so a wall can tell a slam from steering).
            ShoveVelocity /= 1f + drag * dt;
            if (Mathf.Abs(ShoveVelocity) < ShoveEpsilon) ShoveVelocity = 0f;
            float dashVelocity = ShoveVelocity;
            TotalLateralVelocity = LateralVelocity + dashVelocity;
            BankDemand = (drive + slip) / Mathf.Max(steerForce, 0.01f) + dashVelocity / Mathf.Max(fullSteerSpeed, 0.01f);

            // The end of a tube is the system's: over the return stretch the
            // lateral eases home to the band's centre, steering and dash
            // ignored, so the road never unrolls under a body hanging off it.
            if (tube != null)
            {
                float local = Distance - tube.StartDistance;
                float progress = tube.ReturnProgress(local);
                if (progress > 0f)
                {
                    if (!returnLocked) { returnLocked = true; returnFromLateral = Lateral; }
                    // A full tube unwinds to the NEAREST top (a whole number of
                    // turns is the same pose), then snaps that to 0 for the
                    // flat road once the curl-out begins — same pose, no jolt.
                    float home = tube.Unbounded
                        ? Mathf.Round(returnFromLateral / tube.Circumference) * tube.Circumference
                        : (bandMin + bandMax) * 0.5f;
                    if (progress >= 1f)
                    {
                        // Same pose, another number: carry the interpolation
                        // origin over too, or the render would unwind the
                        // turns in one frame.
                        prevLateral -= Lateral;
                        Lateral = 0f;
                    }
                    else Lateral = Mathf.Lerp(returnFromLateral, home, progress);
                    StopLateral();
                    return;
                }
                if (tube.IsUnboundedAt(local))
                {
                    // Round and round: no clamp, no wall.
                    returnLocked = false;
                    Lateral += (LateralVelocity + dashVelocity) * dt;
                    return;
                }
            }
            returnLocked = false;

            // An OPEN edge has no wall: the body runs past it, and hanging
            // beyond the overhang for the whole grace is a fall — a slide or
            // a dash can be steered back from inside that window. If the wall
            // resumes under a body still hanging out there, it is gone.
            if (!HoldOnTrack)
            {
                float next = Lateral + (LateralVelocity + dashVelocity) * dt;
                int side = next > bandMax ? 1 : next < bandMin ? -1 : 0;
                if (side != 0 && track.IsEdgeOpen(Distance, side))
                {
                    Lateral = next;
                    float over = side > 0 ? next - bandMax : bandMin - next;
                    // In the air there is nothing to fall off yet: where it
                    // comes DOWN decides (Land).
                    edgeTimer = over > Params.edgeOverhang && State != ShipState.Airborne ? edgeTimer + dt : 0f;
                    if (edgeTimer >= Params.edgeGraceSeconds) LeaveTrack(side);
                    return;
                }

                float hanging = Mathf.Max(Lateral - bandMax, bandMin - Lateral);
                if (hanging > Params.edgeOverhang)
                {
                    LeaveTrack(Lateral > bandMax ? 1 : -1);
                    return;
                }
            }
            edgeTimer = 0f;

            // The clamp is what guarantees a dash can never leave a walled track.
            Lateral = Mathf.Clamp(Lateral + (LateralVelocity + dashVelocity) * dt, bandMin, bandMax);
            bool hitEdge = Lateral <= bandMin || Lateral >= bandMax;

            // Committed to a ramp: the side rails hold the body on the slope.
            if (Ramp != null)
            {
                float rail = Mathf.Max(0f, Ramp.HalfWidth - Ramp.Definition.entryMargin);
                Lateral = Mathf.Clamp(Lateral, Ramp.Lateral - rail, Ramp.Lateral + rail);
            }
            // Beside a ramp: its edge is a wall. Crossing into it is a side hit —
            // the body is held outside and loses a slice of speed (WallHit is
            // the shared feedback path with the dash slam).
            else if (blockingRamp != null && State == ShipState.Grounded && blockingRamp.Spans(Distance))
            {
                float edge = blockingRamp.Lateral + blockSide * Mathf.Max(0f, blockingRamp.HalfWidth - blockingRamp.Definition.entryMargin);
                bool intoWall = blockSide > 0 ? Lateral < edge : Lateral > edge;
                if (intoWall)
                {
                    Lateral = edge;
                    if (wallHitCooldown <= 0f)
                    {
                        ForwardSpeed *= 1f - Mathf.Clamp01(blockingRamp.Definition.sideHitSpeedLoss);
                        WallHit?.Invoke(Mathf.Abs(LateralVelocity + dashVelocity));
                        wallHitCooldown = Params.wallHitCooldownSeconds;
                    }
                    StopLateral();
                }
            }

            if (hitEdge)
            {
                // Only a dash carried into the wall counts as a slam — gentle
                // steering saturation stays silent, and a cooldown stops spam.
                if (dashVelocity != 0f && wallHitCooldown <= 0f)
                {
                    WallHit?.Invoke(Mathf.Abs(LateralVelocity + dashVelocity));
                    wallHitCooldown = Params.wallHitCooldownSeconds;
                }
                StopLateral(); // the wall ends the dash
            }
        }

        void LeaveTrack(int side)
        {
            edgeTimer = 0f;
            IsSliding = false;
            SetState(ShipState.OffTrack);
            LeftTrack?.Invoke(side);
        }

        void StopLateral()
        {
            LateralVelocity = 0f;
            ShoveVelocity = 0f;
            BankDemand = 0f;
            LateralBlocked = true;
        }

        /// <summary>
        /// Grip, tested on FLAT SWEEPS only (<see cref="TrackManager.FlatSweepAt"/>).
        /// Following the curve takes a lateral acceleration of v²κ toward its
        /// inside; the body holds grip(v) = gripBase + gripPerSpeed × v of it,
        /// and whatever is left over is returned as an acceleration toward
        /// the OUTSIDE of the turn. Slipping outward faster than the slide
        /// threshold is a slide: it scrubs forward speed and fires
        /// <see cref="Sliding"/> once as it begins. Everywhere else — banked
        /// sweeps, straights, sections, the air — the road holds the body.
        /// </summary>
        float SlipAcceleration(float dt)
        {
            bool sliding = false;
            float slip = 0f;
            float excess = 0f;

            if (!HoldOnTrack && State == ShipState.Grounded && track.FlatSweepAt(Distance) != null)
            {
                float curvature = track.GetCurvatureAtDistance(Distance);
                float outward = -Mathf.Sign(curvature);
                float demand = ForwardSpeed * ForwardSpeed * Mathf.Abs(curvature);
                excess = Mathf.Max(0f, demand - (Params.gripBase + Params.gripPerSpeed * ForwardSpeed));
                slip = outward * excess;
                sliding = excess > 0f && outward * LateralVelocity > Params.slideThreshold;
            }

            if (sliding)
            {
                ForwardSpeed *= 1f - Mathf.Clamp01(Params.slideSpeedLoss * dt);
                if (!IsSliding) Sliding?.Invoke(excess);
            }
            IsSliding = sliding;
            return slip;
        }

        /// <summary>
        /// Jump state machine, off the distance just advanced: the flight
        /// while airborne, the slope while committed, otherwise a scan of the live
        /// ramps for a run-up the body is on — inside the entry band it is
        /// committed, beside it the ramp becomes next step's wall.
        /// </summary>
        void StepJump(float dt)
        {
            float d = Distance;
            if (State == ShipState.Looping || State == ShipState.Falling) return;

            if (State == ShipState.Airborne)
            {
                // Constant gravity, integrated exactly — the arc does not
                // depend on the step size.
                AirTime += dt;
                Height += VerticalVelocity * dt - 0.5f * airGravity * dt * dt;
                VerticalVelocity -= airGravity * dt;
                if (Height <= 0f && VerticalVelocity < 0f) { Land(); return; }
                PitchDegrees = -Mathf.Atan2(VerticalVelocity, Mathf.Max(ForwardSpeed, 1f)) * Mathf.Rad2Deg;
                return;
            }

            if (Ramp != null)
            {
                if (d >= Ramp.EndDistance) { TakeOff(); return; }
                RideRamp(d);
                return;
            }

            Height = 0f;
            VerticalVelocity = 0f;
            PitchDegrees = 0f;
            blockingRamp = null;
            foreach (var candidate in JumpRamp.Active)
            {
                if (candidate == null || candidate.Definition == null || !candidate.Spans(d)) continue;
                float rel = Lateral - candidate.Lateral;
                float inner = Mathf.Max(0f, candidate.HalfWidth - candidate.Definition.entryMargin);
                if (Mathf.Abs(rel) <= inner)
                {
                    Ramp = candidate; // committed: no abort window at these speeds
                    RideRamp(d);
                    break;
                }
                blockingRamp = candidate;
                blockSide = rel >= 0f ? 1 : -1;
            }
        }

        void RideRamp(float d)
        {
            Height = Ramp.HeightAt(d);
            VerticalVelocity = Ramp.Definition.Slope * ForwardSpeed;
            PitchDegrees = -Ramp.Definition.rampAngle;
        }

        void TakeOff()
        {
            JumpDefinition def = Ramp.Definition;
            airDefinition = def;
            // The body leaves the lip with the velocity it was riding the
            // slope at; the gravity is then the one number that lands this
            // flight, AT THIS SPEED, exactly the authored air distance on
            // (0 = h0 + vy·T − ½·g·T² with T = distance / speed). Jump
            // strength scales that distance — and so the height with it —
            // and the boost at the lip. The authored cap on the distance is
            // what the generator's landing zone is sized from; a boost
            // blending in during a sub-second flight adds a few metres, well
            // inside its landing clearance.
            float speed = Mathf.Max(ForwardSpeed, 1f);
            float airLength = def.AirDistanceFor(ForwardSpeed) * Params.jumpStrength;
            float airSeconds = Mathf.Max(airLength / speed, 0.05f);
            Height = def.LipHeight;
            VerticalVelocity = def.Slope * speed;
            airGravity = 2f * (Height + VerticalVelocity * airSeconds) / (airSeconds * airSeconds);
            AirTime = 0f;
            float boost = Ramp.Boost * Params.jumpStrength;
            Ramp = null;
            blockingRamp = null;

            SetState(ShipState.Airborne);
            TookOff?.Invoke(boost);
        }

        void Land()
        {
            // Coming down outside the road over an edge with no wall is a
            // fall, not a landing. (Every landing zone is walled today, where
            // the clamp has kept the body in the band all flight.)
            if (!HoldOnTrack)
            {
                track.GetLateralBand(Distance, out float bandMin, out float bandMax);
                if (Mathf.Max(Lateral - bandMax, bandMin - Lateral) > Params.edgeOverhang)
                {
                    airDefinition = null;
                    LeaveTrack(Lateral > bandMax ? 1 : -1);
                    return;
                }
            }

            Height = 0f;
            VerticalVelocity = 0f;
            PitchDegrees = 0f;
            airDefinition = null;
            SetState(ShipState.Grounded);
            Landed?.Invoke();
        }

        // A tube is a state only so readers can tell; the pose function and
        // the lane band do all the work. Ramps and loops never sit in one.
        void StepTubeState()
        {
            if (State != ShipState.Grounded && State != ShipState.OnTube) return;
            bool onTube = track.SectionAt(Distance) is TubeSection;
            if (State == ShipState.Grounded && onTube) SetState(ShipState.OnTube);
            else if (State == ShipState.OnTube && !onTube) SetState(ShipState.Grounded);
        }
    }
}
