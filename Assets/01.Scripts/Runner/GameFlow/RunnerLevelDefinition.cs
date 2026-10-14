using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.SaveData;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>The things an escape run can ask of the pilot. Order is the save format — append only.</summary>
    public enum RunnerObjectiveType { ReachSpeed = 0, JumpCount = 1, RacePosition = 2 }

    /// <summary>What a Race Position objective asks (RaceModePRD R5.3). Serialized — append only.</summary>
    public enum RaceGoalMode { Win = 0, TopX = 1, Exactly = 2 }

    /// <summary>
    /// The run state an objective is judged against — widened from (speed,
    /// jumps) so a goal type can read the race too. <see cref="raceFinishPlace"/>
    /// is 0 until the ship's place is fixed at its lip: a race goal is judged
    /// there and never latched mid-run (D6).
    /// </summary>
    public struct ObjectiveContext
    {
        /// <summary>The ship's CURRENT speed, km/h as the run shows it.</summary>
        public float speedKmh;
        /// <summary>Ramps taken off from this run.</summary>
        public int jumps;
        /// <summary>The ship's live place in the race; 0 = no race.</summary>
        public int racePosition;
        /// <summary>The ship's finishing place, fixed at its lip; 0 = not finished.</summary>
        public int raceFinishPlace;
    }

    /// <summary>
    /// One goal of an escape run — the runner's twin of the city's
    /// <c>LevelObjective</c>: an enum-typed entry whose inspector shows only
    /// its own type's knobs, so new goal types stack into the same list
    /// without a class per type. A main objective pays its <see cref="reward"/>
    /// on the Mission Complete panel; a <see cref="RunnerOptionalChallenge"/>
    /// multiplies instead. Runtime progress never lives here — the
    /// <see cref="GameManager"/> keeps the tallies and asks
    /// <see cref="Satisfied"/> every frame.
    /// </summary>
    [System.Serializable]
    public class RunnerObjective
    {
        [EnumToggleButtons, HideLabel]
        public RunnerObjectiveType type = RunnerObjectiveType.ReachSpeed;

        [ShowIf("type", RunnerObjectiveType.ReachSpeed)]
        [Tooltip("Speed the ship must reach. The first MANDATORY Reach Speed objective is the run's Light Speed — the HUD goal and the speed-lines reference.")]
        [PropertyRange(100f, 10000f), SuffixLabel("km/h", true)]
        public float targetSpeedKmh = 6500f;

        [ShowIf("type", RunnerObjectiveType.JumpCount)]
        [Tooltip("How many ramps the ship must take off from during the run.")]
        [PropertyRange(1, 50)]
        public int jumpCount = 3;

        [ShowIf("type", RunnerObjectiveType.RacePosition)]
        [Tooltip("Win = finish 1st. Top X = finish X-th or better. Exactly = finish X-th, no better and no worse. Judged as the ship leaves by an end ramp; the run fails early the moment it can no longer be met.")]
        [EnumToggleButtons]
        public RaceGoalMode raceMode = RaceGoalMode.Win;

        [ShowIf(nameof(UsesRacePlace))]
        [Tooltip("The place X the mode asks for.")]
        [PropertyRange(1, 30)]
        public int racePosition = 3;

        [HideIf(nameof(IsChallenge))]
        [Tooltip("Money this objective pays on the Mission Complete panel, added to the city level's rewards before the challenge multipliers apply.")]
        [PropertyRange(0, 50000), SuffixLabel("$", true)]
        public int reward = 0;

        [Tooltip("HUD tint and panel accent for this goal. Fully transparent = the type's default color.")]
        public Color accent = new(0f, 0f, 0f, 0f);

        bool UsesRacePlace => type == RunnerObjectiveType.RacePosition && raceMode != RaceGoalMode.Win;

        /// <summary>The place a Race Position goal targets: 1 for Win, else X.</summary>
        public int RaceTarget => raceMode == RaceGoalMode.Win ? 1 : Mathf.Max(1, racePosition);

        /// <summary>True when <paramref name="place"/> (1-based) meets a Race Position goal.</summary>
        public bool MeetsRacePlace(int place) => place > 0 && raceMode switch
        {
            RaceGoalMode.Exactly => place == RaceTarget,
            _ => place <= RaceTarget
        };

        /// <summary>
        /// True once a Race Position goal can no longer be met (D7), from the
        /// rivals that have finished and those still racing: Win / Top X are
        /// gone once X rivals are home; Exactly also once too few are left to
        /// finish behind the ship. Always false for any other type.
        /// </summary>
        public bool RaceGoalImpossible(int finishedRivals, int racingRivals)
        {
            if (type != RunnerObjectiveType.RacePosition) return false;
            int target = RaceTarget;
            if (finishedRivals >= target) return true;
            return raceMode == RaceGoalMode.Exactly && finishedRivals + racingRivals + 1 < target;
        }

        static string Ordinal(int n)
        {
            int tens = n % 100;
            string suffix = tens >= 11 && tens <= 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" };
            return n + suffix;
        }

        /// <summary>True on a <see cref="RunnerOptionalChallenge"/> — it carries a multiplier, not a reward.</summary>
        public virtual bool IsChallenge => false;

        /// <summary>The player-facing line — English like the city's objective summaries, never localized.</summary>
        public string Summary => type switch
        {
            RunnerObjectiveType.ReachSpeed => $"REACH {targetSpeedKmh:0} KM/H",
            RunnerObjectiveType.JumpCount => jumpCount == 1 ? "JUMP ONCE" : $"JUMP {jumpCount} TIMES",
            RunnerObjectiveType.RacePosition => raceMode switch
            {
                RaceGoalMode.TopX => RaceTarget == 1 ? "FINISH 1ST" : $"FINISH TOP {RaceTarget}",
                _ => $"FINISH {Ordinal(RaceTarget)}"
            },
            _ => type.ToString().ToUpperInvariant()
        };

        /// <summary>Inspector list label — never shown to the player.</summary>
        public string EditorLabel => Summary + (!IsChallenge && reward > 0 ? $" ${reward}" : "");

        /// <summary>The accent to draw with — the authored one, or the type default when it was left transparent.</summary>
        public Color Accent => accent.a > 0.001f ? accent : DefaultAccent(type);

        /// <summary>Whether the goal is met for the given run state (speed is the ship's CURRENT speed — the manager latches the result). A race goal is met only once the ship's place is fixed at its lip.</summary>
        public bool Satisfied(in ObjectiveContext run) => type switch
        {
            RunnerObjectiveType.ReachSpeed => run.speedKmh >= targetSpeedKmh,
            RunnerObjectiveType.JumpCount => run.jumps >= jumpCount,
            RunnerObjectiveType.RacePosition => MeetsRacePlace(run.raceFinishPlace),
            _ => false
        };

        /// <summary>The HUD's progress word — empty for speed (the speedometer already shows it), "1/3" for jumps, the live place ("P4") for a race.</summary>
        public string Progress(in ObjectiveContext run) => type switch
        {
            RunnerObjectiveType.JumpCount => $"{Mathf.Min(run.jumps, jumpCount)}/{jumpCount}",
            RunnerObjectiveType.RacePosition => run.raceFinishPlace > 0 ? $"P{run.raceFinishPlace}"
                                              : run.racePosition > 0 ? $"P{run.racePosition}" : string.Empty,
            _ => string.Empty
        };

        public static Color DefaultAccent(RunnerObjectiveType type) => type switch
        {
            RunnerObjectiveType.ReachSpeed => new Color(0.45f, 0.9f, 1f),
            RunnerObjectiveType.JumpCount => new Color(1f, 0.8f, 0.3f),
            RunnerObjectiveType.RacePosition => new Color(1f, 0.45f, 0.85f),
            _ => Color.white
        };
    }

    /// <summary>
    /// An optional goal of the run — a full <see cref="RunnerObjective"/>
    /// plus the reward multiplier completing it earns. There is no brief in
    /// the runner, so every challenge is live from launch: the manager
    /// checks it each frame until it latches, and one still open when the
    /// run is won simply shows FAILED on the panel and multiplies nothing.
    /// </summary>
    [System.Serializable]
    public class RunnerOptionalChallenge : RunnerObjective
    {
        [Tooltip("Reward multiplier for COMPLETING this challenge — the panel shows it as ×N. An unfinished challenge earns nothing.")]
        [PropertyRange(1, 20)]
        public int multiplier = 2;

        public override bool IsChallenge => true;

        /// <summary>The HUD line and the panel row: the condition plus its multiplier.</summary>
        public string ChallengeSummary => $"{Summary} ×{multiplier}";

        /// <summary>Inspector list label.</summary>
        public string ChallengeLabel => $"{EditorLabel} ×{multiplier}";
    }

    /// <summary>
    /// An escape run as data — the runner's twin of the city's
    /// <c>LevelDefinition</c>: the mandatory objectives that WIN the run
    /// (all must be met), the optional challenges that multiply the mission
    /// payout, the scene the Mission Complete panel's NEXT MISSION goes to,
    /// and the rank table used when no city level result precedes the run.
    /// The <see cref="GameManager"/> reads it live (no runtime clone) and
    /// evaluates it every frame; the first mandatory Reach Speed objective is
    /// the run's Light Speed. Add new goal types to
    /// <see cref="RunnerObjectiveType"/> and stack them in either list.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_LevelDefinition", menuName = "FiniteRunner/Runner Level Definition")]
    public class RunnerLevelDefinition : RunnerLevelAsset
    {
        [TitleGroup("Level")]
        [Tooltip("Shown on the Mission Complete panel when no city level preceded this run.")]
        public string levelName = "Escape Run";

        [TitleGroup("Level")]
        [Tooltip("Scene the panel's NEXT MISSION loads (through the loading curtain).")]
        public string nextSceneName = "CarTest";

        [TitleGroup("Level")]
        [Tooltip("Length of this run's track, metres: the three end ramps stand at about this distance (the generator lands the last knot as close as its segments allow). 0 = GameSettings.trackLengthMeters.")]
        [PropertyRange(0f, 100000f), SuffixLabel("m", true)]
        public float trackLengthMeters = 0f;

        [TitleGroup("Level")]
        [Tooltip("A saved track (made in the TrackGenerator's inspector: Generate Track, Save Track As…, Set as Current Track). Set = this level plays exactly that track — its length, its countdown when it sets one, every ramp, orb and gate. Empty = a track is generated for every run (the length above).")]
        public Track.TrackLayoutAsset track;

        [TitleGroup("Level")]
        [Tooltip("Oncoming traffic for this level: hover cars driving the track toward the ship (OncomingTrafficPRD.md). Empty = none.")]
        public Traffic.TrafficDefinition traffic;

        [TitleGroup("Level")]
        [Tooltip("A race: rival hover ships on a grid with the ship, racing it to the end ramps (RaceModePRD.md). Empty = no race. A race level should author no Reach Speed objective and no traffic.")]
        public Race.RaceDefinition race;

        [TitleGroup("Mission complete")]
        [Tooltip("Clip looping in the panel's video holder. Empty = the holder shows a dead NO SIGNAL screen.")]
        public VideoClip completeVideo;

        [TitleGroup("Mission complete")]
        [Tooltip("Rank thresholds for a run played on its own. A run that follows a city level ranks against THAT level's table, carried over by the save.")]
        public RankTable rankTable = new();

        [TitleGroup("Optional challenges")]
        [Tooltip("Live from launch, no accept step. Only COMPLETED ones multiply the mission payout.")]
        [ListDrawerSettings(DraggableItems = true, ListElementLabelName = nameof(RunnerOptionalChallenge.ChallengeLabel))]
        public List<RunnerOptionalChallenge> optionalChallenges = new();

        [TitleGroup("Objectives")]
        [Tooltip("ALL of these must be met to win the run. Each pays its reward on the Mission Complete panel.")]
        [ListDrawerSettings(DraggableItems = true, ShowIndexLabels = true, ListElementLabelName = nameof(RunnerObjective.EditorLabel))]
        public List<RunnerObjective> objectives = new();

        public int Count => objectives != null ? objectives.Count : 0;
        public int ChallengeCount => optionalChallenges != null ? optionalChallenges.Count : 0;

        /// <summary>The run's Light Speed: the first mandatory Reach Speed target, or -1 when the list has none (the caller falls back to its settings).</summary>
        public float LightSpeedKmh
        {
            get
            {
                if (objectives != null)
                    foreach (var step in objectives)
                        if (step != null && step.type == RunnerObjectiveType.ReachSpeed) return step.targetSpeedKmh;
                return -1f;
            }
        }

        /// <summary>The first MANDATORY Race Position objective's index, or -1 — the goal the early fail and the lip judge.</summary>
        public int RaceObjectiveIndex
        {
            get
            {
                if (objectives != null)
                    for (int i = 0; i < objectives.Count; i++)
                        if (objectives[i] != null && objectives[i].type == RunnerObjectiveType.RacePosition) return i;
                return -1;
            }
        }

        /// <summary>The pre-data run (reach 6500 km/h) as an in-memory level — the fallback for a manager with no asset assigned.</summary>
        public static RunnerLevelDefinition CreateDefault()
        {
            var level = CreateInstance<RunnerLevelDefinition>();
            level.name = "DefaultRunnerLevel";
            SeedDefaultObjectives(level);
            return level;
        }

        /// <summary>Fills an empty definition with the default run. Only ever called on a fresh asset — authored lists are never touched.</summary>
        public static void SeedDefaultObjectives(RunnerLevelDefinition level)
        {
            level.objectives.Add(new RunnerObjective { type = RunnerObjectiveType.ReachSpeed, targetSpeedKmh = 6500f, reward = 500 });
        }
    }
}
