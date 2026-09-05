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
    /// kept dirty until <see cref="Flush"/> writes it at the menu's commit
    /// points — the same persistence contract the other pages keep.
    /// </summary>
    public static class PsxLookDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

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
            var screen = MenuScreen.Create("Debug_PsxLook", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabPsx, tabIndex, tabCount);

            Add(screen, settings, refreshers, MenuTextId.PsxIntensity,
                0f, 1f, 0.05f, "0.00", s => s.intensity, (s, v) => s.intensity = v);
            Add(screen, settings, refreshers, MenuTextId.PsxResolution,
                120f, 480f, 20f, "0", s => s.targetHeight, (s, v) => s.targetHeight = Mathf.RoundToInt(v));
            Add(screen, settings, refreshers, MenuTextId.PsxColorBits,
                3f, 8f, 1f, "0", s => s.colorBits, (s, v) => s.colorBits = Mathf.RoundToInt(v));
            Add(screen, settings, refreshers, MenuTextId.PsxDither,
                0f, 1f, 0.05f, "0.00", s => s.dither, (s, v) => s.dither = v);
            Add(screen, settings, refreshers, MenuTextId.PsxWobble,
                0f, 3f, 0.25f, "0.00", s => s.wobble, (s, v) => s.wobble = v);
            Add(screen, settings, refreshers, MenuTextId.PsxWobbleBlock,
                4f, 64f, 4f, "0", s => s.wobbleBlock, (s, v) => s.wobbleBlock = Mathf.RoundToInt(v));
            Add(screen, settings, refreshers, MenuTextId.PsxSwim,
                0f, 2f, 0.1f, "0.0", s => s.swim, (s, v) => s.swim = v);
            Add(screen, settings, refreshers, MenuTextId.PsxJitterRate,
                1f, 60f, 1f, "0", s => s.jitterRate, (s, v) => s.jitterRate = v);
            Add(screen, settings, refreshers, MenuTextId.PsxDepthFalloff,
                0f, 0.05f, 0.005f, "0.000", s => s.wobbleDepthFalloff, (s, v) => s.wobbleDepthFalloff = v);
            return screen;
        }

        static void Add(MenuScreen screen, PsxLookSettings settings, List<System.Action> refreshers,
                        MenuTextId label, float min, float max, float step, string format,
                        System.Func<PsxLookSettings, float> get, System.Action<PsxLookSettings, float> set)
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
