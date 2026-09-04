using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// Builds the main menu's MISSIONS page — the replay list: every mission
    /// the player has BEATEN at least once, one <see cref="MissionRow"/>
    /// each (best rank and total on the right) under a <see cref="StatHeaderRow"/>
    /// per world, the LOG screen's recipe (compact rows, a twelve-row
    /// viewport). Nothing unbeaten is listed — the frontier is the Store's
    /// business, and a locked mission is never shown here. The host calls
    /// the returned <c>refresh</c> every time it opens the page, since the
    /// profile moves between visits; rows are rebuilt, never patched.
    /// </summary>
    public static class MissionSelectScreenFactory
    {
        const float ContentTop = 330f;
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const int VisibleRows = 12;

        /// <summary>
        /// Creates the page (hidden) and fills it. <paramref name="onPlay"/>
        /// receives the chosen entry and <c>true</c> — every listed mission is
        /// a replay.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme,
                                       System.Action<CampaignCatalog.Entry, bool> onPlay,
                                       out System.Action refresh)
        {
            var screen = MenuScreen.Create("MissionsScreen", parent, theme, 0f, ContentTop);
            screen.SetTitle(MenuTextId.Missions);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            screen.SetViewport(VisibleRows);
            Populate(screen, onPlay);
            refresh = () => Populate(screen, onPlay);
            return screen;
        }

        /// <summary>Replaces every row with the catalog's completed missions at the profile's current state.</summary>
        public static void Populate(MenuScreen screen, System.Action<CampaignCatalog.Entry, bool> onPlay)
        {
            screen.ClearRows();
            MenuTextLibrary texts = MenuTextLibrary.Load();
            CampaignCatalog catalog = CampaignCatalog.Load();
            bool playing = Application.isPlaying; // the profile is only read in play — the edit-mode preview is empty

            if (catalog != null && playing)
            {
                WorldDefinition lastWorld = null;
                foreach (CampaignCatalog.Entry entry in catalog.AllMissions())
                {
                    PlayerProfile.MissionRecord record = PlayerStats.Mission(entry.mission.id);
                    if (record == null || !record.completed) continue;

                    // A world header goes over its first cleared mission only.
                    if (entry.world != lastWorld)
                    {
                        lastWorld = entry.world;
                        screen.AddRow<StatHeaderRow>(string.IsNullOrEmpty(entry.world.displayName) ? entry.world.name : entry.world.displayName);
                    }

                    MissionRow row = screen.AddRow<MissionRow>(
                        string.Format(texts.Get(MenuTextId.MissionLabel), entry.number, entry.mission.DisplayName));
                    row.SetValue($"{record.bestRank}  {StatFormat.Money(record.bestTotal)}");

                    CampaignCatalog.Entry captured = entry;
                    row.Activated += () => onPlay?.Invoke(captured, true);
                }
            }

            if (screen.Rows.Count == 0) screen.AddRow<DebugLabelRow>(MenuTextId.NothingHereYet);
            screen.SetFocus(0); // the first mission; the world header above it stays in view
        }
    }
}
