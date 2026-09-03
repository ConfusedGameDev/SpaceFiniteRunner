using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// The one place the Store's ship multipliers touch a
    /// <see cref="ShipDefinition"/>. Every caller hands it a FRESH runtime
    /// clone (never the asset, never a clone already multiplied), so a
    /// restart can't compound levels: the <c>GameManager</c> builds the run's
    /// definition here when the tuning screen is off, and the tuning screen
    /// applies it on top of its points when it is on. Mapping — Handling:
    /// lateral speed and response; Dash Power: dash distance; Speed
    /// Multiplier: the passive speed bleed DIVIDED, so the ship keeps its
    /// speed longer; Jump Strength: the takeoff boost and arc.
    /// </summary>
    public static class ShipUpgradeApplier
    {
        /// <summary>Clone + store multipliers + the armed ship debug overrides — the definition a run flies on.</summary>
        public static ShipDefinition BuildRunDefinition(ShipDefinition baseDefinition)
        {
            if (baseDefinition == null) return null;
            ShipDefinition run = Object.Instantiate(baseDefinition);
            run.name = baseDefinition.name + " (run)";
            Apply(run);
            ShipDebugSettings.Load().ApplyTo(run); // debug values win, same rule as the tuning screen
            return run;
        }

        /// <summary>Multiplies the store's levels into <paramref name="freshClone"/> in place.</summary>
        public static void Apply(ShipDefinition freshClone)
        {
            if (freshClone == null) return;
            float handling = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipHandling);
            float dash = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipDashPower);
            float speed = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipSpeedMultiplier);
            float jump = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipJumpStrength);

            freshClone.lateralSpeed *= handling;
            freshClone.handlingResponse *= handling;
            freshClone.dashDistance *= dash;
            freshClone.passiveDeceleration /= Mathf.Max(0.01f, speed);
            freshClone.jumpStrength *= jump;
        }
    }
}
