using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Tabbed debug pages for the pause menu. Each tab is a normal
    /// <see cref="MenuScreen"/> (same rows, focus and slide language as the
    /// rest of the menus); the bumpers (or Q/E) cycle between tabs, so new
    /// debug pages are one <see cref="AddTab"/> call away. This class only
    /// tracks which tab is active — the PauseMenu owns input and transitions,
    /// exactly like it does for its other sub-screens.
    /// </summary>
    public class DebugMenu
    {
        readonly List<MenuScreen> tabs = new();
        int active;

        public MenuScreen Active => tabs.Count > 0 ? tabs[active] : null;
        public int Count => tabs.Count;

        public void AddTab(MenuScreen screen) => tabs.Add(screen);

        public bool Contains(MenuScreen screen) => screen != null && tabs.Contains(screen);

        /// <summary>Moves the active tab by <paramref name="step"/> (wraps) and returns it.</summary>
        public MenuScreen Cycle(int step)
        {
            active = ((active + step) % tabs.Count + tabs.Count) % tabs.Count;
            return tabs[active];
        }

        public void HideAllImmediate()
        {
            foreach (var tab in tabs) tab.HideImmediate();
        }

        /// <summary>-1 previous tab / +1 next tab this frame: LB/RB on pad, Q/E on keyboard.</summary>
        public static int TabStepPressed()
        {
            int step = 0;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) step--;
                if (keyboard.eKey.wasPressedThisFrame) step++;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftShoulder.wasPressedThisFrame) step--;
                if (pad.rightShoulder.wasPressedThisFrame) step++;
            }
            return step;
        }

        /// <summary>
        /// Stamps the shared tab header on a tab screen: the tab's localized
        /// title (the "DEBUG — …" line lives whole in the text library, so
        /// translators own the full string) plus the bumper hint, so every
        /// debug page advertises how to switch.
        /// </summary>
        public static void AddTabHeader(MenuScreen screen, MenuTheme theme, MenuTextId titleId, int tabIndex, int tabCount)
        {
            screen.AddLabel("TabTitle", new Vector2(0f, 470f), new Vector2(1200f, 60f),
                            titleId, 44, theme.TextPrimary, theme.TitleFont,
                            TextAnchor.MiddleCenter, 0f);
            screen.AddLabel("TabHint", new Vector2(0f, 415f), new Vector2(900f, 40f),
                            $"LB ◀  TAB {tabIndex + 1}/{tabCount}  ▶ RB   (Q/E)", 24,
                            theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter, 0f);
        }
    }
}
