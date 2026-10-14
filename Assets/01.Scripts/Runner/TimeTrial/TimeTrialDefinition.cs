using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.TimeTrial
{
    /// <summary>The two time trials (TimeTrialPRD D2). Serialized — append only.</summary>
    public enum TimeTrialMode
    {
        /// <summary>A set time to reach the end ramps; nothing adds to it.</summary>
        BeatTheClock = 0,
        /// <summary>A short clock fed by checkpoints, clean flying and patrol kills.</summary>
        Arcade = 1,
    }

    /// <summary>
    /// A time trial (TimeTrialPRD.md): the clock a level is played against
    /// instead of the Light Speed goal. Beat the Clock is a fixed time to the
    /// end ramps; Arcade starts nearly empty and every checkpoint adds the time
    /// it takes to reach the next one at a reference pace, plus seconds for a
    /// clean checkpoint and a patrol kill, minus seconds for a life lost. A
    /// level opts in through <c>RunnerLevelDefinition.timeTrial</c>; the
    /// <see cref="TimeTrialSystem"/> clones it at bind, so gameplay never
    /// writes it and the debug menu edits the asset then mirrors onto the
    /// clone. Every time trial tunable lives here, not on the system.
    /// </summary>
    [CreateAssetMenu(fileName = "TimeTrialDefinition", menuName = "FiniteRunner/Time Trial Definition")]
    public class TimeTrialDefinition : ScriptableObject
    {
        [TitleGroup("Mode")]
        [Tooltip("Beat the Clock: a set time to reach the end ramps. Arcade: a short clock that checkpoints, clean flying and patrol kills feed, and lost lives drain.")]
        [EnumToggleButtons, HideLabel]
        public TimeTrialMode mode = TimeTrialMode.BeatTheClock;

        // ---- clock ----
        [TitleGroup("Clock")]
        [ShowIf(nameof(IsBeatTheClock))]
        [Tooltip("The whole time to reach the end ramps, seconds. 0 = the saved track's own time limit, else GameSettings.timeLimitSeconds.")]
        [PropertyRange(0f, 600f), SuffixLabel("s", true)]
        public float timeLimitSeconds = 90f;

        [TitleGroup("Clock")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("What the clock starts on, seconds — enough to reach the first checkpoint at the reference pace, and little more.")]
        [PropertyRange(3f, 120f), SuffixLabel("s", true)]
        public float startSeconds = 20f;

        [TitleGroup("Clock")]
        [Tooltip("The most the clock can hold, seconds: banking time is bounded, arcade style. Also caps Beat the Clock's start.")]
        [PropertyRange(10f, 999f), SuffixLabel("s", true)]
        public float maxClockSeconds = 99.9f;

        [TitleGroup("Clock")]
        [Tooltip("Below this the HUD clock turns red and pulses, seconds.")]
        [PropertyRange(0f, 30f), SuffixLabel("s", true)]
        public float lowTimeWarningSeconds = 5f;

        // ---- checkpoints ----
        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("How many checkpoints a track WITHOUT authored ones gets, spread evenly from the first checkpoint distance to the start of the final run-up. A saved track's own Checkpoint placements always win.")]
        [PropertyRange(1, 40)]
        public int autoCount = 8;

        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Where the first auto checkpoint may stand at the earliest, metres from the start.")]
        [PropertyRange(500f, 20000f), SuffixLabel("m", true)]
        public float firstCheckpointMeters = 2500f;

        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("An auto checkpoint keeps this far from a loop, a tube, a ramp and its landing, and a laser gate, metres. It slides forward past one, else back.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float featureClearance = 150f;

        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("A checkpoint's award is the time to the next one at this share of the speed the ship CROSSES it at (never below the floor below), plus the grace: keep the pace you came in with and you make it. Below 1 is kinder — it allows for slowing down; above 1 asks you to speed up.")]
        [PropertyRange(0.3f, 2f)]
        public float paceShare = 0.9f;

        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("The slowest pace an award is ever sized for, km/h: a checkpoint crossed crawling still pays the time to the next one at this speed.")]
        [PropertyRange(100f, 6000f), SuffixLabel("km/h", true)]
        public float paceFloorKmh = 900f;

        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Seconds added to every checkpoint's award on top of the travel time — the slack for a fall, a sweep braked for, a bad line.")]
        [PropertyRange(0f, 20f), SuffixLabel("s", true)]
        public float graceSeconds = 3f;

        [TitleGroup("Checkpoints")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("A checkpoint's award is never less / more than this band, seconds.")]
        [MinMaxSlider(1f, 120f, true)]
        public Vector2 awardBand = new(4f, 40f);

        // ---- bonuses ----
        [TitleGroup("Bonuses")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Added on reaching a checkpoint with no hit since the last one (hull damage, a beam, a car, a fall, a life), seconds.")]
        [PropertyRange(0f, 20f), SuffixLabel("s", true)]
        public float cleanCheckpointBonus = 2f;

        [TitleGroup("Bonuses")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Added when a patrol cruiser is destroyed — the finisher, or steered into a laser beam, seconds.")]
        [PropertyRange(0f, 30f), SuffixLabel("s", true)]
        public float patrolKillBonus = 3f;

        [TitleGroup("Bonuses")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Taken away when a life is lost (the hull reaches 0, or an arrest) — the ship respawns down the track, seconds.")]
        [PropertyRange(0f, 30f), SuffixLabel("s", true)]
        public float lifePenaltySeconds = 5f;

        [TitleGroup("Bonuses")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("A cruiser an oncoming car destroys pays the kill bonus too. Off: only the player's own kills pay.")]
        public bool trafficKillPays;

        [TitleGroup("Bonuses")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Plain wall contact (the scrape) breaks a clean checkpoint as well. Off: only hard hits, beams, cars, falls and lives do.")]
        public bool scrapeBreaksClean;

        // ---- hazards ----
        [TitleGroup("Hazards")]
        [Tooltip("The patrol hunts in this level (and GameSettings.patrolEnabled is on). Arcade needs it for the kill bonus.")]
        public bool patrolEnabled = true;

        // ---- gates ----
        [TitleGroup("Gate")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("A checkpoint gate's picture, scaled across the road; empty = two code-built pylons and a beam overhead. Colliders are stripped — a checkpoint is judged on distance.")]
        public GameObject gatePrefab;

        [TitleGroup("Gate")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Height of the code-built gate's beam above the road, metres.")]
        [PropertyRange(5f, 80f), SuffixLabel("m", true)]
        public float gateHeight = 28f;

        [TitleGroup("Gate")]
        [ShowIf(nameof(IsArcade))]
        [Tooltip("Thickness of the code-built pylons and beam, metres.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("m", true)]
        public float gateThickness = 2.5f;

        [TitleGroup("Gate")]
        [ShowIf(nameof(IsArcade))]
        [ColorUsage(false, true)] public Color gatePendingColor = new(0.2f, 0.8f, 2.5f);
        [TitleGroup("Gate")]
        [ShowIf(nameof(IsArcade))]
        [ColorUsage(false, true)] public Color gateCleanColor = new(0.3f, 2.5f, 0.5f);
        [TitleGroup("Gate")]
        [ShowIf(nameof(IsArcade))]
        [ColorUsage(false, true)] public Color gateDirtyColor = new(2.5f, 1.4f, 0.2f);

        // ---- debug ----
        [TitleGroup("Debug")]
        [Tooltip("Logs every checkpoint laid and every clock change ([TimeTrial]).")]
        public bool debugLog;

        public bool IsArcade => mode == TimeTrialMode.Arcade;
        public bool IsBeatTheClock => mode == TimeTrialMode.BeatTheClock;
        public float MinAward => Mathf.Min(awardBand.x, awardBand.y);
        public float MaxAward => Mathf.Max(awardBand.x, awardBand.y);
    }
}
