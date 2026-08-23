using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Inversion point between the shared menu framework and game-specific
    /// debug pages that live in higher assemblies. The pause menu talks only
    /// to these hooks; a game module (the city chase) registers its providers
    /// from a [RuntimeInitializeOnLoadMethod], so this assembly never
    /// references the game assemblies and the reference graph stays acyclic.
    /// </summary>
    public static class DebugMenuHooks
    {
        /// <summary>Extra debug pages the current scene offers, or null when the provider has none.</summary>
        public static Func<IDebugTabs> Discover;

        /// <summary>Commits provider-owned settings assets to disk at the menu's commit points.</summary>
        public static Action Flush;

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
