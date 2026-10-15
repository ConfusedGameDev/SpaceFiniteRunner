using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.UI;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.TimeTrial
{
    /// <summary>
    /// A time trial level's checkpoints and clock rules (TimeTrialPRD.md).
    /// Hand-placed under ===SYSTEMS=== (PF_TimeTrialSystem); it finds nothing in
    /// the scene — the composition root (<see cref="GameManager"/>) binds it to
    /// the ship, the track, the level's <see cref="TimeTrialDefinition"/> and
    /// the run's clock (<see cref="ITimeTrialClock"/>), and unbound it idles.
    ///
    /// <b>The clock is never held here.</b> The run owns the countdown; this
    /// system only asks it for time — a checkpoint's award, a clean bonus, a
    /// kill bonus — or takes some back for a life, and raises
    /// <see cref="ClockAdjusted"/> with what was actually applied.
    ///
    /// <b>Checkpoints</b> (Arcade only, D6–D8) are laid once per decided track,
    /// on the first tick after a bind or a regenerate: the track's authored
    /// <see cref="TrackPlacementKind.Checkpoint"/> records when it has any
    /// (each with its own optional overrides, <see cref="TimeTrialCheckpoint"/>),
    /// else <see cref="TimeTrialDefinition.autoCount"/> spread evenly to the
    /// final run-up and slid off every feature keep-out and laser gate — the
    /// same layout the editor's Lay Auto Checkpoints writes. Each
    /// one's award, unless fixed, is the time to the NEXT one (the last: to the end) at
    /// <see cref="TimeTrialDefinition.paceShare"/> of the speed the ship
    /// crosses it at, never slower than the pace floor, plus grace, inside the
    /// award band: hold the pace you came in with and you make it. (Sized off
    /// the ship's cruise at first — but the ship's thrust takes a minute and a
    /// half to reach cruise, its real speed is the orbs', and the first run
    /// timed out short of checkpoint one.) A checkpoint is crossed when
    /// the ship's track distance passes it — no trigger, the project's rule —
    /// and several crossed in one go (a respawn skip) each pay, none clean.
    /// The gates are pictures only, built once per checkpoint at layout and
    /// shown while the generator has built road under them.
    /// </summary>
    public class TimeTrialSystem : MonoBehaviour, ITimeTrialState
    {
        // How far behind the ship a passed gate stays drawn, metres.
        const float GateBehindMeters = 300f;

        IChaseTarget ship;
        TrackManager track;
        TrackGenerator generator;
        ITimeTrialClock clock;
        TimeTrialDefinition source;
        TimeTrialDefinition def; // the runtime clone gameplay reads

        readonly List<TimeTrialCheckpoint> checkpoints = new();
        readonly List<float> awards = new();
        readonly List<sbyte> passedClean = new(); // per checkpoint: -1 pending, 0 passed dirty, 1 passed clean
        readonly List<float> autoSpots = new();
        readonly List<CheckpointGate> gates = new();
        readonly List<sbyte> gateShown = new(); // per gate: the state its colour was last set for
        readonly List<int> gateLabelKey = new(); // per gate: what its label was last written for
        Transform gateRoot;   // the system's own parent, when the track has no TimeTrialCheckpoints
        MenuTextLibrary library;

        bool layoutPending;
        int layoutRevision = -1;
        int nextCheckpoint;   // the index of the next checkpoint to cross
        bool hitSinceLast;    // anything that breaks a clean checkpoint, since the last one
        int cleanCount, patrolKills, livesLost;
        float banked;         // seconds the trial added this run, net

        /// <summary>The system bound to the running level, for the debug page; null when no level has a time trial.</summary>
        public static TimeTrialSystem Live { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() => Live = null; // domain reload is off

        /// <summary>True while bound to a level with a time trial.</summary>
        public bool Bound => def != null && ship != null && track != null && clock != null;
        /// <summary>The live clone (the debug menu mirrors onto it); null unbound.</summary>
        public TimeTrialDefinition Definition => def;
        /// <summary>The authored asset the clone was made from — what the debug page writes.</summary>
        public TimeTrialDefinition DefinitionAsset => source;
        /// <summary>Seconds the trial has added to the clock this run, net of penalties.</summary>
        public float SecondsBanked => banked;

        // ---- ITimeTrialState ----
        public bool TrialActive => Bound;
        public bool IsArcade => Bound && def.IsArcade;
        public float LowTimeSeconds => def != null ? def.lowTimeWarningSeconds : 0f;
        public int CheckpointCount => IsArcade ? checkpoints.Count : 0;
        public int CheckpointsPassed => IsArcade ? nextCheckpoint : 0;
        public int CleanCheckpoints => cleanCount;
        public float CheckpointDistance(int index) => checkpoints[index].distance;
        public float CheckpointAward(int index) => awards[index];
        public int PatrolKills => patrolKills;
        public int LivesLost => livesLost;
        public event System.Action<float, TimeAwardKind> ClockAdjusted;

        [ShowInInspector, ReadOnly]
        string Readout => !Bound ? "unbound"
            : !def.IsArcade ? "beat the clock"
            : $"cp {nextCheckpoint}/{checkpoints.Count}  clean {cleanCount}  kills {patrolKills}  lives -{livesLost}  banked {banked:0.0}s  {(hitSinceLast ? "dirty" : "clean")}";

        /// <summary>
        /// Binds the system to a run. A null <paramref name="definition"/> (the
        /// level has no time trial) unbinds it, so it idles.
        /// </summary>
        public void Bind(IChaseTarget ship, TrackManager track, TrackGenerator generator,
                         TimeTrialDefinition definition, ITimeTrialClock clock)
        {
            Unsubscribe();
            if (definition == null || ship == null || track == null || clock == null)
            {
                Unbind();
                return;
            }

            this.ship = ship;
            this.track = track;
            this.generator = generator;
            this.clock = clock;
            if (generator != null) generator.Regenerated += ResetForRun;

            if (definition != source || def == null)
            {
                ClearGates();
                source = definition;
                if (def != null) Destroy(def);
                def = Instantiate(definition);
            }
            Live = this;
            ResetForRun();
        }

        /// <summary>Drops the time trial: gates torn down, the system idle.</summary>
        public void Unbind()
        {
            if (Live == this) Live = null;
            Unsubscribe();
            ClearGates();
            checkpoints.Clear();
            awards.Clear();
            passedClean.Clear();
            ship = null;
            track = null;
            generator = null;
            clock = null;
            source = null;
            if (def != null) Destroy(def);
            def = null;
        }

        /// <summary>A fresh run: counters cleared, the checkpoints laid again on the next tick (the track may be mid-rebuild now).</summary>
        public void ResetForRun()
        {
            nextCheckpoint = 0;
            hitSinceLast = false;
            cleanCount = patrolKills = livesLost = 0;
            banked = 0f;
            layoutPending = true;
            layoutRevision = -1;
            for (int i = 0; i < passedClean.Count; i++) passedClean[i] = -1;
            foreach (var gate in gates) if (gate != null) gate.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Unbind();
            if (gateRoot != null) Destroy(gateRoot.gameObject);
        }

        void Unsubscribe()
        {
            if (generator != null) generator.Regenerated -= ResetForRun;
        }

        // ---- what the run reports (GameManager) ----

        /// <summary>Something broke the clean run to the next checkpoint: a hull hit, a beam, a car, a fall (R3.1).</summary>
        public void NotifyHit()
        {
            if (Bound) hitSinceLast = true;
        }

        /// <summary>A plain wall scrape: breaks the clean run only with <see cref="TimeTrialDefinition.scrapeBreaksClean"/>.</summary>
        public void NotifyScrape()
        {
            if (Bound && def.scrapeBreaksClean) hitSinceLast = true;
        }

        /// <summary>A patrol cruiser was destroyed (R3.2): the kill bonus, by cause (D10).</summary>
        public void NotifyPatrolKill(PatrolKillCause cause)
        {
            if (!IsArcade) return;
            if (cause == PatrolKillCause.Traffic && !def.trafficKillPays) return;
            patrolKills++;
            Adjust(Ahead().KillBonusOr(def), TimeAwardKind.PatrolKill);
        }

        /// <summary>A life was spent and the run goes on (R3.3): the penalty, and the clean run broken.</summary>
        public void NotifyLifeLost()
        {
            if (!IsArcade) return;
            livesLost++;
            hitSinceLast = true;
            Adjust(-Ahead().LifePenaltyOr(def), TimeAwardKind.LifeLost);
        }

        // The checkpoint the ship is flying toward: its kill / life overrides
        // cover the stretch up to it. Past the last (or none laid), the defaults.
        TimeTrialCheckpoint Ahead() =>
            !layoutPending && nextCheckpoint < checkpoints.Count ? checkpoints[nextCheckpoint] : default;

        void Adjust(float seconds, TimeAwardKind kind)
        {
            if (Mathf.Approximately(seconds, 0f) || clock == null) return;
            float applied = clock.AddTime(seconds);
            if (Mathf.Approximately(applied, 0f)) return;
            banked += applied;
            if (def.debugLog) Debug.Log($"[TimeTrial] {kind} {applied:+0.0;-0.0}s → {clock.TimeRemaining:0.0}s", this);
            ClockAdjusted?.Invoke(applied, kind);
        }

        // ---- the tick ----

        void Update()
        {
            if (!Bound || !def.IsArcade) return;

            if (layoutPending || layoutRevision != track.Revision) TryLayOut();
            if (layoutPending) return;

            if (!ship.Paused) CrossCheckpoints();
            UpdateGates();
        }

        void CrossCheckpoints()
        {
            float at = ship.Distance;
            bool flown = true;
            while (nextCheckpoint < checkpoints.Count && at >= checkpoints[nextCheckpoint].distance)
            {
                // A respawn that skipped several pays each one, but only the
                // first could ever have been flown clean — and only the first
                // charges a dirty penalty: the rest were never flown at all.
                Cross(ship.Speed, flown);
                flown = false;
                if (nextCheckpoint < checkpoints.Count && at >= checkpoints[nextCheckpoint].distance) hitSinceLast = true;
            }
        }

        // ---- layout ----

        void TryLayOut()
        {
            // The finite track is decided whole at run start; until its end zone
            // exists there is nothing to lay checkpoints against.
            if (track.EndZoneStart < 0f || !track.HasEnd) return;
            LayOut();
            layoutPending = false;
            layoutRevision = track.Revision;
        }

        void LayOut()
        {
            checkpoints.Clear();
            awards.Clear();
            passedClean.Clear();
            nextCheckpoint = 0;

            float limit = track.EndZoneStart;

            bool authored = false;
            if (generator != null)
                foreach (var placement in generator.Placements)
                    if (placement.kind == TrackPlacementKind.Checkpoint && placement.distance > 0f && placement.distance < limit)
                    {
                        checkpoints.Add(TimeTrialCheckpoint.FromPlacement(placement));
                        authored = true;
                    }

            if (authored) checkpoints.Sort((a, b) => a.distance.CompareTo(b.distance));
            else
            {
                autoSpots.Clear();
                TimeTrialCheckpoint.LayAuto(track, generator, generator != null ? generator.Placements : null, def, limit, autoSpots);
                foreach (float spot in autoSpots) checkpoints.Add(TimeTrialCheckpoint.At(spot));
            }

            // Until crossed, an award reads at the pace floor: the most it can be.
            for (int i = 0; i < checkpoints.Count; i++)
            {
                awards.Add(AwardAt(i, 0f));
                passedClean.Add(-1);
            }

            BuildGates();
            if (def.debugLog)
                Debug.Log($"[TimeTrial] {checkpoints.Count} checkpoints ({(authored ? "authored" : "auto")}): " +
                          string.Join(", ", checkpoints.ConvertAll(c => $"{c.distance:0}m{(c.set != 0 ? "*" : "")}")), this);
        }

        // The time from checkpoint `index` to the next (the last: to the end)
        // at the pace a ship crossing at `speed` m/s is held to, plus grace,
        // banded then scaled — or the checkpoint's own fixed award, which no
        // pace, band or scale touches.
        float AwardAt(int index, float speed)
        {
            TimeTrialCheckpoint cp = checkpoints[index];
            if (cp.Has(TimeTrialCheckpoint.Overrides.Award)) return cp.award;
            float next = index + 1 < checkpoints.Count ? checkpoints[index + 1].distance : track.EndDistance;
            float pace = Mathf.Max(speed * def.paceShare, def.paceFloorKmh / 3.6f, 1f);
            return Mathf.Clamp((next - cp.distance) / pace + def.graceSeconds, def.MinAward, def.MaxAward) * def.awardScale;
        }

        // The next checkpoint, crossed at `speed`: its award, sized now, then
        // the clean bonus when nothing was hit since the last one, else the
        // dirty penalty (only for one actually `flown`, not a respawn skip).
        void Cross(float speed, bool flown = true)
        {
            bool clean = !hitSinceLast;
            int index = nextCheckpoint++;
            TimeTrialCheckpoint cp = checkpoints[index];
            passedClean[index] = (sbyte)(clean ? 1 : 0);
            awards[index] = AwardAt(index, speed);
            Adjust(awards[index], TimeAwardKind.Checkpoint);
            if (clean)
            {
                cleanCount++;
                Adjust(cp.CleanBonusOr(def), TimeAwardKind.CleanBonus);
            }
            else if (flown) Adjust(-cp.DirtyPenaltyOr(def), TimeAwardKind.DirtyPenalty);
            hitSinceLast = false;
        }

        // ---- gates ----

        // The gates stand under the track's TimeTrialCheckpoints object (next to
        // Pads), else under the system itself.
        Transform GateParent()
        {
            if (generator != null && generator.CheckpointParent != null) return generator.CheckpointParent;
            if (gateRoot == null)
            {
                gateRoot = new GameObject("TimeTrialCheckpoints").transform;
                gateRoot.SetParent(transform, false);
            }
            return gateRoot;
        }

        void BuildGates()
        {
            // A gate the track's regenerate took down (or a parent that moved) is built again.
            for (int i = gates.Count - 1; i >= 0; i--)
                if (gates[i] == null) { gates.RemoveAt(i); gateShown.RemoveAt(i); gateLabelKey.RemoveAt(i); }
            Transform parent = GateParent();
            while (gates.Count < checkpoints.Count)
            {
                var gate = CheckpointGate.Create(parent, $"Checkpoint_{gates.Count}", def.gatePrefab);
                gate.gameObject.SetActive(false);
                gates.Add(gate);
                gateShown.Add(-2);
                gateLabelKey.Add(int.MinValue);
            }
            for (int i = 0; i < gates.Count; i++)
            {
                gates[i].gameObject.SetActive(false);
                gateShown[i] = -2;
                gateLabelKey[i] = int.MinValue;
            }
        }

        void UpdateGates()
        {
            float at = ship.DisplayDistance;
            float built = generator != null ? generator.SettledDistance : float.PositiveInfinity;
            for (int i = 0; i < checkpoints.Count && i < gates.Count; i++)
            {
                CheckpointGate gate = gates[i];
                if (gate == null) continue;
                float d = checkpoints[i].distance;
                bool show = d <= built && d >= at - GateBehindMeters;
                if (gate.gameObject.activeSelf != show)
                {
                    if (show) gate.Place(track, d, def.gateHeight, def.gateThickness, def.labelGap, def.labelSize);
                    gate.gameObject.SetActive(show);
                }
                if (!show) continue;
                sbyte state = passedClean[i];
                if (gateShown[i] != state)
                {
                    gateShown[i] = state;
                    gate.SetColor(state < 0 ? def.gatePendingColor : state > 0 ? def.gateCleanColor : def.gateDirtyColor);
                }
                UpdateLabel(i, gate, state);
            }
        }

        // What crossing gate `index` pays: pending, the award at the ship's
        // speed NOW (or its fixed award) and the clean bonus — the dirty
        // penalty instead once the run to the next one is dirty; crossed, what
        // it paid. Rewritten only when the tenths shown change.
        void UpdateLabel(int index, CheckpointGate gate, sbyte state)
        {
            if (!def.showGateLabels) { gate.SetLabel(null); return; }
            TimeTrialCheckpoint cp = checkpoints[index];
            float award = state < 0 ? AwardAt(index, ship.Speed) : awards[index];
            bool dirty = state == 0 || (state < 0 && index == nextCheckpoint && hitSinceLast);
            float extra = dirty ? -cp.DirtyPenaltyOr(def) : cp.CleanBonusOr(def);
            int key = Mathf.RoundToInt(award * 10f) * 4096 + Mathf.RoundToInt(extra * 10f) * 4 + (dirty ? 1 : 0) + (state + 1) * 2;
            if (gateLabelKey[index] == key) return;
            gateLabelKey[index] = key;

            library ??= MenuTextLibrary.Load();
            string extraText = Mathf.Approximately(extra, 0f) || library == null ? null
                : string.Format(library.Get(dirty ? MenuTextId.TtDirty : MenuTextId.TtClean), CheckpointGate.Seconds(extra));
            gate.SetLabel(CheckpointGate.Compose(CheckpointGate.Seconds(award), def.labelAwardColor,
                                                 extraText, dirty ? def.labelDirtyColor : def.labelCleanColor));
        }

        void ClearGates()
        {
            foreach (var gate in gates)
                if (gate != null) Destroy(gate.gameObject);
            gates.Clear();
            gateShown.Clear();
            gateLabelKey.Clear();
        }

        // ---- debug ----

        /// <summary>Debug: puts the next checkpoint's award (and clean bonus) on the clock as if it were crossed now.</summary>
        [Button, HideInEditorMode]
        public void DebugCrossNext()
        {
            if (!IsArcade || layoutPending || nextCheckpoint >= checkpoints.Count) return;
            Cross(ship.Speed);
        }

        /// <summary>Debug: <paramref name="seconds"/> straight onto the clock — no floater, nothing banked.</summary>
        public void DebugAddTime(float seconds)
        {
            if (Bound) clock.AddTime(seconds);
        }

        /// <summary>Debug: lays the checkpoints again on the track as it stands (after a debug edit to the count or the pace).</summary>
        [Button, HideInEditorMode]
        public void DebugRelayout()
        {
            if (!Bound) return;
            int passed = nextCheckpoint;
            layoutPending = true;
            TryLayOut();
            float at = ship.Distance;
            while (nextCheckpoint < checkpoints.Count && checkpoints[nextCheckpoint].distance <= at) passedClean[nextCheckpoint++] = 0;
            if (def.debugLog) Debug.Log($"[TimeTrial] relaid ({passed} passed before, {nextCheckpoint} behind the ship now)", this);
        }
    }
}
