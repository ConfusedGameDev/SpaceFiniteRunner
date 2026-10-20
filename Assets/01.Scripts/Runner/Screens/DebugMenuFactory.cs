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
    /// rather than written field by field. Only rows that change the running
    /// game are here: a row whose value a saved track, the physics ship or the
    /// duel never reads, or that only bit on the next launch, was removed.
    /// </summary>
    public static class DebugMenuFactory
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        /// <summary>
        /// First tab: RELOAD SCENE, which commits the debug edits and reloads,
        /// so the rows that apply on the next Generate or launch take effect.
        /// Its track rows (the road's shape, TRACK LENGTH, FORCE RUNTIME TRACK,
        /// the spawn amounts and orb-tier odds) are gone: every shipped level
        /// plays a saved track, and those rows either never reached it or only
        /// applied after a reload. Tune them on their assets and re-bake.
        /// </summary>
        public static MenuScreen BuildCoreSettingsTab(RectTransform parent, MenuTheme theme,
                                                      System.Action reloadScene, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CoreSettings", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCore, tabIndex, tabCount);
            screen.AddRow<MenuRow>(MenuTextId.ReloadScene).Activated += () => reloadScene?.Invoke();
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

        /// <summary>
        /// Ship tabs: four pages of <see cref="ShipDefinition"/> sliders (Speed,
        /// Handling, Dash, Hover). Each row writes the ship's definition ASSET
        /// and rebuilds the run's clone from it with the Store's levels
        /// multiplied in, so every stat here applies instantly (launch speed
        /// and hover height, which did not, have no rows). The sliders show the
        /// asset's values (before upgrades).
        /// </summary>
        public static MenuScreen BuildShipSpeedTab(RectTransform parent, MenuTheme theme, ShipMotor motor,
                                                   System.Action onChanged,
                                                   List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ShipSpeed", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabShipSpeed, tabIndex, tabCount);

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
        /// run's clone; every row applies instantly. The launch-only rows (base
        /// speed, start gap) and the far-chase rows the duel's station-keeping
        /// overrides (rubber band, catch-up, boost share, warn distance) are
        /// gone. All values are in m/s and meters, like the sim.
        /// </summary>
        public static MenuScreen BuildPatrolTab(RectTransform parent, MenuTheme theme, PolicePatrol patrol,
                                                System.Action onChanged,
                                                List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Patrol", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPatrol, tabIndex, tabCount);

            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolRamp,
                          0f, 15f, 0.05f, "0.00", d => d.ramp, (d, v) => d.ramp = v);
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.PatrolCatchDistance,
                          0f, 100f, 5f, "0", d => d.catchDistance, (d, v) => d.catchDistance = v);
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
            // timescale UP before you add any more help. (The steer assist row
            // is gone: duelAssistStrength is dormant, nothing reads it.)
            AddPatrolStat(screen, patrol, onChanged, refreshers, MenuTextId.DuelTimeScale,
                          0.1f, 1f, 0.05f, "0.00", d => d.duelTimeScale, (d, v) => d.duelTimeScale = v);
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

        static PadSpawnEntry[] LiveTiers(TrackGenerator generator) => generator.GetSpawner<SpeedOrbSpawner>()?.Tiers;
    }
}
