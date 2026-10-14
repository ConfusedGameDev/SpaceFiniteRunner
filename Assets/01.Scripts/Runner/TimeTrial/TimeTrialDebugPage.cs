using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.TimeTrial
{
    /// <summary>
    /// The pause menu's TIME TRIAL page (TimeTrialPRD §6): three actions —
    /// cross the next checkpoint now, lay the checkpoints again, ten seconds
    /// on the clock — then the clock, checkpoint and bonus knobs. Only a time
    /// trial level shows it (the bound <see cref="TimeTrialSystem.Live"/>).
    /// The trial reads a runtime clone, so every row writes the ASSET (kept for
    /// the pause menu's flush through <see cref="DebugAssetEdits"/>) and then
    /// mirrors the value onto the live clone — the race page's rule. Bonuses
    /// and the low-time warning bite at once; the start time and the time
    /// limit on the next run (retry); the checkpoint count, pace and grace on
    /// the next layout (a retry, or LAY CHECKPOINTS AGAIN).
    /// </summary>
    public static class TimeTrialDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen Build(RectTransform parent, MenuTheme theme, TimeTrialSystem system,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_TimeTrial", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabTimeTrial, tabIndex, tabCount);

            screen.AddRow<MenuRow>(MenuTextId.TtCrossNext).Activated += system.DebugCrossNext;
            screen.AddRow<MenuRow>(MenuTextId.TtRelayout).Activated += system.DebugRelayout;
            screen.AddRow<MenuRow>(MenuTextId.TtAddTime).Activated += () => system.DebugAddTime(10f);

            Slider(screen, system, refreshers, MenuTextId.TtStartSeconds,
                3f, 120f, 1f, "0", d => d.startSeconds, (d, v) => d.startSeconds = v);
            Slider(screen, system, refreshers, MenuTextId.TtTimeLimit,
                0f, 600f, 5f, "0", d => d.timeLimitSeconds, (d, v) => d.timeLimitSeconds = v);
            Slider(screen, system, refreshers, MenuTextId.TtMaxClock,
                10f, 999f, 1f, "0.0", d => d.maxClockSeconds, (d, v) => d.maxClockSeconds = v);
            Slider(screen, system, refreshers, MenuTextId.TtAutoCount,
                1f, 40f, 1f, "0", d => d.autoCount, (d, v) => d.autoCount = Mathf.RoundToInt(v));
            Slider(screen, system, refreshers, MenuTextId.TtPaceShare,
                0.3f, 2f, 0.05f, "0.00", d => d.paceShare, (d, v) => d.paceShare = v);
            Slider(screen, system, refreshers, MenuTextId.TtPaceFloor,
                100f, 6000f, 50f, "0", d => d.paceFloorKmh, (d, v) => d.paceFloorKmh = v);
            Slider(screen, system, refreshers, MenuTextId.TtGrace,
                0f, 20f, 0.5f, "0.0", d => d.graceSeconds, (d, v) => d.graceSeconds = v);
            Slider(screen, system, refreshers, MenuTextId.TtCleanBonus,
                0f, 20f, 0.5f, "0.0", d => d.cleanCheckpointBonus, (d, v) => d.cleanCheckpointBonus = v);
            Slider(screen, system, refreshers, MenuTextId.TtKillBonus,
                0f, 30f, 0.5f, "0.0", d => d.patrolKillBonus, (d, v) => d.patrolKillBonus = v);
            Slider(screen, system, refreshers, MenuTextId.TtLifePenalty,
                0f, 30f, 0.5f, "0.0", d => d.lifePenaltySeconds, (d, v) => d.lifePenaltySeconds = v);
            Slider(screen, system, refreshers, MenuTextId.TtLowTime,
                0f, 30f, 0.5f, "0.0", d => d.lowTimeWarningSeconds, (d, v) => d.lowTimeWarningSeconds = v);
            Toggle(screen, system, refreshers, MenuTextId.TtDebugLog, d => d.debugLog, (d, v) => d.debugLog = v);

            screen.SetViewport(9);
            return screen;
        }

        static TimeTrialDefinition Asset(TimeTrialSystem system) =>
            system.DefinitionAsset != null ? system.DefinitionAsset : system.Definition;

        static void Slider(MenuScreen screen, TimeTrialSystem system, List<System.Action> refreshers, MenuTextId label,
                           float min, float max, float step, string format,
                           System.Func<TimeTrialDefinition, float> get, System.Action<TimeTrialDefinition, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(Asset(system)), format, v => Write(system, d => set(d, v)));
            refreshers?.Add(() => row.SetWithoutNotify(get(Asset(system))));
        }

        static void Toggle(MenuScreen screen, TimeTrialSystem system, List<System.Action> refreshers, MenuTextId label,
                           System.Func<TimeTrialDefinition, bool> get, System.Action<TimeTrialDefinition, bool> set)
        {
            var row = screen.AddRow<MenuToggle>(label);
            void OnChanged(bool v) => Write(system, d => set(d, v));
            row.Configure(get(Asset(system)), OnChanged);
            refreshers?.Add(() => row.Configure(get(Asset(system)), OnChanged));
        }

        // The asset first (marked for the flush), then the live clone.
        static void Write(TimeTrialSystem system, System.Action<TimeTrialDefinition> set)
        {
            var asset = Asset(system);
            set(asset);
            DebugAssetEdits.Touch(asset);
            if (system.Definition != null && system.Definition != asset) set(system.Definition);
        }

        // Registered with the pause menu's page registry right after the race
        // page (6): both are per-level runner systems.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(TimeTrialDebugPage), 7, () =>
                DebugPages.Single(TimeTrialSystem.Live != null && TimeTrialSystem.Live.Bound ? TimeTrialSystem.Live : null, Build));
    }
}
