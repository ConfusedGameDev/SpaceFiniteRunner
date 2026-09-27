using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The pause menu's WEATHER page. Weather is not a runner or a city
    /// feature — both scenes spawn the same <see cref="RainSystem"/> — so the
    /// page lives with the system instead of in either game's debug factory,
    /// and the shared PauseMenu adds it wherever it finds rain running.
    ///
    /// The system re-reads its asset every frame (no runtime clone), so these
    /// sliders edit the asset itself: the change is on screen the moment the
    /// menu is dismissed, and it is kept dirty until the pause menu's flush (<see cref="DebugAssetEdits"/>) writes
    /// it at the menu's commit points — the same persistence contract the
    /// city's car and camera pages keep.
    /// </summary>
    public static class RainDebugPage
    {
        /// <summary>The live weather's asset, or null when the scene has no rain to tune.</summary>
        public static RainSettings Discover()
        {
            RainSystem system = RainSystem.Instance != null
                ? RainSystem.Instance
                : Object.FindFirstObjectByType<RainSystem>();
            return system != null ? system.settings : null;
        }

        /// <summary>
        /// Ten rows of storm. The two min-max bands it exposes (fall speed, drop
        /// size) collapse to one row each that SLIDES the band and keeps its
        /// spread — the spread is what stops the curtain reading as a solid
        /// sheet, so a debug slider must never flatten it to a single value.
        /// Thunder is dialled by rate instead, leaving its spacing band to the
        /// asset.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, RainSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var page = new SettingsDebugPage<RainSettings>("Debug_Weather", parent, theme, MenuTextId.DebugTabWeather, settings, refreshers, tabIndex, tabCount);

            page.Slider(MenuTextId.RainIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            page.Slider(MenuTextId.RainAmount,
                200f, 20000f, 100f, "0", s => s.dropsPerSecond, (s, v) => s.dropsPerSecond = v);
            page.Slider(MenuTextId.RainFallSpeed,
                2f, 80f, 1f, "0", s => Centre(s.fallSpeed), (s, v) => s.fallSpeed = Slide(s.fallSpeed, v, 2f, 80f));
            page.Slider(MenuTextId.RainDropSize,
                0.005f, 0.25f, 0.005f, "0.000", s => Centre(s.dropSize),
                (s, v) => s.dropSize = Slide(s.dropSize, v, 0.005f, 0.25f));
            page.Slider(MenuTextId.RainStreak,
                0f, 0.3f, 0.005f, "0.000", s => s.streakLength, (s, v) => s.streakLength = v);
            page.Slider(MenuTextId.RainWind,
                0f, 40f, 1f, "0", s => s.windSpeed, (s, v) => s.windSpeed = v);
            page.Slider(MenuTextId.RainWindDirection,
                0f, 360f, 5f, "0", s => s.windDirection, (s, v) => s.windDirection = v);
            page.Slider(MenuTextId.RainArea,
                5f, 150f, 1f, "0", s => s.areaRadius, (s, v) => s.areaRadius = v);
            // The rate, not the band: two multiplicative "how often" dials on
            // one page is a trap at 2am. The band stays authored on the asset.
            page.Slider(MenuTextId.ThunderFrequency,
                0.1f, 5f, 0.1f, "0.0", s => s.thunderFrequency, (s, v) => s.thunderFrequency = v);
            page.Slider(MenuTextId.ThunderFlash,
                0f, 1f, 0.05f, "0.00", s => s.flashPeak, (s, v) => s.flashPeak = v);
            return page.Screen;
        }

        static float Centre(Vector2 band) => (band.x + band.y) * 0.5f;

        /// <summary>Moves a min-max band to a new centre, keeping its width and staying inside the slider's range.</summary>
        static Vector2 Slide(Vector2 band, float centre, float min, float max)
        {
            float half = Mathf.Min(Mathf.Abs(band.y - band.x) * 0.5f, (max - min) * 0.5f);
            float clamped = Mathf.Clamp(centre, min + half, max - half);
            return new Vector2(clamped - half, clamped + half);
        }

        // Registered with the pause menu's page registry: any scene with a
        // driver gets this tab, and the menu never names the system.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(RainDebugPage), 10, () => DebugPages.Single(Discover(), Build));
    }
}
