namespace ConfusedGameDev.FiniteRunner.Collectibles
{
    /// <summary>
    /// Marker for "the player's vehicle" as far as pickups are concerned: a
    /// <see cref="Collectible"/> accepts a collider whose attached rigidbody
    /// or parent chain carries one. The ship's <c>ShipMotor</c> and the city
    /// car's <c>CarInput</c> implement it, so the collectible needs to know
    /// neither vehicle type and lives below both games' assemblies. AI cars,
    /// the patrol and props never carry it.
    /// </summary>
    public interface ICollector
    {
    }
}
