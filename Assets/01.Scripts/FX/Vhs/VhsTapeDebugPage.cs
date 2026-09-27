using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The pause menu's VHS TAPE page. Like the fog, the weather and the
    /// speed lines, the tape belongs to neither game — any scene with a
    /// <see cref="VhsTape"/> driver gets it — so the page lives with the
    /// system rather than in either debug factory, and the shared PauseMenu
    /// adds it wherever it finds one. The driver re-reads its asset every
    /// frame (no runtime clone), so these sliders edit the asset itself: the
    /// change is on screen the moment the menu is dismissed, and it is kept
    /// dirty until the pause menu's flush (<see cref="DebugAssetEdits"/>) writes it at the menu's commit points —
    /// the same persistence contract the fog and speed-lines pages keep.
    /// </summary>
    public static class VhsTapeDebugPage
    {
        /// <summary>The live driver's asset, or null when the scene has no tape to tune.</summary>
        public static VhsTapeSettings Discover()
        {
            VhsTape tape = VhsTape.Instance != null
                ? VhsTape.Instance
                : Object.FindFirstObjectByType<VhsTape>();
            return tape != null ? tape.settings : null;
        }

        /// <summary>
        /// Nine rows: the master intensity, the two chroma faults, the row
        /// jitter, the tracking band, the noise, the scanlines, the wash and
        /// the vignette. Frame rate, the band's speed and height, the head
        /// switch and the scanline count stay on the asset.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, VhsTapeSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<VhsTapeSettings>("Debug_VhsTape", parent, theme, MenuTextId.DebugTabVhs, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.VhsIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            page.Slider(MenuTextId.VhsChromaBleed,
                0f, 40f, 1f, "0", s => s.chromaBleed, (s, v) => s.chromaBleed = v);
            page.Slider(MenuTextId.VhsChromaLag,
                0f, 12f, 0.5f, "0.0", s => s.chromaLag, (s, v) => s.chromaLag = v);
            page.Slider(MenuTextId.VhsJitter,
                0f, 12f, 0.5f, "0.0", s => s.jitter, (s, v) => s.jitter = v);
            page.Slider(MenuTextId.VhsTracking,
                0f, 1f, 0.05f, "0.00", s => s.tracking, (s, v) => s.tracking = v);
            page.Slider(MenuTextId.VhsNoise,
                0f, 1f, 0.05f, "0.00", s => s.noise, (s, v) => s.noise = v);
            page.Slider(MenuTextId.VhsScanlines,
                0f, 1f, 0.05f, "0.00", s => s.scanlines, (s, v) => s.scanlines = v);
            page.Slider(MenuTextId.VhsWash,
                0f, 1f, 0.05f, "0.00", s => s.wash, (s, v) => s.wash = v);
            page.Slider(MenuTextId.VhsVignette,
                0f, 1f, 0.05f, "0.00", s => s.vignette, (s, v) => s.vignette = v);
            return page.Screen;
        }

        // Registered with the pause menu's page registry: any scene with a
        // driver gets this tab, and the menu never names the system.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(VhsTapeDebugPage), 40, () => DebugPages.Single(Discover(), Build));
    }
}
