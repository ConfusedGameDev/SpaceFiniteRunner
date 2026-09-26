using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// Builders for the individual debug tabs. The Core Settings tab edits the
    /// TrackGenerator live (spawner densities and orb tiers take effect while streaming; width and
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
                                                      List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CoreSettings", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCore, tabIndex, tabCount);

            // Every row reads the generator at call time (never a captured
            // value): the menu is built in the GameManager's Awake, which can
            // run before the generator's own Awake stamps the saved debug
            // values on — the refreshers re-read on every open (AddTrackStat).
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.TrackWidth,
                         10f, 120f, 5f, "0", g => g.TrackWidth, (g, v) => g.TrackWidth = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.Straightness,
                         0f, 100f, 5f, "0", g => g.Straightness, (g, v) => g.Straightness = v);

            // The finite track's length, metres: 0 = the level's own (or the
            // GameSettings fallback). Needs the reload, like the width.
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.TrackLength,
                         0f, 100000f, 1000f, "0", g => Mathf.Max(0f, g.TrackLengthOverride),
                         (g, v) => g.TrackLengthOverride = v > 0f ? v : -1f);

            // The road's elevation walk (TrackShapeSettings clone — read live,
            // Generate swaps in a fresh clone). Max grade 0 is the flat track;
            // all of these need the reload the tab offers.
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.ElevationBand,
                         0f, 300f, 10f, "0", g => g.Shape.elevationBand, (g, v) => g.Shape.elevationBand = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.MaxGrade,
                         0f, 20f, 1f, "0", g => g.Shape.maxGrade, (g, v) => g.Shape.maxGrade = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.GradeStep,
                         0f, 10f, 0.5f, "0.0", g => g.Shape.maxGradeStepPerKnot, (g, v) => g.Shape.maxGradeStepPerKnot = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.BaselinePull,
                         0f, 1f, 0.05f, "0.00", g => g.Shape.baselinePull, (g, v) => g.Shape.baselinePull = v);

            // Banking into turns (turns come from STRAIGHTNESS above). Max bank 0 = level road.
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.MaxBank,
                         0f, 89f, 1f, "0", g => g.Shape.maxBankAngle, (g, v) => g.Shape.maxBankAngle = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.BankPerTurn,
                         0f, 10f, 0.5f, "0.0", g => g.Shape.bankPerDegreeOfTurn, (g, v) => g.Shape.bankPerDegreeOfTurn = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.BankStep,
                         0f, 90f, 5f, "0", g => g.Shape.maxBankStepPerKnot, (g, v) => g.Shape.maxBankStepPerKnot = v);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.LevelLead,
                         0f, 3000f, 50f, "0", g => g.Shape.levelLeadDistance, (g, v) => g.Shape.levelLeadDistance = v);

            // Where the road can kill: the share of sweeps laid flat (grip
            // tested, outer wall gone) and of straight runs with no walls.
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.UnbankedSweeps,
                         0f, 100f, 5f, "0", g => g.Shape.unbankedSweepChance * 100f, (g, v) => g.Shape.unbankedSweepChance = v / 100f);
            AddTrackStat(screen, generator, saved, onChanged, refreshers, MenuTextId.OpenStraights,
                         0f, 100f, 5f, "0", g => g.Shape.openStraightChance * 100f, (g, v) => g.Shape.openStraightChance = v / 100f);

            // Spawnables: one density row per spawner in the set — a multiplier
            // on its authored spacing (0 = none). Live: it only changes what is
            // still to be streamed. The rows come from the set's ASSETS (this
            // menu can be built before the first Generate); the values are read
            // from and written to the runtime clones, index for index.
            var authored = generator.SpawnSet != null ? generator.SpawnSet.Spawners : System.Array.Empty<TrackSpawner>();
            string densityFormat = MenuTextLibrary.Load().Get(MenuTextId.SpawnDensity);
            SpeedOrbSpawner authoredOrbs = null;
            for (int i = 0; i < authored.Length; i++)
            {
                if (authored[i] == null) continue;
                if (authoredOrbs == null) authoredOrbs = authored[i] as SpeedOrbSpawner;
                int index = i;
                var row = screen.AddRow<DebugSliderRow>(string.Format(densityFormat, authored[i].displayName.ToUpperInvariant()));
                row.Configure(0f, 5f, 0.25f, LiveSpawner(generator, index)?.Density ?? 1f, "0.00", v =>
                {
                    var live = LiveSpawner(generator, index);
                    if (live == null) return;
                    live.Density = v;
                    saved.CaptureFrom(generator);
                    onChanged?.Invoke();
                });
                row.SetLabelTint(authored[i].color);
                refreshers?.Add(() => row.SetWithoutNotify(LiveSpawner(generator, index)?.Density ?? 1f));
            }

            // One color-tinted percentage slider per speed-orb tier. Adjusting
            // one rebalances the others live, so the on-screen table always
            // adds up to exactly 100% — same rule as the inspector's tiers.
            if (authoredOrbs != null)
            {
                var probabilityRows = new List<DebugSliderRow>();
                var tiers = authoredOrbs.Tiers;
                for (int i = 0; i < tiers.Length; i++)
                {
                    int index = i;
                    var row = screen.AddRow<DebugSliderRow>($"{tiers[i].name.ToUpperInvariant()} %");
                    row.Configure(0f, 100f, 1f, LiveTier(generator, index)?.probability ?? tiers[i].probability, "0", v =>
                    {
                        var live = LiveTiers(generator);
                        if (live == null || index >= live.Length) return;
                        RebalanceProbabilities(live, probabilityRows, index, v);
                        saved.CaptureFrom(generator);
                        onChanged?.Invoke();
                    });
                    row.SetLabelTint(tiers[i].color);
                    probabilityRows.Add(row);
                    refreshers?.Add(() =>
                    {
                        var live = LiveTier(generator, index);
                        if (live != null) row.SetWithoutNotify(live.probability);
                    });
                }
            }

            screen.AddRow<MenuRow>(MenuTextId.ReloadScene).Activated += () => reloadScene?.Invoke();
            screen.SetViewport(9); // the shape rows pushed this page past the footer; the rest scroll in
            return screen;
        }

        /// <summary>
        /// Second tab: one color-tinted boost-multiplier slider per spawn
        /// entry (× GameSettings.powerUpSpeedBoost). Applies live to newly
        /// spawned orbs and is saved with the rest of the debug values.
        /// </summary>
        public static MenuScreen BuildMultipliersTab(RectTransform parent, MenuTheme theme,
                                                     TrackGenerator generator, TrackDebugSettings saved,
                                                     System.Action onChanged, List<System.Action> refreshers,
                                                     int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Multipliers", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabMultipliers, tabIndex, tabCount);

            // Built from the set's asset (see the Core tab); edits the runtime clone.
            SpeedOrbSpawner authoredOrbs = null;
            if (generator.SpawnSet != null)
                foreach (var spawner in generator.SpawnSet.Spawners)
                    if (spawner is SpeedOrbSpawner orbs) { authoredOrbs = orbs; break; }

            if (authoredOrbs != null)
            {
                var tiers = authoredOrbs.Tiers;
                for (int i = 0; i < tiers.Length; i++)
                {
                    int index = i;
                    var row = screen.AddRow<DebugSliderRow>($"{tiers[i].name.ToUpperInvariant()} ×");
                    row.Configure(0.1f, 10f, 0.1f, Mathf.Clamp(LiveTier(generator, index)?.multiplier ?? tiers[i].multiplier, 0.1f, 10f), "0.0", v =>
                    {
                        var live = LiveTier(generator, index);
                        if (live == null) return;
                        live.multiplier = v;
                        saved.CaptureFrom(generator);
                        onChanged?.Invoke();
                    });
                    row.SetLabelTint(tiers[i].color);
                    // Same rule as the Core tab: re-read on open, the saved
                    // values may land after the menu was built.
                    refreshers?.Add(() =>
                    {
                        var live = LiveTier(generator, index);
                        if (live != null) row.SetWithoutNotify(Mathf.Clamp(live.multiplier, 0.1f, 10f));
                    });
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
                    AddJumpStat(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.JumpLandingClearance,
                                0f, 600f, 10f, "0", j => j.landingClearance, (j, v) => j.landingClearance = v);
                }
                else if (entry.Runtime is LoopDefinition)
                {
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopRadius,
                                            40f, 250f, 5f, "0", l => l.radius, (l, v) => l.radius = v);
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopFallGravity,
                                            20f, 400f, 10f, "0", l => l.fallGravity, (l, v) => l.fallGravity = v);
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopFallLoss,
                                            0f, 1f, 0.05f, "0.00", l => l.fallSpeedLoss, (l, v) => l.fallSpeedLoss = v);
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopGateHeadroom,
                                            0f, 0.5f, 0.05f, "0.00", l => l.gateHeadroom, (l, v) => l.gateHeadroom = v);
                    // The variation bands: the sliders move each band's maximum (the minimum follows down).
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopDrift,
                                            0f, 600f, 20f, "0", l => l.DriftMax,
                                            (l, v) => l.lateralDriftRange = new Vector2(Mathf.Min(l.lateralDriftRange.x, v), v));
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopCarry,
                                            0f, 1000f, 20f, "0", l => l.CarryMax,
                                            (l, v) => l.forwardCarryRange = new Vector2(Mathf.Min(l.forwardCarryRange.x, v), v));
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopExitYaw,
                                            0f, 60f, 5f, "0", l => l.YawMax,
                                            (l, v) => l.exitYawRange = new Vector2(Mathf.Min(l.exitYawRange.x, v), v));
                    AddStat<LoopDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.LoopTurns,
                                            1f, 3f, 1f, "0", l => l.TurnsMax,
                                            (l, v) => l.turnsRange = new Vector2Int(Mathf.Min(l.turnsRange.x, Mathf.RoundToInt(v)), Mathf.RoundToInt(v)));
                }
                else if (entry.Runtime is TubeDefinition)
                {
                    AddStat<TubeDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.TubeRadius,
                                            20f, 150f, 5f, "0", t => t.radius, (t, v) => t.radius = v);
                    AddStat<TubeDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.TubeBand,
                                            15f, 180f, 5f, "0", t => t.bandDegrees, (t, v) => t.bandDegrees = v);
                    AddStat<TubeDefinition>(screen, generator, entry, saved, onChanged, refreshers, MenuTextId.TubeCurl,
                                            20f, 500f, 10f, "0", t => t.curlLength, (t, v) => t.curlLength = v);
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
            => AddStat(screen, generator, entry, saved, onChanged, refreshers, label, min, max, step, format, get, set);

        // One localized slider row bound to a knob of the entry's runtime
        // definition clone. The lambdas read entry.Runtime at call time, so
        // they always hit the clone the current run was generated with.
        static void AddStat<T>(MenuScreen screen, TrackGenerator generator, TrackGenerator.FeatureSpawnEntry entry,
                               FeatureDebugSettings saved, System.Action onChanged, List<System.Action> refreshers,
                               MenuTextId label, float min, float max, float step, string format,
                               System.Func<T, float> get, System.Action<T, float> set) where T : TrackFeatureDefinition
        {
            T Def() => entry.Runtime as T;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, Def() != null ? get(Def()) : min, format, v =>
            {
                var def = Def();
                if (def == null) return;
                set(def, v);
                saved.CaptureFrom(generator);
                onChanged?.Invoke();
            });
            row.SetLabelTint(entry.color);
            refreshers?.Add(() => { var def = Def(); if (def != null) row.SetWithoutNotify(get(def)); });
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
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.CruiseSpeed,
                        0f, 1000f, 10f, "0", d => d.cruiseSpeed, (d, v) => d.cruiseSpeed = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Thrust,
                        0f, 300f, 5f, "0", d => d.thrust, (d, v) => d.thrust = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.BrakePower,
                        0f, 500f, 10f, "0", d => d.brakeDecel, (d, v) => d.brakeDecel = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.CoastDrag,
                        0f, 100f, 1f, "0", d => d.coastDrag, (d, v) => d.coastDrag = v);
            // The over-cruise bleed: what pulls a boosted ship back down to cruise.
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Deceleration,
                        0f, 50f, 0.5f, "0.0", d => d.passiveDeceleration, (d, v) => d.passiveDeceleration = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Acceleration,
                        1f, 200f, 5f, "0", d => d.acceleration, (d, v) => d.acceleration = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.Weight,
                        0.1f, 5f, 0.1f, "0.0", d => d.weight, (d, v) => d.weight = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.KeyThrottleRamp,
                        0f, 1f, 0.05f, "0.00", d => d.digitalThrottleRampSeconds, (d, v) => d.digitalThrottleRampSeconds = v);
            screen.SetViewport(9);
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
            // Grip on flat sweeps: demand v²κ against gripBase + gripPerSpeed·v.
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.GripBase,
                        0f, 500f, 5f, "0", d => d.gripBase, (d, v) => d.gripBase = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.GripPerSpeed,
                        0f, 3f, 0.05f, "0.00", d => d.gripPerSpeed, (d, v) => d.gripPerSpeed = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.SlideThreshold,
                        0f, 30f, 0.5f, "0.0", d => d.slideThreshold, (d, v) => d.slideThreshold = v);
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.SlideSpeedLoss,
                        0f, 1f, 0.05f, "0.00", d => d.slideSpeedLoss, (d, v) => d.slideSpeedLoss = v);
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
            AddShipStat(screen, motor, saved, onChanged, refreshers, MenuTextId.BarrelRollSeconds,
                        0.2f, 1.5f, 0.05f, "0.00", d => d.barrelRollSeconds, (d, v) => d.barrelRollSeconds = v);
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
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolBoostShare,
                          0f, 1.5f, 0.05f, "0.00", d => d.boostShare, (d, v) => d.boostShare = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolStartGap,
                          0f, 1000f, 25f, "0", d => d.startGap, (d, v) => d.startGap = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolCatchDistance,
                          0f, 100f, 5f, "0", d => d.catchDistance, (d, v) => d.catchDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolWarnDistance,
                          0f, 500f, 10f, "0", d => d.warnDistance, (d, v) => d.warnDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolCatchLateral,
                          0f, 60f, 1f, "0", d => d.alongsideLateral, (d, v) => d.alongsideLateral = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolSustainedCatch,
                          0f, 20f, 0.25f, "0.00", d => d.sustainedCatchSeconds, (d, v) => d.sustainedCatchSeconds = v);
            screen.SetViewport(9);
            return screen;
        }

        /// <summary>
        /// Patrol duel tab: the attack run — how hard the patrol overdrives to
        /// reach you, how often it tries, how long it holds the flank before
        /// it shoves, and how easily you can break it off. Same clone / capture
        /// rules as the other patrol tabs; everything applies instantly, and a
        /// run in progress picks the new numbers up on its next substep.
        ///
        /// ATTACK INTERVAL is the one that decides how much of a run is spent
        /// duelling; CLEAR ROAD AHEAD is how much clean track the patrol
        /// insists on before it will start (it never duels on a ramp, its
        /// landing, a loop, a tube or the final run-up).
        /// </summary>
        public static MenuScreen BuildDuelTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                              PatrolDebugSettings saved, GameSettings runRules,
                                              System.Action onChanged,
                                              List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Duel", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabDuel, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelOverdrive,
                          1f, 2f, 0.01f, "0.00", d => d.attackRunOverdrive, (d, v) => d.attackRunOverdrive = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelCommitFrom,
                          50f, 1500f, 25f, "0", d => d.commitFromDistance, (d, v) => d.commitFromDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelCommitInterval,
                          2f, 60f, 0.5f, "0.0", d => d.commitIntervalSeconds, (d, v) => d.commitIntervalSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelCommitTimeout,
                          2f, 60f, 0.5f, "0.0", d => d.commitTimeoutSeconds, (d, v) => d.commitTimeoutSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelAlongsideDistance,
                          0f, 60f, 1f, "0", d => d.alongsideDistance, (d, v) => d.alongsideDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelFlankOffset,
                          0f, 30f, 0.5f, "0.0", d => d.flankOffsetMeters, (d, v) => d.flankOffsetMeters = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelAlongsideHold,
                          0f, 5f, 0.1f, "0.0", d => d.alongsideHoldSeconds, (d, v) => d.alongsideHoldSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelAbortGrace,
                          0f, 3f, 0.05f, "0.00", d => d.abortGraceSeconds, (d, v) => d.abortGraceSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelBreakOff,
                          0f, 5f, 0.1f, "0.0", d => d.breakOffSeconds, (d, v) => d.breakOffSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelBackOffSpeed,
                          0.5f, 1f, 0.01f, "0.00", d => d.breakOffSpeedFactor, (d, v) => d.breakOffSpeedFactor = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelCooldown,
                          0f, 60f, 0.5f, "0.0", d => d.attackRunCooldownSeconds, (d, v) => d.attackRunCooldownSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelLookahead,
                          50f, 1000f, 25f, "0", d => d.encounterLookaheadMeters, (d, v) => d.encounterLookaheadMeters = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelShoveMeters,
                          0f, 40f, 0.5f, "0.0", d => d.shoveMeters, (d, v) => d.shoveMeters = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelTugForce,
                          0f, 1f, 0.01f, "0.00", d => d.tugPatrolForce, (d, v) => d.tugPatrolForce = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelTugPress,
                          0.01f, 0.5f, 0.01f, "0.00", d => d.tugPressValue, (d, v) => d.tugPressValue = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelAbortMeters,
                          20f, 400f, 5f, "0", d => d.encounterAbortMeters, (d, v) => d.encounterAbortMeters = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelStandoff,
                          0f, 200f, 5f, "0", d => d.standoffDistance, (d, v) => d.standoffDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelKillGap,
                          0f, 1500f, 25f, "0", d => d.killTeleportGap, (d, v) => d.killTeleportGap = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelStationAccel,
                          0f, 300f, 5f, "0", d => d.stationAccel, (d, v) => d.stationAccel = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelDamagePool,
                          1f, 10f, 1f, "0", d => d.damagePoolMax, (d, v) => d.damagePoolMax = Mathf.RoundToInt(v));
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelRamDistance,
                          0f, 30f, 0.5f, "0.0", d => d.ramContactDistance, (d, v) => d.ramContactDistance = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelRamLateral,
                          0f, 20f, 0.5f, "0.0", d => d.ramContactLateral, (d, v) => d.ramContactLateral = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelRamClosing,
                          0f, 100f, 1f, "0", d => d.ramClosingSpeedThreshold, (d, v) => d.ramClosingSpeedThreshold = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelTierPerKill,
                          0f, 1f, 0.01f, "0.00", d => d.tierScalePerKill, (d, v) => d.tierScalePerKill = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelTierMax,
                          1f, 5f, 0.05f, "0.00", d => d.tierScaleMax, (d, v) => d.tierScaleMax = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelTugForceCap,
                          1f, 3f, 0.05f, "0.00", d => d.tugForceMaxScale, (d, v) => d.tugForceMaxScale = v);

            // The cinematic duel: the brake-triggered overshoot, how far the
            // contest walks the locked ship, the finisher's separation and the
            // miss brake.
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelMinClosing,
                          0f, 60f, 1f, "0", d => d.minClosingSpeed, (d, v) => d.minClosingSpeed = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelOvershootHold,
                          0f, 6f, 0.1f, "0.0", d => d.overshootHoldSeconds, (d, v) => d.overshootHoldSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelOvershootBrake,
                          0f, 1f, 0.05f, "0.00", d => d.overshootBrakeThreshold, (d, v) => d.overshootBrakeThreshold = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelOvershootDecel,
                          0f, 200f, 5f, "0", d => d.overshootDecelThreshold, (d, v) => d.overshootDecelThreshold = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelOvershootMargin,
                          0f, 100f, 5f, "0", d => d.overshootTriggerMargin, (d, v) => d.overshootTriggerMargin = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelPushFraction,
                          0f, 0.85f, 0.05f, "0.00", d => d.tugPushFraction, (d, v) => d.tugPushFraction = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelPushGain,
                          0.25f, 8f, 0.25f, "0.00", d => d.tugPushGain, (d, v) => d.tugPushGain = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelFinisherSeparation,
                          0f, 15f, 0.5f, "0.0", d => d.finisherSeparationMeters, (d, v) => d.finisherSeparationMeters = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelMissBrake,
                          0f, 5f, 0.1f, "0.0", d => d.finisherMissBrakeSeconds, (d, v) => d.finisherMissBrakeSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.DuelMissBrakeSpeed,
                          0.2f, 1f, 0.05f, "0.00", d => d.finisherMissBrakeSpeedFactor, (d, v) => d.finisherMissBrakeSpeedFactor = v);

            // The clock and the assist live on GameSettings, which the run
            // reads LIVE and never clones — so these two edit the asset itself,
            // the fall/respawn page's rule, not the patrol tabs'. They are the
            // first two dials to reach for if an exchange feels detached:
            // take the timescale UP before you add any more help.
            if (runRules != null)
            {
                AddRunStat(screen, runRules, refreshers, MenuTextId.DuelTimeScale,
                           0.1f, 1f, 0.05f, "0.00", r => r.duelTimeScale, (r, v) => r.duelTimeScale = v);
                AddRunStat(screen, runRules, refreshers, MenuTextId.DuelAssist,
                           0f, 1f, 0.05f, "0.00", r => r.duelAssistStrength, (r, v) => r.duelAssistStrength = v);
                AddRunStat(screen, runRules, refreshers, MenuTextId.DuelFinisherWindow,
                           0.2f, 3f, 0.05f, "0.00", r => r.finisherWindowSeconds, (r, v) => r.finisherWindowSeconds = v);
                AddRunStat(screen, runRules, refreshers, MenuTextId.DuelHitStop,
                           0f, 0.5f, 0.01f, "0.00", r => r.duelHitStopSeconds, (r, v) => r.duelHitStopSeconds = v);
                AddRunStat(screen, runRules, refreshers, MenuTextId.DuelRamCost,
                           0f, 0.5f, 0.01f, "0.00", r => r.ramSpeedCost, (r, v) => r.ramSpeedCost = v);
                AddRunStat(screen, runRules, refreshers, MenuTextId.DuelArmedWindow,
                           0.5f, 10f, 0.25f, "0.00", r => r.armedWindowSeconds, (r, v) => r.armedWindowSeconds = v);

                // The duel camera framing lives on the camera settings asset,
                // which the rig re-applies live every frame — same edit-the-asset
                // rule as the four rows above.
                var cam = runRules.cameraSettings;
                if (cam != null)
                {
                    AddCameraStat(screen, cam, refreshers, MenuTextId.CamDuelDistance,
                                  1.5f, 80f, 0.5f, "0.0", c => c.duelDistance, (c, v) => c.duelDistance = v);
                    AddCameraStat(screen, cam, refreshers, MenuTextId.CamDuelHeight,
                                  0f, 12f, 0.1f, "0.0", c => c.duelLookHeight, (c, v) => c.duelLookHeight = v);
                    AddCameraStat(screen, cam, refreshers, MenuTextId.CamDuelPitch,
                                  0f, 60f, 1f, "0", c => c.duelPitch, (c, v) => c.duelPitch = v);
                    AddCameraStat(screen, cam, refreshers, MenuTextId.CamDuelBlend,
                                  0.05f, 2f, 0.05f, "0.00", c => c.duelBlendSeconds, (c, v) => c.duelBlendSeconds = v);
                }
            }
            screen.SetViewport(9);
            return screen;
        }

        /// <summary>A live-asset slider over the camera settings, the <see cref="AddRunStat"/> rule for a different asset.</summary>
        static void AddCameraStat(MenuScreen screen, OrbitCameraSettings settings, List<System.Action> refreshers,
                                  MenuTextId label, float min, float max, float step, string format,
                                  System.Func<OrbitCameraSettings, float> get, System.Action<OrbitCameraSettings, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(settings), format, v =>
            {
                set(settings, v);
#if UNITY_EDITOR
                if (settings != null && UnityEditor.EditorUtility.IsPersistent(settings))
                    UnityEditor.EditorUtility.SetDirty(settings);
#endif
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(settings)));
        }

        // A GameSettings row: the asset is read live by the run, so the edit
        // lands at once and is kept dirty for the menu's commit point.
        static void AddRunStat(MenuScreen screen, GameSettings settings, List<System.Action> refreshers,
                               MenuTextId label, float min, float max, float step, string format,
                               System.Func<GameSettings, float> get, System.Action<GameSettings, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(settings), format, v =>
            {
                set(settings, v);
#if UNITY_EDITOR
                if (settings != null && UnityEditor.EditorUtility.IsPersistent(settings))
                    UnityEditor.EditorUtility.SetDirty(settings);
#endif
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(settings)));
        }

        /// <summary>
        /// Patrol driver tab: how the cruiser handles (the ship's own steering
        /// and grip rules, its brake) and how its <see cref="PatrolDriver"/>
        /// reads the road — look-aheads, orb appetite, its cut of an orb.
        /// Same clone / capture rules as the patrol tab; all apply instantly.
        /// </summary>
        public static MenuScreen BuildPatrolDriverTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                                      PatrolDebugSettings saved, System.Action onChanged,
                                                      List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_PatrolDriver", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPatrolDriver, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.LateralSpeed,
                          0f, 100f, 1f, "0", d => d.lateralSpeed, (d, v) => d.lateralSpeed = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.SteerResponse,
                          0.5f, 30f, 0.5f, "0.0", d => d.handlingResponse, (d, v) => d.handlingResponse = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.GripBase,
                          0f, 500f, 5f, "0", d => d.gripBase, (d, v) => d.gripBase = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.GripPerSpeed,
                          0f, 3f, 0.05f, "0.00", d => d.gripPerSpeed, (d, v) => d.gripPerSpeed = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.BrakePower,
                          0f, 500f, 10f, "0", d => d.brakeDecel, (d, v) => d.brakeDecel = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolCurveLookahead,
                          0.5f, 10f, 0.25f, "0.00", d => d.curveLookaheadSeconds, (d, v) => d.curveLookaheadSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolOrbLookahead,
                          0f, 10f, 0.25f, "0.00", d => d.orbLookaheadSeconds, (d, v) => d.orbLookaheadSeconds = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolOrbSeek,
                          0f, 1f, 0.05f, "0.00", d => d.orbSeekWeight, (d, v) => d.orbSeekWeight = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolOrbBoost,
                          0f, 1.5f, 0.05f, "0.00", d => d.orbBoostShare, (d, v) => d.orbBoostShare = v);
            AddPatrolStat(screen, patrol, saved, onChanged, refreshers, MenuTextId.PatrolRampLookahead,
                          0.5f, 10f, 0.25f, "0.00", d => d.rampLookaheadSeconds, (d, v) => d.rampLookaheadSeconds = v);
            screen.SetViewport(9);
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
        // One localized slider row bound to a TrackGenerator knob. Reads the
        // generator at call time and re-reads on every menu open: the menu is
        // built in the GameManager's Awake, which may run before the
        // generator's Awake has applied the saved TrackDebugSettings.
        static void AddTrackStat(MenuScreen screen, TrackGenerator generator, TrackDebugSettings saved,
                                 System.Action onChanged, List<System.Action> refreshers, MenuTextId label,
                                 float min, float max, float step, string format,
                                 System.Func<TrackGenerator, float> get,
                                 System.Action<TrackGenerator, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(generator), format, v =>
            {
                set(generator, v);
                saved.CaptureFrom(generator);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(generator)));
        }

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

        // The runtime clone at a set index, or null before the first Generate.
        static TrackSpawner LiveSpawner(TrackGenerator generator, int index) =>
            index < generator.Spawners.Count ? generator.Spawners[index] : null;

        static PadSpawnEntry[] LiveTiers(TrackGenerator generator) => generator.GetSpawner<SpeedOrbSpawner>()?.Tiers;

        static PadSpawnEntry LiveTier(TrackGenerator generator, int index)
        {
            var tiers = LiveTiers(generator);
            return tiers != null && index < tiers.Length ? tiers[index] : null;
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
