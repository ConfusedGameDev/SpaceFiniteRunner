using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The police patrol chasing the ship down the track. Rubber-band chase:
    /// its target speed is the ship's current speed times a factor, floored
    /// by a minimum that starts at the launch speed and slowly ramps up — so
    /// it speeds up with the player but never drops below that threshold.
    /// The band alone is deliberately conservative and does NOT close the gap
    /// (the shipped asset targets BELOW the ship's speed); what closes it is
    /// the attack run's overdrive, and the raised floor a redeploy brings.
    ///
    /// <b>It drives the same <see cref="TrackBody"/> the ship does</b>, through
    /// a <see cref="PatrolDriver"/> that outputs steer / throttle / brake: the
    /// rubber band is simply the body's cruise target (thrust and over-cruise
    /// bleed both set to the catch-up accel, so the speed moves toward it
    /// exactly as the old MoveTowards did), and everything else — steering
    /// force, grip on flat sweeps, open edges, ramps and jumps, tubes, the
    /// swept pickup query — is the ship's own physics. So it steers for the
    /// ship, goes after boost orbs (which it uses up), rounds a ramp or jumps
    /// it, brakes for flat sweeps, and can fall off: a fall redeploys it
    /// behind the ship and never touches the player. It takes every loop
    /// perfectly (no gate is asked of it). Like the ship it ticks in
    /// FixedUpdate and renders an interpolated pose, so
    /// <see cref="DistanceTravelled"/> / <see cref="GapToShip"/> are the
    /// rendered values (minimap) and the catch is judged on the ticks' own.
    ///
    /// <b>The attack run</b> (<see cref="PatrolEncounter"/>, on
    /// <see cref="GameSettings.patrolDuelEnabled"/>): on a cadence the patrol
    /// overdrives past the ship's speed, pulls onto a flank and shoves the
    /// ship into the wall or off an open edge, then breaks off and cools down.
    /// It never commits on a ramp, its landing, a loop, a tube or the final
    /// run-up, and braking behind it or out-steering the flank aborts it.
    ///
    /// <b>Catch</b>: tailing the ship inside the catch distance for
    /// <see cref="PatrolDefinition.sustainedCatchSeconds"/> is an arrest — the
    /// punishment for refusing to engage, SUSPENDED for the whole of an attack
    /// run (which parks the patrol inside that distance by design). With the
    /// duel off the old proximity catch comes back as well: also inside
    /// <see cref="PatrolDefinition.alongsideLateral"/> across the track is an
    /// immediate arrest. The GameManager polls <see cref="HasCaught"/>.
    /// The chase is never allowed to go stale: outrun the patrol past the
    /// redeploy distance and a fresh one cuts in just behind the ship, already
    /// faster than it, so the only way to shake it is to boost again.
    /// A scene object (referenced by the GameManager, which wires it up via
    /// <see cref="Init"/>); all chase tunables live on its
    /// <see cref="PatrolDefinition"/> asset, cloned at init so the debug menu
    /// edits a live run and never the asset on disk. The cruiser visual is
    /// still built from code — the scene object is just an empty holder.
    /// </summary>
    public class PolicePatrol : MonoBehaviour
    {
        // Inline so the chase sliders are reachable without leaving the scene.
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        PatrolDefinition definition;

        [Tooltip("All cruiser look tunables live on this asset — add new knobs there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        PatrolVisualSettings visualSettings;

        ShipMotor target;
        TrackManager track;
        PatrolDefinition runtimeDef; // clone of the asset, the only copy ever mutated

        float redeployDistance;    // gap that retires this patrol for a fresh one, meters (0 = never)
        float redeployGap;         // meters behind the ship the fresh patrol drops in at
        float redeploySpeedFactor; // fresh patrol's speed as a multiple of the ship's

        float minSpeed;       // current floor: baseSpeed + accumulated ramp
        TrackBody body;       // the same track-space body the ship rides
        TrackGenerator generator; // for the attack run's forbidden-ground test
        readonly PatrolDriver driver = new();
        readonly PatrolEncounter encounter = new();
        bool duelEnabled = true;
        float unscaledStep;   // real seconds per substep, for the bar
        int pendingPresses;   // mash presses drained this tick, spent on the first substep
        float killHideLeft;   // seconds the cruiser stays invisible across a kill's teleport
        float lastTickTime = float.NegativeInfinity; // Time.fixedTime of the last tick, for the render's blend
        float tailTimer;      // seconds spent inside the catch distance
        float shownBank;
        float warnCooldown;
        bool warned;          // proximity warning already raised for this approach
        float blinkTimer;
        bool blinkState;

        Transform visual;
        GameObject redLight;
        GameObject blueLight;

        // Half width / half height of the cruiser's pickup volume, metres.
        static readonly Vector2 PickupReach = new(2.5f, 2.3f);

        /// <summary>Metres from the track start (negative behind the start line), as RENDERED this frame.</summary>
        public float DistanceTravelled { get; private set; }
        public bool HasCaught { get; private set; }

        /// <summary>The track-space body the patrol drives; null until <see cref="Init"/>.</summary>
        public TrackBody Body => body;

        /// <summary>Where the patrol is in its attack run — for the debug overlay and the HUD.</summary>
        public PatrolEncounterState EncounterState => encounter.State;

        /// <summary>Which side of the ship a run is being made from: -1 left, +1 right, 0 when idle.</summary>
        public int EncounterSide => encounter.Side;

        /// <summary>True while the tug-of-war bar is up and being fought over.</summary>
        public bool InTugOfWar => encounter.InTugOfWar;

        /// <summary>True while the exchange owns the world clock and the ship's steering.</summary>
        public bool InExchange => encounter.InExchange;

        /// <summary>The contest as the PATROL's progress: 0.5 at the start, 1 it wins, 0 you do.</summary>
        public float Tug => encounter.Tug;

        /// <summary>
        /// One line of why the duel is doing what it is doing, for the on-screen
        /// readout: the state, the gap, and — when no run is happening — the two
        /// things that gate one. Both of those are invisible when they work,
        /// which is exactly why they need saying out loud.
        /// </summary>
        public string EncounterDebug()
        {
            if (runtimeDef == null) return "duel: no definition";
            string line = $"{encounter.State} gap {SimGap:0} across {AcrossToShip():0}";
            if (encounter.InTugOfWar) return $"{line} tug {encounter.Tug:0.00} side {encounter.Side}";
            if (encounter.State == PatrolEncounterState.Cruising)
                return $"{line} | commit in {encounter.CommitIn(runtimeDef):0.0}s | ground {(encounter.GroundWasClear ? "CLEAR" : "BLOCKED")}";
            return line;
        }

        /// <summary>
        /// The whole duel, on or off. Off restores the old chase exactly,
        /// proximity arrest included — the GameManager wires this from
        /// <see cref="GameSettings.patrolDuelEnabled"/>.
        /// </summary>
        public bool DuelEnabled
        {
            get => duelEnabled;
            set { duelEnabled = value; if (!value) encounter.Reset(); }
        }

        /// <summary>
        /// Drops an attack run in progress without a shove. The run's own
        /// aborts cover the ship's doing; this is for the run ENDING out from
        /// under it (a loss wind-down), where nothing else would notice.
        /// </summary>
        public void AbortEncounter() => encounter.Abort();

        /// <summary>The gap on the simulation's own state — what the catch and the driver judge by.</summary>
        float SimGap => target != null && target.Body != null && body != null ? target.Body.Distance - body.Distance : float.MaxValue;
        /// <summary>How many patrols have joined the chase this run (1 = the launch patrol).</summary>
        public int PatrolNumber { get; private set; } = 1;
        public float GapToShip => target != null ? target.DistanceTravelled - DistanceTravelled : float.MaxValue;

        /// <summary>
        /// True once this patrol has run off the END of the track: it is
        /// falling (then hidden) and never comes back — no redeploy, no catch,
        /// no warning — until the next <see cref="Launch"/>. The track's end
        /// takes every patrol that reaches it, whatever the ship did there.
        /// </summary>
        public bool IsGone { get; private set; }

        /// <summary>
        /// True while the cruiser must not be drawn ANYWHERE — off the end of
        /// the track, or hidden across a kill's teleport. The minimap reads
        /// this rather than <see cref="IsGone"/> so a kill's reposition is
        /// invisible on the map as well as in the world.
        /// </summary>
        public bool HiddenFromMap => IsGone || killHideLeft > 0f;

        // The end fall, flown in world space like the ship's.
        Vector3 offPosition, prevOffPosition, offVelocity;
        Quaternion offRotation;
        int offSide;
        float goneTimer;
        const float GoneHideSeconds = 4f; // the cruiser is switched off this long into its fall

        /// <summary>The live chase tunables — the runtime clone, so the debug menu can edit them mid-run. Null until <see cref="Init"/>.</summary>
        public PatrolDefinition Definition => runtimeDef;

        /// <summary>
        /// A fresh patrol just cut in behind the ship; the argument is its
        /// number. The patrol only rumbles here — the story line announcing it
        /// is the GameManager's (it owns the message texts on GameSettings).
        /// </summary>
        public event System.Action<int> Redeployed;

        /// <summary>
        /// The patrol closed inside its warn distance; the argument is the gap
        /// in meters. Raised once per approach (re-armed when the ship opens
        /// the gap past the warn distance again) — the GameManager turns it
        /// into the patrol's taunt line, gated by GameSettings.
        /// </summary>
        public event System.Action<float> Warned;

        /// <summary>
        /// True while the patrol stands still and cannot catch: the ship is
        /// off the track or waiting to relaunch. Separate from the motor's
        /// pause on purpose — the run's clock keeps running through a hold.
        /// </summary>
        public bool Hold { get; private set; }

        /// <summary>
        /// Starts or ends a hold. Ending it guarantees the ship a head start:
        /// a patrol closer than <paramref name="minGapOnRelease"/> is dropped
        /// back to exactly that gap (the respawn moved the ship, not the patrol).
        /// </summary>
        public void SetHold(bool hold, float minGapOnRelease = 0f)
        {
            if (Hold == hold) return;
            Hold = hold;
            encounter.Reset(); // held or released, no run survives it
            DuelMashInput.Clear();
            if (target != null) target.SteerAssist = 0f;
            if (hold || target == null) return;

            if (body != null && SimGap < minGapOnRelease)
            {
                body.Distance = target.Body.Distance - minGapOnRelease;
                body.SnapInterpolation();
                ApplyPose(1f);
            }
            tailTimer = 0f;
            warnCooldown = 0f;
            warned = GapToShip <= (runtimeDef != null ? runtimeDef.warnDistance : 0f); // no taunt for a gap the respawn made
            encounter.Reset(); // the respawn moved the ship out from under any run
        }

        /// <summary>Proximity rumble that grows as the patrol closes in (GameSettings.patrolProximityRumble).</summary>
        public bool ProximityRumble { get; set; } = true;

        /// <summary>
        /// Wires the scene patrol up for a run: clones its definition (with
        /// any armed <see cref="PatrolDebugSettings"/> overrides on top),
        /// builds the cruiser visual and launches the chase. Called by the
        /// GameManager in Awake; the object stays inert without it.
        /// </summary>
        public void Init(ShipMotor target)
        {
            if (this.target != null)
            {
                this.target.PadImpulse -= OnShipImpulse;
                this.target.DashPerformed -= OnShipDashed;
            }
            this.target = target;
            if (target != null)
            {
                target.PadImpulse += OnShipImpulse;
                target.DashPerformed += OnShipDashed; // the finisher IS a dash
            }
            track = FindFirstObjectByType<TrackManager>();
            generator = FindFirstObjectByType<TrackGenerator>();

            if (definition == null)
            {
                Debug.LogError($"{nameof(PolicePatrol)} has no {nameof(PatrolDefinition)} asset assigned — falling back to defaults.", this);
                definition = ScriptableObject.CreateInstance<PatrolDefinition>();
            }
            runtimeDef = Instantiate(definition);
            PatrolDebugSettings.Load().ApplyTo(runtimeDef);

            // The body is this component's own object: its events die with it.
            body = track != null ? new TrackBody(track) : null;
            if (body != null)
            {
                body.PickedUp += OnPickedUp;
                body.LeftTrack += OnLeftTrack;
            body.ReachedEnd += OnReachedEnd;
            }

            BuildVisual();
            Launch();
        }

        /// <summary>
        /// Arms the "never lose them for good" rule: once the ship is more than
        /// <paramref name="distance"/> meters clear, this patrol drops out and a
        /// fresh one takes over <paramref name="gap"/> meters back, running at
        /// <paramref name="speedFactor"/> times the ship's speed. Pass 0 distance
        /// to disable. Kept separate from Spawn so the chase tunables stay on the
        /// GameSettings asset without growing its argument list.
        /// </summary>
        public void SetRedeployRule(float distance, float gap, float speedFactor)
        {
            redeployDistance = distance;
            redeployGap = gap;
            redeploySpeedFactor = speedFactor;
        }

        /// <summary>Resets the chase to the launch gap behind the start line.</summary>
        public void Launch()
        {
            if (runtimeDef == null || body == null) return; // scene object never Init'd (patrol disabled)
            body.Reset(-runtimeDef.startGap, runtimeDef.baseSpeed);
            minSpeed = runtimeDef.baseSpeed;
            HasCaught = false;
            Hold = false;
            IsGone = false;
            goneTimer = 0f;
            killHideLeft = 0f;
            if (visual != null && !visual.gameObject.activeSelf) visual.gameObject.SetActive(true);
            tailTimer = 0f;
            warnCooldown = 0f;
            warned = false;
            PatrolNumber = 1;
            encounter.Reset();
            ApplyPose(1f);
        }

        void OnDestroy()
        {
            if (target != null)
            {
                target.PadImpulse -= OnShipImpulse;
                target.DashPerformed -= OnShipDashed;
                target.SteerAssist = 0f; // never leave the ship being steered by a patrol that is gone
                target.DashLocked = false;
            }
        }

        /// <summary>
        /// Boost share: every speed-up the ship collects (orbs, ramp takeoffs —
        /// anything through <see cref="ShipMotor.AddSpeedImpulse"/>) hands the
        /// patrol <see cref="PatrolDefinition.boostShare"/> of the ship's ACTUAL
        /// gain at once. Without it a boost opened a gap the rubber band only
        /// closed at catch-up accel, so a few orbs left the patrol for dead;
        /// with it the patrol reacts in the same frame and only the remaining
        /// share is breathing room. Brakes are the player's problem alone —
        /// sharing them would make brake pads harmless. The floor is left
        /// untouched: the share is speed, not a new minimum, so a coasting ship
        /// still gets the ordinary rubber band rather than a boosted floor.
        /// </summary>
        void OnShipImpulse(float rawMagnitude)
        {
            if (rawMagnitude <= 0f || runtimeDef == null || body == null || HasCaught || IsGone) return;
            float gain = target.Definition != null ? target.Definition.ScalePadEffect(rawMagnitude) : rawMagnitude;
            body.ForwardSpeed += gain * runtimeDef.boostShare;
        }

        // Its own pickups: only boost orbs mean anything to it. The orb is
        // used up (it disappears) and a share of its boost is speed above the
        // rubber band's target, which then bleeds back at the catch-up accel.
        // Brake pads and coins are the player's alone.
        void OnPickedUp(ITrackPickup pickup)
        {
            if (pickup is SpeedPad pad && pad.IsBoostOrb)
                body.ForwardSpeed += pad.Take() * runtimeDef.orbBoostShare;
        }

        /// <summary>
        /// The ship dashed. During the finisher's window a dash INTO the patrol
        /// is the kill; any other dash is an ordinary dash that simply spends
        /// the window. Everything outside the window is none of our business.
        /// </summary>
        void OnShipDashed(int direction)
        {
            if (!duelEnabled || IsGone || HasCaught) return;
            if (encounter.ReportDash(direction)) Kill();
        }

        /// <summary>
        /// The kill: a fireball where the cruiser was, the world held for a
        /// beat, and the same object recycled in behind the ship as the next
        /// patrol. There is only ever ONE cruiser — the "destroyed" one is this
        /// one, hidden across its own teleport so the replacement reads as a
        /// fresh car arriving rather than the same one blinking backwards.
        /// The kill costs the player the WHOLE dash meter, so the next patrol's
        /// approach is faced without an evasive move.
        /// </summary>
        void Kill()
        {
            GameSettings rules = target.DashSettings;
            Vector3 origin = visual != null ? visual.position : transform.position;
            if (rules != null && rules.explosionTextures != null && rules.explosionTextures.Count > 0)
                ExplosionVfx.SpawnFireball(origin, rules.explosionTextures,
                                           rules.explosionScale * rules.patrolExplosionScale,
                                           rules.explosionLifetime, rules.explosionParticles);

            HapticsSystem.Instance.Pulse(1f, 1f, 0.5f);
            CameraShake.Shake(rules != null ? rules.explosionShake : default);
            if (rules != null) DuelSlowMo.RequestHitStop(rules.duelHitStopSeconds);
            target.DrainDashMeter();
            target.SteerAssist = 0f;
            target.DashLocked = false;

            // Hidden for the reposition, and off the minimap with it, so the
            // teleport is never seen from either direction.
            killHideLeft = KillHideSeconds;
            if (visual != null) visual.gameObject.SetActive(false);

            float gap = runtimeDef.killTeleportGap > 0f ? runtimeDef.killTeleportGap : redeployGap;
            Redeploy(raiseFloor: true, gapOverride: gap);
        }

        // Long enough to cover the fireball and the reposition; the replacement
        // is hundreds of metres back by the time it shows again.
        const float KillHideSeconds = 0.35f;

        // Over an open edge (a mistimed ramp, a slide the driver misjudged):
        // this cruiser is gone and a fresh one drops in behind the ship. The
        // player is never frozen or penalised by it — and, unlike outrunning
        // a patrol, it does not raise the rubber band's floor.
        void OnLeftTrack(int side) => Redeploy(raiseFloor: false);

        // Off the END of the track (by an end ramp's lip or beside them):
        // there is no road to drop a fresh one onto and nothing left to
        // chase, so this cruiser simply falls — the same world-space
        // ballistics as the ship's fall, from the pose it left by.
        void OnReachedEnd(bool tookRamp)
        {
            track.GetPoseAtDistance(body.Distance, body.Lateral, out Vector3 position, out Quaternion rotation);
            position += rotation * (Vector3.up * body.Height);

            IsGone = true;
            goneTimer = 0f;
            encounter.Reset();
            offSide = body.Lateral >= 0f ? 1 : -1;
            offVelocity = rotation * new Vector3(body.TotalLateralVelocity, body.VerticalVelocity, body.ForwardSpeed);
            offRotation = rotation;
            offPosition = position;
            prevOffPosition = transform.position; // from where it is SEEN, so the fall starts without a pop
            HapticsSystem.Instance.SetChaseIntensity(0f);
        }

        void StepEndFall(float dt)
        {
            GameSettings rules = target.DashSettings;
            float gravity = rules != null ? rules.fallGravity : 30f;
            float tumble = rules != null ? rules.fallTumbleDegreesPerSecond : 120f;
            prevOffPosition = offPosition;
            offVelocity += Vector3.down * (gravity * dt);
            offPosition += offVelocity * dt;
            offRotation *= Quaternion.Euler(tumble * 0.35f * dt, 0f, -offSide * tumble * dt);
            goneTimer += dt;
            if (goneTimer >= GoneHideSeconds && visual != null && visual.gameObject.activeSelf)
                visual.gameObject.SetActive(false);
        }

        void Update()
        {
            if (target == null || track == null || runtimeDef == null || body == null) return;

            Blink(Time.deltaTime);

            // The mash is sampled every FRAME and drained by the fixed tick,
            // so a fast press between two ticks is never dropped.
            DuelMashInput.Poll();

            // A killed cruiser stays invisible until its replacement is in
            // place. Unscaled, because the kill's hit-stop is slowing the world
            // and the hide must not stretch with it.
            if (killHideLeft > 0f)
            {
                killHideLeft -= Time.unscaledDeltaTime;
                if (killHideLeft <= 0f && visual != null && !IsGone) visual.gameObject.SetActive(true);
            }

            // The contest holds the dash; the finisher hands it straight back.
            target.DashLocked = duelEnabled && encounter.LocksDash;

            // The assist is written by the fixed tick, which stops running on
            // a catch, a hold or a pause — so the release is owned here, where
            // nothing can strand the ship being steered for it.
            if (!encounter.InExchange && target.SteerAssist != 0f) target.SteerAssist = 0f;

            if (!HasCaught && !target.Paused && !Hold && !IsGone)
            {
                // Proximity rumble that grows as the patrol closes in. The
                // haptics channel self-fades when this stops being refreshed
                // (pause, catch, hold, or the patrol falling behind again).
                float gap = GapToShip;
                if (ProximityRumble && gap <= runtimeDef.warnDistance && runtimeDef.warnDistance > 0f)
                    HapticsSystem.Instance.SetChaseIntensity(1f - Mathf.Clamp01(gap / runtimeDef.warnDistance));
            }

            // Between two ticks the pose is the track-space blend of them; with
            // no fresh tick (paused, held, caught) it rests on the current state.
            ApplyPose(Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f)));
        }

        void FixedUpdate()
        {
            if (target == null || track == null || runtimeDef == null || body == null) return;
            // Off the end of the track: only the fall is left, on the run's clock.
            if (IsGone)
            {
                if (!target.Paused) StepEndFall(Time.fixedDeltaTime);
                lastTickTime = Time.fixedTime;
                return;
            }
            // Freezes with the ship: tuning screen open or run over — and
            // holds while the ship is off the track or waiting to relaunch.
            if (HasCaught || target.Paused || Hold || target.Body == null) return;

            float dt = Time.fixedDeltaTime;
            GameSettings rules = target.DashSettings;
            int substeps = Mathf.Max(1, rules != null ? rules.simSubsteps : 1);

            body.BeginTick();
            float h = dt / substeps;
            // The bar is judged in REAL seconds, so the duel's slow motion
            // cannot hand the player a longer window to mash in.
            unscaledStep = Time.fixedUnscaledDeltaTime / substeps;
            pendingPresses = encounter.InTugOfWar ? DuelMashInput.ConsumePresses() : 0;
            for (int i = 0; i < substeps && !HasCaught && !IsGone; i++) Step(h, rules);
            lastTickTime = Time.fixedTime;
        }

        void Step(float dt, GameSettings rules)
        {
            // Rubber band: chase the ship's speed (scaled), but never drop
            // below the floor — the launch speed plus the accumulated ramp.
            minSpeed += runtimeDef.ramp * dt;
            float desired = Mathf.Max(minSpeed, target.CurrentSpeed * runtimeDef.rubberBand);

            // The ship has left the END of the track (won or lost): it is no
            // longer on the road to be tailed, caught, warned or cut in
            // behind — this patrol just drives on, and the end takes it too.
            bool shipLeft = target.HasLeftTrackEnd;

            float gap = SimGap;

            // The attack run, if the duel is on. It reads the ship and the
            // road and answers with an overdrive, a flank to steer for and —
            // for one substep — the shove.
            var intent = new PatrolEncounterIntent { SpeedMultiplier = 1f };
            if (duelEnabled && !shipLeft)
            {
                // The tick's presses are spent on the first substep only —
                // splitting them would round most of them away.
                var ctx = new PatrolEncounterContext(gap, AcrossToShip(), target.Body.Lateral,
                                                     target.Body.Distance, runtimeDef, track, generator,
                                                     ShipSteady, unscaledStep,
                                                     rules != null ? rules.duelAssistStrength : 0f,
                                                     pendingPresses,
                                                     rules != null ? rules.finisherWindowSeconds : 1f);
                pendingPresses = 0;
                encounter.Tick(dt, ctx, out intent);
            }
            // Soft assist: ADDED to the player's steering, never a takeover,
            // so the dash (and M2's finisher, which is one) still fires.
            target.SteerAssist = intent.SteerAssist;

            // On the ship's tail it stops gaining: it matches the ship and
            // works on the sideways gap instead of driving through it. A
            // committed run is exempt — forcing the flank is the whole point.
            bool onTail = !shipLeft && gap <= runtimeDef.catchDistance;
            if (onTail && !encounter.Engaged) desired = Mathf.Min(desired, target.CurrentSpeed);
            // Above 1 the run's overdrive is a FLOOR (it must out-drive the
            // ship to reach the flank); below 1 the back-off is a CAP (it must
            // drop behind, whatever the band would otherwise ask for).
            if (intent.SpeedMultiplier > 1f)
                desired = Mathf.Max(desired, target.CurrentSpeed * intent.SpeedMultiplier);
            else if (intent.SpeedMultiplier < 1f)
                desired = Mathf.Min(desired, target.CurrentSpeed * intent.SpeedMultiplier);

            BodyControls controls = driver.Drive(body, target.Body, track, runtimeDef, gap, out float speedCap,
                                                 intent.LineOverride);
            // The sweep's grip limit still wins: an attack run does not get to
            // slide off a flat curve the driver just braked for.
            desired = Mathf.Min(desired, speedCap);

            // The rubber band IS the body's speed model: cruise = the target,
            // thrust and over-cruise bleed = the catch-up accel, so the speed
            // moves toward the target at that rate either way.
            body.Params = new BodyParams
            {
                impulseBlendRate = 1000f,
                cruiseSpeed = desired,
                thrust = runtimeDef.catchUpAccel,
                brakeDecel = runtimeDef.brakeDecel,
                coastDrag = 0f,
                passiveDeceleration = runtimeDef.catchUpAccel,
                lateralSpeed = runtimeDef.lateralSpeed,
                handlingResponse = runtimeDef.handlingResponse,
                gripBase = runtimeDef.gripBase,
                gripPerSpeed = runtimeDef.gripPerSpeed,
                slideThreshold = 4f,
                slideSpeedLoss = 0.1f,
                jumpStrength = 1f,
                wallHitCooldownSeconds = 0.5f,
                pickupReach = PickupReach,
                edgeOverhang = rules != null ? rules.edgeOverhang : 1f,
                edgeGraceSeconds = rules != null ? rules.edgeGraceSeconds : 0.25f,
            };
            body.Step(dt, controls);
            if (body.State == ShipState.OffTrack) return; // it fell: OnLeftTrack has already redeployed it (or the end took it)
            if (shipLeft) return; // nothing left to judge against

            // Never through the ship: the tail is as close as it gets.
            if (SimGap < 1f) body.Distance = target.Body.Distance - 1f;

            // The shove: a sideways slam AWAY from the patrol, sized in metres
            // of travel the way the ship's own dash is. It deals no damage of
            // its own — it feeds the ship's shove channel, so a wall reads it
            // as a slam (hull) and an open edge simply has nothing to catch
            // the ship (hull, plus the fall's lost time).
            if (intent.Shove && encounter.Side != 0) Shove(encounter.Side);

            if (redeployDistance > 0f && SimGap > redeployDistance) Redeploy(raiseFloor: true);

            UpdateCatch(dt);
            if (!HasCaught) WarnIfClose(dt);
        }

        /// <summary>
        /// How far the patrol sits to the side of the ship: ship lateral minus
        /// its own, so POSITIVE means the patrol is to the ship's left. Round a
        /// full tube the two can be whole turns apart, so it is the nearest
        /// equivalent that counts — there is no "far side" of a pipe.
        /// </summary>
        float AcrossToShip()
        {
            float across = target.Body.Lateral - body.Lateral;
            if (track.SectionAt(body.Distance) is TubeSection { Unbounded: true } tube)
                across = Mathf.Repeat(across + tube.Circumference * 0.5f, tube.Circumference) - tube.Circumference * 0.5f;
            return across;
        }

        /// <summary>The ship is on the road and driveable — nothing to attack otherwise.</summary>
        bool ShipSteady => target.State != ShipState.OffTrack
                        && target.State != ShipState.Respawning
                        && target.State != ShipState.Falling;

        // The slam itself. Sized in METRES of sideways travel, converted the
        // way the ship's own dash impulse is (distance x lateral drag), so the
        // number on the asset is the distance it actually moves the ship. It
        // pushes AWAY from the patrol: a patrol on the right (+1) throws the
        // ship left.
        void Shove(int side)
        {
            float drag = target.Definition != null ? target.Definition.handlingResponse : 8f;
            target.AddLateralShove(-side * runtimeDef.shoveMeters * drag);
            HapticsSystem.Instance.Pulse(1f, 0.8f, 0.5f);
        }

        // Tailing the ship inside the catch distance for long enough is an
        // arrest: the punishment for refusing to engage. It is SUSPENDED for
        // the whole of an attack run — a run parks the patrol inside that
        // distance by design, so without this every exchange would arrest the
        // player before it could resolve. The timer is held, not cleared, so
        // a run cannot be used to launder a long tail.
        // With the duel off the old proximity catch comes back with it.
        void UpdateCatch(float dt)
        {
            if (encounter.SuspendsArrest) return;
            if (SimGap > runtimeDef.catchDistance) { tailTimer = 0f; return; }
            tailTimer += dt;

            if (tailTimer >= runtimeDef.sustainedCatchSeconds) { HasCaught = true; return; }
            if (duelEnabled) return;

            if (Mathf.Abs(AcrossToShip()) <= runtimeDef.alongsideLateral) HasCaught = true;
        }

        /// <summary>
        /// A fresh interceptor cuts in just behind the ship — same cruiser,
        /// new number. When the old one was OUTRUN it arrives above the ship's
        /// current speed and that speed becomes the rubber band's new floor,
        /// so coasting is never enough: the player has to find more boosts to
        /// open the gap again. When the old one merely fell off the track the
        /// floor is left alone.
        /// </summary>
        void Redeploy(bool raiseFloor, float gapOverride = 0f)
        {
            float speed = raiseFloor
                ? Mathf.Max(minSpeed, target.CurrentSpeed * redeploySpeedFactor)
                : Mathf.Max(minSpeed, target.CurrentSpeed * runtimeDef.rubberBand);
            if (raiseFloor) minSpeed = speed;
            body.Reset(target.Body.Distance - (gapOverride > 0f ? gapOverride : redeployGap), speed);
            tailTimer = 0f;
            warnCooldown = 0f;
            warned = false;
            PatrolNumber++;
            encounter.Reset(); // a teleport is not an attack run
            ApplyPose(1f);

            HapticsSystem.Instance.Pulse(0.6f, 0.4f, 0.4f);
            Redeployed?.Invoke(PatrolNumber);
        }

        // One warning per approach: it fires when the gap first drops inside
        // the warn distance and re-arms once the ship has opened it again (the
        // cooldown keeps a gap hovering on the line from flapping). The gap
        // itself is on the minimap every frame, so no readout is repeated.
        void WarnIfClose(float dt)
        {
            warnCooldown -= dt;
            float gap = SimGap;
            if (gap > runtimeDef.warnDistance)
            {
                if (warnCooldown <= 0f) warned = false;
                return;
            }
            if (warned) return;
            warned = true;
            warnCooldown = 1.5f;
            Warned?.Invoke(gap);
        }

        void ApplyPose(float alpha)
        {
            if (body == null)
            {
                // Edit-mode preview, or never Init'd: nothing to seat.
                return;
            }

            if (IsGone)
            {
                // Off the end of the track: the world-space fall.
                transform.SetPositionAndRotation(Vector3.Lerp(prevOffPosition, offPosition, alpha), offRotation);
                return;
            }

            float distance = body.DistanceAt(alpha);
            float lateral = body.LateralAt(alpha);
            float height = body.HeightAt(alpha);
            DistanceTravelled = distance;

            Vector3 pos;
            Quaternion rot;
            if (distance >= 0f)
            {
                track.GetPoseAtDistance(distance, lateral, out pos, out rot);
            }
            else
            {
                // Before the start line: extrapolate straight back from it.
                track.GetPoseAtDistance(0f, lateral, out pos, out rot);
                pos += rot * Vector3.back * -distance;
            }
            // The lift is along the track's up, so it survives roll (loops, tubes).
            if (height > 0f) pos += rot * (Vector3.up * height);

            transform.SetPositionAndRotation(pos, rot);

            // Hover bob on the visual child only, same trick as the ship —
            // plus a lean into its steering and the nose following a jump.
            if (visual != null)
            {
                float bob = (Mathf.PerlinNoise(Time.time * 1.3f, 0.53f) - 0.5f) * 0.8f;
                visual.localPosition = new Vector3(0f, 2f + bob, 0f);
                float dt = Time.deltaTime;
                float bank = -Mathf.Clamp(body.BankDemand, -1.25f, 1.25f) * 25f;
                shownBank = dt > 0f ? Mathf.Lerp(shownBank, bank, 1f - Mathf.Exp(-6f * dt)) : bank;
                visual.localRotation = Quaternion.Euler(body.PitchDegrees, 0f, shownBank);
            }
        }

        void Blink(float dt)
        {
            blinkTimer += dt;
            if (blinkTimer < 0.25f) return;
            blinkTimer = 0f;
            blinkState = !blinkState;
            if (redLight != null) redLight.SetActive(blinkState);
            if (blueLight != null) blueLight.SetActive(!blinkState);
        }

        /// <summary>Destroys the built cruiser visual — the runtime build or the baked editor preview.</summary>
        void TearDownVisual()
        {
            var existing = visual != null ? visual : transform.Find("Visual");
            if (existing != null) Kill(existing.gameObject);
            visual = null;
            redLight = null;
            blueLight = null;
        }

        /// <summary>Editor bake: regenerates the cruiser preview from the visual settings so the prefab is visible before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview() => BuildVisual();

        // Cop cruiser built from primitives: dark hull, white cabin, two side
        // skids and an alternating red/blue light bar. Materials and
        // proportions come from the PatrolVisualSettings asset (hardcoded
        // fallbacks keep an unwired patrol working).
        void BuildVisual()
        {
            TearDownVisual();
            var vs = visualSettings;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.one * (vs != null ? vs.overallScale : 1.6f);

            Material bodyMat = vs != null && vs.bodyMaterial != null ? vs.bodyMaterial : MakeMaterial(new Color(0.08f, 0.09f, 0.14f));
            Material trimMat = vs != null && vs.trimMaterial != null ? vs.trimMaterial : MakeMaterial(Color.white);
            Material redMat = vs != null && vs.redLightMaterial != null ? vs.redLightMaterial : MakeMaterial(new Color(1f, 0.1f, 0.1f), emissive: true);
            Material blueMat = vs != null && vs.blueLightMaterial != null ? vs.blueLightMaterial : MakeMaterial(new Color(0.25f, 0.45f, 1f), emissive: true);

            Vector3 hullSize = vs != null ? vs.hullSize : new Vector3(3f, 0.9f, 6f);
            Vector3 cabinPos = vs != null ? vs.cabinPosition : new Vector3(0f, 0.7f, -0.4f);
            Vector3 cabinSize = vs != null ? vs.cabinSize : new Vector3(2f, 0.7f, 2.6f);
            Vector3 skidPos = vs != null ? vs.skidPosition : new Vector3(1.8f, -0.1f, 0f);
            Vector3 skidSize = vs != null ? vs.skidSize : new Vector3(0.6f, 0.5f, 4f);
            Vector3 lightPos = vs != null ? vs.lightPosition : new Vector3(0.55f, 1.35f, -0.4f);
            Vector3 lightScale = Vector3.one * (vs != null ? vs.lightDiameter : 0.7f);

            AddPart(PrimitiveType.Cube, Vector3.zero, hullSize, bodyMat);
            AddPart(PrimitiveType.Cube, cabinPos, cabinSize, trimMat);
            AddPart(PrimitiveType.Cube, new Vector3(-skidPos.x, skidPos.y, skidPos.z), skidSize, bodyMat);
            AddPart(PrimitiveType.Cube, skidPos, skidSize, bodyMat);

            redLight = AddPart(PrimitiveType.Sphere, new Vector3(-lightPos.x, lightPos.y, lightPos.z), lightScale, redMat);
            blueLight = AddPart(PrimitiveType.Sphere, lightPos, lightScale, blueMat);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        GameObject AddPart(PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(type);
            Kill(part.GetComponent<Collider>()); // purely visual
            part.transform.SetParent(visual, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = mat;
            return part;
        }

        static Material MakeMaterial(Color color, bool emissive = false)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 3f);
            }
            return mat;
        }
    }
}
