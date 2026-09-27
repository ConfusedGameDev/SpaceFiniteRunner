using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The pause menu's FOG page. Like the weather, the distance fog belongs
    /// to neither game — any scene with a <see cref="DistanceFog"/> object
    /// gets it — so the page lives with the system rather than in either
    /// debug factory, and the shared PauseMenu adds it wherever it finds one.
    /// The system re-reads its asset every frame (no runtime clone), so these
    /// sliders edit the asset itself: the change is on screen the moment the
    /// menu is dismissed, and it is kept dirty until the pause menu's flush (<see cref="DebugAssetEdits"/>)
    /// writes it at the menu's commit points — the same persistence contract
    /// the rain page keeps.
    /// </summary>
    public static class DistanceFogDebugPage
    {
        /// <summary>The live fog's asset, or null when the scene has no fog to tune.</summary>
        public static DistanceFogSettings Discover()
        {
            DistanceFog fog = DistanceFog.Instance != null
                ? DistanceFog.Instance
                : Object.FindFirstObjectByType<DistanceFog>();
            return fog != null ? fog.settings : null;
        }

        /// <summary>
        /// Nine rows: the fog band (intensity, start, end, thickness, sky
        /// share, height falloff — moving that one above 0 switches the height
        /// group on) and the glitch (start, strength, rate). Colours stay on
        /// the asset; a slider is no place for a colour.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, DistanceFogSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<DistanceFogSettings>("Debug_Fog", parent, theme, MenuTextId.DebugTabDistanceFog, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.FogIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            page.Slider(MenuTextId.FogStart,
                0f, 2000f, 10f, "0", s => s.fogStart, (s, v) => s.fogStart = v);
            page.Slider(MenuTextId.FogEnd,
                50f, 3000f, 10f, "0", s => s.fogEnd, (s, v) => s.fogEnd = v);
            page.Slider(MenuTextId.FogDensity,
                0.5f, 6f, 0.1f, "0.0", s => s.fogDensity, (s, v) => s.fogDensity = v);
            page.Slider(MenuTextId.FogSkyAmount,
                0f, 1f, 0.05f, "0.00", s => s.skyFogAmount, (s, v) => s.skyFogAmount = v);
            page.Slider(MenuTextId.FogHeightFalloff,
                0f, 0.2f, 0.005f, "0.000", s => s.heightFog ? s.heightFalloff : 0f,
                (s, v) => { s.heightFalloff = v; s.heightFog = v > 0f; });
            page.Slider(MenuTextId.FarGlitchStart,
                0f, 3000f, 10f, "0", s => s.glitchStart, (s, v) => s.glitchStart = v);
            page.Slider(MenuTextId.FarGlitchStrength,
                0f, 1f, 0.05f, "0.00", s => s.farGlitch ? s.glitchStrength : 0f,
                (s, v) => { s.glitchStrength = v; s.farGlitch = v > 0f; });
            page.Slider(MenuTextId.FarGlitchRate,
                1f, 60f, 1f, "0", s => s.glitchRate, (s, v) => s.glitchRate = v);
            return page.Screen;
        }

        // Registered with the pause menu's page registry: any scene with a
        // driver gets this tab, and the menu never names the system.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(DistanceFogDebugPage), 20, () => DebugPages.Single(Discover(), Build));
    }
}
