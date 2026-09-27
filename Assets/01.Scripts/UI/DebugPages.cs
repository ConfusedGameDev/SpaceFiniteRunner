using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The registry of debug pages a system brings with it (refactor Step
    /// 10.1). A system that owns a tunable asset — the fog, the weather, the
    /// speed lines, a full-screen look, the city's cars — registers ONE
    /// provider here from a <c>[RuntimeInitializeOnLoadMethod]</c>; the pause
    /// menu asks every provider in order and builds whatever the current scene
    /// offers. So the menu names no system, a system dropped into a new scene
    /// brings its page along, and this assembly still references no game
    /// assembly. Registering again under the same key replaces the entry
    /// (domain reload is off: registration runs on every play entry).
    /// </summary>
    public static class DebugPages
    {
        struct Entry
        {
            public string key;
            public int order;
            public Func<DebugMenuHooks.IDebugTabs> discover;
        }

        static readonly List<Entry> entries = new();

        /// <summary>
        /// Adds (or replaces) a provider. <paramref name="order"/> sorts the
        /// tabs after the runner's own (city pages 0, then the shared FX
        /// pages); <paramref name="discover"/> returns null when the current
        /// scene has nothing for it to tune.
        /// </summary>
        public static void Register(string key, int order, Func<DebugMenuHooks.IDebugTabs> discover)
        {
            entries.RemoveAll(e => e.key == key);
            entries.Add(new Entry { key = key, order = order, discover = discover });
            entries.Sort((a, b) => a.order.CompareTo(b.order));
        }

        /// <summary>Every provider with something to show in the current scene, in order.</summary>
        public static List<DebugMenuHooks.IDebugTabs> Discover()
        {
            var found = new List<DebugMenuHooks.IDebugTabs>();
            foreach (Entry entry in entries)
            {
                DebugMenuHooks.IDebugTabs tabs = entry.discover?.Invoke();
                if (tabs != null && tabs.TabCount > 0) found.Add(tabs);
            }
            return found;
        }

        /// <summary>A one-tab provider for <paramref name="asset"/> (null when there is no asset): the shape every settings page has.</summary>
        public static DebugMenuHooks.IDebugTabs Single<T>(T asset, Func<RectTransform, MenuTheme, T, List<Action>, int, int, MenuScreen> build)
            where T : UnityEngine.Object =>
            asset != null ? new SingleTab<T>(asset, build) : null;

        sealed class SingleTab<T> : DebugMenuHooks.IDebugTabs where T : UnityEngine.Object
        {
            readonly T asset;
            readonly Func<RectTransform, MenuTheme, T, List<Action>, int, int, MenuScreen> build;

            public SingleTab(T asset, Func<RectTransform, MenuTheme, T, List<Action>, int, int, MenuScreen> build)
            {
                this.asset = asset;
                this.build = build;
            }

            public int TabCount => 1;

            public void AddTabs(DebugMenu menu, RectTransform parent, MenuTheme theme,
                                List<Action> refreshers, ref int tab, int tabCount) =>
                menu.AddTab(build(parent, theme, asset, refreshers, tab++, tabCount));
        }
    }

    /// <summary>
    /// The generic settings page (refactor Step 10.1): a tab of slider rows
    /// that edit one asset in place. Each row writes the ASSET and marks it
    /// through <see cref="DebugAssetEdits"/>, so the pause menu's one flush
    /// saves it — six FX pages used to carry their own copies of the row
    /// helper and of that persistence. For assets a system re-reads every
    /// frame (no runtime clone); a page that must mirror onto a clone builds
    /// its own rows.
    /// </summary>
    public sealed class SettingsDebugPage<T> where T : UnityEngine.Object
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        readonly T asset;
        readonly List<Action> refreshers;

        /// <summary>The built tab.</summary>
        public MenuScreen Screen { get; }

        public SettingsDebugPage(string name, RectTransform parent, MenuTheme theme, MenuTextId title, T asset,
                                 List<Action> refreshers, int tabIndex, int tabCount)
        {
            this.asset = asset;
            this.refreshers = refreshers;
            Screen = MenuScreen.Create(name, parent, theme, 0f, ContentTop);
            Screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(Screen, theme, title, tabIndex, tabCount);
        }

        /// <summary>One slider row: reads with <paramref name="get"/>, writes the asset with <paramref name="set"/> and marks it for the flush.</summary>
        public SettingsDebugPage<T> Slider(MenuTextId label, float min, float max, float step, string format,
                                           Func<T, float> get, Action<T, float> set)
        {
            var row = Screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(asset), format, v =>
            {
                set(asset, v);
                DebugAssetEdits.Touch(asset);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(asset)));
            return this;
        }
    }
}
