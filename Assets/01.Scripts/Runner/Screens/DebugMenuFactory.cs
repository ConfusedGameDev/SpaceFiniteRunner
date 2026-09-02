using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// Builders for the individual debug tabs. The Core Settings tab edits the
    /// TrackGenerator live (spawn table takes effect while streaming; width and
    /// straightness need a rebuild) and its RELOAD SCENE row snapshots the
    /// values into <see cref="TrackDebugSettings"/> before reloading, so the
    /// fresh scene comes up with the tweaked track.
    /// </summary>
    public static class DebugMenuFactory
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen BuildCoreSettingsTab(RectTransform parent, MenuTheme theme,
                                                      TrackGenerator generator, TrackDebugSettings saved,
                                                      System.Action reloadScene, System.Action onChanged,
                                                      int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CoreSettings", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCore, tabIndex, tabCount);

            screen.AddRow<DebugSliderRow>(MenuTextId.TrackWidth)
                  .Configure(10f, 120f, 5f, generator.TrackWidth, "0",
                             v => { generator.TrackWidth = v; saved.CaptureFrom(generator); onChanged?.Invoke(); });
            screen.AddRow<DebugSliderRow>(MenuTextId.Straightness)
                  .Configure(0f, 100f, 5f, generator.Straightness, "0",
                             v => { generator.Straightness = v; saved.CaptureFrom(generator); onChanged?.Invoke(); });

            // One color-tinted percentage slider per spawn entry. Adjusting one
            // rebalances the others live, so the on-screen table always adds
            // up to exactly 100% — same rule as the inspector's spawn table.
            var table = generator.SpawnTable;
            if (table != null)
            {
                var probabilityRows = new List<DebugSliderRow>();
                for (int i = 0; i < table.Length; i++)
                {
                    int index = i;
                    var entry = table[i];
                    var row = screen.AddRow<DebugSliderRow>($"{entry.name.ToUpperInvariant()} %");
                    row.Configure(0f, 100f, 1f, entry.probability, "0", v =>
                    {
                        RebalanceProbabilities(table, probabilityRows, index, v);
                        saved.CaptureFrom(generator);
                        onChanged?.Invoke();
                    });
                    row.SetLabelTint(entry.color);
                    probabilityRows.Add(row);
                }
            }

            screen.AddRow<MenuRow>(MenuTextId.ReloadScene).Activated += () => reloadScene?.Invoke();
            return screen;
        }

        /// <summary>
        /// Second tab: one color-tinted boost-multiplier slider per spawn
        /// entry (× GameSettings.powerUpSpeedBoost). Applies live to newly
        /// spawned orbs and is saved with the rest of the debug values.
        /// </summary>
        public static MenuScreen BuildMultipliersTab(RectTransform parent, MenuTheme theme,
                                                     TrackGenerator generator, TrackDebugSettings saved,
                                                     System.Action onChanged, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Multipliers", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabMultipliers, tabIndex, tabCount);

            var table = generator.SpawnTable;
            if (table != null)
            {
                foreach (var entry in table)
                {
                    var captured = entry;
                    var row = screen.AddRow<DebugSliderRow>($"{captured.name.ToUpperInvariant()} ×");
                    row.Configure(0.1f, 10f, 0.1f, Mathf.Clamp(captured.multiplier, 0.1f, 10f), "0.0", v =>
                    {
                        captured.multiplier = v;
                        saved.CaptureFrom(generator);
                        onChanged?.Invoke();
                    });
                    row.SetLabelTint(captured.color);
                }
            }

            return screen;
        }

        /// <summary>
        /// FEATURES tab: the generator's feature spacing band (one slider that
        /// slides the band and keeps its spread), one probability / spacing /
        /// boost row per feature entry, and the jump definition's knobs. The
        /// definition rows edit the entry's runtime CLONE (never the asset);
        /// everything is captured into <see cref="FeatureDebugSettings"/> and
        /// re-applied on the next Generate, so it needs the reload the pause
        /// menu offers — a placed ramp keeps the numbers it was built with.
        /// </summary>
        public static MenuScreen BuildFeaturesTab(RectTransform parent, MenuTheme theme,
                                                  TrackGenerator generator, FeatureDebugSettings saved,
                                                  System.Action onChanged, List<System.Action> refreshers,
                                                  int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Features", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabFeatures, tabIndex, tabCount);

            var spacingRow = screen.AddRow<DebugSliderRow>(MenuTextId.FeatureSpacing);
            spacingRow.Configure(100f, 4000f, 50f, generator.FeatureSpacing.x, "0", v =>
            {
                float spread = generator.FeatureSpacing.y - generator.FeatureSpacing.x;
                generator.FeatureSpacing = new Vector2(v, v + spread);
                saved.CaptureFrom(generator);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => spacingRow.SetWithoutNotify(generator.FeatureSpacing.x));

            var table = generator.FeatureTable;
            if (table == null || table.Length == 0) return screen;

            var probabilityRows = new List<DebugSliderRow>();
            for (int i = 0; i < table.Length; i++)
            {
                int index = i;
                var entry = table[i];
                string tag = entry.name.ToUpperInvariant();

                var row = screen.AddRow<DebugSliderRow>($"{tag} %");
                row.Configure(0f, 100f, 1f, entry.probability, "0", v =>
                {
                    RebalanceProbabilities(table, probabilityRows, index, v);
                    saved.CaptureFrom(generator);
                    onChanged?.Invoke();
                });
                row.SetLabelTint(entry.color);
                probabilityRows.Add(row);

                var spacing = screen.AddRow<DebugSliderRow>($"{tag} SPACING");
                spacing.Configure(0f, 3000f, 50f, entry.minSpacing, "0", v =>
                {
                    entry.minSpacing = v;
                    saved.CaptureFrom(generator);
                    onChanged?.Invoke();
                });
                spacing.SetLabelTint(entry.color);

                var boost = screen.AddRow<DebugSliderRow>($"{tag} ×");
                boost.Configure(0f, 10f, 0.1f, Mathf.Clamp(entry.multiplier, 0f, 10f), "0.0", v =>
                {
                    entry.multiplier = v;
                    saved.CaptureFrom(generator);
                    onChanged?.Invoke();
                });
                boost.SetLabelTint(entry.color);

                if (entry.Runtime is JumpDefinition)
                {
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpWidth,
                                0.05f, 1f, 0.05f, "0.00", j => j.widthFraction, (j, v) => j.widthFraction = v);
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpLength,
                                10f, 200f, 5f, "0", j => j.length, (j, v) => j.length = v);
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpAngle,
                                5f, 45f, 1f, "0", j => j.rampAngle, (j, v) => j.rampAngle = v);
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpAirDistance,
                                0.05f, 3f, 0.05f, "0.00", j => j.airDistancePerSpeed, (j, v) => j.airDistancePerSpeed = v);
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpMaxAir,
                                20f, 2000f, 20f, "0", j => j.airDistanceRange.y,
                                (j, v) => j.airDistanceRange = new Vector2(Mathf.Min(j.airDistanceRange.x, v), v));
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpAirControl,
                                0f, 1f, 0.05f, "0.00", j => j.airControlFactor, (j, v) => j.airControlFactor = v);
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpSideHitLoss,
                                0f, 1f, 0.05f, "0.00", j => j.sideHitSpeedLoss, (j, v) => j.sideHitSpeedLoss = v);
                }
            }
            return screen;
        }

        // One localized slider row bound to a jump definition knob. The lambdas
        // read entry.Runtime at call time, so they always hit the clone the
        // current run was generated with.
        static void AddJumpStat(MenuScreen screen, TrackGenerator generator, TrackGenerator.FeatureSpawnEntry entry,
                                FeatureDebugSettings saved, System.Action onChanged, List<System.Action> refreshers,
                                MenuTextId label, float min, float max, float step, string format,
                                System.Func<JumpDefinition, float> get, System.Action<JumpDefinition, float> set)
        {
            JumpDefinition Jump() => entry.Runtime as JumpDefinition;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, Jump() != null ? get(Jump()) : min, format, v =>
            {
                var jump = Jump();
                if (jump == null) return;
                set(jump, v);
                saved.CaptureFrom(generator);
                onChanged?.Invoke();
            });
            row.SetLabelTint(entry.color);
            refreshers?.Add(() => { var jump = Jump(); if (jump != null) row.SetWithoutNotify(get(jump)); });
        }

        /// <summary>
        /// Ship tabs: four pages of <see cref="ShipDefinition"/> sliders (Speed,
        /// Handling, Dash, Hover). They edit the motor's LIVE definition — in
        /// play that is always the tuning screen's runtime clone, never the
        /// asset on disk — so most stats apply instantly; the rest (launch
        /// speed) need the reload the pause menu offers on the way out. Every
        /// change is captured into <see cref="ShipDebugSettings"/> so it
        /// survives that reload. <paramref name="refreshers"/> collects one
        /// re-read action per row: the pause menu runs them on every open, so
        /// the sliders show the tuned clone's values, not the base asset's.
        /// </summary>
        public static MenuScreen BuildShipSpeedTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                   ShipDebugSettings saved, System.Action onChanged,
                                                   List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipSpeed", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipSpeed, tabIndex, tabCount);

            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.LaunchSpeed,
                        0f, 1000f, 10f, "0", d => d.initialImpulse, (d, v) => d.initialImpulse = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Deceleration,
                        0f, 50f, 0.5f, "0.0", d => d.passiveDeceleration, (d, v) => d.passiveDeceleration = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Acceleration,
                        1f, 200f, 5f, "0", d => d.acceleration, (d, v) => d.acceleration = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Weight,
                        0.1f, 5f, 0.1f, "0.0", d => d.weight, (d, v) => d.weight = v);
            return screen;
        }

        public static MenuScreen BuildShipHandlingTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                      ShipDebugSettings saved, System.Action onChanged,
                                                      List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipHandling", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipHandling, tabIndex, tabCount);

            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.LateralSpeed,
                        0f, 100f, 1f, "0", d => d.lateralSpeed, (d, v) => d.lateralSpeed = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.SteerResponse,
                        0.5f, 30f, 0.5f, "0.0", d => d.handlingResponse, (d, v) => d.handlingResponse = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.BankAngle,
                        0f, 90f, 5f, "0", d => d.maxBankAngle, (d, v) => d.maxBankAngle = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.BankResponse,
                        0.5f, 20f, 0.5f, "0.0", d => d.bankResponse, (d, v) => d.bankResponse = v);
            return screen;
        }

        public static MenuScreen BuildShipDashTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                  ShipDebugSettings saved, System.Action onChanged,
                                                  List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipDash", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipDash, tabIndex, tabCount);

            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.DashDistance,
                        2f, 30f, 1f, "0", d => d.dashDistance, (d, v) => d.dashDistance = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.DashDuration,
                        0.05f, 1f, 0.05f, "0.00", d => d.dashDuration, (d, v) => d.dashDuration = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.DashRecharge,
                        1f, 60f, 1f, "0", d => d.dashRechargeSeconds, (d, v) => d.dashRechargeSeconds = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.DashGhosts,
                        1f, 20f, 1f, "0", d => d.dashGhostCount,
                        (d, v) => d.dashGhostCount = Mathf.RoundToInt(v));
            return screen;
        }

        public static MenuScreen BuildShipHoverTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                   ShipDebugSettings saved, System.Action onChanged,
                                                   List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipHover", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipHover, tabIndex, tabCount);

            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.HoverHeight,
                        0f, 10f, 0.25f, "0.00", d => d.hoverHeight, (d, v) => d.hoverHeight = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.BobAmplitude,
                        0f, 3f, 0.05f, "0.00", d => d.bobAmplitude, (d, v) => d.bobAmplitude = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.BobFrequency,
                        0f, 10f, 0.25f, "0.00", d => d.bobFrequency, (d, v) => d.bobFrequency = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.PitchWobble,
                        0f, 10f, 0.5f, "0.0", d => d.hoverPitchDegrees, (d, v) => d.hoverPitchDegrees = v);
            return screen;
        }

        /// <summary>
        /// Patrol tab: the chase tunables of <see cref="PatrolDefinition"/>.
        /// Same rules as the ship tabs — edits the patrol's live runtime clone
        /// (most stats apply instantly; the start gap needs the reload offered
        /// on the way out), captured into <see cref="PatrolDebugSettings"/> so
        /// they survive it. All values are in m/s and meters, like the sim.
        /// </summary>
        public static MenuScreen BuildPatrolTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                                PatrolDebugSettings saved, System.Action onChanged,
                                                List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Patrol", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPatrol, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolBaseSpeed,
                          1f, 600f, 1f, "0", d => d.baseSpeed, (d, v) => d.baseSpeed = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolRamp,
                          0f, 15f, 0.05f, "0.00", d => d.ramp, (d, v) => d.ramp = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolRubberBand,
                          0.5f, 2f, 0.05f, "0.00", d => d.rubberBand, (d, v) => d.rubberBand = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolCatchUp,
                          0.5f, 150f, 0.5f, "0.0", d => d.catchUpAccel, (d, v) => d.catchUpAccel = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolStartGap,
                          0f, 1000f, 25f, "0", d => d.startGap, (d, v) => d.startGap = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolCatchDistance,
                          0f, 100f, 5f, "0", d => d.catchDistance, (d, v) => d.catchDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolWarnDistance,
                          0f, 500f, 10f, "0", d => d.warnDistance, (d, v) => d.warnDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolAlertLead,
                          0f, 500f, 10f, "0", d => d.alertLead, (d, v) => d.alertLead = v);
            return screen;
        }

        static void AddPatrolStat(MenuScreen screen, PolicePatrol patrol, PatrolDebugSettings saved,
                                  System.Action onChanged, List<System.Action> refreshers, MenuTextId label,
                                  float min, float max, float step, string format,
                                  System.Func<PatrolDefinition, float> get,
                                  System.Action<PatrolDefinition, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(patrol.Definition), format, v =>
            {
                set(patrol.Definition, v);
                saved.CaptureFrom(patrol.Definition);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(patrol.Definition)));
        }

        // One localized slider row bound to a ShipDefinition stat. The lambdas
        // read motor.Definition at call time (never a captured reference), so
        // they always hit whichever clone is currently driving the ship.
        static void AddShipStat(MenuScreen screen, ShipMotor motor, ShipDebugSettings saved,
                                System.Action onChanged, List<System.Action> refreshers, MenuTextId label,
                                float min, float max, float step, string format,
                                System.Func<ShipDefinition, float> get,
                                System.Action<ShipDefinition, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(motor.Definition), format, v =>
            {
                set(motor.Definition, v);
                saved.CaptureFrom(motor.Definition);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(motor.Definition)));
        }

        // The moved slider keeps its value; every other entry scales into the
        // remainder (evenly when they were all zero) and its row's fill and
        // readout refresh without re-firing callbacks.
        static void RebalanceProbabilities(IWeightedEntry[] table,
                                           List<DebugSliderRow> rows, int changed, float value)
        {
            float kept = Mathf.Clamp(value, 0f, 100f);
            table[changed].Probability = kept;

            float othersSum = 0f;
            for (int i = 0; i < table.Length; i++)
                if (i != changed) othersSum += table[i].Probability;

            float remainder = 100f - kept;
            for (int i = 0; i < table.Length; i++)
            {
                if (i == changed) continue;
                table[i].Probability = othersSum > 0f
                    ? table[i].Probability * remainder / othersSum
                    : remainder / (table.Length - 1);
                if (i < rows.Count) rows[i].SetWithoutNotify(table[i].Probability);
            }
        }
    }
}
