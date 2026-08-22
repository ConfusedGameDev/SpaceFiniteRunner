using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using FiniteRunner;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The city chase's pages for the shared pause-menu debug tabs: the core
    /// handling knobs of the player car's <see cref="CarConfig"/> (drive and
    /// grip), the framing knobs of its <see cref="OrbitCameraSettings"/>, the
    /// fleet and chase knobs of the police <see cref="PursuitSettings"/>, and
    /// the <see cref="LevelManager"/>'s objective list (every step in order,
    /// tinted by status, with sliders on the speed and time steps). Same tab
    /// framework as the runner's pages — normal compact-row MenuScreens
    /// cycled with the bumpers.
    ///
    /// One rule differs from the runner's ship/patrol tabs: the city's cars
    /// and camera rig read their settings assets LIVE every step (that is the
    /// point of the inline inspector workflow — there is no runtime clone to
    /// catch), so these sliders edit the assets themselves. Which makes
    /// persistence exact: the edited asset is kept dirty and written to disk
    /// at the pause menu's commit points (resume, reload scene) via
    /// <see cref="Flush"/>, so a tweak survives exiting play mode with no
    /// shadow copy that could drift out of sync. Nothing here needs a scene
    /// reload, so these pages never mark the menu dirty — the chassis knobs
    /// (mass, center of mass) re-run <see cref="CarController.ApplyConfig"/>
    /// on every car using the config instead.
    /// </summary>
    public static class CityDebugMenuFactory
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        static readonly Color DoneTint = new(0.45f, 1f, 0.55f);

        /// <summary>What this scene has to offer the debug menu — nothing at all, in the runner.</summary>
        public static CityDebugTabs Discover() => new(FindCarConfig(), FindCameraSettings(), FindPursuitSettings(), FindLevelManager());

        /// <summary>No pages, for a caller that already knows it isn't in the city.</summary>
        public static CityDebugTabs None => new(null, null, null, null);

        /// <summary>
        /// The level flow, if the scene runs one. The manager rather than its
        /// asset: the page shows live status, and a manager with no asset
        /// assigned plays an in-memory default that only exists after its
        /// Awake — which has run by the time the pause menu builds in Start.
        /// </summary>
        static LevelManager FindLevelManager()
        {
            var manager = Object.FindFirstObjectByType<LevelManager>();
            return manager != null && manager.Level != null ? manager : null;
        }

        /// <summary>
        /// The player car's config: the spawner's prefab first, because the
        /// menu is built in Start and the car itself only arrives afterwards.
        /// Falls back to a live player-driven car (a hand-placed one, or a
        /// menu built late) — never a traffic or police car.
        /// </summary>
        static CarConfig FindCarConfig()
        {
            var spawner = Object.FindFirstObjectByType<PlayerCarSpawner>();
            if (spawner != null && spawner.carPrefab != null)
            {
                var prefabController = spawner.carPrefab.GetComponent<CarController>();
                if (prefabController != null && prefabController.config != null) return prefabController.config;
            }

            foreach (var car in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (car.config != null && car.GetComponent<CarInput>() != null) return car.config;

            return null;
        }

        /// <summary>The chase camera's settings, from whoever owns the rig — the spawner, or the rig itself once it exists.</summary>
        static OrbitCameraSettings FindCameraSettings()
        {
            var spawner = Object.FindFirstObjectByType<PlayerCarSpawner>();
            if (spawner != null && spawner.cameraSettings != null) return spawner.cameraSettings;

            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            return rig != null ? rig.settings : null;
        }

        /// <summary>
        /// The police pursuit settings, from the CityManager that hands them
        /// to the fleet at play start — the PatrolManager it spawns does not
        /// exist yet when the menu is built, so it is only the fallback.
        /// </summary>
        static PursuitSettings FindPursuitSettings()
        {
            var city = Object.FindFirstObjectByType<CityManager>();
            if (city != null && city.pursuitSettings != null) return city.pursuitSettings;

            var manager = Object.FindFirstObjectByType<PatrolManager>();
            if (manager != null && manager.settings != null) return manager.settings;

            var patrol = Object.FindFirstObjectByType<PoliceCarInput>();
            return patrol != null ? patrol.settings : null;
        }

        // ---------------------------------------------------------------- tabs

        /// <summary>
        /// Drivetrain and chassis: what the car does with the throttle. Mass
        /// and the center-of-mass drop are one-time rigidbody setup, so they
        /// re-run ApplyConfig; the rest is pushed to the wheels every physics
        /// step and lands instantly.
        /// </summary>
        public static MenuScreen BuildCarDriveTab(RectTransform parent, MenuTheme theme, CarConfig config,
                                                  List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CarDrive", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCarDrive, tabIndex, tabCount);

            AddCarStat(screen, config, refreshers, MenuTextId.CarMass,
                       400f, 3000f, 50f, "0", c => c.mass, (c, v) => c.mass = v, chassis: true);
            AddCarStat(screen, config, refreshers, MenuTextId.CarCenterOfMass,
                       0f, 1.5f, 0.05f, "0.00", c => c.centerOfMassDrop, (c, v) => c.centerOfMassDrop = v, chassis: true);
            AddCarStat(screen, config, refreshers, MenuTextId.CarDownforce,
                       0f, 200f, 5f, "0", c => c.downforce, (c, v) => c.downforce = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarMotorTorque,
                       200f, 8000f, 100f, "0", c => c.maxMotorTorque, (c, v) => c.maxMotorTorque = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarTopSpeed,
                       40f, 300f, 5f, "0", c => c.topSpeedKmh, (c, v) => c.topSpeedKmh = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarBrakeTorque,
                       500f, 12000f, 250f, "0", c => c.brakeTorque, (c, v) => c.brakeTorque = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarHillRollback,
                       0f, 15f, 0.5f, "0.0", c => c.hillRollbackSlope, (c, v) => c.hillRollbackSlope = v);
            return screen;
        }

        /// <summary>Steering and tire grip: what the car does with the wheel. All of it applies live.</summary>
        public static MenuScreen BuildCarGripTab(RectTransform parent, MenuTheme theme, CarConfig config,
                                                 List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CarGrip", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCarGrip, tabIndex, tabCount);

            AddCarStat(screen, config, refreshers, MenuTextId.CarSteerAngle,
                       10f, 60f, 1f, "0", c => c.maxSteerAngle, (c, v) => c.maxSteerAngle = v);
            AddCarStat(screen, config, refreshers, MenuTextId.SteerResponse,
                       60f, 720f, 20f, "0", c => c.steerResponse, (c, v) => c.steerResponse = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarHandbrakeTorque,
                       500f, 12000f, 250f, "0", c => c.handbrakeTorque, (c, v) => c.handbrakeTorque = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarHandbrakeGrip,
                       0.1f, 1f, 0.05f, "0.00", c => c.handbrakeGrip, (c, v) => c.handbrakeGrip = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarForwardGrip,
                       0.25f, 4f, 0.05f, "0.00", c => c.forwardStiffness, (c, v) => c.forwardStiffness = v);
            AddCarStat(screen, config, refreshers, MenuTextId.CarSideGrip,
                       0.25f, 4f, 0.05f, "0.00", c => c.sideStiffness, (c, v) => c.sideStiffness = v);
            return screen;
        }

        /// <summary>
        /// Chase camera: framing, recentering and the speed FOV kick. The rig
        /// re-applies the settings in Update, which keeps running on a frozen
        /// clock — so these move the camera while the menu is still open.
        /// </summary>
        public static MenuScreen BuildCameraTab(RectTransform parent, MenuTheme theme, OrbitCameraSettings settings,
                                                List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_ChaseCamera", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCamera, tabIndex, tabCount);

            AddCameraStat(screen, settings, refreshers, MenuTextId.CamDistance,
                          3f, 25f, 0.5f, "0.0", s => s.distance, (s, v) => s.distance = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamHeight,
                          0f, 3f, 0.1f, "0.0", s => s.lookHeight, (s, v) => s.lookHeight = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamPitch,
                          0f, 60f, 1f, "0", s => s.defaultPitch, (s, v) => s.defaultPitch = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamDamping,
                          0f, 3f, 0.05f, "0.00", s => s.positionDamping, (s, v) => s.positionDamping = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamRecenterDelay,
                          0.2f, 10f, 0.1f, "0.0", s => s.recenterDelay, (s, v) => s.recenterDelay = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamRecenterSpeed,
                          30f, 360f, 10f, "0", s => s.recenterSpeed, (s, v) => s.recenterSpeed = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamBaseFov,
                          40f, 90f, 1f, "0", s => s.baseFov, (s, v) => s.baseFov = v);
            AddCameraStat(screen, settings, refreshers, MenuTextId.CamSpeedFov,
                          0f, 0.3f, 0.01f, "0.00", s => s.fovPerKmh, (s, v) => s.fovPerKmh = v);
            return screen;
        }

        /// <summary>
        /// Police fleet: how many cruisers are kept alive and where they cut
        /// in. The PatrolManager re-reads all of it on its 1 s maintenance
        /// tick, so raising the count spawns and lowering it retires — but
        /// only once the game is running again, since that tick is on scaled
        /// time and the menu has it frozen.
        /// </summary>
        public static MenuScreen BuildPoliceFleetTab(RectTransform parent, MenuTheme theme, PursuitSettings settings,
                                                     List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_PoliceFleet", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPoliceFleet, tabIndex, tabCount);

            AddPursuitStat(screen, settings, refreshers, MenuTextId.PolicePatrolCount,
                           0f, 25f, 1f, "0", s => s.targetPatrolCount,
                           (s, v) => s.targetPatrolCount = Mathf.RoundToInt(v));

            // The spawn band is one MinMaxSlider on the asset; here it is two
            // rows, each shoving the other along so the pair can never cross.
            DebugSliderRow spawnMin = null, spawnMax = null;
            spawnMin = AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceSpawnMin,
                                      30f, 600f, 10f, "0", s => s.spawnDistanceBand.x, (s, v) =>
                                      {
                                          s.spawnDistanceBand.x = v;
                                          if (s.spawnDistanceBand.y >= v) return;
                                          s.spawnDistanceBand.y = v;
                                          spawnMax?.SetWithoutNotify(v);
                                      });
            spawnMax = AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceSpawnMax,
                                      30f, 600f, 10f, "0", s => s.spawnDistanceBand.y, (s, v) =>
                                      {
                                          s.spawnDistanceBand.y = v;
                                          if (s.spawnDistanceBand.x <= v) return;
                                          s.spawnDistanceBand.x = v;
                                          spawnMin?.SetWithoutNotify(v);
                                      });

            AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceDespawn,
                           100f, 1500f, 25f, "0", s => s.despawnDistance, (s, v) => s.despawnDistance = v);
            return screen;
        }

        /// <summary>
        /// Police chase: what it takes to be spotted, how long they hunt after
        /// losing you, and how fast they drive doing it. Every cruiser reads
        /// these off the shared asset each frame, so the whole fleet turns on
        /// one slider.
        /// </summary>
        public static MenuScreen BuildPoliceChaseTab(RectTransform parent, MenuTheme theme, PursuitSettings settings,
                                                     List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_PoliceChase", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPoliceChase, tabIndex, tabCount);

            AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceDetection,
                           10f, 300f, 5f, "0", s => s.detectionRange, (s, v) => s.detectionRange = v);
            AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceLoseSight,
                           0.5f, 10f, 0.5f, "0.0", s => s.loseSightSeconds, (s, v) => s.loseSightSeconds = v);
            AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceSearchTime,
                           3f, 60f, 1f, "0", s => s.searchDuration, (s, v) => s.searchDuration = v);
            AddPursuitStat(screen, settings, refreshers, MenuTextId.PolicePatrolSpeed,
                           10f, 100f, 5f, "0", s => s.patrolSpeedKmh, (s, v) => s.patrolSpeedKmh = v);
            AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceChaseSpeed,
                           20f, 250f, 5f, "0", s => s.chaseSpeedKmh, (s, v) => s.chaseSpeedKmh = v);
            AddPursuitStat(screen, settings, refreshers, MenuTextId.PoliceCornerSpeed,
                           5f, 80f, 1f, "0", s => s.cornerSpeedKmh, (s, v) => s.cornerSpeedKmh = v);
            return screen;
        }

        /// <summary>
        /// The level's objectives, one row each in list order. Speed and time
        /// steps are sliders that edit the asset live (the manager reads it
        /// every frame, so a raised target regresses the step on the spot);
        /// the other kinds are read-only rows. Every label is tinted by status
        /// — green once done, accent while active, plain while pending — and
        /// the tint keeps following the run while the menu is open. Rows are
        /// built once per menu: objectives added in the inspector mid-play
        /// show up after the next scene load.
        /// </summary>
        public static MenuScreen BuildLevelTab(RectTransform parent, MenuTheme theme, LevelManager manager,
                                               List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Level", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabLevel, tabIndex, tabCount);

            LevelDefinition level = manager.Level;
            string modeLabel = level.mode == CompletionMode.AllMustHold ? "ALL MUST HOLD" : "INDEPENDENT";
            screen.AddLabel("LevelName", new Vector2(0f, 372f), new Vector2(900f, 32f),
                            $"{level.levelName}  ·  {modeLabel}", 22, theme.TextDim, theme.BodyFont,
                            TextAnchor.MiddleCenter, 0f);

            MenuTextLibrary texts = MenuTextLibrary.Load();
            for (int i = 0; i < level.Count; i++)
            {
                LevelObjective objective = level.objectives[i];
                switch (objective.type)
                {
                    case ObjectiveType.ReachSpeed:
                        AddObjectiveStat(screen, theme, manager, i, refreshers, MenuTextId.ObjectiveReachSpeed,
                                         50f, 300f, 5f, "0", o => o.targetSpeedKmh, (o, v) => o.targetSpeedKmh = v);
                        break;
                    case ObjectiveType.SurviveTime:
                        AddObjectiveStat(screen, theme, manager, i, refreshers, MenuTextId.ObjectiveSurvive,
                                         5f, 300f, 5f, "0", o => o.surviveSeconds, (o, v) => o.surviveSeconds = v);
                        break;
                    case ObjectiveType.GoToTarget:
                        // Raw label: the id is data, so this row does not re-localize on a language change.
                        screen.AddRow<DebugLabelRow>($"{texts.Get(MenuTextId.ObjectiveGoTo)}  [{objective.targetId}]")
                              .SetLabelTintProvider(StatusTint(manager, i, theme));
                        break;
                    default:
                        screen.AddRow<DebugLabelRow>(MenuTextId.ObjectiveEscapePolice)
                              .SetLabelTintProvider(StatusTint(manager, i, theme));
                        break;
                }
            }
            return screen;
        }

        // --------------------------------------------------------------- rows

        static void AddObjectiveStat(MenuScreen screen, MenuTheme theme, LevelManager manager, int index,
                                     List<System.Action> refreshers, MenuTextId label,
                                     float min, float max, float step, string format,
                                     System.Func<LevelObjective, float> get,
                                     System.Action<LevelObjective, float> set)
        {
            // Re-resolved on every call: the list is the asset's, and it may be reordered between pauses.
            LevelObjective Objective() =>
                manager != null && manager.Level != null && index < manager.Level.Count ? manager.Level.objectives[index] : null;

            var row = screen.AddRow<DebugSliderRow>(label);
            var initial = Objective();
            row.Configure(min, max, step, initial != null ? get(initial) : min, format, v =>
            {
                var objective = Objective();
                if (objective == null) return;
                set(objective, v);
                MarkDirty(manager.Level);
            });
            refreshers?.Add(() =>
            {
                var objective = Objective();
                if (objective != null) row.SetWithoutNotify(get(objective));
            });
            row.SetLabelTintProvider(StatusTint(manager, index, theme));
        }

        /// <summary>Label tint for an objective row: done, active, or none (pending).</summary>
        static System.Func<Color?> StatusTint(LevelManager manager, int index, MenuTheme theme) => () =>
        {
            if (manager == null) return null;
            if (manager.Completed || manager.IsDone(index)) return DoneTint;
            if (manager.CurrentIndex == index) return theme.Accent;
            return null;
        };

        static void AddCarStat(MenuScreen screen, CarConfig config, List<System.Action> refreshers, MenuTextId label,
                               float min, float max, float step, string format,
                               System.Func<CarConfig, float> get, System.Action<CarConfig, float> set,
                               bool chassis = false)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(config), format, v =>
            {
                set(config, v);
                if (chassis) ReapplyChassis(config);
                MarkDirty(config);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(config)));
        }

        static void AddCameraStat(MenuScreen screen, OrbitCameraSettings settings, List<System.Action> refreshers,
                                  MenuTextId label, float min, float max, float step, string format,
                                  System.Func<OrbitCameraSettings, float> get,
                                  System.Action<OrbitCameraSettings, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(settings), format, v =>
            {
                set(settings, v);
                MarkDirty(settings);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(settings)));
        }

        /// <summary>Returns the row, so paired knobs (the spawn band) can nudge each other's readout.</summary>
        static DebugSliderRow AddPursuitStat(MenuScreen screen, PursuitSettings settings, List<System.Action> refreshers,
                                             MenuTextId label, float min, float max, float step, string format,
                                             System.Func<PursuitSettings, float> get,
                                             System.Action<PursuitSettings, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(settings), format, v =>
            {
                set(settings, v);
                MarkDirty(settings);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(settings)));
            return row;
        }

        /// <summary>
        /// Re-runs the one-time chassis setup on every car sharing this config
        /// — mass, dropped center of mass and wheel substeps are pushed to the
        /// rigidbody once, not per physics step, so the slider would otherwise
        /// do nothing until the next spawn.
        /// </summary>
        static void ReapplyChassis(CarConfig config)
        {
            foreach (var car in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (car.config == config) car.ApplyConfig();
        }

        // -------------------------------------------------------- persistence

#if UNITY_EDITOR
        static readonly List<Object> touched = new();
#endif

        /// <summary>Marks an edited settings asset for the next <see cref="Flush"/>.</summary>
        static void MarkDirty(Object asset)
        {
#if UNITY_EDITOR
            // Only real assets: a LevelManager with nothing assigned plays an
            // in-memory level, and there is nothing on disk to save for it.
            if (asset == null || !UnityEditor.EditorUtility.IsPersistent(asset)) return;
            if (!touched.Contains(asset)) touched.Add(asset);
            UnityEditor.EditorUtility.SetDirty(asset);
#endif
        }

        /// <summary>
        /// Writes every asset these tabs edited to disk (editor only — builds
        /// keep the changes for the app session). Called at the pause menu's
        /// commit points, not on every slider tick, so the tweaks are on disk
        /// well before play mode ends.
        /// </summary>
        public static void Flush()
        {
#if UNITY_EDITOR
            foreach (var asset in touched)
                if (asset != null) UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
            touched.Clear();
#endif
        }
    }

    /// <summary>
    /// The city debug pages available in the current scene, discovered once so
    /// the pause menu can count the tabs before it builds any of them — every
    /// tab header prints its own "TAB n/N".
    /// </summary>
    public class CityDebugTabs
    {
        readonly CarConfig car;
        readonly OrbitCameraSettings orbitCamera;
        readonly PursuitSettings police;
        readonly LevelManager level;

        internal CityDebugTabs(CarConfig car, OrbitCameraSettings orbitCamera, PursuitSettings police, LevelManager level)
        {
            this.car = car;
            this.orbitCamera = orbitCamera;
            this.police = police;
            this.level = level;
        }

        public int TabCount => (car != null ? 2 : 0) + (orbitCamera != null ? 1 : 0) + (police != null ? 2 : 0)
                             + (level != null ? 1 : 0);

        public void AddTabs(DebugMenu menu, RectTransform parent, MenuTheme theme,
                            List<System.Action> refreshers, ref int tab, int tabCount)
        {
            if (car != null)
            {
                menu.AddTab(CityDebugMenuFactory.BuildCarDriveTab(parent, theme, car, refreshers, tab++, tabCount));
                menu.AddTab(CityDebugMenuFactory.BuildCarGripTab(parent, theme, car, refreshers, tab++, tabCount));
            }
            if (orbitCamera != null)
                menu.AddTab(CityDebugMenuFactory.BuildCameraTab(parent, theme, orbitCamera, refreshers, tab++, tabCount));
            if (police != null)
            {
                menu.AddTab(CityDebugMenuFactory.BuildPoliceFleetTab(parent, theme, police, refreshers, tab++, tabCount));
                menu.AddTab(CityDebugMenuFactory.BuildPoliceChaseTab(parent, theme, police, refreshers, tab++, tabCount));
            }
            if (level != null)
                menu.AddTab(CityDebugMenuFactory.BuildLevelTab(parent, theme, level, refreshers, tab++, tabCount));
        }
    }
}
