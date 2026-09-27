using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Inversion point between the shared menu framework and game-specific
    /// debug pages that live in higher assemblies. Pages register with
    /// <see cref="DebugPages"/> (refactor Step 10.1 folded the single-slot
    /// Discover / Flush hooks into that registry and into
    /// <see cref="DebugAssetEdits"/>); what is left here is the full-screen
    /// takeover gate and the tab-batch contract every provider implements.
    /// </summary>
    public static class DebugMenuHooks
    {
        /// <summary>True while a registered full-screen takeover (the city map) is open, blocking pause.</summary>
        public static Func<bool> FullScreenTakeoverOpen;

        /// <summary>A batch of game-specific debug tabs, counted before any is built so headers can print "TAB n/N".</summary>
        public interface IDebugTabs
        {
            int TabCount { get; }
            void AddTabs(DebugMenu menu, RectTransform parent, MenuTheme theme,
                         List<Action> refreshers, ref int tab, int tabCount);
        }
    }
}
