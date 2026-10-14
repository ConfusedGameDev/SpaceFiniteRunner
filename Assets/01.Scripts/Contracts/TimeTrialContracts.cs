namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>Why the clock moved (TimeTrialPRD R3.4). Never serialized.</summary>
    public enum TimeAwardKind
    {
        /// <summary>A checkpoint's own award: the time to the next one.</summary>
        Checkpoint,
        /// <summary>A checkpoint reached with no hit since the last.</summary>
        CleanBonus,
        /// <summary>A patrol cruiser destroyed.</summary>
        PatrolKill,
        /// <summary>A life lost (a penalty — negative).</summary>
        LifeLost,
    }

    /// <summary>
    /// The run's countdown as a time trial may move it: read it, add to it
    /// (negative takes away). The run's owner implements it and clamps; the
    /// time trial never holds the clock itself.
    /// </summary>
    public interface ITimeTrialClock
    {
        /// <summary>Seconds left on the run's countdown.</summary>
        float TimeRemaining { get; }
        /// <summary>Adds <paramref name="seconds"/> (negative subtracts), clamped to the trial's cap and 0. Returns the change actually applied.</summary>
        float AddTime(float seconds);
    }

    /// <summary>
    /// A time trial's live state, read-only (TimeTrialPRD R5): what the HUD's
    /// clock and goal line, the minimap's checkpoint markers and the Mission
    /// Complete rows read, so none of them names the time trial system.
    /// Checkpoints are indexed 0..<see cref="CheckpointCount"/>−1 in track order.
    /// </summary>
    public interface ITimeTrialState
    {
        /// <summary>True while a time trial is bound to the run.</summary>
        bool TrialActive { get; }
        /// <summary>True for the Arcade mode (checkpoints feed the clock); false for Beat the Clock.</summary>
        bool IsArcade { get; }
        /// <summary>Below this many seconds the clock warns.</summary>
        float LowTimeSeconds { get; }

        int CheckpointCount { get; }
        /// <summary>Checkpoints crossed this run.</summary>
        int CheckpointsPassed { get; }
        /// <summary>Of those, the ones crossed clean.</summary>
        int CleanCheckpoints { get; }
        /// <summary>Track distance of checkpoint <paramref name="index"/>, metres.</summary>
        float CheckpointDistance(int index);
        /// <summary>The seconds checkpoint <paramref name="index"/> added (its award, before any clean bonus) once crossed; before that, the most it can add (sized at the pace floor).</summary>
        float CheckpointAward(int index);

        /// <summary>Patrol cruisers destroyed this run that paid the bonus.</summary>
        int PatrolKills { get; }
        /// <summary>Lives lost this run (Arcade's respawns).</summary>
        int LivesLost { get; }

        /// <summary>Raised on every change the trial makes to the clock: (seconds actually applied, why).</summary>
        event System.Action<float, TimeAwardKind> ClockAdjusted;
    }
}
