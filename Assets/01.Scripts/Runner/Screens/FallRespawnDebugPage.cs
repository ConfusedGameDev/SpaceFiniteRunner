using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The pause menu's FALL &amp; RESPAWN page. Its rows come from two
    /// owners, each tuned in one place:
    /// the SHIP's rules — the fall, the respawn wait, its penalty and whether
    /// it is a rolling start, the blink, the clearance, the stall grace — live
    /// on the ship's <see cref="ShipSettings"/>; each row writes that asset and
    /// mirrors onto the ship's live clone. The RUN's rules — the open-edge
    /// overhang and grace, the camera follow, the patrol's head start — live
    /// on <see cref="GameSettings"/>, which the run reads live, so those rows
    /// edit the asset alone. Everything applies at once and is saved at the
    /// menu's commit points through <see cref="DebugAssetEdits"/>.
    /// </summary>
    public static class FallRespawnDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen Build(RectTransform parent, MenuTheme theme, GameSettings rules, HoverShip ship,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_FallRespawn", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabFall, tabIndex, tabCount);

            AddRun(screen, rules, refreshers, MenuTextId.EdgeOverhang,
                   0f, 10f, 0.25f, "0.00", s => s.edgeOverhang, (s, v) => s.edgeOverhang = v);
            AddRun(screen, rules, refreshers, MenuTextId.EdgeGrace,
                   0f, 2f, 0.05f, "0.00", s => s.edgeGraceSeconds, (s, v) => s.edgeGraceSeconds = v);
            AddShip(screen, ship, refreshers, MenuTextId.FallGravity,
                    0f, 200f, 5f, "0", s => s.fallGravity, (s, v) => s.fallGravity = v);
            AddShip(screen, ship, refreshers, MenuTextId.FallDuration,
                    0.1f, 5f, 0.1f, "0.0", s => s.fallDurationSeconds, (s, v) => s.fallDurationSeconds = v);
            AddRun(screen, rules, refreshers, MenuTextId.FallCameraFollow,
                   0f, 5f, 0.1f, "0.0", s => s.fallCameraFollowSeconds, (s, v) => s.fallCameraFollowSeconds = v);
            AddShip(screen, ship, refreshers, MenuTextId.RespawnWait,
                    0f, 10f, 0.25f, "0.00", s => s.respawnWaitSeconds, (s, v) => s.respawnWaitSeconds = v);
            AddShipToggle(screen, ship, refreshers, MenuTextId.RespawnRollingStart,
                          s => s.respawnRollingStart, (s, v) => s.respawnRollingStart = v);
            AddShip(screen, ship, refreshers, MenuTextId.RespawnBlinkRate,
                    1f, 30f, 1f, "0", s => s.respawnBlinkRate, (s, v) => s.respawnBlinkRate = v);
            AddShip(screen, ship, refreshers, MenuTextId.RespawnSpeedPenalty,
                    0f, 1f, 0.05f, "0.00", s => s.respawnSpeedPenalty, (s, v) => s.respawnSpeedPenalty = v);
            AddShip(screen, ship, refreshers, MenuTextId.RespawnClearance,
                    0f, 1000f, 25f, "0", s => s.respawnClearance, (s, v) => s.respawnClearance = v);
            AddRun(screen, rules, refreshers, MenuTextId.RespawnPatrolGap,
                   0f, 1000f, 25f, "0", s => s.respawnMinPatrolGap, (s, v) => s.respawnMinPatrolGap = v);
            AddShip(screen, ship, refreshers, MenuTextId.StallGrace,
                    0f, 10f, 0.25f, "0.00", s => s.stallGraceSeconds, (s, v) => s.stallGraceSeconds = v);
            screen.SetViewport(9);
            return screen;
        }

        // A GameSettings row: the run reads the asset live, so the edit lands at once.
        static void AddRun(MenuScreen screen, GameSettings rules, List<System.Action> refreshers,
                           MenuTextId label, float min, float max, float step, string format,
                           System.Func<GameSettings, float> get, System.Action<GameSettings, float> set)
        {
            if (rules == null) return;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(rules), format, v =>
            {
                set(rules, v);
                DebugAssetEdits.Touch(rules);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(rules)));
        }

        // A ShipSettings row: writes the ship's settings asset, then its live clone.
        static void AddShip(MenuScreen screen, HoverShip ship, List<System.Action> refreshers,
                            MenuTextId label, float min, float max, float step, string format,
                            System.Func<ShipSettings, float> get, System.Action<ShipSettings, float> set)
        {
            if (ship == null || ship.SettingsAsset == null) return;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(ship.SettingsAsset), format, v =>
            {
                var asset = ship.SettingsAsset;
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                if (ship.Settings != null && ship.Settings != asset) set(ship.Settings, v);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(ship.SettingsAsset)));
        }

        /// <summary>An ON/OFF ShipSettings row. Configure sets without notifying, so the reopen readout never re-fires the write.</summary>
        static void AddShipToggle(MenuScreen screen, HoverShip ship, List<System.Action> refreshers, MenuTextId label,
                                  System.Func<ShipSettings, bool> get, System.Action<ShipSettings, bool> set)
        {
            if (ship == null || ship.SettingsAsset == null) return;
            var row = screen.AddRow<MenuToggle>(label);
            void OnChanged(bool v)
            {
                var asset = ship.SettingsAsset;
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                if (ship.Settings != null && ship.Settings != asset) set(ship.Settings, v);
            }
            row.Configure(get(ship.SettingsAsset), OnChanged);
            refreshers?.Add(() => row.Configure(get(ship.SettingsAsset), OnChanged));
        }
    }
}
