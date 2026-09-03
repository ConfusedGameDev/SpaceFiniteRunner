using UnityEngine;

using ConfusedGameDev.FiniteRunner.Store;
namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The one place the Store's car multipliers touch a <see cref="CarConfig"/>:
    /// <see cref="Clone"/> copies the prefab's asset and scales the copy, so
    /// the player drives an upgraded car while the police, the traffic and
    /// the debug pages keep the shared asset (which also keeps the AI's
    /// steer normalisation — they divide by the asset's max steer angle —
    /// in step with their own cars). Mapping — Speed: the top-speed soft cap
    /// (both backends); Acceleration: motor torque and the EVP drive force;
    /// Weight: mass, heavier — the "heavier wins" rule shoves more; Handling:
    /// steer angle, steer response, cornering stiffness and the EVP tire
    /// friction. Resistance is applied where the player takes damage
    /// (<c>LevelManager.ApplyDamage</c>), not here.
    /// </summary>
    public static class CarUpgradeApplier
    {
        /// <summary>A runtime copy of <paramref name="source"/> with the bought levels multiplied in.</summary>
        public static CarConfig Clone(CarConfig source)
        {
            if (source == null) return null;
            CarConfig config = Object.Instantiate(source);
            config.name = source.name + " (upgraded)";

            float speed = StoreUpgrades.Multiplier(StoreSectionKind.Car, UpgradeIds.CarSpeed);
            float acceleration = StoreUpgrades.Multiplier(StoreSectionKind.Car, UpgradeIds.CarAcceleration);
            float weight = StoreUpgrades.Multiplier(StoreSectionKind.Car, UpgradeIds.CarWeight);
            float handling = StoreUpgrades.Multiplier(StoreSectionKind.Car, UpgradeIds.CarHandling);

            config.topSpeedKmh *= speed;
            config.maxMotorTorque *= acceleration;
            config.evpDriveForce *= acceleration;
            config.mass *= weight;
            config.maxSteerAngle *= handling;
            config.steerResponse *= handling;
            config.sideStiffness *= handling;
            config.evpTireFriction *= handling;
            return config;
        }
    }
}
