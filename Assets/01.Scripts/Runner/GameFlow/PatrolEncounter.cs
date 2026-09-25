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
        /// <summary>The player braked with the cruiser in its standoff: it swerves to a flank and HOLDS its speed past the ship before dropping back. An ordinary chase beat, not an attack run.</summary>
        Overshooting,
        /// <summary>The player missed the kill prompt: the cruiser brakes hard and visibly falls away before the ordinary cooldown.</summary>
        MissBraking,
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
        /// <summary>The cruiser's own forward speed, m/s. Without it the approach cannot know how much speed it has to shed, and overshoots.</summary>
        public readonly float PatrolSpeed;
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
        /// <summary>The ship is carrying a strong orb's kill: the exchange skips the contest and opens the prompt (D19).</summary>
        public readonly bool ShipArmed;
        /// <summary>
        /// What the escalation tier multiplies by (1 on the first cruiser of a
        /// run). The attack interval is DIVIDED by it and the push MULTIPLIED,
        /// so one number makes a long run harder in both of D22's ways.
        /// </summary>
        public readonly float TierScale;
        /// <summary>The player's brake input, 0..1. With the cruiser in its standoff, braking is what makes it overshoot.</summary>
        public readonly float ShipBrake;
        /// <summary>The ship's forward acceleration this tick, m/s² — negative when slowing. A brake pad slows the ship with no brake input, and must overshoot the cruiser the same way.</summary>
        public readonly float ShipAccel;

        public PatrolEncounterContext(float gap, float across, float shipLateral, float shipDistance,
                                      PatrolDefinition def, TrackManager track, TrackGenerator generator,
                                      bool shipSteady, float unscaledDt, float assistStrength, int presses,
                                      float finisherWindowSeconds, float pushScale, float shipSpeed,
                                      float patrolSpeed, bool shipArmed, float tierScale,
                                      float shipBrake, float shipAccel)
        {
            ShipBrake = shipBrake;
            ShipAccel = shipAccel;
            TierScale = tierScale <= 0f ? 1f : tierScale;
            ShipArmed = shipArmed;
            ShipSpeed = shipSpeed;
            PatrolSpeed = patrolSpeed;
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
        /// <summary>
        /// The target speed OUTRIGHT, in m/s — not a multiple of the ship's,
        /// not floored, not capped. The overshoot holds the speed the cruiser
        /// had when the player braked (a multiple of the ship's would follow
        /// the ship down, which is the exact thing that stopped it passing);
        /// the miss brake's floor is one too.
        /// </summary>
        public float? AbsoluteSpeedMps;
        /// <summary>Brake to add to the driver's own, 0..1: the miss brake decelerates at the body's brake rate, not the cruise slew.</summary>
        public float Brake;
        /// <summary>
        /// A FLOOR on the target speed in m/s — the hunt. When a run is due the
        /// cruiser closes on its standoff from wherever it is at the run's own
        /// closing rate, instead of waiting for the conservative cruise band
        /// (which targets BELOW the ship) to never get there.
        /// </summary>
        public float? MinSpeedMps;
        /// <summary>
        /// The control lock: while set the ship flies itself to this guide
        /// lateral and the player's stick, throttle, brake and dash are all
        /// ignored. The tug of war walks it toward the edge the cruiser chose;
        /// the finisher holds it. Null = the player has the ship.
        /// </summary>
        public float? ShipLateralTarget;
        /// <summary>The two hulls are grinding this substep: sparks between them.</summary>
        public bool Contact;
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

        /// <summary>
        /// Set for one tick when an armed ship's window was cashed in for a
        /// finisher. The PATROL clears it, because the window belongs to the
        /// ship and the encounter cannot reach it — one orb buys one kill, not
        /// every exchange inside the three seconds.
        /// </summary>
        public bool SpentArming { get; set; }

        /// <summary>True while the bar is up and being fought over.</summary>
        public bool InTugOfWar => State == PatrolEncounterState.TugOfWar;

        /// <summary>True while the exchange owns the world clock and the ship's controls.</summary>
        public bool InExchange => State is PatrolEncounterState.TugOfWar or PatrolEncounterState.Finisher;

        /// <summary>True while the ship's controls belong to the exchange — the tug of war and the kill prompt, one continuous lock.</summary>
        public bool ControlLocked => InExchange;

        /// <summary>The cruiser is sailing past a braking ship on held speed.</summary>
        public bool Overshooting => State == PatrolEncounterState.Overshooting;

        /// <summary>
        /// How far into a fight the picture should be, 0..1, for the duel
        /// camera: rising as a committed run closes the gap, 1 through the
        /// flank hold, the contest and the kill prompt, 0 for everything else
        /// (the rig eases it home itself).
        /// </summary>
        public float DuelCloseness { get; private set; }

        /// <summary>The last answer the forbidden-ground test gave — for the debug readout.</summary>
        public bool GroundWasClear => groundClear;

        /// <summary>
        /// The furthest gap the last tick judged a run FINISHABLE from, for the
        /// debug readout. Worth showing: it is speed-dependent, and a gap
        /// sitting just outside it looks identical to a patrol that has simply
        /// decided not to attack.
        /// </summary>
        public float ReachWas { get; private set; }

        /// <summary>
        /// Seconds still to wait on the cadence before another run may start, for
        /// the debug readout — against the EFFECTIVE interval, which the
        /// escalation tier shortens, not the authored one.
        /// </summary>
        public float CommitIn(PatrolDefinition def) =>
            def == null ? 0f : Mathf.Max(0f, (intervalWas > 0f ? intervalWas : def.commitIntervalSeconds) - commitTimer);

        float stateTimer;      // seconds in the current state
        float commitTimer;     // seconds since the last run ended — the cadence
        float outsideTimer;    // seconds the ship has held outside the flank
        float commitGap;       // the gap the current run started from, to tell escaping from jitter
        float intervalWas;     // the effective (tier-shortened) commit interval, for the readout

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

        float overshootSpeed;    // m/s the cruiser had when the player braked — what it holds while sailing past
        int laneClearSide;       // the flank a cruiser AHEAD of the ship keeps to while it drops back behind (0 = none)
        float pushStartLateral;  // the ship's lateral when the contest opened — what the push is measured from
        float finisherLateral;   // where the push left the ship — held there through the kill prompt
        float finisherTimer;     // REAL seconds the prompt has been open (the world is at 30 %, the window is not)

        // Room the push leaves between the ship and the lane's edge at a full
        // bar. Over an open edge the fall clock starts past the edge; over a
        // wall it is the wall. Either way the bar must not finish the job the
        // SHOVE exists for.
        const float PushEdgeClearance = 2.5f;

        /// <summary>
        /// True from the first overdrive to the last frame of the wind-down:
        /// the run owns the patrol. The miss brake is a wind-down like the
        /// break-off (the arrest stays suspended, the story queue held); the
        /// overshoot is NOT engaged — it is an ordinary chase beat.
        /// </summary>
        public bool Engaged => State is PatrolEncounterState.Committing
                                     or PatrolEncounterState.Alongside
                                     or PatrolEncounterState.TugOfWar
                                     or PatrolEncounterState.Finisher
                                     or PatrolEncounterState.BreakingOff
                                     or PatrolEncounterState.MissBraking;

        /// <summary>
        /// The tail-time arrest does not run for the whole duel cycle —
        /// through the run AND the back-off after it. Two reasons: a run holds
        /// the patrol inside the catch distance on purpose, so without this
        /// every exchange would arrest the player before it could resolve; and
        /// the patrol leaves the exchange with the ship's speed still on it, so
        /// it takes a few seconds to open the gap again even while backing off.
        /// The arrest is for a patrol tailing you OUTSIDE all of that.
        /// </summary>
        public bool SuspendsArrest => Engaged
                                      || State is PatrolEncounterState.Cooldown or PatrolEncounterState.Overshooting; // a cruiser sailing past is not tailing

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
            tug = 0.5f;
            overshootSpeed = 0f;
            laneClearSide = 0;
            pushStartLateral = 0f;
            finisherLateral = 0f;
            finisherTimer = 0f;
            DuelCloseness = 0f;
            groundTestedAt = float.NegativeInfinity;
            groundTestedFor = -1f;
        }

        /// <summary>
        /// The exchange takes the dash away for its whole length — the contest
        /// AND the kill prompt, since the kill is a press, not a dash. The
        /// control lock already swallows it; this is the named gate on top.
        /// </summary>
        public bool LocksDash => InExchange;

        /// <summary>
        /// The player pressed a dash shoulder (-1 left, +1 right) while the
        /// kill prompt was open. Returns true when it was the kill — the
        /// shoulder on the cruiser's side. The WRONG shoulder is a miss, and a
        /// miss sends the cruiser into its hard brake: one swing, no retry.
        /// </summary>
        public bool ReportFinisherPress(int direction)
        {
            if (State != PatrolEncounterState.Finisher || direction == 0) return false;
            bool killed = direction == Side;
            // On a kill the recycle resets the whole encounter a moment later
            // anyway; a miss goes into the brake so the prompt never hangs.
            Enter(killed ? PatrolEncounterState.BreakingOff : PatrolEncounterState.MissBraking);
            return killed;
        }

        /// <summary>Ends a run in progress without a shove; the cooldown still applies. An overshoot in progress drops straight back.</summary>
        public void Abort()
        {
            if (Engaged) Enter(PatrolEncounterState.BreakingOff);
            else if (State == PatrolEncounterState.Overshooting) Enter(PatrolEncounterState.Cooldown);
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
            // The trigger has to allow for BRAKING DISTANCE, not just the
            // standoff: a cruiser closing at +150 m/s needs ~190 m to shed it,
            // and 45 m of standoff cannot absorb that however hard it brakes.
            // An overshoot in progress owns the speed outright: the standoff
            // would re-match the ship the same substep and the pass never happens.
            if (!Engaged && State != PatrolEncounterState.Overshooting
                && ctx.Gap - def.standoffDistance <= BrakingDistance(ctx))
            {
                intent.SpeedMultiplier = ApproachSpeed(ctx, def.standoffDistance);
                intent.SpeedIsAbsolute = true;
                intent.HighAuthority = true;
            }

            // The overshoot: with the cruiser sitting in its standoff, the
            // player BRAKING is answered by the cruiser swerving to a flank and
            // holding the speed it had — it sails past, and for a couple of
            // seconds it is ahead of the ship, an obstacle and a ram target.
            // Without this the absolute standoff clamp follows the ship's speed
            // down within a substep and the cruiser just brakes with it.
            // A cruiser AHEAD of (or level with) the ship that wants to be
            // behind it keeps to a flank until it is. Left to the driver it
            // steers for the ship's own lateral — INTO the ship's lane — and
            // the lane clamp then shunts it along a metre off the ship's nose
            // for ever, slower than the ship yet never falling back. Measured:
            // a whole run glued at gap 0. Out on a flank the two are side by
            // side, nothing is clamped, and the ship simply passes it. The side
            // is the one it overshot on, else whichever it is already nearer.
            if (State is PatrolEncounterState.Cruising or PatrolEncounterState.Cooldown)
            {
                if (ctx.Gap < def.alongsideDistance)
                {
                    if (laneClearSide == 0) laneClearSide = ctx.Across > 0f ? -1 : 1;
                    intent.LineOverride = FlankLine(ctx, laneClearSide);
                }
                else laneClearSide = 0;
            }

            if (State is PatrolEncounterState.Cruising or PatrolEncounterState.Cooldown
                && ctx.Gap > 0f && ctx.Gap <= def.standoffDistance + def.overshootTriggerMargin
                && ctx.ShipSteady
                && ctx.PatrolSpeed > ctx.ShipSpeed * 0.9f
                && ((def.overshootBrakeThreshold > 0f && ctx.ShipBrake >= def.overshootBrakeThreshold)
                    || (def.overshootDecelThreshold > 0f && ctx.ShipAccel <= -def.overshootDecelThreshold)))
            {
                overshootSpeed = Mathf.Max(ctx.PatrolSpeed, ctx.ShipSpeed);
                Side = PickSide(ctx);
                laneClearSide = Side; // and it stays on that flank while it drops back afterwards
                Enter(PatrolEncounterState.Overshooting);
            }

            switch (State)
            {
                case PatrolEncounterState.Cruising:
                    ReachWas = CommitReach(ctx);
                    intervalWas = def.commitIntervalSeconds / ctx.TierScale;
                    bool due = commitTimer >= intervalWas;
                    // THE HUNT. The cruise band targets below the ship's speed
                    // on purpose (a boost has to buy breathing room), so on its
                    // own it never brings the cruiser back to the standoff once
                    // the gap has opened — at 40 m/s it sat 170 m back with the
                    // commit gate open for ever, because the run can only start
                    // from inside its reach. So once a run is DUE the cruiser
                    // closes on the standoff from wherever it is, at the run's
                    // own closing rate (a floor, so a boost still out-runs it),
                    // and only then does the standoff's absolute profile take
                    // over and hold it there for the commit.
                    if (due && !intent.SpeedIsAbsolute && ctx.Gap > def.standoffDistance)
                    {
                        float toGo = ctx.Gap - def.standoffDistance;
                        float able = Mathf.Sqrt(2f * Authority(ctx) * ApproachSafety * toGo);
                        intent.MinSpeedMps = ctx.ShipSpeed + Mathf.Min(able, Closing(ctx));
                        intent.HighAuthority = true;
                    }
                    if (due
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
                    // One continuous approach, all the way to the flank: the
                    // overdrive is its speed LIMIT, not a flat target, and it
                    // eases off over the last few metres so the cruiser arrives
                    // level instead of sailing past. The whole run shares the
                    // profile, so there is no handover for an overshoot to hide
                    // in. (The approach from further out is the cruise band's
                    // job — `commitFromDistance` is 70 m for that reason.)
                    intent.SpeedMultiplier = ApproachSpeed(ctx, FlankStationMeters);
                    intent.SpeedIsAbsolute = true;
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
                    intent.SpeedMultiplier = ApproachSpeed(ctx, FlankStationMeters);
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side);
                    // The flank hold is the wind-up; then the contest opens —
                    // unless the ship is ARMED, in which case the contest is
                    // exactly what the orb bought its way out of (D19) and the
                    // kill prompt opens instead. The hold still plays, so the
                    // beat reads the same: the cruiser pulls level, then the
                    // window. Slow-mo and assist come with the Finisher either
                    // way, so nothing about the moment is skipped but the mash.
                    if (stateTimer >= def.alongsideHoldSeconds)
                    {
                        if (ctx.ShipArmed)
                        {
                            SpentArming = true; // the patrol spends the ship's window
                            Enter(PatrolEncounterState.Finisher);
                            break;
                        }
                        tug = 0.5f;
                        pushStartLateral = ctx.ShipLateral; // the push is measured from where the ship was caught
                        DuelMashInput.Clear(); // presses made before the bar existed do not count
                        Enter(PatrolEncounterState.TugOfWar);
                    }
                    break;

                case PatrolEncounterState.TugOfWar:
                    // The ship is LOCKED for the contest — stick, throttle,
                    // brake and dash all taken — so the only aborts left are
                    // the road's: the ground going bad, the ship leaving it, or
                    // the cruiser's own station keeping slipping behind.
                    if (!ctx.ShipSteady || !ClearToContinue(ctx) || ShipGotPast(ctx))
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }

                    intent.SpeedMultiplier = ApproachSpeed(ctx, FlankStationMeters);
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side); // ship-relative: glued to the pushed ship, in contact
                    intent.Contact = Mathf.Abs(ctx.Across) <= def.flankOffsetMeters + 3f;

                    // REAL seconds, both sides of it. The world is at 30% for
                    // the look of the thing; if the bar rode the same clock the
                    // slow-mo would hand the player three times as long to
                    // mash, which is a mechanical refund, not perception.
                    // The push is what the damage pool has left of it: this is
                    // the ONLY thing rear ramming buys, and it is bought before
                    // the contest, not during it.
                    tug += def.tugPatrolForce * PushNow(ctx) * ctx.UnscaledDt;
                    if (ctx.Presses > 0) tug -= def.tugPressValue * ctx.Presses;
                    tug = Mathf.Clamp01(tug);

                    // The bar is the contest AND the ship's position: as it goes
                    // the cruiser's way the locked ship is walked toward the edge
                    // the cruiser chose, across a share of the room it has —
                    // never the whole room, so the bar can never drop the ship by
                    // itself. A full bar leaves it at the brink for the shove.
                    intent.ShipLateralTarget = PushTarget(ctx);

                    if (tug >= 1f)
                    {
                        // The patrol wins: the bar bottoming out is a shove on
                        // the REAL ship, in the direction it was going. The bar
                        // was never the stake by itself. The lock drops with the
                        // state, so the slam lands on a ship the player has back.
                        intent.ShipLateralTarget = null;
                        intent.Shove = true;
                        Enter(PatrolEncounterState.BreakingOff);
                    }
                    else if (tug <= 0f)
                    {
                        // The player wins: the two separate a little and the kill
                        // prompt opens. Slow-mo and the lock carry straight
                        // through (D33) so contest, prompt and kill read as one
                        // authored beat rather than three events.
                        finisherLateral = ctx.ShipLateral;
                        finisherTimer = 0f;
                        Enter(PatrolEncounterState.Finisher);
                    }
                    break;

                case PatrolEncounterState.Finisher:
                    // The cruiser peels out by the separation, the ship is held
                    // where the push left it, and the button decides it. Only
                    // the clock ends it otherwise — the geometry checks are gone
                    // on purpose, because a window this short should not be
                    // snatched away by a ramp coming into view. The window is
                    // REAL seconds: the world is at 30 % and a scaled second here
                    // was three real ones.
                    intent.SpeedMultiplier = ApproachSpeed(ctx, FlankStationMeters);
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    intent.LineOverride = ctx.ShipLateral + Side * (def.flankOffsetMeters + def.finisherSeparationMeters);
                    intent.ShipLateralTarget = finisherLateral;
                    finisherTimer += ctx.UnscaledDt;
                    if (finisherTimer >= ctx.FinisherWindowSeconds)
                        Enter(PatrolEncounterState.MissBraking);
                    break;

                case PatrolEncounterState.Overshooting:
                    // Held speed, the flank line so it clears the lane clamp,
                    // and no reading of the gap: the hold IS the sail-past.
                    // When it is over the cooldown's approach drops the cruiser
                    // back behind the ship into its standoff.
                    intent.AbsoluteSpeedMps = overshootSpeed;
                    intent.HighAuthority = true;
                    intent.LineOverride = FlankLine(ctx, Side);
                    if (stateTimer >= def.overshootHoldSeconds || !ctx.ShipSteady)
                        Enter(PatrolEncounterState.Cooldown);
                    break;

                case PatrolEncounterState.MissBraking:
                    // The player fumbled the kill: the cruiser brakes HARD and
                    // falls visibly away, then cools down like any other
                    // wind-down. The floor is a share of the ship's speed, so
                    // it drops back rather than stopping dead in the lane.
                    {
                        float floor = ctx.ShipSpeed * def.finisherMissBrakeSpeedFactor;
                        intent.AbsoluteSpeedMps = floor;
                        intent.Brake = ctx.PatrolSpeed > floor ? 1f : 0f;
                        intent.HighAuthority = true;
                        if (stateTimer >= def.finisherMissBrakeSeconds) Enter(PatrolEncounterState.Cooldown);
                    }
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
                    intent.SpeedMultiplier = Mathf.Min(def.breakOffSpeedFactor,
                                                       ApproachSpeed(ctx, def.standoffDistance));
                    intent.SpeedIsAbsolute = true;
                    intent.HighAuthority = true;
                    if (stateTimer >= def.attackRunCooldownSeconds) Enter(PatrolEncounterState.Cruising);
                    break;
            }

            DuelCloseness = ClosenessNow(ctx);
        }

        /// <summary>
        /// The duel camera's drive: 0 for the chase, rising as a committed run
        /// closes from where it started to the alongside gap, 1 through the
        /// flank hold, the contest and the prompt, and 0 again the moment the
        /// exchange is over (the rig eases the picture home itself).
        /// </summary>
        float ClosenessNow(in PatrolEncounterContext ctx)
        {
            switch (State)
            {
                case PatrolEncounterState.Committing:
                {
                    float span = Mathf.Max(commitGap - ctx.Def.alongsideDistance, 1f);
                    return 1f - Mathf.Clamp01((ctx.Gap - ctx.Def.alongsideDistance) / span);
                }
                case PatrolEncounterState.Alongside:
                case PatrolEncounterState.TugOfWar:
                case PatrolEncounterState.Finisher:
                    return 1f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Where the locked ship should be this substep: its lateral when the
        /// contest opened, walked AWAY from the cruiser by the bar's excess over
        /// centre across <see cref="PatrolDefinition.tugPushFraction"/> of the
        /// room to that side's lane edge (less a clearance). A bar the player is
        /// winning leaves the ship where it was — the ship is pushed, never pulled.
        /// </summary>
        float PushTarget(in PatrolEncounterContext ctx)
        {
            int away = -Side; // the cruiser on the right (+1) pushes the ship left
            float room = 0f;
            if (ctx.Track != null && away != 0)
            {
                ctx.Track.GetLateralBand(ctx.ShipDistance, out float min, out float max);
                room = away > 0 ? max - pushStartLateral : pushStartLateral - min;
                room = Mathf.Max(0f, room - PushEdgeClearance);
            }
            float drive = Mathf.Max(0f, (tug - 0.5f) * 2f);
            return pushStartLateral + away * room * ctx.Def.tugPushFraction * drive;
        }

        void Enter(PatrolEncounterState next)
        {
            State = next;
            stateTimer = 0f;
            outsideTimer = 0f;
            if (next is PatrolEncounterState.Cruising or PatrolEncounterState.Cooldown)
            {
                Side = 0;
                overshootSpeed = 0f;
                pushStartLateral = 0f;
                finisherLateral = 0f;
                finisherTimer = 0f;
            }
            // The cadence counts from the END of a run, not the start of one,
            // so a long exchange does not immediately earn another. Both
            // wind-downs end a run.
            if (next is PatrolEncounterState.BreakingOff or PatrolEncounterState.MissBraking) commitTimer = 0f;
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
            float closing = Closing(ctx);
            return Mathf.Max(ctx.Def.commitFromDistance, commitGap + closing * CommitEscapeSeconds);
        }

        /// <summary>
        /// The overdrive's closing rate, m/s: a share of the SHIP's speed,
        /// floored by <see cref="PatrolDefinition.minClosingSpeed"/>. The floor
        /// is what lets a run happen at all against a slow or stopped ship —
        /// every speed the cruiser drives is a multiple of the ship's, so
        /// without it the reach shrank to nothing below ~100 km/h and the
        /// cruiser sat in its standoff for ever, never attacking.
        /// </summary>
        static float Closing(in PatrolEncounterContext ctx) =>
            Mathf.Max(1f, ctx.ShipSpeed * (ctx.Def.attackRunOverdrive - 1f), ctx.Def.minClosingSpeed);

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
            return Mathf.Min(def.commitFromDistance,
                             Closing(ctx) * def.commitTimeoutSeconds * CommitReachSafety);
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

        // Where the cruiser sits while it works the flank: a few metres BEHIND
        // the ship, never level and never past it. Level reads fine on screen
        // (the flank offset is what separates them) and this way the profile's
        // own slack cannot put the cruiser in front.
        const float FlankStationMeters = 3f;

        // How much of the available deceleration the approach plans on using.
        // Under 1 because the body's speed only SLEWS toward the command at
        // that same rate, so a profile planned at full authority arrives late.
        const float ApproachSafety = 0.75f;

        static float Authority(in PatrolEncounterContext ctx) =>
            Mathf.Max(1f, ctx.Def.stationAccel > 0f ? ctx.Def.stationAccel : ctx.Def.catchUpAccel);

        /// <summary>
        /// How much road the cruiser needs to shed its CURRENT closing speed.
        /// The standoff's trigger has to allow for this: a cruiser arriving at
        /// +150 m/s needs ~190 m to stop gaining, and 45 m of standoff cannot
        /// absorb that however hard it brakes.
        /// </summary>
        static float BrakingDistance(in PatrolEncounterContext ctx)
        {
            float closing = Mathf.Max(0f, ctx.PatrolSpeed - ctx.ShipSpeed);
            return closing * closing / (2f * Authority(ctx) * ApproachSafety);
        }

        /// <summary>
        /// The speed that closes the gap to <paramref name="station"/> and
        /// ARRIVES THERE, at any speed — the overdrive is its limit, not a flat
        /// target, and it eases off over the last stretch.
        ///
        /// This replaced proportional control on position, which cannot do the
        /// job however it is tuned: it asks for full closing speed right up to
        /// the station and only then starts shedding it, which costs
        /// closing² / 2a metres. That is 4 m at cruise — invisible, and why a
        /// speed-clamped soak passed — but 68 m at 600 m/s and 188 m at Light
        /// Speed, and after a redeploy's 1.25x floor over 500 m. THAT is the
        /// cruiser sailing past the player instead of pacing them.
        ///
        /// Behind the station it closes, ahead of it it drops back, and the ship
        /// braking hard is the one thing that can still put the cruiser in front
        /// — which is exactly D6's overshoot, and the way into a rear ram.
        /// </summary>
        static float ApproachSpeed(in PatrolEncounterContext ctx, float station)
        {
            float toGo = ctx.Gap - station;                 // + still behind it, - past it
            // The fastest approach that can still come to rest in what is left.
            float able = Mathf.Sqrt(2f * Authority(ctx) * ApproachSafety * Mathf.Abs(toGo));
            float cap = Closing(ctx);
            float closing = Mathf.Sign(toGo) * Mathf.Min(able, cap);
            return 1f + closing / Mathf.Max(ctx.ShipSpeed, 1f);
        }

        /// <summary>
        /// What actually multiplies <see cref="PatrolDefinition.tugPatrolForce"/>
        /// this substep: the damage pool the player has knocked out of the
        /// cruiser, times the escalation the run has earned. The escalation half
        /// carries its own, LOWER cap (<see cref="PatrolDefinition.tugForceMaxScale"/>):
        /// D22 lets a long run compress the player's recovery time, but R7.2 is
        /// explicit that it must never make the mash mathematically unwinnable,
        /// so the interval may keep shrinking while the push stops growing.
        /// </summary>
        static float PushNow(in PatrolEncounterContext ctx)
        {
            float tier = Mathf.Min(ctx.TierScale, Mathf.Max(1f, ctx.Def.tugForceMaxScale));
            return Mathf.Max(MinPushScale, ctx.PushScale) * tier;
        }

        // A weakened cruiser pushes weaker. Floored well above zero on
        // purpose: an emptied pool must leave a contest that is trivial, not
        // one that resolves itself while the player watches — the mash still
        // has to be the thing that wins it.
        const float MinPushScale = 0.15f;

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
