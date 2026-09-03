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
    /// dirty until <see cref="Flush"/> writes it at the menu's commit points —
    /// the same persistence contract the fog page keeps.
    /// </summary>
    public static class SpeedLinesDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

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
            var screen = MenuScreen.Create("Debug_SpeedLines", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabSpeedLines, tabIndex, tabCount);

            Add(screen, settings, refreshers, MenuTextId.SpeedLinesIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesStart,
                0f, 1f, 0.05f, "0.00", s => s.speedBand.x, (s, v) => s.speedBand.x = Mathf.Min(v, s.speedBand.y));
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesFull,
                0f, 1f, 0.05f, "0.00", s => s.speedBand.y, (s, v) => s.speedBand.y = Mathf.Max(v, s.speedBand.x));
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesDensity,
                0f, 1f, 0.05f, "0.00", s => s.density, (s, v) => s.density = v);
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesWidth,
                0f, 1f, 0.05f, "0.00", s => s.lineWidth, (s, v) => s.lineWidth = v);
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesInnerMax,
                0f, 1f, 0.02f, "0.00", s => s.innerRadius.y, (s, v) => s.innerRadius.y = Mathf.Max(v, s.innerRadius.x));
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesInnerMin,
                0f, 1f, 0.02f, "0.00", s => s.innerRadius.x, (s, v) => s.innerRadius.x = Mathf.Min(v, s.innerRadius.y));
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesFlicker,
                1f, 60f, 1f, "0", s => s.flickerRate, (s, v) => s.flickerRate = v);
            Add(screen, settings, refreshers, MenuTextId.SpeedLinesResponse,
                1f, 20f, 0.5f, "0.0", s => s.responseSharpness, (s, v) => s.responseSharpness = v);
            return screen;
        }

        static void Add(MenuScreen screen, SpeedLinesSettings settings, List<System.Action> refreshers,
                        MenuTextId label, float min, float max, float step, string format,
                        System.Func<SpeedLinesSettings, float> get, System.Action<SpeedLinesSettings, float> set)
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

        /// <summary>Writes the tuned asset to disk (editor only) — called at the pause menu's commit points, not on every slider tick.</summary>
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
