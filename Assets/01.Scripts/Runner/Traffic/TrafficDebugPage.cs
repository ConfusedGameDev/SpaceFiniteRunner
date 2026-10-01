using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Traffic
{
    /// <summary>
    /// The pause menu's TRAFFIC page: how many oncoming cars, how fast, how
    /// far ahead they appear and where they stop near the end. Only a level
    /// with traffic shows it (the bound <see cref="TrafficSystem.Live"/>). The
    /// system reads a runtime clone, so every row writes the ASSET (kept for
    /// the pause menu's flush through <see cref="DebugAssetEdits"/>) and then
    /// mirrors the value onto the live clone — the patrol tab's rule. All
    /// rows apply on the next tick. The pool is built at bind, so raising
    /// CARS ON THE ROAD past what was prewarmed only bites on the next level
    /// load.
    /// </summary>
    public static class TrafficDebugPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen Build(RectTransform parent, MenuTheme theme, TrafficSystem system,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Traffic", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabTraffic, tabIndex, tabCount);

            Add(screen, system, refreshers, MenuTextId.TrafficMaxActive,
                0f, 24f, 1f, "0", d => d.maxActive, (d, v) => d.maxActive = Mathf.RoundToInt(v));
            Add(screen, system, refreshers, MenuTextId.TrafficSpeedMin,
                10f, 200f, 5f, "0", d => d.speedBand.x, (d, v) => d.speedBand.x = Mathf.Min(v, d.speedBand.y));
            Add(screen, system, refreshers, MenuTextId.TrafficSpeedMax,
                10f, 200f, 5f, "0", d => d.speedBand.y, (d, v) => d.speedBand.y = Mathf.Max(v, d.speedBand.x));
            Add(screen, system, refreshers, MenuTextId.TrafficSpawnAhead,
                1f, 10f, 0.5f, "0.0", d => d.spawnAheadSeconds, (d, v) => d.spawnAheadSeconds = v);
            Add(screen, system, refreshers, MenuTextId.TrafficMinSpawnAhead,
                200f, 2000f, 50f, "0", d => d.minSpawnAhead, (d, v) => d.minSpawnAhead = v);
            Add(screen, system, refreshers, MenuTextId.TrafficNoSpawnNearEnd,
                0f, 5000f, 100f, "0", d => d.noSpawnNearEnd, (d, v) => d.noSpawnNearEnd = v);
            return screen;
        }

        static void Add(MenuScreen screen, TrafficSystem system, List<System.Action> refreshers, MenuTextId label,
                        float min, float max, float step, string format,
                        System.Func<TrafficDefinition, float> get, System.Action<TrafficDefinition, float> set)
        {
            TrafficDefinition Asset() => system.DefinitionAsset != null ? system.DefinitionAsset : system.Definition;
            var row = screen.AddRow<DebugSliderRow>(label);
            row.Configure(min, max, step, get(Asset()), format, v =>
            {
                var asset = Asset();
                set(asset, v);
                DebugAssetEdits.Touch(asset);
                if (system.Definition != null && system.Definition != asset) set(system.Definition, v);
            });
            refreshers?.Add(() => row.SetWithoutNotify(get(Asset())));
        }

        // Registered with the pause menu's page registry, after the runner's
        // own tabs and before the shared FX pages (city 0, fog 20, …).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register() =>
            DebugPages.Register(nameof(TrafficDebugPage), 5, () =>
                DebugPages.Single(TrafficSystem.Live != null && TrafficSystem.Live.Bound ? TrafficSystem.Live : null, Build));
    }
}
