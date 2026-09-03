namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// The string ids the store, the profile and gameplay agree on. A
    /// category id names WHAT is upgraded ("car.speed") and a model id names
    /// the thing carrying it ("car.quadron"); the profile keys a bought level
    /// on the pair, so a second car later has its own levels. They are plain
    /// strings in the save file — never rename one, add beside it.
    /// </summary>
    public static class UpgradeIds
    {
        /// <summary>Highest level a category reaches; the definition tables carry exactly this many rows.</summary>
        public const int MaxLevel = 10;

        // ---------------------------------------------------------- categories
        public const string CarSpeed = "car.speed";
        public const string CarAcceleration = "car.acceleration";
        public const string CarWeight = "car.weight";
        public const string CarResistance = "car.resistance";
        public const string CarHandling = "car.handling";

        public const string ShipHandling = "ship.handling";
        public const string ShipDashPower = "ship.dashPower";
        public const string ShipSpeedMultiplier = "ship.speedMultiplier";
        public const string ShipJumpStrength = "ship.jumpStrength";

        public const string CharHackingSpeed = "char.hackingSpeed";
        public const string CharHackValue = "char.hackValue";
        public const string CharStrength = "char.strength";
        public const string CharRange = "char.range";
        public const string CharAccuracy = "char.accuracy";

        // -------------------------------------------------------------- models
        public const string CarQuadron = "car.quadron";
        public const string ShipNabucodonosor = "ship.nabucodonosor";
        public const string CharRob = "char.rob";
    }
}
