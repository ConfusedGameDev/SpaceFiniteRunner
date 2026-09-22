using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// <b>The runner's rules over the standalone ship.</b> The <see cref="HoverShip"/>
    /// on the same object (required) does the flying; the motor is what the
    /// runner SEES of it: every tick it reads where that ship is on the track
    /// (the <see cref="TrackGuide"/>'s projection of its world pose) and
    /// mirrors it into its own <see cref="TrackBody"/> — distance, lateral,
    /// height, speed, state — and it forwards the ship's events and commands.
    /// Everything that was written against the motor keeps working without a
    /// line changed: the GameManager's win / lose and feedback wiring, the
    /// generator's streaming and loop-reachability, the HUD, the ghost trail,
    /// even the track-space patrol, which chases the mirrored body.
    ///
    /// What the runner adds to a free ship lives here too, as rules over the
    /// physical flight rather than as motion: the <b>loop gate</b> (verdict at
    /// the mouth; a fail lets go at the top of the first turn and drops onto
    /// the exit, the track-space drop played through
    /// <see cref="HoverBody.MoveOutOfPlay"/>), the <b>ramp boost</b> at the
    /// lip, and the <b>tube return</b> (steering taken over the last stretch
    /// of a pipe so the road never unrolls under a ship hanging off its side).
    /// The flight itself — hover, walls, jumps, falls, pickups — is the
    /// standalone ship's. (Until the M9 cutover the motor was the runner's
    /// whole track-space simulation; that code is gone, the <see cref="TrackBody"/>
    /// it drove lives on as the patrol's model and as the mirror here.)
    /// </summary>
    public partial class ShipMotor
    {
        HoverShip physicsShip;
        TrackGuide physicsGuide;
        JumpRamp physicsRamp;
        JumpRamp physicsSideHitRamp;  // the ramp whose flank has already cost this ship its side hit
        bool paused, autopilot;
        bool physicsDropping;         // the failed loop's drop, flown here
        float physicsDropSpeed;       // the speed the ship comes out of the drop with
        bool physicsLeftEnd;          // off the end of a finite track: the motor's own world flight carries the ship from here
        float physicsSweepFrom;       // the track distance the last tick ended on: where this tick's analytic sweep starts
        readonly List<ITrackPickup> physicsTouched = new();

        /// <summary>True when a <see cref="HoverShip"/> on this object does the flying.</summary>
        public bool IsPhysics => physicsShip != null;

        /// <summary>The standalone ship underneath, in physics mode.</summary>
        public HoverShip PhysicsShip => physicsShip;

        /// <summary>Is <paramref name="ship"/> this ship? A pickup taken in physics mode names the ship underneath.</summary>
        public bool Is(IShip ship) => ReferenceEquals(ship, this) || (physicsShip != null && ReferenceEquals(ship, physicsShip));

        void BindPhysicsShip()
        {
            physicsShip = GetComponent<HoverShip>();
            if (physicsShip == null) return;
            physicsShip.LaunchOnStart = false; // the motor launches it, from the track's start line
            physicsGuide = FindFirstObjectByType<TrackGuide>();
            if (physicsGuide == null)
                Debug.LogError("ShipMotor: physics mode needs a TrackGuide in the scene (and a TrackColliderBuilder to fly on).", this);
        }

        void SubscribePhysics()
        {
            if (physicsShip == null) return;
            physicsShip.PadImpulse += ForwardPadImpulse;
            physicsShip.DashPerformed += ForwardDash;
            physicsShip.BarrelRollStarted += ForwardRoll;
            physicsShip.MeterFilled += ForwardMeterFilled;
            physicsShip.WallHit += ForwardWallHit;
            physicsShip.Sliding += ForwardSliding;
            physicsShip.FellOff += ForwardFellOff;
            physicsShip.RespawnStarted += ForwardRespawnStarted;
            physicsShip.Respawned += ForwardRespawned;
            physicsShip.TookOff += OnPhysicsTookOff;
            physicsShip.Landed += ForwardLanded;
        }

        void UnsubscribePhysics()
        {
            if (physicsShip == null) return;
            physicsShip.PadImpulse -= ForwardPadImpulse;
            physicsShip.DashPerformed -= ForwardDash;
            physicsShip.BarrelRollStarted -= ForwardRoll;
            physicsShip.MeterFilled -= ForwardMeterFilled;
            physicsShip.WallHit -= ForwardWallHit;
            physicsShip.Sliding -= ForwardSliding;
            physicsShip.FellOff -= ForwardFellOff;
            physicsShip.RespawnStarted -= ForwardRespawnStarted;
            physicsShip.Respawned -= ForwardRespawned;
            physicsShip.TookOff -= OnPhysicsTookOff;
            physicsShip.Landed -= ForwardLanded;
        }

        void ForwardPadImpulse(float raw) => PadImpulse?.Invoke(raw);
        void ForwardDash(int direction) => DashPerformed?.Invoke(direction);
        void ForwardRoll(int direction) => BarrelRollStarted?.Invoke(direction);
        void ForwardMeterFilled() => MeterFilled?.Invoke();
        void ForwardWallHit(float speed) => WallHit?.Invoke(speed);
        void ForwardSliding(float excess) => Sliding?.Invoke(excess);
        void ForwardFellOff() => FellOff?.Invoke();
        // The ship has just been put back on the road, possibly kilometres on (the respawn rule skips sections, sweeps and
        // ramps), and it stands there out of play — its guide sample frozen — for the whole wait. The runner must hear
        // where it is NOW: the generator streams road, and the colliders under it, off this distance, and a ship
        // relaunched before they exist has nothing to stand on.
        void ForwardRespawnStarted(Vector3 teleport)
        {
            float hint = float.NaN;
            if (physicsGuide != null && physicsGuide.TryProject(physicsShip.Body.Position, ref hint, out GuideSample at))
            {
                body.Mirror(at.distance, at.lateral, 0f, 0f, 0f);
                body.SnapInterpolation();
            }
            RespawnStarted?.Invoke(teleport);
        }
        void ForwardRespawned() => Respawned?.Invoke();
        void ForwardLanded() => Landed?.Invoke();

        // The takeoff boost rides the pad path, exactly as on the track-space ship: "+N", shake and rumble come free.
        void OnPhysicsTookOff()
        {
            // Off the lip of an END ramp, or off the end of the road between them, is not a jump: no boost, no TookOff,
            // no jump counted — the track-space rule. It is the end of the track.
            // (Said nothing here: the motor's own tick, later this same step, sees the ship past the road and ends the run.)
            if (NearTrackEnd(body.Distance)) return;

            JumpRamp ramp = RampAt(body.Distance, 40f);
            if (ramp != null && !physicsDropping)
            {
                float boost = ramp.Boost * definition.jumpStrength;
                if (boost != 0f) AddSpeedImpulse(boost);
            }
            TookOff?.Invoke();
        }

        // ------------------------------------------------------------ commands
        /// <summary>The run's rules reach the standalone ship through the settings sync, live; its world is the runner's own colliders and nothing else.</summary>
        void ConfigurePhysics(GameSettings settings)
        {
            if (physicsShip == null || physicsShip.Settings == null) return;
            ShipSettings shipSettings = RunnerShipSettingsSync.Ensure(gameObject, settings, physicsShip.Settings).Settings;
            shipSettings.groundLayers = ShipLayers.GroundMask | ShipLayers.SurfaceMask; // the city may still be loaded beside the runner: never its colliders
            MatchSurfaceToShip();
        }

        // The colliders lie one hover height under the flight line and the model is lifted by the same amount, so the
        // ROOT rides the flight line (pads, camera, patrol pose stay put) and the model hovers where it always did.
        void MatchSurfaceToShip()
        {
            if (physicsShip == null || definition == null) return;
            if (physicsShip.Settings != null) physicsShip.Settings.visualLift = definition.hoverHeight;
            var colliders = FindFirstObjectByType<TrackColliderBuilder>();
            if (colliders != null && !Mathf.Approximately(colliders.SurfaceSink, definition.hoverHeight))
            {
                colliders.SurfaceSink = definition.hoverHeight;
                colliders.Clear(); // rebuilt at the new depth on its next update
            }
        }

        void LaunchPhysics()
        {
            HasStopped = false;
            Autopilot = false;
            ClearLoop();
            physicsDropping = false;
            physicsLeftEnd = false;
            physicsSweepFrom = 0f;
            offMode = OffTrackMode.Fall;
            physicsRamp = null;
            physicsSideHitRamp = null;
            MatchSurfaceToShip();
            // A restart has just thrown the old track's colliders away: the new ones must exist before the ship is seated on them.
            var colliders = FindFirstObjectByType<TrackColliderBuilder>();
            if (colliders != null) colliders.BuildNow();

            track.GetPoseAtDistance(0f, 0f, out Vector3 position, out Quaternion rotation);
            physicsShip.SetDefinition(definition);
            physicsShip.Launch(position, rotation);
            body.Reset(0f, definition.initialImpulse);
            MirrorInto(0f, 0f, 0f);
            body.SnapInterpolation();
            lastTickTime = Time.fixedTime;
            Launched?.Invoke();
        }

        // ---------------------------------------------------------------- tick
        void TickPhysics(float dt)
        {
            HoverBody hover = physicsShip.Body;
            body.BeginTick();

            if (physicsDropping) { StepPhysicsDrop(dt); return; }
            if (physicsLeftEnd) { StepPhysicsEnd(dt); return; }

            float distance = body.Distance, lateral = body.Lateral, height = 0f;
            if (physicsShip.Guide != null)
            {
                GuideSample sample = physicsShip.GuideSample;
                distance = sample.distance;
                lateral = sample.lateral;
                height = Mathf.Max(0f, sample.height);
            }
            MirrorInto(distance, lateral, height);
            HasStopped = physicsShip.HasStopped;

            if (NearTrackEnd(distance) && PastTheRoad() && hover.State != ShipState.OffTrack && hover.State != ShipState.Respawning) { BeginPhysicsEnd(); return; }

            physicsRamp = hover.State == ShipState.Grounded && height > 0.25f ? RampAt(distance, 0f) : null;
            SweepLaserGates(hover, distance, lateral, height);
            UpdateRampSideHit(hover, distance, lateral);
            UpdatePhysicsLoop(distance);
            UpdateTubeReturn(distance, lateral);
            body.SetState(MapState(hover.State, distance));
        }

        void MirrorInto(float distance, float lateral, float height)
        {
            HoverBody hover = physicsShip.Body;
            body.Mirror(distance, lateral, height, hover.ForwardSpeed, hover.TotalLateralVelocity);
        }

        // The standalone ship knows Grounded / Airborne / OffTrack / Respawning; the track says which kind of ground it is.
        ShipState MapState(ShipState hoverState, float distance)
        {
            if (hoverState != ShipState.Grounded) return hoverState;
            if (loopSection != null) return ShipState.Looping;
            return track.SectionAt(distance) is TubeSection ? ShipState.OnTube : ShipState.Grounded;
        }

        JumpRamp RampAt(float distance, float slack)
        {
            foreach (JumpRamp ramp in JumpRamp.Active)
                if (ramp != null && ramp.Definition != null && distance >= ramp.StartDistance - slack && distance <= ramp.EndDistance + slack)
                    return ramp;
            return null;
        }

        // ------------------------------------------------------- the track's end
        // A finite track stops: the ship leaves it for good, by the lip of an end ramp (the way out — the GameManager
        // turns that into the escape) or off the bare end of the road (lost). Where the ship IS on the track is only
        // as fresh as the guide's last projection, so "at the end" reaches back by what a tick and a refresh can hide.
        bool NearTrackEnd(float distance)
        {
            if (track == null || !track.HasEnd || physicsLeftEnd) return false;
            float slack = physicsShip.Body.ForwardSpeed * Time.fixedDeltaTime + physicsShip.Settings.guideRefreshMeters + 1f;
            return distance >= track.EndDistance - slack;
        }

        // Near the end by the numbers is not yet off it: the road (and the ramps' lips) end exactly AT the end, so the
        // ship has left when nothing of the track is under it any more.
        bool PastTheRoad()
        {
            HoverBody hover = physicsShip.Body;
            if (hover.State == ShipState.Airborne) return true;
            track.GetPoseAtDistance(track.EndDistance, 0f, out Vector3 end, out Quaternion rotation);
            return Vector3.Dot(hover.Position - end, rotation * Vector3.forward) >= 0f;
        }

        // The track-space flight, started from the physical ship's own pose and velocity: the motor flies it (StepFall —
        // a terminal fall, or the escape once the listener calls BeginEscape) and carries the standalone ship along out
        // of play. Falling, not OffTrack, on the ship: its recovery must never respawn a ship that has nowhere to come back to.
        void BeginPhysicsEnd()
        {
            HoverBody hover = physicsShip.Body;
            bool tookRamp = (physicsRamp != null && physicsRamp.IsEndRamp) || EndRampUnder(hover);
            physicsLeftEnd = true;
            physicsShip.SteerOverride = null;

            offMode = OffTrackMode.TerminalFall;
            offSide = body.Lateral >= 0f ? 1 : -1;
            offTimer = 0f;
            speedAtFall = hover.ForwardSpeed;
            offVelocity = hover.Velocity;
            offRotation = hover.Rotation;
            offPosition = prevOffPosition = hover.Position;

            hover.StopLateralMotion();
            hover.SetState(ShipState.Falling);
            body.Mirror(track.EndDistance, body.Lateral, 0f, hover.ForwardSpeed, 0f);
            body.SetState(ShipState.OffTrack);
            ReachedTrackEnd?.Invoke(tookRamp);
        }

        // Left by a lip: the ship is over (or just past) an end ramp's lane and above the road by more than a hover.
        bool EndRampUnder(HoverBody hover)
        {
            foreach (JumpRamp ramp in JumpRamp.Active)
            {
                if (ramp == null || !ramp.IsEndRamp || ramp.Definition == null) continue;
                if (Mathf.Abs(body.Lateral - ramp.Lateral) > ramp.HalfWidth) continue;
                track.GetPoseAtDistance(track.EndDistance, body.Lateral, out Vector3 road, out Quaternion rotation);
                float above = Vector3.Dot(hover.Position - road, rotation * Vector3.up);
                if (above > ramp.Definition.LipHeight * 0.5f) return true;
            }
            return false;
        }

        void StepPhysicsEnd(float dt)
        {
            StepFall(dt);
            physicsShip.Body.MoveOutOfPlay(offPosition, offRotation);
        }

        // ------------------------------------------------------------ laser gates
        // A gate is a track-space thing (an analytic pickup with a finer test of its own, no collider): the body that
        // swept the registry for it is not stepped in physics mode, so the motor asks over the distance this tick covered.
        // Pads, orbs and coins are NOT taken here — they have colliders and the standalone ship's own sweep takes them.
        void SweepLaserGates(HoverBody hover, float distance, float lateral, float height)
        {
            float from = physicsSweepFrom;
            physicsSweepFrom = distance;
            if (hover.State != ShipState.Grounded && hover.State != ShipState.Airborne) return;
            if (distance <= from || distance - from > 400f) return; // a teleport (launch, respawn) is not a sweep

            physicsTouched.Clear();
            PickupRegistry.Sweep(from, distance, lateral, height, pickupReach.x, pickupReach.y, 0f, physicsTouched);
            foreach (ITrackPickup pickup in physicsTouched)
                if (pickup is LaserGate gate && gate.Touches(from, distance, lateral, height, pickupReach))
                    gate.RaiseHit(this);
        }

        // Beside a ramp its edge is a wall, and meeting it is the runner's side hit: a slice of speed and the wall-hit
        // feedback, once per ramp. The flank collider does the holding out; it runs along the road, so by itself it
        // would cost nothing and say nothing.
        void UpdateRampSideHit(HoverBody hover, float distance, float lateral)
        {
            if (hover.State != ShipState.Grounded || physicsRamp != null) return;
            JumpRamp ramp = RampAt(distance, 0f);
            if (ramp == null || ramp == physicsSideHitRamp) return;
            float hull = Mathf.Min(physicsShip.Settings.hullRadius, Mathf.Max(0.1f, definition.hoverHeight - 0.25f));
            float flank = Mathf.Max(1f, ramp.HalfWidth - ramp.Definition.entryMargin);
            float beside = Mathf.Abs(lateral - ramp.Lateral) - flank;
            if (beside < 0f || beside > hull + 0.25f) return; // over the slope (about to ride it), or clear of the flank

            physicsSideHitRamp = ramp;
            float sideways = Mathf.Abs(hover.TotalLateralVelocity);
            hover.ForwardSpeed *= 1f - Mathf.Clamp01(ramp.Definition.sideHitSpeedLoss);
            hover.StopLateralMotion();
            WallHit?.Invoke(sideways);
        }

        // The end of a tube is the system's: over the return stretch the stick is replaced by a pull to the
        // top of the pipe (the nearest one — the guide's lateral is already the short way round).
        void UpdateTubeReturn(float distance, float lateral)
        {
            float? steer = null;
            if (!Autopilot && track.SectionAt(distance) is TubeSection tube && tube.ReturnProgress(distance - tube.StartDistance) > 0f)
                steer = Mathf.Clamp(-lateral / AutopilotReach, -1f, 1f);
            physicsShip.SteerOverride = steer;
        }

        // ---------------------------------------------------------------- loops
        void UpdatePhysicsLoop(float distance)
        {
            if (loopSection != null)
            {
                if (!loopPassed && distance - loopSection.StartDistance >= loopSection.FirstTopLocal) { BeginPhysicsDrop(); return; }
                if (distance >= loopSection.EndDistance || distance < loopSection.StartDistance - 50f) ClearLoop();
                return;
            }

            if (physicsShip.State != ShipState.Grounded) return;
            foreach (LoopFeature candidate in LoopFeature.Active)
            {
                if (candidate == null || candidate.Section == null || !candidate.Section.Contains(distance)) continue;
                loop = candidate;
                loopSection = candidate.Section;
                loopDefinition = candidate.Definition;
                loopPassed = CurrentSpeed >= candidate.RequiredSpeed;
                LoopEntered?.Invoke(loopPassed);
                break;
            }
        }

        // Too slow for the loop: it lets go at the top of the first turn and comes down on the (displaced) exit —
        // the track-space drop, with the standalone ship carried along it out of play.
        void BeginPhysicsDrop()
        {
            HoverBody hover = physicsShip.Body;
            fallTopPosition = hover.Position;
            fallTopRotation = hover.Rotation;
            loopSection.GetExitPose(0f, out fallExitPosition, out fallExitRotation);
            fallHeight = Mathf.Max(1f, loopSection.Radius * 2f);
            fallDistance = prevFallDistance = 0f;
            fallVelocity = 0f;
            physicsDropSpeed = hover.ForwardSpeed * (1f - Mathf.Clamp01(loopDefinition.fallSpeedLoss));
            physicsDropping = true;

            hover.StopLateralMotion();
            hover.SetState(ShipState.Falling);
            body.SetState(ShipState.Falling);
            // The distance is parked at the exit: the patrol, which never slows and always does the perfect loop, gains the whole fall.
            body.Mirror(loopSection.EndDistance, 0f, 0f, 0f, 0f);
            body.SnapInterpolation();
            LoopFailed?.Invoke();
        }

        void StepPhysicsDrop(float dt)
        {
            fallVelocity += loopDefinition.fallGravity * dt;
            fallDistance += fallVelocity * dt;
            float t = Mathf.Clamp01(fallDistance / fallHeight);
            physicsShip.Body.MoveOutOfPlay(Vector3.Lerp(fallTopPosition, fallExitPosition, t), Quaternion.Slerp(fallTopRotation, fallExitRotation, t));
            if (t < 1f) return;

            physicsDropping = false;
            physicsShip.Body.Reset(fallExitPosition, fallExitRotation, physicsDropSpeed);
            ClearLoop();
            body.SetState(ShipState.Grounded);
            Landed?.Invoke();
        }

        // -------------------------------------------------------------- render
        void RenderPhysics(float alpha)
        {
            // The standalone ship poses the transform and the model; the motor only reports where that is on the track.
            DistanceTravelled = body.DistanceAt(alpha);
            LateralOffset = body.LateralAt(alpha);
            AirHeight = body.HeightAt(alpha);
        }
    }
}
