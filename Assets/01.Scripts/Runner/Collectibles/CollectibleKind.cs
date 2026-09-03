namespace ConfusedGameDev.FiniteRunner.Collectibles
{
    /// <summary>
    /// What collecting a <see cref="Collectible"/> does — the switch the
    /// <see cref="CollectibleManager"/> runs. Values are the save/prefab
    /// format: append only, never reorder.
    /// </summary>
    public enum CollectibleKind
    {
        /// <summary>Counted only: the LOG's per-id rows and the city's COLLECT OBJECTS objectives.</summary>
        Item = 0,

        /// <summary>Its <see cref="Collectible.Value"/> in dollars is banked into the profile at pickup.</summary>
        Money = 1,
    }
}
