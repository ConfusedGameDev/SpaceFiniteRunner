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
    /// in step with their own cars). The clone remembers its asset, and
    /// <see cref="Refresh"/> rebuilds it from that asset, so a debug edit to
    /// the asset reaches the player's car too, upgrades still applied.
    /// Mapping — Speed: the top-speed soft cap
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
            config.upgradeSource = source;
            Multiply(config);
            return config;
        }

        /// <summary>
        /// Re-copies an upgraded clone from its asset and multiplies the
        /// levels in again — the clone's values are always "asset × upgrades",
        /// never a drifted copy. No-op on anything that is not such a clone.
        /// </summary>
        public static void Refresh(CarConfig clone)
        {
            if (clone == null || clone.upgradeSource == null) return;
            CarConfig source = clone.upgradeSource;
            string name = clone.name;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), clone);
            clone.name = name;
            clone.upgradeSource = source;
            Multiply(clone);
        }

        static void Multiply(CarConfig config)
        {
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
        }
    }
}
