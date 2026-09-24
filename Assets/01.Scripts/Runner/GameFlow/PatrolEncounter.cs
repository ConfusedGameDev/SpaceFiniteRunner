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
        /// <summary>Holding the flank it picked, working up to the shove.</summary>
        Alongside,
        /// <summary>The mash contest (not built yet).</summary>
        TugOfWar,
        /// <summary>The kill prompt (not built yet).</summary>
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

        public PatrolEncounterContext(float gap, float across, float shipLateral, float shipDistance,
                                      PatrolDefinition def, TrackManager track, TrackGenerator generator,
                                      bool shipSteady, float unscaledDt, float assistStrength, int presses)
        {
            UnscaledDt = unscaledDt;
            AssistStrength = assistStrength;
            Presses = presses;
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
        /// (backing off after one); exactly 1 leaves the band alone.
        /// </summary>
        public float SpeedMultiplier;
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
    /// ship's speed to force its way onto a flank. It picks the side that
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

        float stateTimer;      // seconds in the current state
        float commitTimer;     // seconds since the last run ended — the cadence
        float outsideTimer;    // seconds the ship has held outside the flank

        // The road test sweeps a few hundred metres, and it is asked every
        // substep. Its answer cannot change until the ship has moved, so it is
        // cached and re-read once per stride — the same stride the sweep
        // samples at, so nothing is ever missed.
        const float GroundRetestMeters = 25f;
        float groundTestedAt = float.NegativeInfinity;
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

            switch (State)
            {
                case PatrolEncounterState.Cruising:
                    if (commitTimer >= def.commitIntervalSeconds
                        && ctx.Gap > 0f && ctx.Gap <= def.commitFromDistance
                        && ctx.ShipSteady
                        && GroundClear(ctx))
                    {
                        Side = PickSide(ctx);
                        Enter(PatrolEncounterState.Committing);
                    }
                    break;

                case PatrolEncounterState.Committing:
                    // Overdrive until it draws level. It gives up if the ship
                    // pulls clear again, if the road stops allowing it, or if
                    // the burst simply never lands.
                    if (!ctx.ShipSteady || !GroundClear(ctx)
                        || ctx.Gap > def.commitFromDistance
                        || stateTimer >= def.commitTimeoutSeconds)
                    {
                        Enter(PatrolEncounterState.BreakingOff);
                        break;
                    }
                    intent.SpeedMultiplier = def.attackRunOverdrive;
                    intent.LineOverride = FlankLine(ctx, Side);
                    if (ctx.Gap <= def.alongsideDistance && Mathf.Abs(ctx.Across) <= def.alongsideLateral)
                        Enter(PatrolEncounterState.Alongside);
                    break;

                case PatrolEncounterState.Alongside:
                    if (!ctx.ShipSteady || !GroundClear(ctx) || ctx.Gap < 0f)
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
                    // It has to keep up to stay level, so the overdrive stays on.
                    intent.SpeedMultiplier = def.attackRunOverdrive;
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
                    if (!ctx.ShipSteady || !GroundClear(ctx) || ctx.Gap < 0f)
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

                    intent.SpeedMultiplier = def.attackRunOverdrive;
                    intent.LineOverride = FlankLine(ctx, Side);
                    intent.SteerAssist = Assist(ctx);

                    // REAL seconds, both sides of it. The world is at 30% for
                    // the look of the thing; if the bar rode the same clock the
                    // slow-mo would hand the player three times as long to
                    // mash, which is a mechanical refund, not perception.
                    tug += def.tugPatrolForce * ctx.UnscaledDt;
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
                        // The player wins. M2 turns this into the finisher
                        // prompt; for now the patrol simply lets go.
                        Enter(PatrolEncounterState.BreakingOff);
                    }
                    break;

                // Backing off is ACTIVE: the patrol drives below the ship's
                // speed until the next run. Left to the ordinary band it would
                // sit on the bumper for ten seconds bleeding off its overdrive,
                // which is the permanent tailgating the duel exists to replace.
                case PatrolEncounterState.BreakingOff:
                    intent.SpeedMultiplier = def.breakOffSpeedFactor;
                    if (stateTimer >= def.breakOffSeconds) Enter(PatrolEncounterState.Cooldown);
                    break;

                case PatrolEncounterState.Cooldown:
                    intent.SpeedMultiplier = def.breakOffSpeedFactor;
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
        bool GroundClear(in PatrolEncounterContext ctx)
        {
            if (Mathf.Abs(ctx.ShipDistance - groundTestedAt) < GroundRetestMeters) return groundClear;
            groundTestedAt = ctx.ShipDistance;
            groundClear = TrackHazards.IsEncounterGroundClear(ctx.Track, ctx.Generator,
                                                              ctx.ShipDistance,
                                                              ctx.ShipDistance + ctx.Def.encounterLookaheadMeters);
            return groundClear;
        }

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
