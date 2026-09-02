namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// What the ship is doing with respect to the track. Everything that
    /// wants to know "is the ship flying" — air-lane pickups, the chase
    /// camera's forced framing, HUD, haptics — reads this off
    /// <see cref="ShipMotor.State"/> rather than a per-feature flag, and the
    /// enum is where the loop and tube features will add their own values.
    /// </summary>
    public enum ShipState
    {
        /// <summary>On the flight line (or riding up a ramp — still track-bound).</summary>
        Grounded,

        /// <summary>Off a jump ramp: following its arc above the track, steering at reduced authority.</summary>
        Airborne,

        /// <summary>Inside a loop section: the track pose itself goes round (and upside down); steering is ordinary lateral steering.</summary>
        Looping,

        /// <summary>Dropped off the top of a loop it was too slow for: falling straight down onto the exit, off the track entirely.</summary>
        Falling,

        /// <summary>On a tube section: the road is a pipe and lateral is an arc round it; steering is ordinary, the band is the section's.</summary>
        OnTube,
    }
}
