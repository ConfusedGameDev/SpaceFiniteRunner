namespace HoverRacer
{
    /// <summary>
    /// Increases the vehicle's speed when triggered.
    /// Set TrackObjectConfigSO.speedDelta to a positive value (e.g. +10).
    /// </summary>
    public class SpeedBooster : SpeedModifierBase
    {
        public override void ApplyTo(VehicleController vehicle)
        {
            vehicle.ModifySpeed(config.speedDelta);
        }
    }
}
