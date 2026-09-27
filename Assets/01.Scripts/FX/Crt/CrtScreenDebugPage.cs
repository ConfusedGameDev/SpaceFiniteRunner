using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The pause menu's CRT SCREEN page. Like the fog, the weather, the speed
    /// lines, the VHS tape and the PSX look, the tube belongs to neither game
    /// — any scene with a <see cref="CrtScreen"/> driver gets it — so the
    /// page lives with the system rather than in either debug factory, and
    /// the shared PauseMenu adds it wherever it finds one. The driver re-reads
    /// its asset every frame (no runtime clone), so these sliders edit the
    /// asset itself: the change is on screen the moment the menu is
    /// dismissed, and it is kept dirty until the pause menu's flush (<see cref="DebugAssetEdits"/>) writes it at
    /// the menu's commit points — the same persistence contract the other
    /// pages keep. (The player's own CRT dial is the VIDEO settings page, not
    /// this one — this page is the designer's.)
    /// </summary>
    public static class CrtScreenDebugPage
    {
        /// <summary>The live driver's asset, or null when the scene has no tube to tune.</summary>
        public static CrtScreenSettings Discover()
        {
            CrtScreen screen = CrtScreen.Instance != null
                ? CrtScreen.Instance
                : Object.FindFirstObjectByType<CrtScreen>();
            return screen != null ? screen.settings : null;
        }

        /// <summary>
        /// Nine rows: the master intensity, the curvature and corner radius,
        /// the scanlines, the phosphor bleed, the halation, the grille, the
        /// convergence error and the refresh flicker. The scanline count, the
        /// stripe width, the refresh rate and the vignette stay inspector-only.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, CrtScreenSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<CrtScreenSettings>("Debug_CrtScreen", parent, theme, MenuTextId.DebugTabCrt, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.CrtIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            page.Slider(MenuTextId.CrtCurvature,
                0f, 1f, 0.05f, "0.00", s => s.curvature, (s, v) => s.curvature = v);
            page.Slider(MenuTextId.CrtCorners,
                0f, 0.3f, 0.01f, "0.00", s => s.cornerRadius, (s, v) => s.cornerRadius = v);
            page.Slider(MenuTextId.CrtScanlines,
                0f, 1f, 0.05f, "0.00", s => s.scanlines, (s, v) => s.scanlines = v);
            page.Slider(MenuTextId.CrtBleed,
                0f, 8f, 0.5f, "0.0", s => s.bleed, (s, v) => s.bleed = v);
            page.Slider(MenuTextId.CrtGlow,
                0f, 1f, 0.05f, "0.00", s => s.glow, (s, v) => s.glow = v);
            page.Slider(MenuTextId.CrtMask,
                0f, 1f, 0.05f, "0.00", s => s.mask, (s, v) => s.mask = v);
            page.Slider(MenuTextId.CrtConvergence,
                0f, 4f, 0.25f, "0.00", s => s.convergence, (s, v) => s.convergence = v);
            page.Slider(MenuTextId.CrtFlicker,
                0f, 1f, 0.05f, "0.00", s => s.flicker, (s, v) => s.flicker = v);
            return page.Screen;
        }

        // Registered with the pause menu's page registry: any scene with a
        // driver gets this tab, and the menu never names the system.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(CrtScreenDebugPage), 60, () => DebugPages.Single(Discover(), Build));
    }
}
