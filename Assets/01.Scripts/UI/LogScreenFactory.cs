using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.SaveData;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Builds the LOG page: one scrollable list of the player's lifetime
    /// stats and records read off the <see cref="PlayerProfile"/>, in
    /// sections (GLOBAL, TOTALED VEHICLES, COLLECTIBLES, LAST LEVEL, ESCAPE RUNS) headed by
    /// non-focusable <see cref="StatHeaderRow"/>s. Compact rows, twelve
    /// visible at once under a <see cref="MenuScreen.SetViewport"/> window;
    /// the focus scrolls the rest into view. The pause menu owns one and
    /// calls the returned <c>refresh</c> every time it opens, since the
    /// vehicle list grows between pauses — the rows are rebuilt from the
    /// profile rather than patched. Static and self-contained so the main
    /// menu can host the same page later.
    ///
    /// <b>Adding a stat</b> is one <c>Stat(...)</c> line in <see cref="Populate"/>
    /// under the right header (plus its <see cref="MenuTextId"/>).
    /// </summary>
    public static class LogScreenFactory
    {
        // Rows at 330 → -352 (12 × 62) keep clear of the footer strip; the
        // title plate sits at contentTop + 150.
        const float ContentTop = 330f;
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const int VisibleRows = 12;

        /// <summary>Creates the page (hidden) and fills it; <paramref name="refresh"/> rebuilds the rows from the current profile.</summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme, out System.Action refresh)
        {
            var screen = MenuScreen.Create("LogScreen", parent, theme, 0f, ContentTop);
            screen.SetTitle(MenuTextId.Log);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            screen.SetViewport(VisibleRows);
            Populate(screen);
            refresh = () => Populate(screen);
            return screen;
        }

        /// <summary>Replaces every row with the profile's current values.</summary>
        public static void Populate(MenuScreen screen)
        {
            screen.ClearRows();
            PlayerProfile p = PlayerProfileStore.Profile;
            var g = p.global;
            var r = p.runner;
            var last = p.lastLevel;

            Header(screen, MenuTextId.LogSectionGlobal);
            Stat(screen, MenuTextId.StatPlayTime, StatFormat.Duration(g.playTimeSeconds));
            Stat(screen, MenuTextId.StatLevelsCompleted, StatFormat.Count(g.levelsCompleted));
            Stat(screen, MenuTextId.StatDeaths, StatFormat.Count(g.deaths));
            Stat(screen, MenuTextId.StatArrests, StatFormat.Count(g.arrests));
            Stat(screen, MenuTextId.StatMaxSpeed, StatFormat.Kmh(g.maxCarSpeedKmh));
            Stat(screen, MenuTextId.StatMaxJump, StatFormat.Meters(g.maxJumpMeters));
            Stat(screen, MenuTextId.StatMoneyEarned, StatFormat.Money(g.moneyEarned));
            Stat(screen, MenuTextId.StatBonusObjectives, StatFormat.Count(g.bonusObjectivesCompleted));

            Header(screen, MenuTextId.LogSectionVehicles);
            Stat(screen, MenuTextId.StatTotaledCars, StatFormat.Count(g.totaledCars));
            Stat(screen, MenuTextId.StatTotaledPolice, StatFormat.Count(g.totaledPoliceCars));
            var byVehicle = new List<PlayerProfile.CountEntry>(p.totaledByVehicle);
            byVehicle.Sort((a, b) => b.count != a.count ? b.count.CompareTo(a.count) : string.CompareOrdinal(a.label, b.label));
            foreach (var entry in byVehicle)
                if (entry.count > 0)
                    screen.AddRow<StatRow>(string.IsNullOrEmpty(entry.label) ? entry.key : entry.label)
                          .SetValue(StatFormat.Count(entry.count));

            Header(screen, MenuTextId.LogSectionCollectibles);
            Stat(screen, MenuTextId.StatCollectibles, StatFormat.Count(g.collectiblesFound));
            var collectibles = new List<PlayerProfile.CountEntry>(p.collectibles);
            collectibles.Sort((a, b) => b.count != a.count ? b.count.CompareTo(a.count) : string.CompareOrdinal(a.label, b.label));
            foreach (var entry in collectibles)
                if (entry.count > 0)
                    screen.AddRow<StatRow>(string.IsNullOrEmpty(entry.label) ? entry.key : entry.label)
                          .SetValue(StatFormat.Count(entry.count));

            Header(screen, MenuTextId.LogSectionLastLevel);
            if (string.IsNullOrEmpty(last.levelId))
            {
                screen.AddRow<DebugLabelRow>(MenuTextId.NothingHereYet);
            }
            else
            {
                Stat(screen, MenuTextId.StatLevelName, last.levelName);
                Stat(screen, MenuTextId.StatLastObjective, last.lastObjective);
                Stat(screen, MenuTextId.StatMoneyEarned, StatFormat.Money(last.moneyEarned));
                Stat(screen, MenuTextId.StatOptionalObjectives, StatFormat.Ratio(last.optionalCompleted, last.optionalAccepted));
            }

            Header(screen, MenuTextId.LogSectionRunner);
            Stat(screen, MenuTextId.StatEscapesAttempted, StatFormat.Count(r.escapesAttempted));
            Stat(screen, MenuTextId.StatEscapesCompleted, StatFormat.Count(r.escapesCompleted));
            Stat(screen, MenuTextId.StatMaxSpeed, StatFormat.Kmh(r.maxSpeedKmh));
            Stat(screen, MenuTextId.StatFastestEscape, StatFormat.Lap(r.fastestEscapeSeconds));
            Stat(screen, MenuTextId.StatPowerUps, StatFormat.Count(r.powerUpsCollected));
            Stat(screen, MenuTextId.StatSlowDowns, StatFormat.Count(r.slowDownsCollected));

            screen.SetFocus(0); // lands on the first stat; the header above it stays in view
        }

        static void Header(MenuScreen screen, MenuTextId id) => screen.AddRow<StatHeaderRow>(id);

        static void Stat(MenuScreen screen, MenuTextId id, string value) => screen.AddRow<StatRow>(id).SetValue(value);
    }
}
