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
    /// dismissed, and it is kept dirty until <see cref="Flush"/> writes it at
    /// the menu's commit points — the same persistence contract the other
    /// pages keep. (The player's own CRT dial is the VIDEO settings page, not
    /// this one — this page is the designer's.)
    /// </summary>
    public static class CrtScreenDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

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
            var screen = MenuScreen.Create("Debug_CrtScreen", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabCrt, tabIndex, tabCount);

            Add(screen, settings, refreshers, MenuTextId.CrtIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            Add(screen, settings, refreshers, MenuTextId.CrtCurvature,
                0f, 1f, 0.05f, "0.00", s => s.curvature, (s, v) => s.curvature = v);
            Add(screen, settings, refreshers, MenuTextId.CrtCorners,
                0f, 0.3f, 0.01f, "0.00", s => s.cornerRadius, (s, v) => s.cornerRadius = v);
            Add(screen, settings, refreshers, MenuTextId.CrtScanlines,
                0f, 1f, 0.05f, "0.00", s => s.scanlines, (s, v) => s.scanlines = v);
            Add(screen, settings, refreshers, MenuTextId.CrtBleed,
                0f, 8f, 0.5f, "0.0", s => s.bleed, (s, v) => s.bleed = v);
            Add(screen, settings, refreshers, MenuTextId.CrtGlow,
                0f, 1f, 0.05f, "0.00", s => s.glow, (s, v) => s.glow = v);
            Add(screen, settings, refreshers, MenuTextId.CrtMask,
                0f, 1f, 0.05f, "0.00", s => s.mask, (s, v) => s.mask = v);
            Add(screen, settings, refreshers, MenuTextId.CrtConvergence,
                0f, 4f, 0.25f, "0.00", s => s.convergence, (s, v) => s.convergence = v);
            Add(screen, settings, refreshers, MenuTextId.CrtFlicker,
                0f, 1f, 0.05f, "0.00", s => s.flicker, (s, v) => s.flicker = v);
            return screen;
        }

        static void Add(MenuScreen screen, CrtScreenSettings settings, List<System.Action> refreshers,
                        MenuTextId label, float min, float max, float step, string format,
                        System.Func<CrtScreenSettings, float> get, System.Action<CrtScreenSettings, float> set)
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
