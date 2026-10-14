using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using Unity.Profiling;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Race
{
    /// <summary>
    /// A race level's rival field (RaceModePRD.md): N rival hover ships on a
    /// grid with the ship, racing it to the end ramps. Hand-placed under
    /// ===SYSTEMS=== (PF_RaceSystem); it finds nothing in the scene — the
    /// composition root (<see cref="GameManager"/>) binds it to the ship, the
    /// track and the level's <see cref="RaceDefinition"/>, and unbound it idles.
    /// The pool is built at bind (D12): one <see cref="Rival"/> shell per entry,
    /// never instantiated or destroyed mid-run.
    ///
    /// <b>Two fidelities</b> (D10): a rival inside the window round the ship on
    /// built road runs the full <see cref="TrackBody"/> sim — ramps, grip on
    /// flat sweeps, open edges, orbs, the end lip — and is drawn; outside it,
    /// it is a <b>ghost</b>: distance plus its pace, hidden, unable to crash.
    /// A ghost is re-seated into full sim only on plain road (no section, no
    /// ramp span), on the nearest free lateral; a rival leaving the window is
    /// ghosted only once grounded. The window's ahead edge sits past the fog,
    /// so neither handoff is seen.
    ///
    /// <b>Standings</b> (D8, R5.2) are this system's <see cref="IRaceStandings"/>:
    /// a racer's place is fixed when it leaves by an end ramp (finish order is
    /// lip order), a ghost that crosses the end finishes too, and the live
    /// position of everyone still on the track is by distance. A destroyed
    /// rival is DNF for good (D3).
    /// </summary>
    /// <summary>How a rival was wrecked (R5.1). Never serialized.</summary>
    public enum RivalWreck
    {
        /// <summary>Crossed a laser gate's beam.</summary>
        Laser,
        /// <summary>Went off an open edge.</summary>
        Fell,
        /// <summary>Reached the end of the track off a ramp (a gap at the end).</summary>
        MissedRamp,
        /// <summary>Its ram hull ran out — the ship rammed it to death.</summary>
        Rammed,
    }

    public class RaceSystem : MonoBehaviour, IRaceStandings
    {
        IChaseTarget ship;
        IControlTakeover control; // what a contact may do to the ship (the ship's own, when it offers it)
        TrackManager track;
        TrackGenerator generator;
        GameSettings rules;
        RaceDefinition source;
        RaceDefinition def; // the runtime clone gameplay reads
        float lightSpeedMps;
        System.Func<float> shipBaseThrust; // the player's ship's thrust before upgrades, m/s² (read at lights out)
        float shipThrust = 25f;            // the ship's base thrust, read at lights out (thrustShare applies live)
        float launchSpeed;                 // the ship's speed at lights out: every rival's launch impulse
        float raceTime;                    // seconds since lights out (the surge waves run on it)
        readonly System.Random runRng = new();

        readonly List<Rival> rivals = new();
        Transform poolRoot;
        readonly List<TrackObstacles.Obstacle> obstacles = new();
        readonly List<Vector2> free = new();
        float[] endRamps;

        // Every laser gate of the run as its DECIDED placement (built or not),
        // sorted by start, as the zone a rival must keep its centre out of
        // before its own hull is added: the rivals plan round gates the
        // generator has not built yet. Rebuilt when the placements change.
        readonly List<TrackObstacles.Obstacle> gateZones = new();
        int gateZonesRevision = -1, gateZonesSource = -1;
        float gateZonesVertical = -1f;  // the vertical half width the zones were built with
        float measuredVertical;         // the widest vertical beam seen built this run (its model's sheet)
        int verticalMeasureTick;

        bool gridPending; // the grid is laid on the first tick: the track may not exist at bind
        bool launched;    // lights out: the ship has moved
        int finishedCount, dnfCount;
        int playerPosition;
        int playerFinishPlace; // fixed once the ship has left the end of the track
        float lastTickTime;

        static readonly ProfilerMarker TickMarker = new("RaceSystem.Tick");
        static readonly ProfilerMarker RacingMarker = new("RaceSystem.StepRacing");

        /// <summary>The system bound to the running level, for the debug page; null when no level has a race.</summary>
        public static RaceSystem Live { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() => Live = null; // domain reload is off

        /// <summary>True while bound to a level with a race.</summary>
        public bool Bound => def != null && ship != null && track != null;
        /// <summary>The live clone of the level's race (the debug menu mirrors onto it); null unbound.</summary>
        public RaceDefinition Definition => def;
        /// <summary>The authored asset the clone was made from — what the debug page writes.</summary>
        public RaceDefinition DefinitionAsset => source;
        public IReadOnlyList<Rival> Rivals => rivals;

        // ---- IRaceStandings ----
        public bool RaceActive => Bound;
        public int RacerCount => Bound ? rivals.Count + 1 : 0;
        public int PlayerPosition => playerPosition;
        public int FinishedCount => finishedCount;
        public int DnfCount => dnfCount;
        public int RacingCount => Bound ? rivals.Count - finishedCount - dnfCount : 0;
        public int RivalCount => Bound ? rivals.Count : 0;
        public float RivalDistance(int index) => rivals[index].Distance;
        public RacerState RivalState(int index) => rivals[index].State;
        public int RivalFinishPlace(int index) => rivals[index].FinishPlace;
        public Color RivalColor(int index) => rivals[index].Entry.mapColor;
        public event System.Action<int, int> PlayerPositionChanged;

        /// <summary>
        /// Raised when a rival is wrecked (DNF): the rival, how, and whether it
        /// is the PLAYER's kill (R5.6 — rammed to death, or wrecked within
        /// <see cref="RaceDefinition.creditWindow"/> of its last contact with
        /// the ship). The composition root pays the bounty on it.
        /// </summary>
        public event System.Action<Rival, RivalWreck, bool> RivalDestroyed;
        /// <summary>Raised when a rival's place is fixed at the lip: the rival (its <see cref="Rival.FinishPlace"/> is set).</summary>
        public event System.Action<Rival> RivalFinished;

        [ShowInInspector, ReadOnly] string Standing => Bound ? $"P{playerPosition}/{RacerCount}  fin {finishedCount}  dnf {dnfCount}" : "unbound";

        /// <summary>
        /// Binds the system to a run. A null <paramref name="definition"/> (the
        /// level is not a race) or one with no rival unbinds it, so it idles.
        /// Rebuilds the pool when the definition changes.
        /// <paramref name="lightSpeedKmh"/> is the run's Light Speed, the
        /// reference every rival's pace is a share of.
        /// </summary>
        public void Bind(IChaseTarget ship, TrackManager track, TrackGenerator generator, GameSettings rules,
                         RaceDefinition definition, float lightSpeedKmh, System.Func<float> shipBaseThrust = null)
        {
            Unsubscribe();
            if (definition == null || !definition.HasRivals || ship == null || track == null)
            {
                Unbind();
                return;
            }

            this.ship = ship;
            control = ship as IControlTakeover;
            this.track = track;
            this.generator = generator;
            this.rules = rules;
            lightSpeedMps = Mathf.Max(1f, lightSpeedKmh / 3.6f);
            this.shipBaseThrust = shipBaseThrust;
            if (generator != null) generator.Regenerated += ResetForRun;

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

        /// <summary>Drops the race: the pool torn down, the system idle.</summary>
        public void Unbind()
        {
            if (Live == this) Live = null;
            Unsubscribe();
            ClearPool();
            ship = null;
            control = null;
            track = null;
            generator = null;
            rules = null;
            source = null;
            if (def != null) Destroy(def);
            def = null;
            playerPosition = 0;
        }

        /// <summary>A fresh race: everyone back on the grid (laid on the next tick), standings cleared.</summary>
        public void ResetForRun()
        {
            if (!Bound) return;
            gridPending = true;
            launched = false;
            shipSpeedRef = 0f;
            raceTime = 0f;
            finishedCount = dnfCount = 0;
            playerFinishPlace = 0;
            endRamps = null;
            foreach (var rival in rivals) rival.Park();
            pendingDebug = DebugAction.None;
        }

        void OnDestroy() => Unbind();

        void Unsubscribe()
        {
            if (generator != null) generator.Regenerated -= ResetForRun;
        }

        // ---- pool ----

        void BuildPool()
        {
            if (poolRoot == null)
            {
                poolRoot = new GameObject("Rivals").transform;
                poolRoot.SetParent(transform, false);
            }
            for (int i = 0; i < def.rivals.Count; i++)
            {
                var entry = def.rivals[i];
                if (entry == null) continue;
                var shell = new GameObject($"Rival_{i}_{entry.displayName}");
                shell.transform.SetParent(poolRoot, false);
                var rival = shell.AddComponent<Rival>();
                rival.Build(rivals.Count, entry, track, 7919 * (i + 1));
                rivals.Add(rival);
            }
        }

        void ClearPool()
        {
            foreach (var rival in rivals)
                if (rival != null) Destroy(rival.gameObject);
            rivals.Clear();
        }

        // ---- the grid (R1.2) ----

        void LayGrid()
        {
            gridPending = false;
            int columns = Mathf.Max(1, def.gridColumns);
            int slots = rivals.Count + 1;
            int rows = (slots + columns - 1) / columns;
            int playerSlot = def.playerGridSlot >= 0
                ? Mathf.Min(def.playerGridSlot, slots - 1)
                : Mathf.Min((rows - 1) * columns + (columns - 1) / 2, slots - 1);
            int playerRow = playerSlot / columns, playerColumn = playerSlot % columns;

            // The ship launches a little way in, on the flight line.
            float shipStart = rules != null ? Mathf.Max(0f, rules.launchDistanceMeters) : 0f;
            track.GetLateralBand(Mathf.Max(0f, shipStart), out float bandMin, out float bandMax);

            int slot = 0;
            foreach (var rival in rivals)
            {
                if (slot == playerSlot) slot++;
                int row = slot / columns, column = slot % columns;
                float distance = shipStart + (playerRow - row) * def.gridRowSpacing;
                float margin = rival.Entry.halfWidth + 1f;
                float lateral = Mathf.Clamp((column - playerColumn) * def.gridLaneSpacing, bandMin + margin, bandMax - margin);
                rival.PlaceOnGrid(distance, lateral);
                RollForm(rival, lateral, bandMin + margin, bandMax - margin);
                slot++;
            }
            endRamps = generator != null && track.HasEnd ? generator.EndRampLaterals() : null;
            UpdateStandings();
        }

        // Its form on the day (D9's spread, rolled per run): thrust and pace
        // off the authored values by up to formVariance, a phase for its
        // surge wave, its reaction at lights out, and the lane it races in —
        // pulled from its grid slot toward a random one by laneSpread. Without
        // this every rival accelerated identically and the grid shape held
        // the whole race.
        void RollForm(Rival rival, float gridLateral, float laneMin, float laneMax)
        {
            float variance = def.formVariance;
            rival.RunThrustScale = rival.Entry.thrustFactor * (1f + Roll(-variance, variance));
            rival.RunPaceScale = 1f + Roll(-variance, variance);
            rival.SurgePhase = Roll(0f, 1000f);
            rival.Driver.RunSalt = runRng.Next();
            rival.LaunchDelay = Roll(def.LaunchReactionMin, Mathf.Max(def.LaunchReactionMin, def.LaunchReactionMax));
            float lane = laneMax > laneMin ? Roll(laneMin, laneMax) : gridLateral;
            rival.Driver.HomeLateral = Mathf.Lerp(gridLateral, lane, def.laneSpread);
        }

        float Roll(float min, float max) => min + (float)runRng.NextDouble() * (max - min);

        // ---- the run ----

        void FixedUpdate()
        {
            if (!Bound || track == null) return;
            if (gridPending)
            {
                if (track.Length <= 0f) return; // no road yet
                LayGrid();
            }
            if (ship.Paused) return;

            float dt = Time.fixedDeltaTime;
            if (!launched)
            {
                // Lights out the tick the ship moves: everyone gets its launch impulse.
                if (ship.Speed <= 0.1f) return;
                launched = true;
                float baseThrust = shipBaseThrust != null ? shipBaseThrust() : 0f;
                shipThrust = baseThrust > 0f ? baseThrust : 25f;
                launchSpeed = ship.Speed;
                shipSpeedRef = ship.Speed;
            }
            raceTime += dt;
            // The ship's recent speed, for the lead governor: smoothed so one
            // hit does not stall the field.
            shipSpeedRef = Mathf.Lerp(shipSpeedRef, ship.Speed, 1f - Mathf.Exp(-dt / Mathf.Max(def.leadSpeedSmoothing, 0.01f)));
            RunDebugAction();
            if (endRamps == null && generator != null && track.HasEnd) endRamps = generator.EndRampLaterals();

            RefreshGateZones();
            using var tick = TickMarker.Auto();
            int substeps = Mathf.Max(1, rules != null ? rules.simSubsteps : 1);
            float h = dt / substeps;
            float windowMin = ship.Distance - def.FullSimBehind;
            float windowMax = ship.Distance + def.FullSimAhead;
            if (generator != null) windowMax = Mathf.Min(windowMax, generator.SettledDistance);

            foreach (var rival in rivals)
            {
                switch (rival.State)
                {
                    case RacerState.Grid:
                        // Its reaction: off the line a moment after the lights.
                        rival.LaunchDelay -= dt;
                        if (rival.LaunchDelay <= 0f) rival.Launch(launchSpeed);
                        break;

                    case RacerState.Finished:
                    case RacerState.Dnf:
                        rival.StepFlight(dt);
                        break;

                    case RacerState.Ghost:
                        rival.StepGhost(dt, Pace(rival) * def.ghostPaceFactor, Thrust(rival) * def.ghostPaceFactor); // no hazards to lose time to: slower at both
                        if (track.HasEnd && rival.Distance >= track.EndDistance) { FinishRival(rival); break; }
                        if (rival.Distance >= windowMin && rival.Distance <= windowMax) TrySeat(rival);
                        break;

                    case RacerState.Racing:
                        StepRacing(rival, h, substeps);
                        if (rival.State != RacerState.Racing) break;
                        bool inWindow = rival.Distance >= windowMin && rival.Distance <= windowMax;
                        if (!inWindow && CanGhost(rival)) rival.ToGhost();
                        break;
                }
            }

            KeepApart();
            ShipContact();
            UpdateStandings();
            lastTickTime = Time.fixedTime;
        }

        void Update()
        {
            if (!Bound) return;
            float alpha = Mathf.Clamp01((Time.time - lastTickTime) / Mathf.Max(Time.fixedDeltaTime, 1e-5f));
            if (ship.Paused) alpha = 1f;
            foreach (var rival in rivals) rival.ApplyPose(alpha);
        }

        // The pace a rival aims at: its share of Light Speed, nudged toward the
        // ship by the soft rubber band (D9) — a rival behind the ship is pushed
        // a little, one ahead held back a little, never more than the band.
        // The rubber band (D9): ahead of the ship a rival eases off, behind it
        // pushes, up to ±rubberBand at rubberRange. It scales BOTH the pace
        // cap and the thrust — the field rarely reaches its cap, so a band on
        // the cap alone never pulled a runaway leader back.
        float Rubber(Rival rival)
        {
            float gap = ship.Distance - rival.Distance; // positive: the rival is behind
            return 1f + def.rubberBand * Mathf.Clamp(gap / Mathf.Max(def.rubberRange, 1f), -1f, 1f);
        }

        float shipSpeedRef; // the ship's smoothed speed, m/s

        float Pace(Rival rival)
        {
            float pace = rival.Entry.cruiseFraction * rival.RunPaceScale * lightSpeedMps * Rubber(rival);
            // The lead governor: ahead of the ship, no faster than its recent
            // speed × a share that falls with the lead — never below the
            // launch speed, so a stalled ship does not stop the field.
            float lead = rival.Distance - ship.Distance;
            if (def.leadGovernor && launched && lead > 0f)
            {
                float factor = Mathf.Lerp(def.leadCapNear, def.leadCapFar, Mathf.Clamp01(lead / Mathf.Max(def.rubberRange, 1f)));
                pace = Mathf.Min(pace, Mathf.Max(shipSpeedRef * factor, launchSpeed));
            }
            return pace;
        }

        // Its acceleration now: the field's thrust × its form × its surge, a
        // slow wave of its own so the pack keeps trading places, × the band.
        float Thrust(Rival rival)
        {
            float wave = Mathf.PerlinNoise(raceTime / Mathf.Max(def.surgePeriod, 0.1f), rival.SurgePhase) - 0.5f;
            return def.thrustShare * shipThrust * rival.RunThrustScale * Mathf.Max(0.1f, 1f + def.surgeAmount * 2f * wave) * Rubber(rival);
        }

        void StepRacing(Rival rival, float h, int substeps)
        {
            using var marker = RacingMarker.Auto();
            var body = rival.Body;
            body.BeginTick();
            GameSettings r = rules != null ? rules : GameSettings.Default;
            // The line is planned once per tick, not per substep: a substep is
            // a few metres of road, and the plan looks seconds ahead.
            var spot = new ShipSpot
            {
                valid = ship.Steady && !ship.HasLeftTrackEnd,
                distance = ship.Distance,
                lateral = ship.Lateral,
                speed = ship.Speed,
            };
            BodyControls controls = rival.Driver.Drive(body, track, def, endRamps, spot, generator != null ? gateZones : null, out float speedCap);
            rival.RecordTrace(rival.Driver.LastLine);
            float thrust = Thrust(rival);
            for (int i = 0; i < substeps; i++)
            {
                if (i > 0) controls.steer = LineDriving.SteerFor(body, rival.Driver.LastLine);
                body.Params = new BodyParams
                {
                    impulseBlendRate = 1000f,
                    cruiseSpeed = Mathf.Min(Pace(rival), speedCap),
                    thrust = thrust,
                    brakeDecel = def.brakeDecel,
                    coastDrag = 0f,
                    passiveDeceleration = def.overPaceBleed,
                    lateralSpeed = def.lateralSpeed,
                    handlingResponse = def.handlingResponse,
                    gripBase = def.gripBase,
                    gripPerSpeed = def.gripPerSpeed,
                    slideThreshold = def.slideThreshold,
                    slideSpeedLoss = def.slideSpeedLoss,
                    jumpStrength = 1f,
                    wallHitCooldownSeconds = 0.5f,
                    pickupReach = new Vector2(rival.Entry.halfWidth, 3f),
                    edgeOverhang = r.edgeOverhang,
                    edgeGraceSeconds = r.edgeGraceSeconds,
                };
                rival.OrbBoostShare = def.orbBoostShare;
                body.Step(h, controls);
                rival.CountFullSim(h);

                if (rival.PendingExit.HasValue)
                {
                    bool byRamp = rival.PendingExit.Value;
                    rival.PendingExit = null;
                    if (byRamp) FinishRival(rival);
                    else DestroyRival(rival, track.HasEnd && rival.Body.Distance >= track.EndDistance - 1f ? RivalWreck.MissedRamp : RivalWreck.Fell);
                    return;
                }
                if (rival.LaserPending)
                {
                    rival.LaserPending = false;
                    DestroyRival(rival, RivalWreck.Laser);
                    return;
                }
            }
        }

        // Full sim → ghost only on the ground and off any ramp: an airborne or
        // committed body is mid-set-piece and keeps its sim until it is down.
        static bool CanGhost(Rival rival)
        {
            var body = rival.Body;
            return (body.State == ShipState.Grounded || body.State == ShipState.OnTube) && body.Ramp == null;
        }

        void RefreshGateZones()
        {
            if (generator == null) return;
            // A vertical beam's sheet is its prefab model's width, which no
            // placement records: measured off the built gates (a few times a
            // second — they are built kilometres ahead of the ship).
            if (verticalMeasureTick-- <= 0)
            {
                verticalMeasureTick = 25;
                var pickups = PickupRegistry.All;
                for (int i = 0; i < pickups.Count; i++)
                    if (pickups[i] is LaserGate built && built.Available && built.Variant == LaserGateVariant.Vertical)
                        measuredVertical = Mathf.Max(measuredVertical, built.TrackHalfExtents.x);
            }
            float vertical = Mathf.Max(def.verticalBeamHalfWidth, measuredVertical);
            var placements = generator.Placements;
            if (track.Revision == gateZonesRevision && placements.Count == gateZonesSource && Mathf.Approximately(vertical, gateZonesVertical)) return;
            gateZonesRevision = track.Revision;
            gateZonesSource = placements.Count;
            gateZonesVertical = vertical;
            gateZones.Clear();
            foreach (var p in placements)
            {
                if (p.kind != TrackPlacementKind.LaserGate) continue;
                var variant = (LaserGateVariant)p.variant;
                float half = Mathf.Max(0f, p.data.x) * 0.5f;
                // Across: the beam (or the rotor's disc) plus the emitters
                // past its ends; a vertical beam is only its own sheet. Along:
                // a rotor sweeps a disc, the others are a thin sheet.
                float across = variant == LaserGateVariant.Vertical ? vertical : half + def.gateClearance;
                float depth = variant == LaserGateVariant.Rotor ? half + 1f : 2f;
                gateZones.Add(new TrackObstacles.Obstacle
                {
                    start = p.distance - depth,
                    end = p.distance + depth,
                    min = p.lateral - across,
                    max = p.lateral + across,
                });
            }
            gateZones.Sort((a, b) => a.start.CompareTo(b.start));
        }

        // Ghost → full sim only on plain road: no loop or tube, no ramp's span.
        void TrySeat(Rival rival)
        {
            float d = rival.Distance;
            if (d < 0f || track.SectionAt(d) != null) return;
            foreach (var ramp in JumpRamp.Active)
                if (ramp != null && ramp.Spans(d)) return;

            track.GetLateralBand(d, out float bandMin, out float bandMax);
            float pad = rival.Entry.halfWidth + 1f;
            obstacles.Clear();
            // Clear for as far as its driver looks for a gate: seated closer
            // than that to a beam, it has no time to steer round it.
            float clearTo = d + Mathf.Max(200f, rival.Speed * def.hazardLookaheadSeconds);
            TrackObstacles.Collect(d - 60f, clearTo, pad, obstacles);
            foreach (var zone in gateZones)
            {
                if (zone.start > clearTo) break;
                if (zone.end < d - 60f) continue;
                obstacles.Add(new TrackObstacles.Obstacle { start = zone.start, end = zone.end, min = zone.min - pad, max = zone.max + pad });
            }
            TrackObstacles.FreeIntervals(bandMin + pad, bandMax - pad, obstacles, free);
            if (!TrackObstacles.Nearest(free, rival.Driver.HomeLateral, out float lateral)) return;
            rival.Seat(lateral);
        }

        /// <summary>Debug: the leading rival still on the track crosses the line now (tests the early fail and the finish order).</summary>
        [Button("Finish Leader"), PropertyOrder(-1)]
        public void DebugFinishLeader()
        {
            if (!Bound || !Application.isPlaying) return;
            Rival leader = null;
            foreach (var rival in rivals)
                if ((rival.State == RacerState.Racing || rival.State == RacerState.Ghost || rival.State == RacerState.Grid)
                    && (leader == null || rival.Distance > leader.Distance))
                    leader = rival;
            if (leader == null) return;
            if (leader.State == RacerState.Racing) leader.ToGhost(); // finished off the map, not mid-air in view
            FinishRival(leader);
            UpdateStandings();
        }

        // The debug page's actions are queued and run on the next race tick:
        // the page lives in the pause menu, and a wreck made while the clock
        // is stopped could never play the kill cam it is there to test.
        enum DebugAction { None, KillNearest, DnfAll, FinishAll }
        DebugAction pendingDebug;

        /// <summary>Debug: wrecks the rival nearest the ship — one in view first — credited to the player, on the next race tick: the kill cam's and the bounty's test.</summary>
        [Button("Kill Nearest"), PropertyOrder(-1)]
        public void DebugKillNearest() => QueueDebug(DebugAction.KillNearest);

        /// <summary>Debug: every rival still in the race is wrecked on the next race tick (tests the field running out).</summary>
        [Button("DNF All"), PropertyOrder(-1)]
        public void DebugDnfAll() => QueueDebug(DebugAction.DnfAll);

        /// <summary>Debug: every rival still in the race crosses the line, leader first, on the next race tick.</summary>
        [Button("Finish All"), PropertyOrder(-1)]
        public void DebugFinishAll() => QueueDebug(DebugAction.FinishAll);

        void QueueDebug(DebugAction action)
        {
            if (Bound && Application.isPlaying) pendingDebug = action;
        }

        static bool StillRacing(Rival rival) =>
            rival.State == RacerState.Racing || rival.State == RacerState.Ghost || rival.State == RacerState.Grid;

        void RunDebugAction()
        {
            DebugAction action = pendingDebug;
            pendingDebug = DebugAction.None;
            switch (action)
            {
                case DebugAction.KillNearest:
                    // In view first (the kill cam's case), else the nearest ghost.
                    Rival nearest = null;
                    foreach (var rival in rivals)
                    {
                        if (!StillRacing(rival)) continue;
                        bool inView = rival.State == RacerState.Racing, nearestInView = nearest != null && nearest.State == RacerState.Racing;
                        if (nearest == null || (inView && !nearestInView)
                            || (inView == nearestInView && Mathf.Abs(rival.Distance - ship.Distance) < Mathf.Abs(nearest.Distance - ship.Distance)))
                            nearest = rival;
                    }
                    if (nearest != null) DestroyRival(nearest, RivalWreck.Rammed);
                    break;
                case DebugAction.DnfAll:
                    foreach (var rival in rivals)
                        if (StillRacing(rival)) DestroyRival(rival, RivalWreck.Fell);
                    break;
                case DebugAction.FinishAll:
                    int guard = rivals.Count;
                    while (guard-- > 0 && rivals.Exists(StillRacing)) DebugFinishLeader();
                    break;
            }
            if (action != DebugAction.None) UpdateStandings();
        }

        void FinishRival(Rival rival)
        {
            finishedCount++;
            float gravity = rules != null ? rules.winEscapeGravity : 0f;
            rival.Finish(finishedCount, gravity, def.finishFlightSeconds);
            RivalFinished?.Invoke(rival);
        }

        void DestroyRival(Rival rival, RivalWreck cause)
        {
            dnfCount++;
            bool byPlayer = cause == RivalWreck.Rammed
                            || Time.time - rival.LastShipContactTime <= def.creditWindow;
            float lateral = rival.State == RacerState.Racing ? rival.Body.Lateral : rival.Lateral;
            rival.WreckNote = $"{cause}{(byPlayer ? " (player)" : "")}{(rival.Driver.Mistake.Length > 0 ? " mistake:" + rival.Driver.Mistake : "")} at {rival.Distance:0} m, lateral {lateral:0.0}, line {rival.Driver.LastLine:0.0}"
                              + $" | {rival.TraceAt(2f)} | {rival.TraceAt(1f)} | {rival.TraceAt(0.5f)}";
            if (def.debugReadout) Debug.Log($"[Race] {rival.Entry.displayName} wrecked: {rival.WreckNote}", this);
            var look = new ExplosionLook();
            if (rules != null)
            {
                look.textures = rules.explosionTextures;
                look.scale = rules.explosionScale;
                look.lifetime = rules.explosionLifetime;
                look.particles = rules.explosionParticles;
            }
            rival.Destroyed(look);
            RivalDestroyed?.Invoke(rival, cause, byPlayer);
        }

        // Rival ↔ rival (R2.6): two full-sim rivals overlapping along the track
        // keep the separation across it — the patrol's lane clamp, both ways.
        // Never across a full tube (the lateral wraps) or with one in the air
        // or on a ramp's rails.
        void KeepApart()
        {
            float separation = def.rivalSeparation;
            for (int i = 0; i < rivals.Count; i++)
            {
                var a = rivals[i];
                if (!Settled(a)) continue;
                for (int j = i + 1; j < rivals.Count; j++)
                {
                    var b = rivals[j];
                    if (!Settled(b)) continue;
                    if (Mathf.Abs(a.Body.Distance - b.Body.Distance) > a.Entry.halfLength + b.Entry.halfLength) continue;
                    float across = b.Body.Lateral - a.Body.Lateral;
                    if (Mathf.Abs(across) >= separation) continue;
                    float side = across >= 0f ? 1f : -1f;
                    float push = (separation - Mathf.Abs(across)) * 0.5f;
                    track.GetLateralBand(a.Body.Distance, out float bandMin, out float bandMax);
                    a.Body.Lateral = Mathf.Clamp(a.Body.Lateral - side * push, bandMin, bandMax);
                    b.Body.Lateral = Mathf.Clamp(b.Body.Lateral + side * push, bandMin, bandMax);
                }
            }
        }

        // Ship <-> rival (R2.6 / M3's contact, the hull part): analytic, in
        // track space, like every contact in this game — the rival has no
        // collider. Overlapping boxes are pushed apart along whichever axis
        // they overlap least: side by side, both hulls are thrown apart across
        // the track; nose to tail, the rival is held clear along it. Only the
        // rival's body is ever moved — the ship is a physics body this system
        // does not own, so it gets the dash's own shove channel and the ram's
        // speed loss, through IControlTakeover. A contact scores only on the
        // tick it begins; staying in contact is just held apart.
        /// <summary>Contact with the ship is off (R4.5): the kill cam holds it while it owns the picture.</summary>
        public bool ContactSuspended { get; set; }

        /// <summary>The ship's track, for the kill cam's framing; null unbound.</summary>
        public TrackManager Track => track;

        void ShipContact()
        {
            bool shipIn = ship.Steady && !ship.HasLeftTrackEnd && !ContactSuspended;
            foreach (var rival in rivals)
            {
                if (!shipIn || !Settled(rival)) { rival.InContact = false; continue; }
                var body = rival.Body;
                float along = body.Distance - ship.Distance; // positive: the rival is ahead
                float across = body.Lateral - ship.Lateral;  // positive: the rival is on the ship's +lateral side
                float reachL = rival.Entry.halfLength + def.shipHalfLength;
                float reachW = rival.Entry.halfWidth + def.shipHalfWidth;
                if (Mathf.Abs(along) >= reachL || Mathf.Abs(across) >= reachW) { rival.InContact = false; continue; }

                bool fresh = !rival.InContact;
                rival.InContact = true;
                rival.LastShipContactTime = Time.time;
                float side = across >= 0f ? 1f : -1f;
                track.GetLateralBand(body.Distance, out float bandMin, out float bandMax);
                float depthAcross = (reachW - Mathf.Abs(across)) / reachW;
                float depthAlong = (reachL - Mathf.Abs(along)) / reachL;

                if (depthAcross < depthAlong)
                {
                    // Side by side: held a hull's width apart, and a fresh
                    // contact throws both away from each other.
                    body.Lateral = Mathf.Clamp(ship.Lateral + side * reachW, bandMin, bandMax);
                    if (body.LateralVelocity * side < 0f) body.LateralVelocity = 0f;
                    if (!fresh) continue;
                    body.AddLateralImpulse(side * def.sideShoveMeters * def.handlingResponse);
                    control?.AddLateralShove(-side * def.sideShoveMeters * ship.HandlingResponse);
                    ContactFeedback(along, across, 0.6f);
                }
                else if (along > 0f)
                {
                    // The ship into its tail: the rival is shunted ahead. A
                    // fast arrival is a ram — the ship pays a share of its
                    // speed, the rival is knocked forward and sideways.
                    float closing = ship.Speed - body.ForwardSpeed;
                    body.Distance = ship.Distance + reachL;
                    if (fresh && closing >= def.ramClosingThreshold)
                    {
                        body.ForwardSpeed = ship.Speed + closing * def.ramSpeedTransfer;
                        float knock = Mathf.Abs(across) > 0.5f ? side : (runRng.NextDouble() < 0.5 ? -1f : 1f);
                        body.AddLateralImpulse(knock * def.ramKnockMeters * def.handlingResponse);
                        control?.ApplyImpactSpeedLoss(def.ramSpeedCost);
                        ContactFeedback(along, across, 1f);
                        // Its hull: the last ram wrecks it — the player's kill.
                        rival.RamHull--;
                        if (rival.RamHull <= 0)
                        {
                            DestroyRival(rival, RivalWreck.Rammed);
                            continue;
                        }
                    }
                    else body.ForwardSpeed = Mathf.Max(body.ForwardSpeed, ship.Speed);
                }
                else
                {
                    // The rival into the ship's tail: held behind at the
                    // ship's speed, bounced back off a fast arrival.
                    float closing = body.ForwardSpeed - ship.Speed;
                    body.Distance = ship.Distance - reachL;
                    body.ForwardSpeed = Mathf.Min(body.ForwardSpeed, ship.Speed);
                    if (fresh && closing >= def.ramClosingThreshold)
                    {
                        body.ForwardSpeed = Mathf.Max(0f, ship.Speed - closing * 0.3f);
                        control?.VisualKick(-side * 0.8f);
                        ContactFeedback(along, across, 0.7f);
                    }
                }
            }
        }

        // Sparks where the hulls met, a rumble and the wall hit's shake.
        void ContactFeedback(float along, float across, float strength)
        {
            track.GetPoseAtDistance(Mathf.Max(0f, ship.Distance + along * 0.5f), ship.Lateral + across * 0.5f,
                                    out Vector3 pose, out Quaternion rotation);
            pose += rotation * (Vector3.up * 2f);
            SparkleVfx.SpawnBurst(pose, rotation * Vector3.up, def.contactSparkColor, 6f * strength, Mathf.RoundToInt(24 * strength));
            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(0.7f * strength, 0.5f * strength, 0.2f);
            if (rules != null && rules.wallHitShake != null) CameraShake.Shake(rules.wallHitShake);
        }

        bool Settled(Rival rival)
        {
            if (rival.State != RacerState.Racing) return false;
            var body = rival.Body;
            if (body.State != ShipState.Grounded || body.Ramp != null) return false;
            return track.SectionAt(body.Distance) is not TubeSection;
        }

        /// <summary>
        /// Fixes the ship's finishing place NOW — the composition root calls
        /// it from the ship's own lip, inside that tick, so a rival finishing
        /// later in the same physics step cannot steal the place (finish order
        /// is lip order, D8). Idempotent; 0 while unbound.
        /// </summary>
        public int FixPlayerFinish()
        {
            if (!Bound) return 0;
            if (playerFinishPlace == 0) playerFinishPlace = finishedCount + 1;
            UpdateStandings();
            return playerFinishPlace;
        }

        void UpdateStandings()
        {
            int position;
            if (ship.HasLeftTrackEnd || playerFinishPlace > 0)
            {
                // The ship's own lip fixes its place, like everyone's.
                if (playerFinishPlace == 0) playerFinishPlace = finishedCount + 1;
                position = playerFinishPlace;
            }
            else
            {
                position = 1 + finishedCount;
                float shipDistance = ship.Distance;
                foreach (var rival in rivals)
                {
                    if (rival.State != RacerState.Racing && rival.State != RacerState.Ghost && rival.State != RacerState.Grid) continue;
                    if (rival.Distance > shipDistance) position++;
                }
            }
            if (position == playerPosition) return;
            int was = playerPosition;
            playerPosition = position;
            if (was != 0) PlayerPositionChanged?.Invoke(was, position);
        }

        // ---- debug readout ----

        // Rebuilt a few times a second, not every OnGUI event: string building
        // every frame (two events per frame) cost ~2 ms in the profiler.
        const float ReadoutInterval = 0.25f;
        readonly StringBuilder readout = new();
        string readoutText = string.Empty;
        float readoutNext;
        GUIStyle readoutStyle;

        void Awake() => useGUILayout = false; // a plain GUI.Label: no layout pass

        // Ghost wireframes: a box per ghost at its distance and home lane,
        // drawn with GL after the cameras render (URP still invokes it).
        static Material wireMaterial;

        void OnRenderObject()
        {
            if (!Bound || !def.debugGhostWireframes) return;
            if (wireMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;
                wireMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                wireMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                wireMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            wireMaterial.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            foreach (var rival in rivals)
            {
                if (rival.State != RacerState.Ghost) continue;
                var entry = rival.Entry;
                track.GetPoseAtDistance(Mathf.Max(0f, rival.Distance), rival.Lateral, out Vector3 at, out Quaternion frame);
                GL.Color(entry.mapColor);
                Vector3 centre = at + frame * new Vector3(0f, entry.hoverHeight + 1.5f, 0f);
                Vector3 x = frame * Vector3.right * entry.halfWidth;
                Vector3 y = frame * Vector3.up * 1.5f;
                Vector3 z = frame * Vector3.forward * entry.halfLength;
                for (int i = 0; i < 4; i++)
                {
                    // The four edges along each axis.
                    float a = (i & 1) == 0 ? -1f : 1f, b = (i & 2) == 0 ? -1f : 1f;
                    Line(centre - x + a * y + b * z, centre + x + a * y + b * z);
                    Line(centre + a * x - y + b * z, centre + a * x + y + b * z);
                    Line(centre + a * x + b * y - z, centre + a * x + b * y + z);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        static void Line(Vector3 from, Vector3 to)
        {
            GL.Vertex(from);
            GL.Vertex(to);
        }

        void OnGUI()
        {
            if (!Bound || !def.debugReadout || Event.current.type != EventType.Repaint) return;
            if (Time.unscaledTime >= readoutNext)
            {
                readoutNext = Time.unscaledTime + ReadoutInterval;
                BuildReadout();
            }
            readoutStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false, normal = { textColor = Color.white } }; // a long wreck note runs off the right, never clips the rows below
            GUI.Label(new Rect(12f, 120f, Screen.width - 24f, 24f + 20f * rivals.Count), readoutText, readoutStyle);
        }

        void BuildReadout()
        {
            readout.Clear();
            readout.Append($"RACE  P{playerPosition}/{RacerCount}  finished {finishedCount}  dnf {dnfCount}\n");
            foreach (var rival in rivals)
            {
                float gap = rival.Distance - ship.Distance;
                readout.Append($"{rival.Entry.displayName,-10} {rival.State,-8} ");
                if (rival.FinishPlace > 0) readout.Append($"P{rival.FinishPlace} ");
                readout.Append($"{gap,8:+0;-0} m  {rival.Speed * 3.6f,6:0} km/h  pace {Pace(rival) * 3.6f:0}  thrust {Thrust(rival):0.0}");
                readout.Append($"  hull {rival.RamHull}");
                if (rival.Driver.Mistake.Length > 0) readout.Append($"  MISTAKE:{rival.Driver.Mistake}");
                if (rival.WreckNote.Length > 0) readout.Append($"  WRECK {rival.WreckNote}");
                readout.Append($"  avg full {rival.FullSimAverage * 3.6f:0} / ghost {rival.GhostAverage * 3.6f:0}\n");
            }
            readoutText = readout.ToString();
        }
    }
}
