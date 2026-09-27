using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The pause menu's PSX LOOK page. Like the fog, the weather, the speed
    /// lines and the VHS tape, the console look belongs to neither game — any
    /// scene with a <see cref="PsxLook"/> driver gets it — so the page lives
    /// with the system rather than in either debug factory, and the shared
    /// PauseMenu adds it wherever it finds one. The driver re-reads its asset
    /// every frame (no runtime clone), so these sliders edit the asset itself:
    /// the change is on screen the moment the menu is dismissed, and it is
    /// kept dirty until the pause menu's flush (<see cref="DebugAssetEdits"/>) writes it at the menu's commit
    /// points — the same persistence contract the other pages keep.
    /// </summary>
    public static class PsxLookDebugPage
    {
        /// <summary>The live driver's asset, or null when the scene has no console look to tune.</summary>
        public static PsxLookSettings Discover()
        {
            PsxLook look = PsxLook.Instance != null
                ? PsxLook.Instance
                : Object.FindFirstObjectByType<PsxLook>();
            return look != null ? look.settings : null;
        }

        /// <summary>
        /// Nine rows: the master intensity, the virtual resolution, the colour
        /// bits and dither, the wobble, the block size, the swim, the jitter
        /// rate and the depth falloff — every knob of the asset.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, PsxLookSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<PsxLookSettings>("Debug_PsxLook", parent, theme, MenuTextId.DebugTabPsx, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.PsxIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            page.Slider(MenuTextId.PsxResolution,
                120f, 480f, 20f, "0", s => s.targetHeight, (s, v) => s.targetHeight = Mathf.RoundToInt(v));
            page.Slider(MenuTextId.PsxColorBits,
                3f, 8f, 1f, "0", s => s.colorBits, (s, v) => s.colorBits = Mathf.RoundToInt(v));
            page.Slider(MenuTextId.PsxDither,
                0f, 1f, 0.05f, "0.00", s => s.dither, (s, v) => s.dither = v);
            page.Slider(MenuTextId.PsxWobble,
                0f, 3f, 0.25f, "0.00", s => s.wobble, (s, v) => s.wobble = v);
            page.Slider(MenuTextId.PsxWobbleBlock,
                4f, 64f, 4f, "0", s => s.wobbleBlock, (s, v) => s.wobbleBlock = Mathf.RoundToInt(v));
            page.Slider(MenuTextId.PsxSwim,
                0f, 2f, 0.1f, "0.0", s => s.swim, (s, v) => s.swim = v);
            page.Slider(MenuTextId.PsxJitterRate,
                1f, 60f, 1f, "0", s => s.jitterRate, (s, v) => s.jitterRate = v);
            page.Slider(MenuTextId.PsxDepthFalloff,
                0f, 0.05f, 0.005f, "0.000", s => s.wobbleDepthFalloff, (s, v) => s.wobbleDepthFalloff = v);
            return page.Screen;
        }

        // Registered with the pause menu's page registry: any scene with a
        // driver gets this tab, and the menu never names the system.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(PsxLookDebugPage), 50, () => DebugPages.Single(Discover(), Build));
    }
}
