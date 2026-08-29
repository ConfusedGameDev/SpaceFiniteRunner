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
    /// menu is dismissed, and it is kept dirty until <see cref="Flush"/>
    /// writes it at the menu's commit points — the same persistence contract
    /// the rain page keeps.
    /// </summary>
    public static class DistanceFogDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

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
            var screen = MenuScreen.Create("Debug_Fog", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabDistanceFog, tabIndex, tabCount);

            Add(screen, settings, refreshers, MenuTextId.FogIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            Add(screen, settings, refreshers, MenuTextId.FogStart,
                0f, 2000f, 10f, "0", s => s.fogStart, (s, v) => s.fogStart = v);
            Add(screen, settings, refreshers, MenuTextId.FogEnd,
                50f, 3000f, 10f, "0", s => s.fogEnd, (s, v) => s.fogEnd = v);
            Add(screen, settings, refreshers, MenuTextId.FogDensity,
                0.5f, 6f, 0.1f, "0.0", s => s.fogDensity, (s, v) => s.fogDensity = v);
            Add(screen, settings, refreshers, MenuTextId.FogSkyAmount,
                0f, 1f, 0.05f, "0.00", s => s.skyFogAmount, (s, v) => s.skyFogAmount = v);
            Add(screen, settings, refreshers, MenuTextId.FogHeightFalloff,
                0f, 0.2f, 0.005f, "0.000", s => s.heightFog ? s.heightFalloff : 0f,
                (s, v) => { s.heightFalloff = v; s.heightFog = v > 0f; });
            Add(screen, settings, refreshers, MenuTextId.FarGlitchStart,
                0f, 3000f, 10f, "0", s => s.glitchStart, (s, v) => s.glitchStart = v);
            Add(screen, settings, refreshers, MenuTextId.FarGlitchStrength,
                0f, 1f, 0.05f, "0.00", s => s.farGlitch ? s.glitchStrength : 0f,
                (s, v) => { s.glitchStrength = v; s.farGlitch = v > 0f; });
            Add(screen, settings, refreshers, MenuTextId.FarGlitchRate,
                1f, 60f, 1f, "0", s => s.glitchRate, (s, v) => s.glitchRate = v);
            return screen;
        }

        static void Add(MenuScreen screen, DistanceFogSettings settings, List<System.Action> refreshers,
                        MenuTextId label, float min, float max, float step, string format,
                        System.Func<DistanceFogSettings, float> get, System.Action<DistanceFogSettings, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(settings), format, v =>
            {
                set(settings, v);
                MarkDirty(settings);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(settings)));
        }

        // -------------------------------------------------------- persistence

#if UNITY_EDITOR
        static readonly List<Object> touched = new();
#endif

        /// <summary>Marks the edited asset for the next <see cref="Flush"/> — a no-op for the in-memory default, which has no file to save.</summary>
        static void MarkDirty(Object asset)
        {
#if UNITY_EDITOR
            if (asset == null || !UnityEditor.EditorUtility.IsPersistent(asset)) return;
            if (!touched.Contains(asset)) touched.Add(asset);
            UnityEditor.EditorUtility.SetDirty(asset);
#endif
        }

        /// <summary>Writes the tuned fog to disk (editor only) — called at the pause menu's commit points, not on every slider tick.</summary>
        public static void Flush()
        {
#if UNITY_EDITOR
            foreach (var asset in touched)
                if (asset != null) UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
            touched.Clear();
#endif
        }
    }
}
