namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>
    /// The player's held boost as the HUD reads it: how full the meter is and
    /// what it is doing. The meter owns the rules; a reader never drives it.
    /// Polled, not evented — <see cref="EmptyPresses"/> is a counter a reader
    /// compares against what it last saw, so nothing has to subscribe across a
    /// play session (domain reload is off).
    /// </summary>
    public interface IBoostMeter
    {
        /// <summary>False when the meter is switched off: no boost, no bar.</summary>
        bool MeterEnabled { get; }
        /// <summary>0 (empty) .. 1 (full).</summary>
        float Fraction { get; }
        /// <summary>True while the boost is held and pushing the ship.</summary>
        bool IsBoosting { get; }
        /// <summary>True while the meter waits out its cooldown or refills.</summary>
        bool IsRecovering { get; }
        /// <summary>Counts every press made on an empty meter.</summary>
        int EmptyPresses { get; }
    }
}
