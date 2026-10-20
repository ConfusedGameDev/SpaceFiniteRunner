using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The pause menu's BOOST METER page: the held boost's thrust and its
    /// meter. <see cref="BoostMeter"/> reads its asset live (no runtime clone),
    /// so these sliders edit the asset itself — the change bites the moment the
    /// menu is dismissed, and is saved at the pause menu's commit points
    /// (<see cref="DebugAssetEdits"/>). Shown only where a ship carries a meter.
    /// </summary>
    public static class BoostMeterDebugPage
    {
        /// <summary>The live meter's asset, or null when no ship carries one.</summary>
        public static BoostMeterSettings Discover() =>
            BoostMeter.Live != null ? BoostMeter.Live.Settings : null;

        /// <summary>Four rows: thrust, drain, the refill's cooldown and the refill rate.</summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, BoostMeterSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<BoostMeterSettings>("Debug_BoostMeter", parent, theme, MenuTextId.DebugTabBoostMeter, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.BoostMeterPotence,
                0f, 1500f, 10f, "0", s => s.potenceKmhPerSecond, (s, v) => s.potenceKmhPerSecond = v);
            page.Slider(MenuTextId.BoostMeterDrain,
                0.05f, 2f, 0.05f, "0.00", s => s.drainPerSecond, (s, v) => s.drainPerSecond = v);
            page.Slider(MenuTextId.BoostMeterCooldown,
                0f, 15f, 0.5f, "0.0", s => s.cooldownSeconds, (s, v) => s.cooldownSeconds = v);
            page.Slider(MenuTextId.BoostMeterRefill,
                0.05f, 2f, 0.05f, "0.00", s => s.refillPerSecond, (s, v) => s.refillPerSecond = v);
            return page.Screen;
        }

        // Registered with the pause menu's page registry, after TIME TRIAL (7).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(BoostMeterDebugPage), 8, () => DebugPages.Single(Discover(), Build));
    }
}
