using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// Builders for the runner's debug tabs. One rule for every row: gameplay
    /// never writes a settings asset, the debug menu does — each row writes the
    /// ASSET (so the edit survives a reload and play-mode exit, saved at the
    /// menu's commit points through <see cref="DebugAssetEdits"/>) and then
    /// mirrors the value onto the run's live clone, so it applies at once
    /// where the system reads live. The ship's clone carries the Store's
    /// upgrades, so it is rebuilt from the asset (<see cref="ShipUpgradeApplier.Refresh"/>)
    /// rather than written field by field. Track layout rows (width,
    /// straightness, shape, features) take effect on the next Generate: the
    /// Core tab's RELOAD SCENE row.
    /// </summary>
    public static class DebugMenuFactory
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen BuildCoreSettingsTab(RectTransform parent, MenuTheme theme,
                                                      TrackGenerator generator, GameManager game,
                                                      System.Action reloadScene, System.Action onChanged,
                                                      List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CoreSettings", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCore, tabIndex, tabCount);

            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.TrackWidth,
                         10f, 120f, 5f, "0", s => s.trackWidth, (s, v) => s.trackWidth = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.Straightness,
                         0f, 100f, 5f, "0", s => s.straightness, (s, v) => s.straightness = v);

            // The finite track's length, metres: written to the asset the run
            // resolves it from (the level's own, else the GameSettings fallback).
            if (game != null)
            {
                var lengthRow = screen.AddRow<DebugSliderRow>(MenuTextId.TrackLength);
                lengthRow.Configure(1000f, 100000f, 1000f, game.TrackLengthMeters, "0", v =>
                {
                    game.SetTrackLengthFromDebug(v);
                    onChanged?.Invoke();
                });
                refreshers?.Add(() => lengthRow.SetWithoutNotify(game.TrackLengthMeters));

                // 1 = ignore the level's saved track and generate one from the
                // next restart (to compare them). A runtime switch: no asset is written.
                var forceRow = screen.AddRow<DebugSliderRow>("FORCE RUNTIME TRACK");
                forceRow.Configure(0f, 1f, 1f, game.ForceRuntimeTrack ? 1f : 0f, "0", v => game.ForceRuntimeTrack = v > 0.5f);
                refreshers?.Add(() => forceRow.SetWithoutNotify(game.ForceRuntimeTrack ? 1f : 0f));
            }

            // The road's elevation walk. Max grade 0 is the flat track.
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.ElevationBand,
                         0f, 300f, 10f, "0", s => s.elevationBand, (s, v) => s.elevationBand = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.MaxGrade,
                         0f, 20f, 1f, "0", s => s.maxGrade, (s, v) => s.maxGrade = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.GradeStep,
                         0f, 10f, 0.5f, "0.0", s => s.maxGradeStepPerKnot, (s, v) => s.maxGradeStepPerKnot = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.BaselinePull,
                         0f, 1f, 0.05f, "0.00", s => s.baselinePull, (s, v) => s.baselinePull = v);

            // Banking into turns (turns come from STRAIGHTNESS above). Max bank 0 = level road.
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.MaxBank,
                         0f, 89f, 1f, "0", s => s.maxBankAngle, (s, v) => s.maxBankAngle = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.BankPerTurn,
                         0f, 10f, 0.5f, "0.0", s => s.bankPerDegreeOfTurn, (s, v) => s.bankPerDegreeOfTurn = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.BankStep,
                         0f, 90f, 5f, "0", s => s.maxBankStepPerKnot, (s, v) => s.maxBankStepPerKnot = v);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.LevelLead,
                         0f, 3000f, 50f, "0", s => s.levelLeadDistance, (s, v) => s.levelLeadDistance = v);

            // Where the road can kill: the share of sweeps laid flat (grip
            // tested, outer wall gone) and of straight runs with no walls.
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.UnbankedSweeps,
                         0f, 100f, 5f, "0", s => s.unbankedSweepChance * 100f, (s, v) => s.unbankedSweepChance = v / 100f);
            AddShapeStat(screen, generator, onChanged, refreshers, MenuTextId.OpenStraights,
                         0f, 100f, 5f, "0", s => s.openStraightChance * 100f, (s, v) => s.openStraightChance = v / 100f);

            // Spawnables: one spacing row per spawner in the set — the band's
            // minimum, sliding the band and keeping its spread. Live: it only
            // changes what is still to be streamed. Rows come from the set's
            // ASSETS (this menu can be built before the first Generate).
            var authored = generator.SpawnSet != null ? generator.SpawnSet.Spawners : System.Array.Empty<TrackSpawner>();
            SpeedOrbSpawner authoredOrbs = null;
            for (int i = 0; i < authored.Length; i++)
            {
                var asset = authored[i];
                if (asset == null) continue;
                if (authoredOrbs == null) authoredOrbs = asset as SpeedOrbSpawner;
                int index = i;
                var row = screen.AddRow<DebugSliderRow>($"{asset.displayName.ToUpperInvariant()} SPACING");
                row.Configure(10f, 5000f, 50f, asset.spacing.x, "0", v =>
                {
                    SlideBand(ref asset.spacing, v);
                    DebugAssetEdits.Touch(asset);
                    var live = LiveSpawner(generator, index);
                    if (live != null && live != asset) live.spacing = asset.spacing;
                    onChanged?.Invoke();
                });
                row.SetLabelTint(asset.color);
                refreshers?.Add(() => row.SetWithoutNotify(asset.spacing.x));
            }

            // One color-tinted percentage slider per speed-orb tier. Adjusting
            // one rebalances the others, so the table always adds up to 100%.
            if (authoredOrbs != null)
            {
                var probabilityRows = new List<DebugSliderRow>();
                var tiers = authoredOrbs.Tiers;
                for (int i = 0; i < tiers.Length; i++)
                {
                    int index = i;
                    var row = screen.AddRow<DebugSliderRow>($"{tiers[i].name.ToUpperInvariant()} %");
                    row.Configure(0f, 100f, 1f, tiers[i].probability, "0", v =>
                    {
                        RebalanceProbabilities(tiers, probabilityRows, index, v);
                        DebugAssetEdits.Touch(authoredOrbs);
                        MirrorTiers(tiers, LiveTiers(generator));
                        onChanged?.Invoke();
                    });
                    row.SetLabelTint(tiers[i].color);
                    probabilityRows.Add(row);
                    refreshers?.Add(() => row.SetWithoutNotify(tiers[index].probability));
                }
            }

            screen.AddRow<MenuRow>(MenuTextId.ReloadScene).Activated += () => reloadScene?.Invoke();
            screen.SetViewport(9); // the shape rows pushed this page past the footer; the rest scroll in
            return screen;
        }

        /// <summary>
        /// Second tab: one color-tinted boost-multiplier slider per orb tier
        /// (× GameSettings.powerUpSpeedBoost), on the spawner asset and its
        /// live clone. Applies to newly spawned orbs.
        /// </summary>
        public static MenuScreen BuildMultipliersTab(RectTransform parent, MenuTheme theme,
                                                     TrackGenerator generator,
                                                     System.Action onChanged, List<System.Action> refreshers,
                                                     int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Multipliers", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabMultipliers, tabIndex, tabCount);

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
                    row.Configure(0.1f, 10f, 0.1f, Mathf.Clamp(tiers[i].multiplier, 0.1f, 10f), "0.0", v =>
                    {
                        tiers[index].multiplier = v;
                        DebugAssetEdits.Touch(authoredOrbs);
                        MirrorTiers(tiers, LiveTiers(generator));
                        onChanged?.Invoke();
                    });
                    row.SetLabelTint(tiers[i].color);
                    refreshers?.Add(() => row.SetWithoutNotify(Mathf.Clamp(tiers[index].multiplier, 0.1f, 10f)));
                }
            }

            return screen;
        }

        /// <summary>
        /// FEATURES tab: the feature spacing band (one slider that slides the
        /// band and keeps its spread), one probability / spacing / boost row
        /// per feature entry, and each definition's knobs. The table lives on
        /// the shape asset and the definitions are assets of their own: every
        /// row writes the asset and mirrors onto the run's clone. A placed
        /// feature keeps the numbers it was built with, so most rows need the
        /// reload the pause menu offers.
        /// </summary>
        public static MenuScreen BuildFeaturesTab(RectTransform parent, MenuTheme theme,
                                                  TrackGenerator generator,
                                                  System.Action onChanged, List<System.Action> refreshers,
                                                  int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Features", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            screen.SetViewport(9); // dozens of feature rows — nine at a time, the rest scroll in
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabFeatures, tabIndex, tabCount);

            var shapeAsset = generator.ShapeAsset;
            if (shapeAsset == null) return screen;

            var spacingRow = screen.AddRow<DebugSliderRow>(MenuTextId.FeatureSpacing);
            spacingRow.Configure(100f, 4000f, 50f, shapeAsset.featureSpacing.x, "0", v =>
            {
                SlideBand(ref shapeAsset.featureSpacing, v);
                DebugAssetEdits.Touch(shapeAsset);
                if (generator.Shape != shapeAsset) generator.Shape.featureSpacing = shapeAsset.featureSpacing;
                onChanged?.Invoke();
            });
            refreshers?.Add(() => spacingRow.SetWithoutNotify(shapeAsset.featureSpacing.x));

            var table = shapeAsset.featureTable;
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
                    DebugAssetEdits.Touch(shapeAsset);
                    MirrorFeatureTable(table, generator.FeatureTable);
                    onChanged?.Invoke();
                });
                row.SetLabelTint(entry.color);
                probabilityRows.Add(row);

                var spacing = screen.AddRow<DebugSliderRow>($"{tag} SPACING");
                spacing.Configure(0f, 3000f, 50f, entry.minSpacing, "0", v =>
                {
                    entry.minSpacing = v;
                    DebugAssetEdits.Touch(shapeAsset);
                    MirrorFeatureTable(table, generator.FeatureTable);
                    onChanged?.Invoke();
                });
                spacing.SetLabelTint(entry.color);

                var boost = screen.AddRow<DebugSliderRow>($"{tag} ×");
                boost.Configure(0f, 10f, 0.1f, Mathf.Clamp(entry.multiplier, 0f, 10f), "0.0", v =>
                {
                    entry.multiplier = v;
                    DebugAssetEdits.Touch(shapeAsset);
                    MirrorFeatureTable(table, generator.FeatureTable);
                    onChanged?.Invoke();
                });
                boost.SetLabelTint(entry.color);

                if (entry.definition is JumpDefinition)
                {
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpWidth,
                                0.05f, 1f, 0.05f, "0.00", j => j.widthFraction, (j, v) => j.widthFraction = v);
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpLength,
                                10f, 200f, 5f, "0", j => j.length, (j, v) => j.length = v);
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpAngle,
                                5f, 45f, 1f, "0", j => j.rampAngle, (j, v) => j.rampAngle = v);
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpAirDistance,
                                0.05f, 3f, 0.05f, "0.00", j => j.airDistancePerSpeed, (j, v) => j.airDistancePerSpeed = v);
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpMaxAir,
                                20f, 2000f, 20f, "0", j => j.airDistanceRange.y,
                                (j, v) => j.airDistanceRange = new Vector2(Mathf.Min(j.airDistanceRange.x, v), v));
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpAirControl,
                                0f, 1f, 0.05f, "0.00", j => j.airControlFactor, (j, v) => j.airControlFactor = v);
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpSideHitLoss,
                                0f, 1f, 0.05f, "0.00", j => j.sideHitSpeedLoss, (j, v) => j.sideHitSpeedLoss = v);
                    AddStat<JumpDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.JumpLandingClearance,
                                0f, 600f, 10f, "0", j => j.landingClearance, (j, v) => j.landingClearance = v);
                }
                else if (entry.definition is LoopDefinition)
                {
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopRadius,
                                            40f, 250f, 5f, "0", l => l.radius, (l, v) => l.radius = v);
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopFallGravity,
                                            20f, 400f, 10f, "0", l => l.fallGravity, (l, v) => l.fallGravity = v);
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopFallLoss,
                                            0f, 1f, 0.05f, "0.00", l => l.fallSpeedLoss, (l, v) => l.fallSpeedLoss = v);
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopGateHeadroom,
                                            0f, 0.5f, 0.05f, "0.00", l => l.gateHeadroom, (l, v) => l.gateHeadroom = v);
                    // The variation bands: the sliders move each band's maximum (the minimum follows down).
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopDrift,
                                            0f, 600f, 20f, "0", l => l.DriftMax,
                                            (l, v) => l.lateralDriftRange = new Vector2(Mathf.Min(l.lateralDriftRange.x, v), v));
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopCarry,
                                            0f, 1000f, 20f, "0", l => l.CarryMax,
                                            (l, v) => l.forwardCarryRange = new Vector2(Mathf.Min(l.forwardCarryRange.x, v), v));
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopExitYaw,
                                            0f, 60f, 5f, "0", l => l.YawMax,
                                            (l, v) => l.exitYawRange = new Vector2(Mathf.Min(l.exitYawRange.x, v), v));
                    AddStat<LoopDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.LoopTurns,
                                            1f, 3f, 1f, "0", l => l.TurnsMax,
                                            (l, v) => l.turnsRange = new Vector2Int(Mathf.Min(l.turnsRange.x, Mathf.RoundToInt(v)), Mathf.RoundToInt(v)));
                }
                else if (entry.definition is TubeDefinition)
                {
                    AddStat<TubeDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.TubeRadius,
                                            20f, 150f, 5f, "0", t => t.radius, (t, v) => t.radius = v);
                    AddStat<TubeDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.TubeBand,
                                            15f, 180f, 5f, "0", t => t.bandDegrees, (t, v) => t.bandDegrees = v);
                    AddStat<TubeDefinition>(screen, generator, index, entry, onChanged, refreshers, MenuTextId.TubeCurl,
                                            20f, 500f, 10f, "0", t => t.curlLength, (t, v) => t.curlLength = v);
                }
            }
            return screen;
        }

        // One localized slider row bound to a knob of a feature definition:
        // writes the definition ASSET, then the run's clone of it (read at
        // call time off the live table, so it always hits the current run's).
        static void AddStat<T>(MenuScreen screen, TrackGenerator generator, int index,
                               TrackGenerator.FeatureSpawnEntry assetEntry,
                               System.Action onChanged, List<System.Action> refreshers,
                               MenuTextId label, float min, float max, float step, string format,
                               System.Func<T, float> get, System.Action<T, float> set) where T : TrackFeatureDefinition
        {
            var asset = assetEntry.definition as T;
            if (asset == null) return;
            T Clone()
            {
                var live = generator.FeatureTable;
                return live != null && index < live.Length ? live[index].Runtime as T : null;
            }
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(asset), format, v =>
            {
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                var clone = Clone();
                if (clone != null && clone != asset) set(clone, v);
                onChanged?.Invoke();
            });
            row.SetLabelTint(assetEntry.color);
            refreshers?.Add(() => row.SetWithoutNotify(get(asset)));
        }

        // One localized slider row bound to a TrackShapeSettings knob: writes
        // the shape asset, then the generator's clone. Takes effect on the next
        // Generate (the Core tab's reload).
        static void AddShapeStat(MenuScreen screen, TrackGenerator generator,
                                 System.Action onChanged, List<System.Action> refreshers, MenuTextId label,
                                 float min, float max, float step, string format,
                                 System.Func<TrackShapeSettings, float> get,
                                 System.Action<TrackShapeSettings, float> set)
        {
            TrackShapeSettings Source() => generator.ShapeAsset != null ? generator.ShapeAsset : generator.Shape;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(Source()), format, v =>
            {
                var asset = Source();
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                if (generator.Shape != asset) set(generator.Shape, v);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(Source())));
        }

        // Moves a (min, max) band so its minimum lands on value, keeping its spread.
        static void SlideBand(ref Vector2 band, float value)
        {
            float spread = Mathf.Max(0f, band.y - band.x);
            band = new Vector2(value, value + spread);
        }

        // Copies the asset tiers' probabilities and multipliers onto the live clone's, index for index.
        static void MirrorTiers(PadSpawnEntry[] asset, PadSpawnEntry[] live)
        {
            if (asset == null || live == null || asset == live) return;
            for (int i = 0; i < asset.Length && i < live.Length; i++)
            {
                live[i].probability = asset[i].probability;
                live[i].multiplier = asset[i].multiplier;
            }
        }

        // Copies the asset feature table's roll values onto the live clone's, index for index.
        static void MirrorFeatureTable(TrackGenerator.FeatureSpawnEntry[] asset, TrackGenerator.FeatureSpawnEntry[] live)
        {
            if (asset == null || live == null || asset == live) return;
            for (int i = 0; i < asset.Length && i < live.Length; i++)
            {
                live[i].probability = asset[i].probability;
                live[i].minSpacing = asset[i].minSpacing;
                live[i].multiplier = asset[i].multiplier;
            }
        }

        /// <summary>
        /// Ship tabs: four pages of <see cref="ShipDefinition"/> sliders (Speed,
        /// Handling, Dash, Hover). Each row writes the ship's definition ASSET
        /// and rebuilds the run's clone from it with the Store's levels
        /// multiplied in, so most stats apply instantly; the rest (launch
        /// speed) need the reload the pause menu offers on the way out. The
        /// sliders show the asset's values (before upgrades).
        /// </summary>
        public static MenuScreen BuildShipSpeedTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                   System.Action onChanged,
                                                   List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipSpeed", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipSpeed, tabIndex, tabCount);

            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.LaunchSpeed,
                        0f, 1000f, 10f, "0", d => d.initialImpulse, (d, v) => d.initialImpulse = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.CruiseSpeed,
                        0f, 3500f, 10f, "0", d => d.cruiseSpeed, (d, v) => d.cruiseSpeed = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.Thrust,
                        0f, 300f, 5f, "0", d => d.thrust, (d, v) => d.thrust = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.BrakePower,
                        0f, 500f, 10f, "0", d => d.brakeDecel, (d, v) => d.brakeDecel = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.CoastDrag,
                        0f, 100f, 1f, "0", d => d.coastDrag, (d, v) => d.coastDrag = v);
            // The over-cruise bleed: what pulls a boosted ship back down to cruise.
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.Deceleration,
                        0f, 50f, 0.5f, "0.0", d => d.passiveDeceleration, (d, v) => d.passiveDeceleration = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.Acceleration,
                        1f, 200f, 5f, "0", d => d.acceleration, (d, v) => d.acceleration = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.Weight,
                        0.1f, 5f, 0.1f, "0.0", d => d.weight, (d, v) => d.weight = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.KeyThrottleRamp,
                        0f, 1f, 0.05f, "0.00", d => d.digitalThrottleRampSeconds, (d, v) => d.digitalThrottleRampSeconds = v);
            screen.SetViewport(9);
            return screen;
        }

        public static MenuScreen BuildShipHandlingTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                      System.Action onChanged,
                                                      List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipHandling", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipHandling, tabIndex, tabCount);

            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.LateralSpeed,
                        0f, 100f, 1f, "0", d => d.lateralSpeed, (d, v) => d.lateralSpeed = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.SteerResponse,
                        0.5f, 30f, 0.5f, "0.0", d => d.handlingResponse, (d, v) => d.handlingResponse = v);
            // Grip on flat sweeps: demand v²κ against gripBase + gripPerSpeed·v.
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.GripBase,
                        0f, 500f, 5f, "0", d => d.gripBase, (d, v) => d.gripBase = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.GripPerSpeed,
                        0f, 3f, 0.05f, "0.00", d => d.gripPerSpeed, (d, v) => d.gripPerSpeed = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.SlideThreshold,
                        0f, 30f, 0.5f, "0.0", d => d.slideThreshold, (d, v) => d.slideThreshold = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.SlideSpeedLoss,
                        0f, 1f, 0.05f, "0.00", d => d.slideSpeedLoss, (d, v) => d.slideSpeedLoss = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.BankAngle,
                        0f, 90f, 5f, "0", d => d.maxBankAngle, (d, v) => d.maxBankAngle = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.BankResponse,
                        0.5f, 20f, 0.5f, "0.0", d => d.bankResponse, (d, v) => d.bankResponse = v);
            return screen;
        }

        public static MenuScreen BuildShipDashTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                  System.Action onChanged,
                                                  List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipDash", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipDash, tabIndex, tabCount);

            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.DashDistance,
                        2f, 30f, 1f, "0", d => d.dashDistance, (d, v) => d.dashDistance = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.DashDuration,
                        0.05f, 1f, 0.05f, "0.00", d => d.dashDuration, (d, v) => d.dashDuration = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.DashRecharge,
                        1f, 60f, 1f, "0", d => d.dashRechargeSeconds, (d, v) => d.dashRechargeSeconds = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.DashGhosts,
                        1f, 20f, 1f, "0", d => d.dashGhostCount,
                        (d, v) => d.dashGhostCount = Mathf.RoundToInt(v));
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.BarrelRollSeconds,
                        0.2f, 1.5f, 0.05f, "0.00", d => d.barrelRollSeconds, (d, v) => d.barrelRollSeconds = v);
            return screen;
        }

        public static MenuScreen BuildShipHoverTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                   System.Action onChanged,
                                                   List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipHover", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipHover, tabIndex, tabCount);

            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.HoverHeight,
                        0f, 10f, 0.25f, "0.00", d => d.hoverHeight, (d, v) => d.hoverHeight = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.BobAmplitude,
                        0f, 3f, 0.05f, "0.00", d => d.bobAmplitude, (d, v) => d.bobAmplitude = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.BobFrequency,
                        0f, 10f, 0.25f, "0.00", d => d.bobFrequency, (d, v) => d.bobFrequency = v);
            AddShipStat(screen, motor, onChanged, refreshers, MenuTextId.PitchWobble,
                        0f, 10f, 0.5f, "0.0", d => d.hoverPitchDegrees, (d, v) => d.hoverPitchDegrees = v);
            return screen;
        }

        /// <summary>
        /// Patrol tab: the chase tunables of <see cref="PatrolDefinition"/>.
        /// Each row writes the patrol's definition asset and mirrors onto the
        /// run's clone (most stats apply instantly; the start gap needs the
        /// reload offered on the way out). All values are in m/s and meters, like the sim.
        /// </summary>
        public static MenuScreen BuildPatrolTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                                System.Action onChanged,
                                                List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Patrol", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPatrol, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolBaseSpeed,
                          1f, 600f, 1f, "0", d => d.baseSpeed, (d, v) => d.baseSpeed = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolRamp,
                          0f, 15f, 0.05f, "0.00", d => d.ramp, (d, v) => d.ramp = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolRubberBand,
                          0.5f, 2f, 0.05f, "0.00", d => d.rubberBand, (d, v) => d.rubberBand = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolCatchUp,
                          0.5f, 150f, 0.5f, "0.0", d => d.catchUpAccel, (d, v) => d.catchUpAccel = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolBoostShare,
                          0f, 1.5f, 0.05f, "0.00", d => d.boostShare, (d, v) => d.boostShare = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolStartGap,
                          0f, 1000f, 25f, "0", d => d.startGap, (d, v) => d.startGap = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolCatchDistance,
                          0f, 100f, 5f, "0", d => d.catchDistance, (d, v) => d.catchDistance = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolWarnDistance,
                          0f, 500f, 10f, "0", d => d.warnDistance, (d, v) => d.warnDistance = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolCatchLateral,
                          0f, 60f, 1f, "0", d => d.alongsideLateral, (d, v) => d.alongsideLateral = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolSustainedCatch,
                          0f, 20f, 0.25f, "0.00", d => d.sustainedCatchSeconds, (d, v) => d.sustainedCatchSeconds = v);
            screen.SetViewport(9);
            return screen;
        }

        /// <summary>
        /// Patrol duel tab: the attack run — how hard the patrol overdrives to
        /// reach you, how often it tries, how long it holds the flank before
        /// it shoves, and how easily you can break it off. Same asset + clone
        /// rule as the other patrol tabs; everything applies instantly, and a
        /// run in progress picks the new numbers up on its next substep.
        ///
        /// ATTACK INTERVAL is the one that decides how much of a run is spent
        /// duelling; CLEAR ROAD AHEAD is how much clean track the patrol
        /// insists on before it will start (it never duels on a ramp, its
        /// landing, a loop, a tube or the final run-up).
        /// </summary>
        public static MenuScreen BuildDuelTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                              GameSettings runRules,
                                              System.Action onChanged,
                                              List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Duel", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabDuel, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelOverdrive,
                          1f, 2f, 0.01f, "0.00", d => d.attackRunOverdrive, (d, v) => d.attackRunOverdrive = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelCommitFrom,
                          50f, 1500f, 25f, "0", d => d.commitFromDistance, (d, v) => d.commitFromDistance = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelCommitInterval,
                          2f, 60f, 0.5f, "0.0", d => d.commitIntervalSeconds, (d, v) => d.commitIntervalSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelCommitTimeout,
                          2f, 60f, 0.5f, "0.0", d => d.commitTimeoutSeconds, (d, v) => d.commitTimeoutSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelAlongsideDistance,
                          0f, 60f, 1f, "0", d => d.alongsideDistance, (d, v) => d.alongsideDistance = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelFlankOffset,
                          0f, 30f, 0.5f, "0.0", d => d.flankOffsetMeters, (d, v) => d.flankOffsetMeters = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelAlongsideHold,
                          0f, 5f, 0.1f, "0.0", d => d.alongsideHoldSeconds, (d, v) => d.alongsideHoldSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelAbortGrace,
                          0f, 3f, 0.05f, "0.00", d => d.abortGraceSeconds, (d, v) => d.abortGraceSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelBreakOff,
                          0f, 5f, 0.1f, "0.0", d => d.breakOffSeconds, (d, v) => d.breakOffSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelBackOffSpeed,
                          0.5f, 1f, 0.01f, "0.00", d => d.breakOffSpeedFactor, (d, v) => d.breakOffSpeedFactor = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelCooldown,
                          0f, 60f, 0.5f, "0.0", d => d.attackRunCooldownSeconds, (d, v) => d.attackRunCooldownSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelLookahead,
                          50f, 1000f, 25f, "0", d => d.encounterLookaheadMeters, (d, v) => d.encounterLookaheadMeters = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelShoveMeters,
                          0f, 40f, 0.5f, "0.0", d => d.shoveMeters, (d, v) => d.shoveMeters = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTugForce,
                          0f, 1f, 0.01f, "0.00", d => d.tugPatrolForce, (d, v) => d.tugPatrolForce = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTugPress,
                          0.01f, 0.5f, 0.01f, "0.00", d => d.tugPressValue, (d, v) => d.tugPressValue = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelAbortMeters,
                          20f, 400f, 5f, "0", d => d.encounterAbortMeters, (d, v) => d.encounterAbortMeters = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelStandoff,
                          0f, 200f, 5f, "0", d => d.standoffDistance, (d, v) => d.standoffDistance = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelKillGap,
                          0f, 1500f, 25f, "0", d => d.killTeleportGap, (d, v) => d.killTeleportGap = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelStationAccel,
                          0f, 300f, 5f, "0", d => d.stationAccel, (d, v) => d.stationAccel = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelDamagePool,
                          1f, 10f, 1f, "0", d => d.damagePoolMax, (d, v) => d.damagePoolMax = Mathf.RoundToInt(v));
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelRamDistance,
                          0f, 30f, 0.5f, "0.0", d => d.ramContactDistance, (d, v) => d.ramContactDistance = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelRamLateral,
                          0f, 20f, 0.5f, "0.0", d => d.ramContactLateral, (d, v) => d.ramContactLateral = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelRamClosing,
                          0f, 100f, 1f, "0", d => d.ramClosingSpeedThreshold, (d, v) => d.ramClosingSpeedThreshold = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTierPerKill,
                          0f, 1f, 0.01f, "0.00", d => d.tierScalePerKill, (d, v) => d.tierScalePerKill = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTierMax,
                          1f, 5f, 0.05f, "0.00", d => d.tierScaleMax, (d, v) => d.tierScaleMax = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTugForceCap,
                          1f, 3f, 0.05f, "0.00", d => d.tugForceMaxScale, (d, v) => d.tugForceMaxScale = v);

            // The cinematic duel: the brake-triggered overshoot, how far the
            // contest walks the locked ship, the finisher's separation and the
            // miss brake.
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelMinClosing,
                          0f, 60f, 1f, "0", d => d.minClosingSpeed, (d, v) => d.minClosingSpeed = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelOvershootHold,
                          0f, 6f, 0.1f, "0.0", d => d.overshootHoldSeconds, (d, v) => d.overshootHoldSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelOvershootBrake,
                          0f, 1f, 0.05f, "0.00", d => d.overshootBrakeThreshold, (d, v) => d.overshootBrakeThreshold = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelOvershootDecel,
                          0f, 200f, 5f, "0", d => d.overshootDecelThreshold, (d, v) => d.overshootDecelThreshold = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelOvershootMargin,
                          0f, 100f, 5f, "0", d => d.overshootTriggerMargin, (d, v) => d.overshootTriggerMargin = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelPushFraction,
                          0f, 0.85f, 0.05f, "0.00", d => d.tugPushFraction, (d, v) => d.tugPushFraction = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelPushGain,
                          0.25f, 8f, 0.25f, "0.00", d => d.tugPushGain, (d, v) => d.tugPushGain = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelFinisherSeparation,
                          0f, 15f, 0.5f, "0.0", d => d.finisherSeparationMeters, (d, v) => d.finisherSeparationMeters = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelMissBrake,
                          0f, 5f, 0.1f, "0.0", d => d.finisherMissBrakeSeconds, (d, v) => d.finisherMissBrakeSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelMissBrakeSpeed,
                          0.2f, 1f, 0.05f, "0.00", d => d.finisherMissBrakeSpeedFactor, (d, v) => d.finisherMissBrakeSpeedFactor = v);

            // The duel's clock, windows and ram price (moved off GameSettings
            // onto the patrol's definition in refactor Step 4). Take the
            // timescale UP before you add any more help.
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTimeScale,
                          0.1f, 1f, 0.05f, "0.00", d => d.duelTimeScale, (d, v) => d.duelTimeScale = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelAssist,
                          0f, 1f, 0.05f, "0.00", d => d.duelAssistStrength, (d, v) => d.duelAssistStrength = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelFinisherWindow,
                          0.2f, 3f, 0.05f, "0.00", d => d.finisherWindowSeconds, (d, v) => d.finisherWindowSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelHitStop,
                          0f, 0.5f, 0.01f, "0.00", d => d.duelHitStopSeconds, (d, v) => d.duelHitStopSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelRamCost,
                          0f, 0.5f, 0.01f, "0.00", d => d.ramSpeedCost, (d, v) => d.ramSpeedCost = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelArmedWindow,
                          0.5f, 10f, 0.25f, "0.00", d => d.armedWindowSeconds, (d, v) => d.armedWindowSeconds = v);

            {
                // The duel camera framing lives on the scene rig's own settings
                // asset, which the rig re-applies live every frame, so these
                // rows edit that asset directly (nothing to mirror).
                OrbitCameraRig rig = CameraRigInstaller.FindRig(patrol.gameObject.scene);
                var cam = rig != null ? rig.settings : null;
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

        /// <summary>A slider over the camera settings asset, which the rig reads live — so it edits the asset alone.</summary>
        static void AddCameraStat(MenuScreen screen, OrbitCameraSettings settings, List<System.Action> refreshers,
                                  MenuTextId label, float min, float max, float step, string format,
                                  System.Func<OrbitCameraSettings, float> get, System.Action<OrbitCameraSettings, float> set) =>
            SettingsDebugPage<OrbitCameraSettings>.AddSlider(screen, settings, refreshers, label, min, max, step, format, get, set);

        /// <summary>
        /// Patrol driver tab: how the cruiser handles (the ship's own steering
        /// and grip rules, its brake) and how its <see cref="PatrolDriver"/>
        /// reads the road — look-aheads, orb appetite, its cut of an orb.
        /// Same asset + clone rule as the patrol tab; all apply instantly.
        /// </summary>
        public static MenuScreen BuildPatrolDriverTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                                      System.Action onChanged,
                                                      List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_PatrolDriver", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPatrolDriver, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.LateralSpeed,
                          0f, 100f, 1f, "0", d => d.lateralSpeed, (d, v) => d.lateralSpeed = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.SteerResponse,
                          0.5f, 30f, 0.5f, "0.0", d => d.handlingResponse, (d, v) => d.handlingResponse = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.GripBase,
                          0f, 500f, 5f, "0", d => d.gripBase, (d, v) => d.gripBase = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.GripPerSpeed,
                          0f, 3f, 0.05f, "0.00", d => d.gripPerSpeed, (d, v) => d.gripPerSpeed = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.BrakePower,
                          0f, 500f, 10f, "0", d => d.brakeDecel, (d, v) => d.brakeDecel = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolCurveLookahead,
                          0.5f, 10f, 0.25f, "0.00", d => d.curveLookaheadSeconds, (d, v) => d.curveLookaheadSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolOrbLookahead,
                          0f, 10f, 0.25f, "0.00", d => d.orbLookaheadSeconds, (d, v) => d.orbLookaheadSeconds = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolOrbSeek,
                          0f, 1f, 0.05f, "0.00", d => d.orbSeekWeight, (d, v) => d.orbSeekWeight = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolOrbBoost,
                          0f, 1.5f, 0.05f, "0.00", d => d.orbBoostShare, (d, v) => d.orbBoostShare = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolRampLookahead,
                          0.5f, 10f, 0.25f, "0.00", d => d.rampLookaheadSeconds, (d, v) => d.rampLookaheadSeconds = v);
            screen.SetViewport(9);
            return screen;
        }

        // One localized slider row bound to a PatrolDefinition knob: writes the
        // asset, then the run's clone (read at call time).
        static void AddPatrolStat(MenuScreen screen, PolicePatrol patrol,
                                  System.Action onChanged, List<System.Action> refreshers, MenuTextId label,
                                  float min, float max, float step, string format,
                                  System.Func<PatrolDefinition, float> get,
                                  System.Action<PatrolDefinition, float> set)
        {
            PatrolDefinition Asset() => patrol.DefinitionAsset != null ? patrol.DefinitionAsset : patrol.Definition;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(Asset()), format, v =>
            {
                var asset = Asset();
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                if (patrol.Definition != null && patrol.Definition != asset) set(patrol.Definition, v);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(Asset())));
        }

        // One localized slider row bound to a ShipDefinition stat: writes the
        // asset the run's definition was built from, then rebuilds the run's
        // clone from it (Store levels re-applied). Read at call time, so it
        // always hits whichever clone is driving the ship.
        static void AddShipStat(MenuScreen screen, ShipMotor motor,
                                System.Action onChanged, List<System.Action> refreshers, MenuTextId label,
                                float min, float max, float step, string format,
                                System.Func<ShipDefinition, float> get,
                                System.Action<ShipDefinition, float> set)
        {
            ShipDefinition Asset()
            {
                var live = motor.Definition;
                return live != null && live.Source != null ? live.Source : live;
            }
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, Asset() != null ? get(Asset()) : min, format, v =>
            {
                var asset = Asset();
                if (asset == null) return;
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                ShipUpgradeApplier.Refresh(motor.Definition);
                onChanged?.Invoke();
            });
            refreshers?.Add(() => { var asset = Asset(); if (asset != null) row.SetWithoutNotify(get(asset)); });
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
