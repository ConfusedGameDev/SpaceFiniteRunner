using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The pause menu's FALL &amp; RESPAWN page: the run rules of leaving the
    /// track (edge overhang and grace, the fall, the respawn wait and its
    /// penalty and whether it is a rolling start, the patrol's head start) plus the stall grace. These live on
    /// <see cref="GameSettings"/>, which the run reads LIVE and never clones —
    /// so, like the fog and rain pages and unlike the ship and patrol tabs,
    /// these sliders edit the asset itself: every change applies at once, it
    /// is kept dirty until <see cref="Flush"/> writes it at the menu's commit
    /// points (editor only; a build keeps it for the session), and nothing
    /// here needs a reload.
    /// </summary>
    public static class FallRespawnDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen Build(RectTransform parent, MenuTheme theme, GameSettings settings,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_FallRespawn", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabFall, tabIndex, tabCount);

            Add(screen, settings, refreshers, MenuTextId.EdgeOverhang,
                0f, 10f, 0.25f, "0.00", s => s.edgeOverhang, (s, v) => s.edgeOverhang = v);
            Add(screen, settings, refreshers, MenuTextId.EdgeGrace,
                0f, 2f, 0.05f, "0.00", s => s.edgeGraceSeconds, (s, v) => s.edgeGraceSeconds = v);
            Add(screen, settings, refreshers, MenuTextId.FallGravity,
                0f, 200f, 5f, "0", s => s.fallGravity, (s, v) => s.fallGravity = v);
            Add(screen, settings, refreshers, MenuTextId.FallDuration,
                0.1f, 5f, 0.1f, "0.0", s => s.fallDurationSeconds, (s, v) => s.fallDurationSeconds = v);
            Add(screen, settings, refreshers, MenuTextId.FallCameraFollow,
                0f, 5f, 0.1f, "0.0", s => s.fallCameraFollowSeconds, (s, v) => s.fallCameraFollowSeconds = v);
            Add(screen, settings, refreshers, MenuTextId.RespawnWait,
                0f, 10f, 0.25f, "0.00", s => s.respawnWaitSeconds, (s, v) => s.respawnWaitSeconds = v);
            AddToggle(screen, settings, refreshers, MenuTextId.RespawnRollingStart,
                s => s.respawnRollingStart, (s, v) => s.respawnRollingStart = v);
            Add(screen, settings, refreshers, MenuTextId.RespawnBlinkRate,
                1f, 30f, 1f, "0", s => s.respawnBlinkRate, (s, v) => s.respawnBlinkRate = v);
            Add(screen, settings, refreshers, MenuTextId.RespawnSpeedPenalty,
                0f, 1f, 0.05f, "0.00", s => s.respawnSpeedPenalty, (s, v) => s.respawnSpeedPenalty = v);
            Add(screen, settings, refreshers, MenuTextId.RespawnClearance,
                0f, 1000f, 25f, "0", s => s.respawnClearance, (s, v) => s.respawnClearance = v);
            Add(screen, settings, refreshers, MenuTextId.RespawnPatrolGap,
                0f, 1000f, 25f, "0", s => s.respawnMinPatrolGap, (s, v) => s.respawnMinPatrolGap = v);
            Add(screen, settings, refreshers, MenuTextId.StallGrace,
                0f, 10f, 0.25f, "0.00", s => s.stallGraceSeconds, (s, v) => s.stallGraceSeconds = v);
            screen.SetViewport(9);
            return screen;
        }

        static void Add(MenuScreen screen, GameSettings settings, List<System.Action> refreshers,
                        MenuTextId label, float min, float max, float step, string format,
                        System.Func<GameSettings, float> get, System.Action<GameSettings, float> set)
        {
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(settings), format, v =>
            {
                set(settings, v);
                MarkDirty(settings);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(settings)));
        }

        /// <summary>An ON/OFF row on a GameSettings bool. Configure sets without notifying, so the reopen readout never re-fires the write.</summary>
        static void AddToggle(MenuScreen screen, GameSettings settings, List<System.Action> refreshers, MenuTextId label,
                              System.Func<GameSettings, bool> get, System.Action<GameSettings, bool> set)
        {
            var row = screen.AddRow<MenuToggle>(label);
            void OnChanged(bool v)
            {
                set(settings, v);
                MarkDirty(settings);
            }
            row.Configure(get(settings), OnChanged);
            refreshers?.Add(() => row.Configure(get(settings), OnChanged));
        }

        // -------------------------------------------------------- persistence

#if UNITY_EDITOR
        static readonly List<Object> touched = new();
#endif

        /// <summary>Marks the edited asset for the next <see cref="Flush"/> — a no-op for an in-memory instance, which has no file to save.</summary>
        static void MarkDirty(Object asset)
        {
#if UNITY_EDITOR
            if (asset == null || !UnityEditor.EditorUtility.IsPersistent(asset)) return;
            if (!touched.Contains(asset)) touched.Add(asset);
            UnityEditor.EditorUtility.SetDirty(asset);
#endif
        }

        /// <summary>Writes the tuned rules to disk (editor only) — called at the pause menu's commit points, not on every slider tick.</summary>
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
