using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Race
{
    /// <summary>
    /// The pause menu's RACE page (RaceModePRD §6.4): the field's pace, the
    /// contact, the kill cam, the readouts, and three actions — wreck the
    /// nearest rival (the kill cam's test), wreck them all, finish them all.
    /// Only a race level shows it (the bound <see cref="RaceSystem.Live"/>).
    /// The race reads a runtime clone, so every row writes the ASSET (kept
    /// for the pause menu's flush through <see cref="DebugAssetEdits"/>) and
    /// then mirrors the value onto the live clone — the traffic page's rule;
    /// every row bites on the next race tick, except FORM VARIANCE and LANE
    /// SPREAD, which are rolled at the grid (the next run or retry). The
    /// actions are queued and run
    /// on the first race tick after the menu closes, so a wreck made from
    /// here can still play the kill cam.
    /// </summary>
    public static class RaceDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen Build(RectTransform parent, MenuTheme theme, RaceSystem system,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Race", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabRace, tabIndex, tabCount);

            screen.AddRow<MenuRow>(MenuTextId.RaceKillNearest).Activated += system.DebugKillNearest;
            screen.AddRow<MenuRow>(MenuTextId.RaceDnfAll).Activated += system.DebugDnfAll;
            screen.AddRow<MenuRow>(MenuTextId.RaceFinishAll).Activated += system.DebugFinishAll;

            Toggle(screen, system, refreshers, MenuTextId.RaceLeadGovernor, d => d.leadGovernor, (d, v) => d.leadGovernor = v);
            Slider(screen, system, refreshers, MenuTextId.RaceLeadCapFar,
                0.6f, 1.1f, 0.01f, "0.00", d => d.leadCapFar, (d, v) => d.leadCapFar = v);
            Slider(screen, system, refreshers, MenuTextId.RaceThrustShare,
                0.3f, 1.5f, 0.02f, "0.00", d => d.thrustShare, (d, v) => d.thrustShare = v);
            Slider(screen, system, refreshers, MenuTextId.RaceRubberBand,
                0f, 0.3f, 0.01f, "0.00", d => d.rubberBand, (d, v) => d.rubberBand = v);
            Slider(screen, system, refreshers, MenuTextId.RaceGhostPace,
                0.5f, 1.2f, 0.01f, "0.00", d => d.ghostPaceFactor, (d, v) => d.ghostPaceFactor = v);
            Slider(screen, system, refreshers, MenuTextId.RaceOrbShare,
                0f, 2f, 0.05f, "0.00", d => d.orbBoostShare, (d, v) => d.orbBoostShare = v);
            Slider(screen, system, refreshers, MenuTextId.RaceFormVariance,
                0f, 0.3f, 0.01f, "0.00", d => d.formVariance, (d, v) => d.formVariance = v);
            Slider(screen, system, refreshers, MenuTextId.RaceSurgeAmount,
                0f, 0.5f, 0.01f, "0.00", d => d.surgeAmount, (d, v) => d.surgeAmount = v);
            Slider(screen, system, refreshers, MenuTextId.RaceLaneSpread,
                0f, 1f, 0.05f, "0.00", d => d.laneSpread, (d, v) => d.laneSpread = v);
            Slider(screen, system, refreshers, MenuTextId.RaceRamSpeedCost,
                0f, 0.3f, 0.01f, "0.00", d => d.ramSpeedCost, (d, v) => d.ramSpeedCost = v);
            Slider(screen, system, refreshers, MenuTextId.RaceSideShove,
                0f, 20f, 0.5f, "0.0", d => d.sideShoveMeters, (d, v) => d.sideShoveMeters = v);
            Slider(screen, system, refreshers, MenuTextId.RaceCreditWindow,
                0f, 10f, 0.25f, "0.00", d => d.creditWindow, (d, v) => d.creditWindow = v);

            Toggle(screen, system, refreshers, MenuTextId.RaceKillCam, d => d.killCam, (d, v) => d.killCam = v);
            Slider(screen, system, refreshers, MenuTextId.RaceKillCamScale,
                0.05f, 1f, 0.05f, "0.00", d => d.killCamTimeScale, (d, v) => d.killCamTimeScale = v);
            Slider(screen, system, refreshers, MenuTextId.RaceKillCamSeconds,
                0.2f, 4f, 0.1f, "0.0", d => d.killCamSeconds, (d, v) => d.killCamSeconds = v);
            Slider(screen, system, refreshers, MenuTextId.RaceKillCamRange,
                50f, 3000f, 50f, "0", d => d.killCamRange, (d, v) => d.killCamRange = v);

            Toggle(screen, system, refreshers, MenuTextId.RaceReadout, d => d.debugReadout, (d, v) => d.debugReadout = v);
            Toggle(screen, system, refreshers, MenuTextId.RaceGhostWireframes, d => d.debugGhostWireframes, (d, v) => d.debugGhostWireframes = v);

            screen.SetViewport(9);
            return screen;
        }

        static RaceDefinition Asset(RaceSystem system) =>
            system.DefinitionAsset != null ? system.DefinitionAsset : system.Definition;

        static void Slider(MenuScreen screen, RaceSystem system, List<System.Action> refreshers, MenuTextId label,
                           float min, float max, float step, string format,
                           System.Func<RaceDefinition, float> get, System.Action<RaceDefinition, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(Asset(system)), format, v => Write(system, d => set(d, v)));
            refreshers?.Add(() => row.SetWithoutNotify(get(Asset(system))));
        }

        static void Toggle(MenuScreen screen, RaceSystem system, List<System.Action> refreshers, MenuTextId label,
                           System.Func<RaceDefinition, bool> get, System.Action<RaceDefinition, bool> set)
        {
            var row = screen.AddRow<MenuToggle>(label);
            void OnChanged(bool v) => Write(system, d => set(d, v));
            row.Configure(get(Asset(system)), OnChanged);
            refreshers?.Add(() => row.Configure(get(Asset(system)), OnChanged));
        }

        // The asset first (marked for the flush), then the live clone.
        static void Write(RaceSystem system, System.Action<RaceDefinition> set)
        {
            var asset = Asset(system);
            set(asset);
            DebugAssetEdits.Touch(asset);
            if (system.Definition != null && system.Definition != asset) set(system.Definition);
        }

        // Registered with the pause menu's page registry right after the
        // traffic page (5): both are per-level runner systems.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(RaceDebugPage), 6, () =>
                DebugPages.Single(RaceSystem.Live != null && RaceSystem.Live.Bound ? RaceSystem.Live : null, Build));
    }
}
