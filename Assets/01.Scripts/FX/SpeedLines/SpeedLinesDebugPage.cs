using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The pause menu's SPEED LINES page. Like the fog and the weather, the
    /// speed lines belong to neither game — any scene with a
    /// <see cref="SpeedLines"/> driver gets it — so the page lives with the
    /// system rather than in either debug factory, and the shared PauseMenu
    /// adds it wherever it finds one. The driver re-reads its asset every
    /// frame (no runtime clone), so these sliders edit the asset itself: the
    /// change is on screen the moment the menu is dismissed, and it is kept
    /// dirty until the pause menu's flush (<see cref="DebugAssetEdits"/>) writes it at the menu's commit points —
    /// the same persistence contract the fog page keeps.
    /// </summary>
    public static class SpeedLinesDebugPage
    {
        /// <summary>The live driver's asset, or null when the scene has no speed lines to tune.</summary>
        public static SpeedLinesSettings Discover()
        {
            SpeedLines lines = SpeedLines.Instance != null
                ? SpeedLines.Instance
                : Object.FindFirstObjectByType<SpeedLines>();
            return lines != null ? lines.settings : null;
        }

        /// <summary>
        /// Nine rows: the master intensity, the speed band (start / full, each
        /// clamped against the other), density, width, the clear radius at
        /// both ends of the band, the flicker rate and the response. Colour and
        /// the line counts stay on the asset.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, SpeedLinesSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<SpeedLinesSettings>("Debug_SpeedLines", parent, theme, MenuTextId.DebugTabSpeedLines, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.SpeedLinesIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            page.Slider(MenuTextId.SpeedLinesStart,
                0f, 1f, 0.05f, "0.00", s => s.speedBand.x, (s, v) => s.speedBand.x = Mathf.Min(v, s.speedBand.y));
            page.Slider(MenuTextId.SpeedLinesFull,
                0f, 1f, 0.05f, "0.00", s => s.speedBand.y, (s, v) => s.speedBand.y = Mathf.Max(v, s.speedBand.x));
            page.Slider(MenuTextId.SpeedLinesDensity,
                0f, 1f, 0.05f, "0.00", s => s.density, (s, v) => s.density = v);
            page.Slider(MenuTextId.SpeedLinesWidth,
                0f, 1f, 0.05f, "0.00", s => s.lineWidth, (s, v) => s.lineWidth = v);
            page.Slider(MenuTextId.SpeedLinesInnerMax,
                0f, 1f, 0.02f, "0.00", s => s.innerRadius.y, (s, v) => s.innerRadius.y = Mathf.Max(v, s.innerRadius.x));
            page.Slider(MenuTextId.SpeedLinesInnerMin,
                0f, 1f, 0.02f, "0.00", s => s.innerRadius.x, (s, v) => s.innerRadius.x = Mathf.Min(v, s.innerRadius.y));
            page.Slider(MenuTextId.SpeedLinesFlicker,
                1f, 60f, 1f, "0", s => s.flickerRate, (s, v) => s.flickerRate = v);
            page.Slider(MenuTextId.SpeedLinesResponse,
                1f, 20f, 0.5f, "0.0", s => s.responseSharpness, (s, v) => s.responseSharpness = v);
            return page.Screen;
        }

        // Registered with the pause menu's page registry: any scene with a
        // driver gets this tab, and the menu never names the system.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(SpeedLinesDebugPage), 30, () => DebugPages.Single(Discover(), Build));
    }
}
