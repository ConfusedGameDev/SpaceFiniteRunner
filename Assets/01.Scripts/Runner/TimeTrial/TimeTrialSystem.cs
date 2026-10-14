using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Contracts;
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
    /// <see cref="TrackPlacementKind.Checkpoint"/> records when it has any,
    /// else <see cref="TimeTrialDefinition.autoCount"/> spread evenly to the
    /// final run-up and slid off every feature keep-out and laser gate. Each
    /// one's award is the time to the NEXT one (the last: to the end) at
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

        readonly List<float> checkpoints = new();
        readonly List<float> awards = new();
        readonly List<sbyte> passedClean = new(); // per checkpoint: -1 pending, 0 passed dirty, 1 passed clean
        readonly List<(float start, float end)> blocked = new();
        readonly List<Gate> gates = new();
        Transform gateRoot;

        bool layoutPending;
        int layoutRevision = -1;
        int nextCheckpoint;   // the index of the next checkpoint to cross
        bool hitSinceLast;    // anything that breaks a clean checkpoint, since the last one
        int cleanCount, patrolKills, livesLost;
        float banked;         // seconds the trial added this run, net

        sealed class Gate
        {
            public GameObject root;
            public Material material;
            public sbyte shown = -2; // the state the colour was last set for
        }

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
        public float CheckpointDistance(int index) => checkpoints[index];
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
            foreach (var gate in gates) gate.root.SetActive(false);
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
            Adjust(def.patrolKillBonus, TimeAwardKind.PatrolKill);
        }

        /// <summary>A life was spent and the run goes on (R3.3): the penalty, and the clean run broken.</summary>
        public void NotifyLifeLost()
        {
            if (!IsArcade) return;
            livesLost++;
            hitSinceLast = true;
            Adjust(-def.lifePenaltySeconds, TimeAwardKind.LifeLost);
        }

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
            while (nextCheckpoint < checkpoints.Count && at >= checkpoints[nextCheckpoint])
            {
                // A respawn that skipped several pays each one, but only the
                // first could ever have been flown clean.
                Cross(ship.Speed);
                // The rest of a skipped run were never flown at all.
                if (nextCheckpoint < checkpoints.Count && at >= checkpoints[nextCheckpoint]) hitSinceLast = true;
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
            float start = Mathf.Min(def.firstCheckpointMeters, limit * 0.5f);

            bool authored = false;
            if (generator != null)
                foreach (var placement in generator.Placements)
                    if (placement.kind == TrackPlacementKind.Checkpoint && placement.distance > 0f && placement.distance < limit)
                    {
                        checkpoints.Add(placement.distance);
                        authored = true;
                    }

            if (authored) checkpoints.Sort();
            else LayAuto(start, limit);

            // Until crossed, an award reads at the pace floor: the most it can be.
            for (int i = 0; i < checkpoints.Count; i++)
            {
                awards.Add(AwardAt(i, 0f));
                passedClean.Add(-1);
            }

            BuildGates();
            if (def.debugLog)
                Debug.Log($"[TimeTrial] {checkpoints.Count} checkpoints ({(authored ? "authored" : "auto")}): " +
                          string.Join(", ", checkpoints.ConvertAll(d => $"{d:0}m")), this);
        }

        // The time from checkpoint `index` to the next (the last: to the end)
        // at the pace a ship crossing at `speed` m/s is held to, plus grace.
        float AwardAt(int index, float speed)
        {
            float next = index + 1 < checkpoints.Count ? checkpoints[index + 1] : track.EndDistance;
            float pace = Mathf.Max(speed * def.paceShare, def.paceFloorKmh / 3.6f, 1f);
            return Mathf.Clamp((next - checkpoints[index]) / pace + def.graceSeconds, def.MinAward, def.MaxAward);
        }

        // The next checkpoint, crossed at `speed`: its award, sized now, and the
        // clean bonus when nothing was hit since the last one.
        void Cross(float speed)
        {
            bool clean = !hitSinceLast;
            int index = nextCheckpoint++;
            passedClean[index] = (sbyte)(clean ? 1 : 0);
            awards[index] = AwardAt(index, speed);
            Adjust(awards[index], TimeAwardKind.Checkpoint);
            if (clean)
            {
                cleanCount++;
                Adjust(def.cleanCheckpointBonus, TimeAwardKind.CleanBonus);
            }
            hitSinceLast = false;
        }

        // Evenly from the first spot to the run-up, each slid off what the road
        // claims (D7): forward past a keep-out, else back, else dropped.
        void LayAuto(float start, float limit)
        {
            CollectBlocked(limit);
            int count = Mathf.Max(1, def.autoCount);
            float span = limit - start;
            if (span <= 0f) return;
            float step = count > 1 ? span / count : span;
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float spot = start + step * i;
                float slideRoom = step * 0.45f;
                float placed = Clear(spot, spot + slideRoom, limit, forward: true);
                if (float.IsNaN(placed)) placed = Clear(spot, Mathf.Max(previous + 1f, spot - slideRoom), limit, forward: false);
                if (float.IsNaN(placed) || placed <= previous + 1f) continue;
                checkpoints.Add(placed);
                previous = placed;
            }
        }

        // The first free spot from `from` toward `to` (either direction), or NaN.
        float Clear(float from, float to, float limit, bool forward)
        {
            float spot = from;
            for (int guard = 0; guard < 64; guard++)
            {
                if (forward ? spot > to : spot < to) return float.NaN;
                if (spot <= 0f || spot >= limit) return float.NaN;
                int hit = BlockedAt(spot);
                if (hit < 0) return spot;
                spot = forward ? blocked[hit].end + 1f : blocked[hit].start - 1f;
            }
            return float.NaN;
        }

        int BlockedAt(float distance)
        {
            for (int i = 0; i < blocked.Count; i++)
                if (distance >= blocked[i].start && distance <= blocked[i].end) return i;
            return -1;
        }

        // Everything a checkpoint keeps off, widened by the clearance: the
        // generator's keep-outs (features, landings, the run-up) and sections,
        // read by sampling its own predicate, plus every decided laser gate.
        void CollectBlocked(float limit)
        {
            blocked.Clear();
            float clearance = def.featureClearance;
            const float Stride = 25f;
            float runStart = -1f;
            for (float d = 0f; d <= limit + Stride; d += Stride)
            {
                bool clear = TrackHazards.IsEncounterGroundClear(track, generator, d, d) || d >= limit;
                if (!clear && runStart < 0f) runStart = d;
                else if (clear && runStart >= 0f)
                {
                    blocked.Add((runStart - Stride - clearance, d + clearance));
                    runStart = -1f;
                }
            }
            if (generator != null)
                foreach (var placement in generator.Placements)
                    if (placement.kind == TrackPlacementKind.LaserGate || placement.kind == TrackPlacementKind.Ramp)
                        blocked.Add((placement.distance - clearance, placement.distance + clearance));
        }

        // ---- gates ----

        void BuildGates()
        {
            if (gateRoot == null)
            {
                gateRoot = new GameObject("CheckpointGates").transform;
                gateRoot.SetParent(transform, false);
            }
            while (gates.Count < checkpoints.Count) gates.Add(CreateGate(gates.Count));
            foreach (var gate in gates)
            {
                gate.root.SetActive(false);
                gate.shown = -2;
            }
        }

        Gate CreateGate(int index)
        {
            var gate = new Gate { root = new GameObject($"Checkpoint_{index}") };
            gate.root.transform.SetParent(gateRoot, false);
            gate.root.SetActive(false);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            gate.material = shader != null ? new Material(shader) { name = "CheckpointGate (runtime)" } : null;

            if (def.gatePrefab != null)
            {
                var visual = Instantiate(def.gatePrefab, gate.root.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Destroy(c);
                if (gate.material != null)
                    foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = gate.material;
                return gate;
            }

            // Two pylons and a beam overhead, sized to the road at layout (unit
            // cubes, scaled in Place).
            for (int part = 0; part < 3; part++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = part == 0 ? "PylonL" : part == 1 ? "PylonR" : "Beam";
                cube.transform.SetParent(gate.root.transform, false);
                Destroy(cube.GetComponent<Collider>());
                var r = cube.GetComponent<Renderer>();
                if (gate.material != null) r.sharedMaterial = gate.material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return gate;
        }

        void Place(Gate gate, float distance)
        {
            track.GetPoseAtDistance(distance, 0f, out Vector3 position, out Quaternion rotation);
            gate.root.transform.SetPositionAndRotation(position, rotation);
            if (def.gatePrefab != null) return;

            track.GetLateralBand(distance, out float min, out float max);
            float half = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max)) + 2f;
            float t = def.gateThickness, h = def.gateHeight;
            Transform root = gate.root.transform;
            for (int i = 0; i < root.childCount && i < 3; i++)
            {
                Transform part = root.GetChild(i);
                if (i < 2)
                {
                    part.localPosition = new Vector3((i == 0 ? -1f : 1f) * half, h * 0.5f - 1f, 0f);
                    part.localScale = new Vector3(t, h, t);
                }
                else
                {
                    part.localPosition = new Vector3(0f, h - 1f, 0f);
                    part.localScale = new Vector3(half * 2f + t, t, t);
                }
            }
        }

        void UpdateGates()
        {
            float at = ship.DisplayDistance;
            float built = generator != null ? generator.SettledDistance : float.PositiveInfinity;
            for (int i = 0; i < checkpoints.Count && i < gates.Count; i++)
            {
                Gate gate = gates[i];
                float d = checkpoints[i];
                bool show = d <= built && d >= at - GateBehindMeters;
                if (gate.root.activeSelf != show)
                {
                    if (show) Place(gate, d);
                    gate.root.SetActive(show);
                }
                if (!show || gate.material == null) continue;
                sbyte state = passedClean[i];
                if (gate.shown == state) continue;
                gate.shown = state;
                Color color = state < 0 ? def.gatePendingColor : state > 0 ? def.gateCleanColor : def.gateDirtyColor;
                gate.material.SetColor(BaseColorId, color);
            }
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void ClearGates()
        {
            foreach (var gate in gates)
            {
                if (gate.root != null) Destroy(gate.root);
                if (gate.material != null) Destroy(gate.material);
            }
            gates.Clear();
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
            while (nextCheckpoint < checkpoints.Count && checkpoints[nextCheckpoint] <= at) passedClean[nextCheckpoint++] = 0;
            if (def.debugLog) Debug.Log($"[TimeTrial] relaid ({passed} passed before, {nextCheckpoint} behind the ship now)", this);
        }
    }
}
