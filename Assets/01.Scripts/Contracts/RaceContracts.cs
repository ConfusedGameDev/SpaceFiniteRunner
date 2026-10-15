namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>Where one racer stands in a race. Never serialized.</summary>
    public enum RacerState
    {
        /// <summary>On the grid, waiting for the ship to launch.</summary>
        Grid,
        /// <summary>Racing, simulated in full round the ship (visible).</summary>
        Racing,
        /// <summary>Racing out of the ship's sight: distance and pace only, cannot crash.</summary>
        Ghost,
        /// <summary>Left the track by an end ramp: its place is fixed.</summary>
        Finished,
        /// <summary>Destroyed (a beam, a fall, a gap at the end, rammed to death): out of the race.</summary>
        Dnf,
    }

    /// <summary>
    /// The live standings of a race (RaceModePRD R5.2), read-only: what the
    /// HUD's position readout, the minimap's rival dots and the race objective
    /// read, so none of them ever names the race system. Racer index 0..
    /// <see cref="RivalCount"/>−1 are the rivals; the player's ship is not a
    /// racer entry, only <see cref="PlayerPosition"/>. Places count from 1.
    /// </summary>
    public interface IRaceStandings
    {
        /// <summary>True while a race is bound to the run.</summary>
        bool RaceActive { get; }
        /// <summary>Rivals + the ship.</summary>
        int RacerCount { get; }
        /// <summary>The player's place right now: 1 + finished rivals + rivals still racing ahead of the ship. Fixed once the ship has left the end of the track.</summary>
        int PlayerPosition { get; }
        /// <summary>Rivals that left by an end ramp.</summary>
        int FinishedCount { get; }
        /// <summary>Rivals still on the track (full sim or ghost).</summary>
        int RacingCount { get; }
        /// <summary>Rivals destroyed.</summary>
        int DnfCount { get; }

        int RivalCount { get; }
        /// <summary>Track distance of rival <paramref name="index"/>, metres (the finish line's once finished).</summary>
        float RivalDistance(int index);
        RacerState RivalState(int index);
        /// <summary>Its finishing place, or 0 while it has none.</summary>
        int RivalFinishPlace(int index);
        /// <summary>Its colour on the map.</summary>
        UnityEngine.Color RivalColor(int index);

        /// <summary>Raised whenever <see cref="PlayerPosition"/> changes: (old, new).</summary>
        event System.Action<int, int> PlayerPositionChanged;
    }
}
