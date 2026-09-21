namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Something a ship takes by flying through it, in a level with no track
    /// to key it on. The ship finds it — a box swept along the path it
    /// actually flew this tick (<see cref="ShipPickupSweeper"/>), against the
    /// pickup's own collider — and the pickup decides what being taken means.
    /// A trigger callback cannot do this job: at 36 m a step the ship is
    /// never "inside" a 1 m orb on any tick. The runner's pads and coins
    /// implement this beside their track-space contract, so both ships take them.
    /// </summary>
    public interface IShipPickup
    {
        /// <summary>False once used up (or while waiting to come back): the sweep skips it.</summary>
        bool Available { get; }

        void PickUp(IShip ship);
    }
}
