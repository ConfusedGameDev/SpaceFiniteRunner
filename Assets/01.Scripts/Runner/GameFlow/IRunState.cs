namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// What a view of the run may read: the clock, the lives and hull, the
    /// Light Speed target and the objectives, how much track is left. The
    /// HUD and the chase minimap bind to this, never to the game flow that
    /// implements it (today the <see cref="GameManager"/>), so a HUD dropped
    /// into a scene without a run simply hides. Lives in the Runner assembly,
    /// not Contracts: it hands out the runner's own level types.
    /// </summary>
    public interface IRunState
    {
        /// <summary>Seconds left on the countdown.</summary>
        float TimeRemaining { get; }
        /// <summary>The run has ended (won or lost); readouts freeze.</summary>
        bool RunOver { get; }
        /// <summary>An ending is playing out (the win's fly-past, the MISSION FAILED banner): no story lines.</summary>
        bool IsEnding { get; }
        /// <summary>Lives left in this runner entry.</summary>
        int LivesLeft { get; }
        /// <summary>Hull and lives are in play.</summary>
        bool HullEnabled { get; }
        /// <summary>The hull's fill, 0..1; below 0 when the run has no hull.</summary>
        float HullFraction { get; }
        /// <summary>The run's Light Speed target, km/h.</summary>
        float LightSpeedKmh { get; }
        /// <summary>Light Speed has been reached (it latches).</summary>
        bool LightSpeedReached { get; }
        /// <summary>Jumps taken this run (jump objectives count them).</summary>
        int JumpCount { get; }
        /// <summary>The run's level: its objectives and optional challenges.</summary>
        RunnerLevelDefinition Level { get; }
        bool IsObjectiveDone(int index);
        bool IsChallengeDone(int index);
        /// <summary>How far ahead of the ship a "+N" boost text spawns, metres.</summary>
        float BoostTextLeadMeters { get; }
        /// <summary>The track ends (a finite run).</summary>
        bool HasTrackEnd { get; }
        /// <summary>Metres from the ship to the track's end.</summary>
        float DistanceRemaining { get; }
    }
}
