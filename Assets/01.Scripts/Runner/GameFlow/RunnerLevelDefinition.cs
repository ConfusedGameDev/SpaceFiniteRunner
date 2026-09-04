using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.SaveData;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>The things an escape run can ask of the pilot. Order is the save format — append only.</summary>
    public enum RunnerObjectiveType { ReachSpeed = 0, JumpCount = 1 }

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

        [HideIf(nameof(IsChallenge))]
        [Tooltip("Money this objective pays on the Mission Complete panel, added to the city level's rewards before the challenge multipliers apply.")]
        [PropertyRange(0, 50000), SuffixLabel("$", true)]
        public int reward = 0;

        [Tooltip("HUD tint and panel accent for this goal. Fully transparent = the type's default color.")]
        public Color accent = new(0f, 0f, 0f, 0f);

        /// <summary>True on a <see cref="RunnerOptionalChallenge"/> — it carries a multiplier, not a reward.</summary>
        public virtual bool IsChallenge => false;

        /// <summary>The player-facing line — English like the city's objective summaries, never localized.</summary>
        public string Summary => type switch
        {
            RunnerObjectiveType.ReachSpeed => $"REACH {targetSpeedKmh:0} KM/H",
            RunnerObjectiveType.JumpCount => jumpCount == 1 ? "JUMP ONCE" : $"JUMP {jumpCount} TIMES",
            _ => type.ToString().ToUpperInvariant()
        };

        /// <summary>Inspector list label — never shown to the player.</summary>
        public string EditorLabel => Summary + (!IsChallenge && reward > 0 ? $" ${reward}" : "");

        /// <summary>The accent to draw with — the authored one, or the type default when it was left transparent.</summary>
        public Color Accent => accent.a > 0.001f ? accent : DefaultAccent(type);

        /// <summary>Whether the goal is met for the given run state (speed is the ship's CURRENT speed — the manager latches the result).</summary>
        public bool Satisfied(float speedKmh, int jumps) => type switch
        {
            RunnerObjectiveType.ReachSpeed => speedKmh >= targetSpeedKmh,
            RunnerObjectiveType.JumpCount => jumps >= jumpCount,
            _ => false
        };

        /// <summary>The HUD's progress word — empty for speed (the speedometer already shows it), "1/3" for jumps.</summary>
        public string Progress(float speedKmh, int jumps) => type switch
        {
            RunnerObjectiveType.JumpCount => $"{Mathf.Min(jumps, jumpCount)}/{jumpCount}",
            _ => string.Empty
        };

        public static Color DefaultAccent(RunnerObjectiveType type) => type switch
        {
            RunnerObjectiveType.ReachSpeed => new Color(0.45f, 0.9f, 1f),
            RunnerObjectiveType.JumpCount => new Color(1f, 0.8f, 0.3f),
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
