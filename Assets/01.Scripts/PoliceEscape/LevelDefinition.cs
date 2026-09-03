using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using ConfusedGameDev.FiniteRunner.SaveData;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>The things a level can ask of the player. Order is the save format — append only.</summary>
    public enum ObjectiveType { ReachSpeed = 0, EscapePolice = 1, GoToTarget = 2, SurviveTime = 3, ChaseCar = 4, DestroyCars = 5, CollectObjects = 6, Jump = 7 }

    /// <summary>What a Jump step measures a jump by. Order is the save format — append only.</summary>
    public enum JumpMeasure { Distance = 0, AirTime = 1 }

    /// <summary>
    /// How the objective list completes. <see cref="Independent"/>: steps
    /// complete in order and a finished step stays finished.
    /// <see cref="AllMustHold"/>: steps still complete in order, but an earlier
    /// state step (speed, police) can regress — the level falls back to it and
    /// every later step's progress resets — so the level only completes when
    /// everything holds at once. Order is the save format — append only.
    /// </summary>
    public enum CompletionMode { Independent = 0, AllMustHold = 1 }

    /// <summary>
    /// The optional clock on a step. <see cref="CompleteWithin"/> is a
    /// deadline: the step's condition must be met within the seconds, or
    /// the run is lost ("chase the car down in under 60 s").
    /// <see cref="HoldFor"/> is a sustain: the condition must stay true for
    /// the whole span, and a lapse restarts the count ("stay above 130 km/h
    /// for 60 s"). Order is the save format — append only.
    /// </summary>
    public enum TimeRule { None = 0, CompleteWithin = 1, HoldFor = 2 }

    /// <summary>
    /// One step of a level. A plain enum-typed entry rather than a polymorphic
    /// class: the designer picks the type and only that type's knobs show, the
    /// list drags and reorders with the stock drawer, and the data stays
    /// trivially serializable. Speed and time are the "adjustable" parameters
    /// the debug menu exposes; the target id is resolved at runtime against
    /// the scene's <see cref="TargetObject"/>s. Runtime progress never lives
    /// here — the asset is shared and read live by the LevelManager.
    /// </summary>
    [System.Serializable]
    public class LevelObjective
    {
        [EnumToggleButtons, HideLabel]
        public ObjectiveType type = ObjectiveType.ReachSpeed;

        [ShowIf("type", ObjectiveType.ReachSpeed)]
        [Tooltip("Speed the player must reach (and, in All-Must-Hold levels, keep).")]
        [PropertyRange(50f, 300f), SuffixLabel("km/h", true)]
        public float targetSpeedKmh = 130f;

        [ShowIf("type", ObjectiveType.SurviveTime)]
        [Tooltip("Seconds this step must stay active without the level resetting. The timer only runs while the step is current.")]
        [PropertyRange(5f, 300f), SuffixLabel("s", true)]
        public float surviveSeconds = 30f;

        [ShowIf("@this.type == ObjectiveType.GoToTarget || this.type == ObjectiveType.ChaseCar")]
        [Tooltip("Go To: id of the TargetObject placed in the scene (unknown ids never complete — the HUD flags them). Chase Car: the id given to the escaping car — the LevelManager promotes a nearby traffic car to it when the step activates.")]
        public string targetId = "";

        [ShowIf("type", ObjectiveType.GoToTarget)]
        [Tooltip("Horizontal distance to the target that counts as arriving.")]
        [PropertyRange(3f, 60f), SuffixLabel("m", true)]
        public float arriveRadius = 12f;

        [ShowIf("type", ObjectiveType.EscapePolice)]
        [Tooltip("Off: the step completes the moment no patrol is hunting (even if none ever was). On: a pursuit has to be seen first, then shaken.")]
        public bool mustBeHuntedFirst;

        // Destroy Cars: how many, and which. Kind and paint are FILTERS —
        // Unknown means "any" — so "5 cars", "5 buses", "5 red cars" and
        // "5 red trucks" are all the same step with different filters.
        [ShowIf("type", ObjectiveType.DestroyCars)]
        [Tooltip("How many matching cars must die while this step is active. Chain explosions count — leading a cruiser into a wreck is the point.")]
        [PropertyRange(1, 50)]
        public int destroyCount = 5;

        [ShowIf("type", ObjectiveType.DestroyCars)]
        [Tooltip("Only this kind of vehicle counts. Unknown = any kind.")]
        public VehicleKind destroyKind = VehicleKind.Unknown;

        [ShowIf("type", ObjectiveType.DestroyCars)]
        [Tooltip("Only this paint counts. Unknown = any colour.")]
        public VehiclePaint destroyPaint = VehiclePaint.Unknown;

        // Collect Objects: how many pickups, and of which id. An empty id
        // means any Collectible counts.
        [ShowIf("type", ObjectiveType.CollectObjects)]
        [Tooltip("How many collectibles must be picked up while this step is active.")]
        [PropertyRange(1, 50)]
        public int collectCount = 3;

        [ShowIf("type", ObjectiveType.CollectObjects)]
        [Tooltip("Id of the Collectibles that count (the Collectible component's id). Empty = any collectible.")]
        public string collectId = "";

        // Jump: one landed jump that covers the distance or stays airborne
        // for the time — measured by the CityStatsRecorder off the player car
        // (every wheel off the ground), the same numbers the LOG records.
        [ShowIf("type", ObjectiveType.Jump)]
        [Tooltip("Distance: horizontal metres covered while every wheel is off the ground. Air Time: seconds in the air. One landed jump reaching the target completes the step.")]
        [EnumToggleButtons]
        public JumpMeasure jumpMeasure = JumpMeasure.Distance;

        [ShowIf("@this.type == ObjectiveType.Jump && this.jumpMeasure == JumpMeasure.Distance")]
        [Tooltip("Horizontal metres a single jump must cover.")]
        [PropertyRange(5f, 200f), SuffixLabel("m", true)]
        public float jumpMeters = 30f;

        [ShowIf("@this.type == ObjectiveType.Jump && this.jumpMeasure == JumpMeasure.AirTime")]
        [Tooltip("Seconds a single jump must stay in the air.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float jumpSeconds = 2f;

        // Time rule: the clock a step can carry on top of its condition.
        // Survive is itself a timer, so it takes none.
        [HideIf("type", ObjectiveType.SurviveTime)]
        [Tooltip("None: no clock. Complete Within: the step must be done before the seconds run out — miss it and the run is lost. Hold For: the condition must stay true for the whole span; a lapse restarts the count.")]
        [EnumToggleButtons]
        public TimeRule timeRule = TimeRule.None;

        [ShowIf(nameof(HasTimeRule))]
        [Tooltip("Complete Within: the deadline, counted from the moment the step activates. Hold For: how long the condition must hold without a break.")]
        [PropertyRange(5f, 600f), SuffixLabel("s", true)]
        public float timeSeconds = 60f;

        [Tooltip("Dialogue line shown when the step becomes active. {0} = the speed / seconds / target id. Leave empty for the built-in line.")]
        [MultiLineProperty(2)]
        public string briefing = "";

        [Tooltip("Dialogue accent and HUD tint for this step. Fully transparent = the type's default color.")]
        public Color accent = new(0.45f, 0.9f, 1f);

        // Money: every main objective pays its own reward on the Mission
        // Complete panel (on top of the level's flat bonus); a challenge
        // multiplies instead, so the field is hidden on one.
        [HideIf(nameof(IsChallenge))]
        [Tooltip("Money this objective pays on the Mission Complete panel. Added to the level's flat bonus before the challenge multipliers apply.")]
        [PropertyRange(0, 50000), SuffixLabel("$", true)]
        public int reward = 0;

        /// <summary>True on an <see cref="OptionalChallenge"/> — it carries a multiplier, not a reward.</summary>
        public virtual bool IsChallenge => false;

        // Cinema: an optional video played the moment the step activates,
        // before its briefing line, with the world frozen under it. The
        // duration is authoritative — a shorter one cuts the clip, a longer
        // one holds its last frame — so it is auto-filled from the clip but
        // stays a plain editable number.
        [ToggleGroup(nameof(hasCinema), "Cinema")]
        [Tooltip("Play a video when this step becomes active. The world freezes under it; a long press of Enter / A skips it.")]
        public bool hasCinema;

        [ToggleGroup(nameof(hasCinema))]
        [Tooltip("The clip. Assigning one sets the duration to its length; edit the duration afterwards if the cinema should run shorter or hold longer.")]
        [OnValueChanged(nameof(FetchDuration))]
        public VideoClip cinemaClip;

        [ToggleGroup(nameof(hasCinema))]
        [Tooltip("Display format, one of the rows of the Cinema Format Library asset (Resources/PoliceEscape_CinemaFormats).")]
        [ValueDropdown(nameof(CinemaFormatIds))]
        public string cinemaFormat = CinemaFormatLibrary.FullScreenId;

        [ToggleGroup(nameof(hasCinema))]
        [Tooltip("How long the cinema stays up. Auto-filled from the clip; shorter cuts the clip, longer holds its last frame.")]
        [PropertyRange(0.5f, 300f), SuffixLabel("s", true)]
        public float cinemaSeconds = 5f;

        /// <summary>Copies the clip's length into the duration — also run automatically whenever the clip field changes.</summary>
        [ToggleGroup(nameof(hasCinema))]
        [Button("Fetch Duration"), ShowIf("@cinemaClip != null")]
        public void FetchDuration()
        {
            if (cinemaClip == null || cinemaClip.length <= 0d) return;
            cinemaSeconds = Mathf.Clamp((float)cinemaClip.length, 0.5f, 300f);
        }

        /// <summary>A cinema plays only when the toggle is on AND a clip is assigned — a bare toggle briefs normally.</summary>
        public bool HasCinema => hasCinema && cinemaClip != null;

        // Completion message: an optional dialogue line the moment the step is
        // done. The world keeps running under it, but the level waits for it
        // to clear (then for the delay below) before the next step activates.
        [ToggleGroup(nameof(hasCompletionMessage), "Completion message")]
        [Tooltip("Speak a line when this step completes. The next step waits until it has cleared the screen.")]
        public bool hasCompletionMessage;

        [ToggleGroup(nameof(hasCompletionMessage))]
        [Tooltip("The line. {0} = the speed / seconds / count, {1} = the time rule's seconds, {2} = the Destroy Cars filter, {3} = the Collect Objects id.")]
        [MultiLineProperty(2)]
        public string completionMessage = "";

        [Tooltip("Pause after this step completes — and its completion message, if any, has cleared — before the next step activates and briefs. 0 = at once.")]
        [PropertyRange(0f, 30f), SuffixLabel("s", true)]
        public float nextDelaySeconds = 0f;

        /// <summary>A completion line plays only when the toggle is on AND there is text.</summary>
        public bool HasCompletionMessage => hasCompletionMessage && !string.IsNullOrWhiteSpace(completionMessage);

        /// <summary>The completion line with its placeholders filled (same {0}..{3} as the briefing).</summary>
        public string CompletionText => Format(completionMessage);

        /// <summary>Speed and time are the parameters the debug menu can slide.</summary>
        public bool HasAdjustableValue => type == ObjectiveType.ReachSpeed || type == ObjectiveType.SurviveTime || type == ObjectiveType.DestroyCars || type == ObjectiveType.CollectObjects || type == ObjectiveType.Jump || HasTimeRule;

        /// <summary>The Jump step's target in its own measure — metres or seconds.</summary>
        public float JumpTarget => jumpMeasure == JumpMeasure.AirTime ? jumpSeconds : jumpMeters;

        /// <summary>The Jump step's unit for readouts: "M" or "S".</summary>
        public string JumpUnit => jumpMeasure == JumpMeasure.AirTime ? "S" : "M";

        /// <summary>The Jump step's unit as a word for dialogue: "meters" or "seconds".</summary>
        public string JumpUnitWord => jumpMeasure == JumpMeasure.AirTime ? "seconds" : "meters";

        /// <summary>A landed jump's value in this step's measure.</summary>
        public float JumpValue(float meters, float seconds) => jumpMeasure == JumpMeasure.AirTime ? seconds : meters;

        /// <summary>The Destroy Cars filter as words — "RED TRUCK", "BUS", or "CARS" when anything counts.</summary>
        public string DestroyTargetText => VehicleIdentity.Describe(destroyKind, destroyPaint, "CARS");

        /// <summary>Does a dead car count toward this Destroy Cars step?</summary>
        public bool CountsKill(VehicleIdentity identity) =>
            type == ObjectiveType.DestroyCars && identity.Matches(destroyKind, destroyPaint);

        /// <summary>The Collect Objects target as words — the id upper-cased, or "ITEMS" when any collectible counts.</summary>
        public string CollectTargetText => string.IsNullOrWhiteSpace(collectId) ? "ITEMS" : collectId.Trim().ToUpperInvariant();

        /// <summary>Does a picked-up collectible count toward this Collect Objects step?</summary>
        public bool CountsCollectible(string id) =>
            type == ObjectiveType.CollectObjects
            && (string.IsNullOrWhiteSpace(collectId) || string.Equals(collectId.Trim(), id, System.StringComparison.Ordinal));

        /// <summary>The step carries a clock — a deadline or a sustain. Survive steps never do: they ARE the clock.</summary>
        public bool HasTimeRule => type != ObjectiveType.SurviveTime && timeRule != TimeRule.None;

        /// <summary>The step must be finished before <see cref="timeSeconds"/> run out.</summary>
        public bool HasDeadline => HasTimeRule && timeRule == TimeRule.CompleteWithin;

        /// <summary>The step's condition must stay true for <see cref="timeSeconds"/> in a row.</summary>
        public bool MustHold => HasTimeRule && timeRule == TimeRule.HoldFor;

        /// <summary>Inspector list label: the player-facing summary plus a cinema marker — never shown to the player.</summary>
        public string EditorLabel => Summary + (!IsChallenge && reward > 0 ? $" ${reward}" : "") + (HasCinema ? " [CINEMA]" : "") + (HasCompletionMessage ? " [MSG]" : "");

        /// <summary>Dropdown source for <see cref="cinemaFormat"/> — a member here, since an Odin expression cannot see the child namespace.</summary>
        static IEnumerable<string> CinemaFormatIds() => CinemaFormatLibrary.Ids();

        /// <summary>The accent to draw with — the authored one, or the type default when it was left transparent.</summary>
        public Color Accent => accent.a > 0.001f ? accent : DefaultAccent(type);

        /// <summary>The value that fills {0} in the briefing, as text.</summary>
        public string ValueText => type switch
        {
            ObjectiveType.ReachSpeed => targetSpeedKmh.ToString("0"),
            ObjectiveType.SurviveTime => surviveSeconds.ToString("0"),
            ObjectiveType.GoToTarget => string.IsNullOrEmpty(targetId) ? "?" : targetId,
            ObjectiveType.ChaseCar => string.IsNullOrEmpty(targetId) ? "?" : targetId,
            ObjectiveType.DestroyCars => destroyCount.ToString(),
            ObjectiveType.CollectObjects => collectCount.ToString(),
            ObjectiveType.Jump => jumpMeasure == JumpMeasure.AirTime ? jumpSeconds.ToString("0.0") : jumpMeters.ToString("0"),
            _ => ""
        };

        /// <summary>List element label in the inspector: the condition plus its clock, when it has one.</summary>
        public string Summary => BaseSummary + TimeSummary;

        string BaseSummary => type switch
        {
            ObjectiveType.ReachSpeed => $"REACH {targetSpeedKmh:0} KM/H",
            ObjectiveType.EscapePolice => mustBeHuntedFirst ? "ESCAPE POLICE (after a chase)" : "ESCAPE POLICE",
            ObjectiveType.GoToTarget => $"GO TO {(string.IsNullOrEmpty(targetId) ? "?" : targetId)}",
            ObjectiveType.SurviveTime => $"SURVIVE {surviveSeconds:0} S",
            ObjectiveType.ChaseCar => $"CHASE {(string.IsNullOrEmpty(targetId) ? "?" : targetId)}",
            ObjectiveType.DestroyCars => $"DESTROY {destroyCount} × {DestroyTargetText}",
            ObjectiveType.CollectObjects => $"COLLECT {collectCount} × {CollectTargetText}",
            ObjectiveType.Jump => $"JUMP {ValueText} {JumpUnit}",
            _ => type.ToString()
        };

        string TimeSummary => HasDeadline ? $" IN {timeSeconds:0} S" : MustHold ? $" FOR {timeSeconds:0} S" : "";

        /// <summary>
        /// The dialogue line for this step — the authored one with {0} (the
        /// value), {1} (the time rule's seconds), {2} (the Destroy Cars
        /// filter as words) and {3} (the Collect Objects id) filled, or the
        /// built-in default, which grows a clause for the clock when the step
        /// has one.
        /// </summary>
        public string BriefingText
        {
            get
            {
                string template = string.IsNullOrWhiteSpace(briefing) ? DefaultBriefing(type) + DefaultTimeClause : briefing;
                return Format(template);
            }
        }

        /// <summary>Fills a template's {0} value, {1} time-rule seconds, {2} Destroy Cars filter, {3} Collect Objects id and {4} Jump unit word.</summary>
        public string Format(string template)
        {
            try { return string.Format(template, ValueText, timeSeconds.ToString("0"), DestroyTargetText.ToLowerInvariant(), CollectTargetText.ToLowerInvariant(), JumpUnitWord); }
            catch (System.FormatException) { return template; } // a stray brace in authored text must not throw mid-run
        }

        string DefaultTimeClause => HasDeadline ? " You've got {1} seconds!" : MustHold ? " Keep it up for {1} seconds!" : "";

        public static Color DefaultAccent(ObjectiveType type) => type switch
        {
            ObjectiveType.EscapePolice => new Color(1f, 0.4f, 0.35f),
            ObjectiveType.GoToTarget => new Color(1f, 0.85f, 0.4f),
            ObjectiveType.SurviveTime => new Color(0.75f, 0.6f, 1f),
            ObjectiveType.ChaseCar => new Color(1f, 0.9f, 0.2f),
            ObjectiveType.DestroyCars => new Color(1f, 0.55f, 0.25f),
            ObjectiveType.CollectObjects => new Color(0.6f, 1f, 0.9f),
            ObjectiveType.Jump => new Color(0.55f, 0.75f, 1f),
            _ => new Color(0.45f, 0.9f, 1f)
        };

        public static string DefaultBriefing(ObjectiveType type) => type switch
        {
            ObjectiveType.ReachSpeed => "We need to get to {0} km/h!",
            ObjectiveType.EscapePolice => "We need to escape the police, NOW!",
            ObjectiveType.GoToTarget => "Get to {0} — I'll mark the distance.",
            ObjectiveType.SurviveTime => "Stay alive for {0} seconds!",
            ObjectiveType.ChaseCar => "Chase down {0} and take it out — don't let it get away!",
            ObjectiveType.DestroyCars => "Wreck {0} {2} — I don't care how.",
            ObjectiveType.CollectObjects => "Grab {0} {3} — they're scattered around, look for the glow.",
            ObjectiveType.Jump => "Find a ramp — I need a jump of {0} {4}!",
            _ => ""
        };
    }

    /// <summary>
    /// One optional goal offered on the mission brief: a full
    /// <see cref="LevelObjective"/> — the same types, knobs and clock as a
    /// main step — plus the reward multiplier completing it earns. Accepted
    /// challenges run in PARALLEL with the main list for the whole level
    /// (never sequential, never regressing): the LevelManager checks each
    /// every frame until it completes and latches, and a Complete Within
    /// deadline that runs out FAILS the challenge (its multiplier is lost)
    /// rather than the run. Only completed challenges multiply the payout at
    /// the end. A challenge's briefing line and cinema are ignored — the
    /// brief already presented it.
    /// </summary>
    [System.Serializable]
    public class OptionalChallenge : LevelObjective
    {
        [Tooltip("Reward multiplier for COMPLETING this challenge — the brief shows it as ×N. A failed or unaccepted challenge earns nothing.")]
        [PropertyRange(1, 20)]
        public int multiplier = 2;

        public override bool IsChallenge => true;

        /// <summary>The brief's row text and the HUD line: the condition plus its multiplier.</summary>
        public string ChallengeSummary => $"{Summary} ×{multiplier}";

        /// <summary>Inspector list label.</summary>
        public string ChallengeLabel => $"{EditorLabel} ×{multiplier}";
    }

    /// <summary>
    /// A city-chase level as data: the ordered objective list, how it
    /// completes, the dialogue framing and the scene handed over to at the
    /// end. The LevelManager reads this asset LIVE every frame (no runtime
    /// clone), which is what lets the debug menu's sliders apply instantly and
    /// persist straight into the asset, the same rule as the other city
    /// settings. Drag objectives to reorder them; each shows only its own
    /// type's knobs.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelDefinition", menuName = "PoliceEscape/Level Definition")]
    public class LevelDefinition : ScriptableObject
    {
        [TitleGroup("Level")]
        [Tooltip("Display name, for the designers and the debug menu.")]
        public string levelName = "Level 1";

        [TitleGroup("Level")]
        [Tooltip("Scene loaded once every objective is done — must be listed in Build Settings.")]
        public string nextSceneName = "FiniteRunner_Test";

        [TitleGroup("Level")]
        [Tooltip("Independent: a finished step stays finished. All Must Hold: speed / police steps can regress and pull the level back to them, resetting later steps — everything has to hold at once.")]
        [EnumToggleButtons]
        public CompletionMode mode = CompletionMode.Independent;

        [TitleGroup("Messages")]
        [Tooltip("Name shown next to the dialogue portrait.")]
        public string speakerName = "OPERATOR";

        [TitleGroup("Messages")]
        [Tooltip("How long each objective message stays on screen after typing out.")]
        [PropertyRange(1f, 10f), SuffixLabel("s", true)]
        public float messageHoldSeconds = 4f;

        [TitleGroup("Messages")]
        [Tooltip("Line shown when the last objective completes. The glitch handoff starts only after it disappears.")]
        [MultiLineProperty(2)]
        public string completionMessage = "Hack complete. LFG!";

        [TitleGroup("Messages")]
        [Tooltip("Line shown when a Complete Within step runs out of time. The game-over glitch starts only after it disappears.")]
        [MultiLineProperty(2)]
        public string timeUpMessage = "Too slow. We lost them.";

        [TitleGroup("Messages")]
        [Tooltip("Line shown when an accepted optional challenge's deadline runs out (a completed challenge speaks its own completion message instead). {0} = the challenge, {1} = its multiplier. Empty = no line.")]
        [MultiLineProperty(2)]
        public string challengeFailedMessage = "Forget {0} — that bonus is gone.";

        [TitleGroup("Messages")]
        [Tooltip("How long the screen holds at full corruption after the completion line before the next scene loads.")]
        [PropertyRange(0.2f, 4f), SuffixLabel("s", true)]
        public float completionGlitchHoldSeconds = 1.2f;

        [TitleGroup("Mission brief")]
        [Tooltip("Clip looping in the brief screen's video panel. Empty = the panel shows a dead NO SIGNAL screen.")]
        public VideoClip briefVideo;

        [TitleGroup("Mission brief")]
        [Tooltip("Flat mission bonus, paid on top of every objective's own reward. The sum is what the accepted challenges multiply.")]
        [PropertyRange(0, 100000), SuffixLabel("$", true)]
        public int baseReward = 1000;

        [TitleGroup("Mission brief")]
        [Tooltip("Money thresholds the mission total (this level + the escape run after it, multiplied) is ranked against on the Mission Complete panel.")]
        public RankTable rankTable = new();

        [TitleGroup("Mission brief")]
        [Tooltip("Extra goals offered on the brief, each a toggle the player can take on for a bigger payout. Full objectives (any type, any clock) that run beside the main list; only COMPLETED ones multiply the reward.")]
        [ListDrawerSettings(DraggableItems = true, ListElementLabelName = nameof(OptionalChallenge.ChallengeLabel))]
        public List<OptionalChallenge> optionalChallenges = new();

        [TitleGroup("Objectives")]
        [Tooltip("Played top to bottom. Drag to reorder.")]
        [ListDrawerSettings(DraggableItems = true, ShowIndexLabels = true, ListElementLabelName = nameof(LevelObjective.EditorLabel))]
        public List<LevelObjective> objectives = new();

        public int Count => objectives != null ? objectives.Count : 0;

        /// <summary>Every main objective's reward added up (challenges carry none).</summary>
        public long ObjectiveRewardTotal
        {
            get
            {
                long sum = 0;
                if (objectives != null) foreach (var step in objectives) if (step != null) sum += step.reward;
                return sum;
            }
        }

        /// <summary>The money on the table before any multiplier: the flat bonus plus every objective's reward.</summary>
        public long RewardBase => baseReward + ObjectiveRewardTotal;

        /// <summary>
        /// The pre-data flow (reach 130 km/h, then shake the police) as an
        /// in-memory level — the runtime fallback for a manager with no asset
        /// and the seed the scene builder writes into a fresh asset.
        /// </summary>
        public static LevelDefinition CreateDefault()
        {
            var level = CreateInstance<LevelDefinition>();
            level.name = "DefaultLevel";
            SeedDefaultObjectives(level);
            return level;
        }

        /// <summary>Fills an empty objective list with the default two steps; leaves an authored list alone.</summary>
        public static void SeedDefaultObjectives(LevelDefinition level)
        {
            if (level.objectives == null) level.objectives = new List<LevelObjective>();
            if (level.objectives.Count > 0) return;
            level.objectives.Add(new LevelObjective { type = ObjectiveType.ReachSpeed, targetSpeedKmh = 130f });
            level.objectives.Add(new LevelObjective { type = ObjectiveType.EscapePolice, accent = LevelObjective.DefaultAccent(ObjectiveType.EscapePolice) });
        }
    }
}
