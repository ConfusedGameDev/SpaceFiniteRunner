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
    /// dirty until <see cref="Flush"/> writes it at the menu's commit points —
    /// the same persistence contract the fog and speed-lines pages keep.
    /// </summary>
    public static class VhsTapeDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

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
            var screen = MenuScreen.Create("Debug_VhsTape", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabVhs, tabIndex, tabCount);

            Add(screen, settings, refreshers, MenuTextId.VhsIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            Add(screen, settings, refreshers, MenuTextId.VhsChromaBleed,
                0f, 40f, 1f, "0", s => s.chromaBleed, (s, v) => s.chromaBleed = v);
            Add(screen, settings, refreshers, MenuTextId.VhsChromaLag,
                0f, 12f, 0.5f, "0.0", s => s.chromaLag, (s, v) => s.chromaLag = v);
            Add(screen, settings, refreshers, MenuTextId.VhsJitter,
                0f, 12f, 0.5f, "0.0", s => s.jitter, (s, v) => s.jitter = v);
            Add(screen, settings, refreshers, MenuTextId.VhsTracking,
                0f, 1f, 0.05f, "0.00", s => s.tracking, (s, v) => s.tracking = v);
            Add(screen, settings, refreshers, MenuTextId.VhsNoise,
                0f, 1f, 0.05f, "0.00", s => s.noise, (s, v) => s.noise = v);
            Add(screen, settings, refreshers, MenuTextId.VhsScanlines,
                0f, 1f, 0.05f, "0.00", s => s.scanlines, (s, v) => s.scanlines = v);
            Add(screen, settings, refreshers, MenuTextId.VhsWash,
                0f, 1f, 0.05f, "0.00", s => s.wash, (s, v) => s.wash = v);
            Add(screen, settings, refreshers, MenuTextId.VhsVignette,
                0f, 1f, 0.05f, "0.00", s => s.vignette, (s, v) => s.vignette = v);
            return screen;
        }

        static void Add(MenuScreen screen, VhsTapeSettings settings, List<System.Action> refreshers,
                        MenuTextId label, float min, float max, float step, string format,
                        System.Func<VhsTapeSettings, float> get, System.Action<VhsTapeSettings, float> set)
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
