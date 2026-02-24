namespace HoverRacer
{
    /// <summary>
    /// Decreases the vehicle's speed when triggered.
    /// Set TrackObjectConfigSO.speedDelta to a negative value (e.g. -8).
    /// </summary>
    public class SpeedDecreaser : SpeedModifierBase
    {
        public override void ApplyTo(VehicleController vehicle)
        {
            vehicle.ModifySpeed(config.speedDelta);
        }
    }
}
