using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Where the patrol is in an attack run. Explicit states, because the
    /// chase used to imply its mode from four booleans at once and the duel
    /// adds too many to keep doing that. Never serialized — but treated as
    /// append-only anyway, so the milestones that fill in
    /// <see cref="TugOfWar"/> and <see cref="Finisher"/> do not renumber it.
    /// </summary>
    public enum PatrolEncounterState
    {
        /// <summary>The ordinary rubber-band chase: the patrol is not attacking.</summary>
        Cruising,
        /// <summary>Overdriving past the ship's speed to force the flank. The burst IS the telegraph.</summary>
        Committing,
        /// <summary>Holding the flank it picked — level with the ship, not faster — and working up to the shove.</summary>
        Alongside,
        /// <summary>The mash contest.</summary>
        TugOfWar,
        /// <summary>The kill prompt.</summary>
        Finisher,
        /// <summary>Disengaging: no overdrive, no flank, the ordinary driver back in charge.</summary>
        BreakingOff,
        /// <summary>Barred from committing again until it expires.</summary>
        Cooldown,
    }

    /// <summary>Everything the encounter needs to read, gathered once per substep by the patrol.</summary>
    public readonly struct PatrolEncounterContext
    {
        /// <summary>Ship distance minus patrol distance: positive while the patrol trails.</summary>
        public readonly float Gap;
        /// <summary>Ship lateral minus patrol lateral, tube-wrapped — positive when the patrol is LEFT of the ship.</summary>
        public readonly float Across;
        public readonly float ShipLateral;
        /// <summary>The ship's forward speed, m/s: the overdrive's closing rate is relative to it, so reachability scales with it.</summary>
        public readonly float ShipSpeed;
        /// <summary>Where the SHIP is: the ground test reads ahead of it, never behind, because the streamer culls behind.</summary>
        public readonly float ShipDistance;
        public readonly PatrolDefinition Def;
        public readonly TrackManager Track;
        public readonly TrackGenerator Generator;
        /// <summary>The ship is on the road and driveable — not off it, falling or waiting to respawn.</summary>
        public readonly bool ShipSteady;
        /// <summary>Real seconds this substep took — the bar is integrated against these, never the slowed clock.</summary>
        public readonly float UnscaledDt;
        /// <summary>Share of full steering authority the assist may use (GameSettings.duelAssistStrength).</summary>
        public readonly float AssistStrength;
        /// <summary>Mash presses counted since the last substep.</summary>
        public readonly int Presses;
        /// <summary>How long the kill prompt stays open (GameSettings.finisherWindowSeconds).</summary>
        public readonly float FinisherWindowSeconds;
        /// <summary>What the cruiser's remaining damage pool leaves of its push: 1 at full, less for every rear ram it has taken.</summary>
        public readonly float PushScale;

        public PatrolEncounterContext(float gap, float across, float shipLateral, float shipDistance,
                                      PatrolDefinition def, TrackManager track, TrackGenerator generator,
                                      bool shipSteady, float unscaledDt, float assistStrength, int presses,
                                      float finisherWindowSeconds, float pushScale, float shipSpeed)
        {
            ShipSpeed = shipSpeed;
            PushScale = pushScale;
            UnscaledDt = unscaledDt;
            AssistStrength = assistStrength;
            Presses = presses;
            FinisherWindowSeconds = finisherWindowSeconds;
            Gap = gap;
            Across = across;
            ShipLateral = shipLateral;
            ShipDistance = shipDistance;
            Def = def;
            Track = track;
            Generator = generator;
            ShipSteady = shipSteady;
        }
    }

    /// <summary>What the encounter asks of the patrol this substep.</summary>
    public struct PatrolEncounterIntent
    {
        /// <summary>
        /// The ship's speed, times this, as the rubber band's target. Above 1
        /// it is a FLOOR (the attack run's overdrive); below 1 it is a CAP
        /// (backing off after one); exactly 1 leaves the band alone. With
        /// <see cref="SpeedIsAbsolute"/> it is none of those — it is the
        /// target outright.
        /// </summary>
        public float SpeedMultiplier;
        /// <summary>
        /// The run OWNS the speed: the target is the ship's speed times the
        /// multiplier, floor and cap both ignored. Station keeping needs this —
        /// as a floor it could only ever speed the cruiser up, and the rubber
        /// band's floor (raised above the ship's speed on every redeploy) would
        /// drive it straight past the flank it is trying to hold.
        /// </summary>
        public bool SpeedIsAbsolute;
        /// <summary>
        /// The encounter is actively managing the speed, so it gets the run's
        /// own acceleration rate (<see cref="PatrolDefinition.stationAccel"/>)
        /// instead of the deliberately sluggish cruise band. Separate from
        /// <see cref="SpeedIsAbsolute"/> because the two are different
        /// questions: the attack run wants the RATE without the cap, since the
        /// redeploy floor above it is what closes the gap.
        /// </summary>
        public bool HighAuthority;
        /// <summary>The lateral the driver should steer for instead of the ship's, or null for the ordinary line.</summary>
        public float? LineOverride;
        /// <summary>Set for exactly one substep: slam the ship sideways now.</summary>
        public bool Shove;
        /// <summary>Steering to ADD to the player's own, -1..1. 0 = hands off.</summary>
        public float SteerAssist;
    }

    /// <summary>
    /// The patrol's attack run, as a state machine the patrol owns and ticks.
    /// Plain C# and off the Unity object, the same split
    /// <see cref="PatrolDriver"/> already makes for the steering brain — the
    /// difference being that this one carries state between ticks.
    ///
    /// The rhythm is approach → exchange → gap, on a cadence
    /// (<see cref="PatrolDefinition.commitIntervalSeconds"/>) rather than as
    /// an emergent property of the chase: it waits out the interval, checks
    /// the road ahead is ground worth duelling on, then OVERDRIVES past the
    /// ship's speed to force its way onto a flank — and once it is there it
    /// HOLDS STATION, steering its speed at level with the ship rather than
    /// staying faster than it, because a cruiser that keeps overdriving simply
    /// drives past the flank it just won. It picks the side that
    /// leaves the ship between it and an open edge, so the shove throws the
    /// ship off the road; on a walled stretch the same shove pins it into the
    /// wall instead. Either way the damage is the road's, not the patrol's —
    /// the shove itself deals none.
    ///
    /// Breaking the geometry by any means ends it with nothing gained: brake
    /// behind it, out-steer the flank, or simply reach a ramp — the run aborts
    /// and goes on cooldown. The arrest timer is suspended for the whole
    /// engagement, because the run parks the patrol inside the catch distance
    /// by design and would otherwise arrest the player every single time.
    /// </summary>
    public sealed class PatrolEncounter
    {
        public PatrolEncounterState State { get; private set; } = PatrolEncounterState.Cruising;

        /// <summary>Which side of the ship the run is being made from: -1 left, +1 right, 0 when idle.</summary>
        public int Side { get; private set; }

        /// <summary>
        /// The contest, as the PATROL's progress: 0.5 at the start, 1 when it
        /// has won and shoves, 0 when the player has. Only meaningful while
        /// <see cref="InTugOfWar"/>.
        /// </summary>
        public float Tug => tug;

        /// <summary>True while the bar is up and being fought over.</summary>
        public bool InTugOfWar => State == PatrolEncounterState.TugOfWar;

        /// <summary>True while the exchange owns the world clock and the ship's steering.</summary>
        public bool InExchange => State is PatrolEncounterState.TugOfWar or PatrolEncounterState.Finisher;

        /// <summary>The last answer the forbidden-ground test gave — for the debug readout.</summary>
        public bool GroundWasClear => groundClear;

        /// <summary>
        /// The furthest gap the last tick judged a run FINISHABLE from, for the
        /// debug readout. Worth showing: it is speed-dependent, and a gap
        /// sitting just outside it looks identical to a patrol that has simply
        /// decided not to attack.
        /// </summary>
        public float ReachWas { get; private set; }

        /// <summary>Seconds still to wait on the cadence before another run may start. For the debug readout.</summary>
        public float CommitIn(PatrolDefinition def) =>
            def == null ? 0f : Mathf.Max(0f, def.commitIntervalSeconds - commitTimer);

        float stateTimer;      // seconds in the current state
        float commitTimer;     // seconds since the last run ended — the cadence
        float outsideTimer;    // seconds the ship has held outside the flank
        float commitGap;       // the gap the current run started from, to tell escaping from jitter

        // The road test sweeps a few hundred metres, and it is asked every
        // substep. Its answer cannot change until the ship has moved, so it is
        // cached and re-read once per stride — the same stride the sweep
        // samples at, so nothing is ever missed.
        const float GroundRetestMeters = 25f;
        float groundTestedAt = float.NegativeInfinity;
        float groundTestedFor = -1f;
        bool groundClear;

        // How far the PATROL has driven the contest, 0 (the player has won) to
        // 1 (the patrol has). Kept in the patrol's own terms rather than
        // mirrored per side, so the model never has to know which way is which
        // — the HUD does the mirroring, because the mirroring is a presentation
        // question. It starts every contest at dead centre.
        float tug;

        /// <summary>True from the first overdrive to the last frame of the break-off: the run owns the patrol.</summary>
        public bool Engaged => State is PatrolEncounterState.Committing
                                     or PatrolEncounterState.Alongside
                                     or PatrolEncounterState.TugOfWar
                                     or PatrolEncounterState.Finisher
                                     or PatrolEncounterState.BreakingOff;

        /// <summary>
        /// The tail-time arrest does not run for the whole duel cycle —
        /// through the run AND the back-off after it. Two reasons: a run holds
        /// the patrol inside the catch distance on purpose, so without this
        /// every exchange would arrest the player before it could resolve; and
        /// the patrol leaves the exchange with the ship's speed still on it, so
        /// it takes a few seconds to open the gap again even while backing off.
        /// The arrest is for a patrol tailing you OUTSIDE all of that.
        /// </summary>
        public bool SuspendsArrest => Engaged || State == PatrolEncounterState.Cooldown;

        /// <summary>
        /// Back to an ordinary chase with the cadence restarted. Every path
        /// that moves the patrol or the ship out from under a run calls this:
        /// launch, redeploy, a hold, a fall, and the end of the track.
        /// </summary>
        public void Reset()
        {
            State = PatrolEncounterState.Cruising;
            Side = 0;
            stateTimer = 0f;
            commitTimer = 0f;
            outsideTimer = 0f;
            groundTestedAt = float.NegativeInfinity;
            groundTestedFor = -1f;
        }

        /// <summary>
        /// The contest takes the dash away — it is not an escape from the bar —
        /// and the finisher hands it straight back, because the finisher IS a
        /// dash. Nothing else in the cycle touches it.
        /// </summary>
        public bool LocksDash => State == PatrolEncounterState.TugOfWar;

        /// <summary>
        /// The ship just dashed, in the given direction (-1 left, +1 right).
        /// Returns true when that was the kill: the finisher was open and the
        /// dash went INTO the patrol. A dash the wrong way is an ordinary dash
        /// with ordinary consequences, and it ends the window either way —
        /// there is one swing, not a retry.
        /// </summary>
        public bool ReportDash(int direction)
        {
            if (State != PatrolEncounterState.Finisher || direction == 0) return false;
            bool killed = direction == Side;
            // Either way the window is spent. On a kill the recycle resets the
            // whole encounter a moment later anyway; this just makes sure a
            // miss cannot leave the prompt hanging.
            Enter(PatrolEncounterState.BreakingOff);
            return killed;
        }

        /// <summary>Ends a run in progress without a shove; the cooldown still applies.</summary>
        public void Abort()
        {
            if (Engaged) Enter(PatrolEncounterState.BreakingOff);
        }

        /// <summary>
        /// One substep. Returns what the patrol should do about it; the patrol
        /// stays the only thing that touches the body, the ship or the track.
        /// </summary>
        public void Tick(float dt, in PatrolEncounterContext ctx, out PatrolEncounterIntent intent)
        {
            intent = new PatrolEncounterIntent { SpeedMultiplier = 1f };

            PatrolDefinition def = ctx.Def;
            if (def == null) { Reset(); return; }

            stateTimer += dt;
            // The cadence runs everywhere, so the cooldown and the interval
            // overlap rather than stacking: "12 s between runs" means 12 s,
            // not 12 s on top of the 8 s break.
            commitTimer += dt;

            // Standoff: a patrol that is not attacking HOLDS ITS DISTANCE.
            // This has to be a position controller, not a wish. It used to be
            // one flat "0.85x the ship" target that the cruise band could only
            // reach at its own 3.33 m/s²: from a 1.25x redeploy that is a
            // fourteen-second deceleration, and the standoff is barely a second
            // and a half wide at those closing speeds — so the cruiser sailed
            // straight past the ship still doing +24 m/s, every single time.
            // The old absolute distance clamp hid it by pinning the cruiser a
            // metre off the nose; without one it was the chase quietly DYING,
            // because Cruising can only commit to a run from BEHIND (Gap > 0),
            // so a cruiser that gets in front never attacks again.
            //
            // So: proportional (full speed at the standoff, the back-off factor
            // on the tail and beyond it), ABSOLUTE so the redeploy floor cannot
            // overrule it, and — the part that actually matters — flagged for
            // the patrol to give it real deceleration authority.
            if (!Engaged && ctx.Gap < def.standoffDistance)
            {
                intent.SpeedMultiplier = Standoff(ctx);
                intent.SpeedIsAbsolute = true;
                intent.HighAuthority = true;
            }

            switch (State)
            {
                case PatrolEncounterState.Cruising:
                    ReachWas = CommitReach(ctx);
                    if (commitTimer >= def.commitIntervalSeconds
                        && ctx.Gap > 0f && ctx.Gap <= ReachWas
                        && ctx.ShipSteady
                        && ClearToStart(ctx))
                    {
                        Side = PickSide(ctx);
                        commitGap = ctx.Gap;
                        Enter(PatrolEncounterState.Committing);
                    }
                    break;

                case PatrolEncounterState.Committing:
                    // Overdrive until it draws level. It gives up if the ship
                    // pulls clear again, if the road stops allowing it, or if
                    // the burst simply never lands.
                    if (!ctx.ShipSteady || !ClearToContinue(ctx)
                        || ctx.Gap > Escaped(ctx)
                        || stateTimer >= def.commitTimeoutSeconds)
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }
                    // A FLOOR, never an absolute target. This was briefly
                    // absolute "so the telegraph reads the same every time" and
                    // it broke the approach outright: a redeployed cruiser is
                    // already running ABOVE the ship on its raised floor, which
                    // per the chase's design is the thing that closes the gap —
                    // capping it at 1.15x the ship made committing a SLOWDOWN,
                    // so runs timed out having closed nothing and the break-off
                    // then threw the gap wider than it started. Measured: it
                    // committed at 439 m and was back at 692 m, three times
                    // running, and never once reached the flank.
                    intent.SpeedMultiplier = def.attackRunOverdrive;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side);
                    if (ctx.Gap <= def.alongsideDistance && Mathf.Abs(ctx.Across) <= def.alongsideLateral)
                        Enter(PatrolEncounterState.Alongside);
                    break;

                case PatrolEncounterState.Alongside:
                    if (!ctx.ShipSteady || !ClearToContinue(ctx) || ShipGotPast(ctx))
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }
                    // Out-steering the flank for long enough shakes it off:
                    // the ship's lateral speed beats the patrol's, so this is
                    // a real escape and not a formality.
                    outsideTimer = Mathf.Abs(ctx.Across) > def.alongsideLateral ? outsideTimer + dt : 0f;
                    if (outsideTimer >= def.abortGraceSeconds)
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }
                    // Level with the ship, not faster than it — the wind-up is
                    // a cruiser pacing you, which is what makes the shove read.
                    intent.SpeedMultiplier = StationSpeed(ctx);
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side);
                    // The flank hold is the wind-up; then the contest opens.
                    if (stateTimer >= def.alongsideHoldSeconds)
                    {
                        tug = 0.5f;
                        DuelMashInput.Clear(); // presses made before the bar existed do not count
                        Enter(PatrolEncounterState.TugOfWar);
                    }
                    break;

                case PatrolEncounterState.TugOfWar:
                    // Breaking the geometry still ends it, and still for free:
                    // the bar is the contest, but the road is the stake, and
                    // getting off the flank is a legitimate answer to both.
                    if (!ctx.ShipSteady || !ClearToContinue(ctx) || ShipGotPast(ctx))
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }
                    outsideTimer = Mathf.Abs(ctx.Across) > def.alongsideLateral ? outsideTimer + dt : 0f;
                    if (outsideTimer >= def.abortGraceSeconds)
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }

                    intent.SpeedMultiplier = StationSpeed(ctx);
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side);
                    intent.SteerAssist = Assist(ctx);

                    // REAL seconds, both sides of it. The world is at 30% for
                    // the look of the thing; if the bar rode the same clock the
                    // slow-mo would hand the player three times as long to
                    // mash, which is a mechanical refund, not perception.
                    // The push is what the damage pool has left of it: this is
                    // the ONLY thing rear ramming buys, and it is bought before
                    // the contest, not during it.
                    tug += def.tugPatrolForce * Mathf.Max(MinPushScale, ctx.PushScale) * ctx.UnscaledDt;
                    if (ctx.Presses > 0) tug -= def.tugPressValue * ctx.Presses;
                    tug = Mathf.Clamp01(tug);

                    if (tug >= 1f)
                    {
                        // The patrol wins: the bar bottoming out is a shove on
                        // the REAL ship, in the direction it was going. The bar
                        // was never the stake by itself.
                        intent.Shove = true;
                        Enter(PatrolEncounterState.BreakingOff);
                    }
                    else if (tug <= 0f)
                    {
                        // The player wins: the kill window opens. Slow-mo and
                        // assist carry straight through it (D33) so contest,
                        // prompt and kill read as one authored beat rather
                        // than three events.
                        Enter(PatrolEncounterState.Finisher);
                    }
                    break;

                case PatrolEncounterState.Finisher:
                    // Missable, and missing it costs nothing: you won the
                    // contest, you keep your hull, you just do not get the
                    // kill. Only the clock ends it — the geometry checks are
                    // gone on purpose, because a window this short should not
                    // be snatched away by a ramp coming into view.
                    intent.SpeedMultiplier = StationSpeed(ctx);
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side);
                    intent.SteerAssist = Assist(ctx);
                    if (stateTimer >= ctx.FinisherWindowSeconds)
                        Enter(PatrolEncounterState.BreakingOff);
                    break;

                // Backing off is ACTIVE: the patrol drives below the ship's
                // speed until the next run. Left to the ordinary band it would
                // sit on the bumper for ten seconds bleeding off its overdrive,
                // which is the permanent tailgating the duel exists to replace.
                case PatrolEncounterState.BreakingOff:
                    // Absolute, like the standoff and for the same reason: the
                    // cruiser leaves an exchange with the ship's speed on it,
                    // and a cap it cannot decelerate to is not a back-off.
                    intent.SpeedMultiplier = def.breakOffSpeedFactor;
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    if (stateTimer >= def.breakOffSeconds) Enter(PatrolEncounterState.Cooldown);
                    break;

                case PatrolEncounterState.Cooldown:
                    // Once it is a standoff behind the ship the standoff itself
                    // takes over — this only has to get it back there.
                    intent.SpeedMultiplier = ctx.Gap < def.standoffDistance
                        ? Mathf.Min(def.breakOffSpeedFactor, Standoff(ctx))
                        : def.breakOffSpeedFactor;
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    if (stateTimer >= def.attackRunCooldownSeconds) Enter(PatrolEncounterState.Cruising);
                    break;
            }
        }

        void Enter(PatrolEncounterState next)
        {
            State = next;
            stateTimer = 0f;
            outsideTimer = 0f;
            if (next is PatrolEncounterState.Cruising or PatrolEncounterState.Cooldown) Side = 0;
            // The cadence counts from the END of a run, not the start of one,
            // so a long exchange does not immediately earn another.
            if (next == PatrolEncounterState.BreakingOff) commitTimer = 0f;
        }

        // The road the exchange would play out on: ahead of the SHIP, because
        // that is where it ends up, and because the streamer culls the ground
        // behind it that a patrol-relative window would have to read.
        bool GroundClear(in PatrolEncounterContext ctx, float lookahead)
        {
            // Cached per stride AND per window, because the two callers ask
            // about different distances and a shared cache would answer one
            // with the other's result.
            if (Mathf.Abs(ctx.ShipDistance - groundTestedAt) < GroundRetestMeters
                && Mathf.Approximately(groundTestedFor, lookahead))
                return groundClear;
            groundTestedAt = ctx.ShipDistance;
            groundTestedFor = lookahead;
            groundClear = TrackHazards.IsEncounterGroundClear(ctx.Track, ctx.Generator,
                                                              ctx.ShipDistance,
                                                              ctx.ShipDistance + lookahead);
            return groundClear;
        }

        /// <summary>
        /// Enough clean road to be worth STARTING on. The full lookahead.
        /// </summary>
        bool ClearToStart(in PatrolEncounterContext ctx) => GroundClear(ctx, ctx.Def.encounterLookaheadMeters);

        /// <summary>
        /// Whether a run already under way has to let go. This is a much
        /// SHORTER window than the one that starts a run, and that difference
        /// is the whole point: judging an in-progress exchange by the starting
        /// distance means any feature that drifts into that range kills a run
        /// that only just began, which on a busy track is every run. What
        /// matters here is what the ship is about to actually reach.
        /// </summary>
        bool ClearToContinue(in PatrolEncounterContext ctx) =>
            GroundClear(ctx, Mathf.Min(ctx.Def.encounterAbortMeters, ctx.Def.encounterLookaheadMeters));

        // How much of the overdrive's burst is allowed to be spent purely on
        // closing, leaving the rest for drawing level and the flank hold.
        const float CommitReachSafety = 0.7f;

        // How long the ship has to out-run the overdrive before the run gives
        // up on it. Seconds rather than metres, because the overdrive's closing
        // rate scales with speed and so must the distance that counts as away.
        const float CommitEscapeSeconds = 2f;

        /// <summary>
        /// The gap at which a run in progress accepts the ship has got AWAY:
        /// the gap it started from plus a couple of seconds of the overdrive's
        /// own closing rate. It must never be the same number the run STARTED
        /// at (this was <c>commitFromDistance</c>, for both): a run entered
        /// exactly on its own abort line dies to a metre of jitter, which is
        /// precisely what happened — measured committing at 121.0 m and
        /// breaking off at 120.5 m with the road completely clear, every time.
        /// </summary>
        float Escaped(in PatrolEncounterContext ctx)
        {
            float closing = Mathf.Max(1f, ctx.ShipSpeed * (ctx.Def.attackRunOverdrive - 1f));
            return Mathf.Max(ctx.Def.commitFromDistance, commitGap + closing * CommitEscapeSeconds);
        }

        /// <summary>
        /// The furthest gap a run could actually be FINISHED from. The overdrive
        /// closes <c>(attackRunOverdrive - 1) x shipSpeed</c> per second, so the
        /// reachable gap scales with the ship's speed — which means
        /// <see cref="PatrolDefinition.commitFromDistance"/>, being a fixed
        /// number of METRES, cannot be right across this game's speed range. At
        /// 150 m/s the burst closes 22 m/s, so its authored 450 m needs twenty
        /// seconds of a fifteen-second timeout; at Light Speed the same 450 m
        /// takes under a second. Measured before this existed: the patrol
        /// committed at 400 m, closed honestly to 125 m, and timed out there
        /// every time without ever reaching the flank. The authored distance
        /// stays as the outer permission; this is the reality check under it.
        /// </summary>
        static float CommitReach(in PatrolEncounterContext ctx)
        {
            PatrolDefinition def = ctx.Def;
            float closing = Mathf.Max(1f, ctx.ShipSpeed * (def.attackRunOverdrive - 1f));
            return Mathf.Min(def.commitFromDistance,
                             closing * def.commitTimeoutSeconds * CommitReachSafety);
        }

        /// <summary>
        /// The ship has got fully past it — D23's cheap escape, earned by
        /// braking. A metre of jitter is NOT that: station keeping oscillates
        /// around level by design, so the test is a whole alongside window
        /// ahead rather than merely a negative gap. Judging it on the sign of
        /// the gap aborted every single run the instant the cruiser drew level.
        /// </summary>
        static bool ShipGotPast(in PatrolEncounterContext ctx) =>
            ctx.Gap < -Mathf.Max(ctx.Def.alongsideDistance, 1f);

        // Station keeping: the speed that holds the cruiser LEVEL with the
        // ship. A run used to ask for `attackRunOverdrive` the whole way
        // through, which is a permanent "faster than the ship" — it only ever
        // looked like holding a flank because a positional clamp pinned the
        // cruiser a metre off the ship's nose. Without that crutch a blanket
        // overdrive simply drives past and the run aborts, so the run steers
        // its speed AT the station instead: proportional to how far off level
        // it is, the overdrive as the ceiling, a matching back-off as the
        // floor. The station is level (gap 0), so the error IS the gap.
        const float StationBackOff = 0.12f;

        /// <summary>
        /// Holding the standoff: the ship's own speed out at
        /// <see cref="PatrolDefinition.standoffDistance"/>, falling to
        /// <see cref="PatrolDefinition.breakOffSpeedFactor"/> on the tail and
        /// staying there for a cruiser that has slipped in FRONT of the ship —
        /// which is how one recovers from having overshot. Never above 1: the
        /// standoff only ever says "no closer", and what closes the gap is the
        /// rubber band outside it and the attack run's overdrive inside it.
        /// </summary>
        static float Standoff(in PatrolEncounterContext ctx)
        {
            float t = Mathf.Clamp01(ctx.Gap / Mathf.Max(ctx.Def.standoffDistance, 1f));
            return Mathf.Lerp(ctx.Def.breakOffSpeedFactor, 1f, t);
        }

        static float StationSpeed(in PatrolEncounterContext ctx)
        {
            float window = Mathf.Max(ctx.Def.alongsideDistance, 1f);
            float t = Mathf.Clamp(ctx.Gap / window, -1f, 1f); // + behind station, - ahead of it
            return t >= 0f
                ? 1f + t * Mathf.Max(0f, ctx.Def.attackRunOverdrive - 1f)
                : 1f + t * StationBackOff;
        }

        // A weakened cruiser pushes weaker. Floored well above zero on
        // purpose: an emptied pool must leave a contest that is trivial, not
        // one that resolves itself while the player watches — the mash still
        // has to be the thing that wins it.
        const float MinPushScale = 0.15f;

        /// <summary>
        /// The soft assist (added to the player's steering, never a takeover).
        /// It wants the middle of the road, which is also the answer to the
        /// open edge the patrol deliberately parked you next to — so one pull
        /// covers both jobs. Scaled by the strength on GameSettings; at full
        /// authority it still loses to a player steering the other way,
        /// because it is one term in a sum and theirs is the other.
        /// </summary>
        static float Assist(in PatrolEncounterContext ctx)
        {
            if (ctx.Track == null || ctx.AssistStrength <= 0f) return 0f;
            ctx.Track.GetLateralBand(ctx.ShipDistance, out float min, out float max);
            float centre = (min + max) * 0.5f;
            float half = Mathf.Max((max - min) * 0.5f, 1f);
            // Proportional to how far out the ship is, so the middle of the
            // road is left alone and the edge is pulled at hardest.
            return Mathf.Clamp((centre - ctx.ShipLateral) / half, -1f, 1f) * ctx.AssistStrength;
        }

        // The flank it drives for: beside the ship, not behind it. Lateral is
        // right-positive, so the right flank (+1) is the higher lateral.
        static float FlankLine(in PatrolEncounterContext ctx, int side) =>
            ctx.ShipLateral + side * ctx.Def.flankOffsetMeters;

        /// <summary>
        /// Which side to attack from. The shove is only as good as what it
        /// pushes the ship into, so the patrol takes the side that leaves an
        /// open edge on the far side of the ship. With edges on both sides or
        /// neither it keeps the side it is already on, and on a dead heat it
        /// takes the side with more road to work in.
        /// </summary>
        static int PickSide(in PatrolEncounterContext ctx)
        {
            TrackManager track = ctx.Track;
            if (track != null)
            {
                float probe = ctx.ShipDistance + ctx.Def.alongsideDistance;
                bool leftOpen = track.IsEdgeOpen(probe, -1);
                bool rightOpen = track.IsEdgeOpen(probe, 1);
                if (leftOpen != rightOpen) return leftOpen ? 1 : -1; // sit opposite the drop
            }

            // Across is ship-minus-patrol: positive means the patrol is to the
            // ship's LEFT (the lower lateral), so it keeps that side.
            if (Mathf.Abs(ctx.Across) > 0.5f) return ctx.Across > 0f ? -1 : 1;

            if (track != null)
            {
                track.GetLateralBand(ctx.ShipDistance, out float min, out float max);
                return ctx.ShipLateral - min >= max - ctx.ShipLateral ? -1 : 1;
            }
            return 1;
        }
    }
}
