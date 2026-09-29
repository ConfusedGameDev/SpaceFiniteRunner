using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The pause menu's DEBUG TOOLS page: developer utilities rather than
    /// tuning — today the Y debug boost (<c>Ship.DebugBoostChord</c>),
    /// its switch and its size. Both live on <see cref="GameSettings"/>, which
    /// the run reads live and never clones, so the rows edit the asset alone,
    /// apply at once and are saved at the menu's commit points through
    /// <see cref="DebugAssetEdits"/> — no reload prompt.
    /// </summary>
    public static class DebugToolsPage
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen Build(RectTransform parent, MenuTheme theme, GameSettings rules,
                                       List<System.Action> refreshers, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Tools", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, MenuTextId.DebugTabTools, tabIndex, tabCount);

            var toggle = screen.AddRow<MenuToggle>(MenuTextId.DebugBoostChord);
            void OnToggled(bool v)
            {
                rules.debugBoostChord = v;
                DebugAssetEdits.Touch(rules);
            }
            toggle.Configure(rules.debugBoostChord, OnToggled);
            refreshers?.Add(() => toggle.Configure(rules.debugBoostChord, OnToggled));

            var amount = screen.AddRow<DebugSliderRow>(MenuTextId.DebugBoostAmount);
            amount.Configure(50f, 5000f, 50f, rules.debugBoostKmh, "0", v =>
            {
                rules.debugBoostKmh = v;
                DebugAssetEdits.Touch(rules);
            });
            refreshers?.Add(() => amount.SetWithoutNotify(rules.debugBoostKmh));

            screen.SetViewport(9);
            return screen;
        }
    }
}
