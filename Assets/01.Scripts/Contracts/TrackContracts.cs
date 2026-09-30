namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>
    /// What a streamed track follows: how far along it the thing it streams
    /// for has come, and how fast it is going. The runner's ship implements
    /// it; the track never learns what it is. Distance from the track start
    /// is the authoritative coordinate, metres; speed is m/s.
    /// </summary>
    public interface IStreamFocus
    {
        /// <summary>Metres from the track start.</summary>
        float Distance { get; }
        /// <summary>Forward speed, m/s.</summary>
        float Speed { get; }
    }

    /// <summary>
    /// The few numbers of the flying vehicle that shape what the track
    /// builds: the speed a loop's reachability test predicts with, and the
    /// jump strength a ramp's arc scales by. A plain view of the vehicle's
    /// stats — the track never reads the vehicle's definition asset.
    /// </summary>
    public interface IShipPerformance
    {
        /// <summary>The speed the throttle holds, m/s.</summary>
        float CruiseSpeed { get; }
        /// <summary>The bleed that pulls a boosted vehicle back down to cruise, m/s².</summary>
        float PassiveDeceleration { get; }
        /// <summary>Multiplier on a ramp's takeoff boost and arc.</summary>
        float JumpStrength { get; }
    }

    /// <summary>
    /// The run's rules the track is built to: how long it is, how it ends,
    /// what a boost is worth, where the air lane sits, whether hull repair
    /// is in play, and the speed a loop's gate demands at a distance. The
    /// runner's game flow implements it; with none present the track uses
    /// its own defaults (an endless preview).
    /// </summary>
    public interface ITrackRunRules
    {
        /// <summary>The finite track's length, metres (0 = endless).</summary>
        float TrackLengthMeters { get; }
        /// <summary>Length of the straight, featureless run-up to the end ramps, metres.</summary>
        float EndRunUpMeters { get; }
        /// <summary>Gap between the end ramps, metres.</summary>
        float EndRampGapMeters { get; }
        /// <summary>Gap between the outer end ramps and the walls, metres.</summary>
        float EndRampSideGapMeters { get; }
        /// <summary>The base boost every orb tier multiplies, m/s.</summary>
        float PowerUpSpeedBoost { get; }
        /// <summary>Height of the air lane above the flight line, metres.</summary>
        float AirLaneHeight { get; }
        /// <summary>Hull and repair are in play (repair orbs spawn only then).</summary>
        bool HullEnabled { get; }
        /// <summary>m/s → the km/h the run shows (3.6 = true km/h).</summary>
        float SpeedDisplayMultiplier { get; }
        /// <summary>The speed a loop's gate at <paramref name="distance"/> demands, m/s.</summary>
        float LoopRequiredSpeed(float distance);
    }

    /// <summary>
    /// Something a repair pickup can mend. Returns the hull points restored
    /// (0 when already full). The pickup looks for it on the collector's
    /// GameObject; nothing on it = the pickup is left alone.
    /// </summary>
    public interface IRepairable
    {
        float RepairFromPickup();
    }
}
