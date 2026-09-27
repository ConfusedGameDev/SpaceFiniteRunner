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
    /// applies it on top of its points when it is on. A run definition is
    /// always "asset × levels": the debug pages edit the asset and call
    /// <see cref="Refresh"/> to rebuild the clone from it. Mapping — Handling:
    /// lateral speed and response; Dash Power: dash distance; Speed
    /// Multiplier: the passive speed bleed DIVIDED, so the ship keeps its
    /// speed longer; Jump Strength: the takeoff boost and arc.
    /// </summary>
    public static class ShipUpgradeApplier
    {
        /// <summary>Clone + store multipliers — the definition a run flies on. Handed a clone, it clones that clone's asset, so levels never compound.</summary>
        public static ShipDefinition BuildRunDefinition(ShipDefinition baseDefinition)
        {
            if (baseDefinition == null) return null;
            ShipDefinition run = baseDefinition.CloneForRun();
            if (baseDefinition.Source != null) run.CopyFromSource();
            Apply(run);
            return run;
        }

        /// <summary>Rebuilds a run definition from its asset and multiplies the levels in again. No-op on an asset.</summary>
        public static void Refresh(ShipDefinition runDefinition)
        {
            if (runDefinition == null || runDefinition.Source == null) return;
            runDefinition.CopyFromSource();
            Apply(runDefinition);
        }

        /// <summary>Multiplies the store's levels into <paramref name="freshClone"/> in place.</summary>
        public static void Apply(ShipDefinition freshClone)
        {
            if (freshClone == null) return;
            float handling = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipHandling);
            float dash = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipDashPower);
            float speed = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipSpeedMultiplier);
            float jump = StoreUpgrades.Multiplier(StoreSectionKind.Ship, UpgradeIds.ShipJumpStrength);

            // Handling: the steering force (lateral speed × response, both
            // scaled) AND the grip a flat sweep is held with.
            freshClone.lateralSpeed *= handling;
            freshClone.handlingResponse *= handling;
            freshClone.gripBase *= handling;
            freshClone.gripPerSpeed *= handling;
            // Dash power: the distance — the shove is derived from it
            // (DashImpulse), and scaling the response above keeps it honest.
            freshClone.dashDistance *= dash;
            // Speed: a higher cruise, and a boost that lasts longer above it.
            freshClone.cruiseSpeed *= speed;
            freshClone.passiveDeceleration /= Mathf.Max(0.01f, speed);
            freshClone.jumpStrength *= jump;
        }
    }
}
