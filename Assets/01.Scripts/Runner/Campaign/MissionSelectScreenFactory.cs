using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// Builds the main menu's MISSIONS page — the campaign map: every
    /// mission of every reached world, one <see cref="MissionRow"/> each
    /// under a <see cref="StatHeaderRow"/> per world, the LOG screen's
    /// recipe (compact rows, a twelve-row viewport). A completed mission
    /// shows its best rank and total and replays; the frontier shows NEXT
    /// and plays exactly like the Store's START MISSION; a locked one is
    /// greyed with its requirement printed, and anything past the frontier
    /// is greyed blank — never hidden. A world is reached once the one
    /// before it is fully complete. The host calls the returned
    /// <c>refresh</c> every time it opens the page, since the profile moves
    /// between visits; rows are rebuilt, never patched.
    /// </summary>
    public static class MissionSelectScreenFactory
    {
        const float ContentTop = 330f;
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const int VisibleRows = 12;

        /// <summary>
        /// Creates the page (hidden) and fills it. <paramref name="onPlay"/>
        /// receives the chosen entry and whether it is a replay;
        /// <paramref name="onRefused"/> answers a press on a locked row.
        /// </summary>
        public static MenuScreen Build(RectTransform parent, MenuTheme theme,
                                       System.Action<CampaignCatalog.Entry, bool> onPlay, System.Action onRefused,
                                       out System.Action refresh)
        {
            var screen = MenuScreen.Create("MissionsScreen", parent, theme, 0f, ContentTop);
            screen.SetTitle(MenuTextId.Missions);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            screen.SetViewport(VisibleRows);
            Populate(screen, onPlay, onRefused);
            refresh = () => Populate(screen, onPlay, onRefused);
            return screen;
        }

        /// <summary>Replaces every row with the catalog's missions at the profile's current state.</summary>
        public static void Populate(MenuScreen screen, System.Action<CampaignCatalog.Entry, bool> onPlay, System.Action onRefused)
        {
            screen.ClearRows();
            MenuTextLibrary texts = MenuTextLibrary.Load();
            CampaignCatalog catalog = CampaignCatalog.Load();
            bool playing = Application.isPlaying; // the profile is only read in play — the edit-mode preview lists everything unplayed
            if (catalog == null)
            {
                screen.AddRow<DebugLabelRow>(MenuTextId.NothingHereYet);
                return;
            }

            CampaignCatalog.Entry frontier = playing ? CampaignProgress.Frontier(catalog) : default;
            WorldDefinition lastWorld = null;
            foreach (CampaignCatalog.Entry entry in catalog.AllMissions())
            {
                if (entry.world != lastWorld)
                {
                    // The next world opens only once the previous one is done.
                    if (lastWorld != null && !(playing && CampaignProgress.IsWorldComplete(lastWorld))) break;
                    lastWorld = entry.world;
                    screen.AddRow<StatHeaderRow>(string.IsNullOrEmpty(entry.world.displayName) ? entry.world.name : entry.world.displayName);
                }

                MissionRow row = screen.AddRow<MissionRow>(
                    string.Format(texts.Get(MenuTextId.MissionLabel), entry.number, entry.mission.DisplayName));

                PlayerProfile.MissionRecord record = playing ? PlayerStats.Mission(entry.mission.id) : null;
                bool completed = record != null && record.completed;
                bool playable;
                if (completed)
                {
                    row.SetValue($"{record.bestRank}  {StatFormat.Money(record.bestTotal)}");
                    playable = true;
                }
                else if (frontier.IsSet && entry.mission == frontier.mission)
                {
                    playable = CampaignProgress.RequirementsMet(entry.mission);
                    row.SetValue(playable ? texts.Get(MenuTextId.MissionNext)
                                          : RequirementText.Describe(CampaignProgress.FirstUnmet(entry.mission), texts));
                }
                else
                {
                    playable = false;
                }
                row.SetEnabled(playable);

                CampaignCatalog.Entry captured = entry;
                bool replay = completed;
                row.Activated += () =>
                {
                    if (row.Enabled) onPlay?.Invoke(captured, replay);
                    else onRefused?.Invoke();
                };
            }

            if (screen.Rows.Count == 0) screen.AddRow<DebugLabelRow>(MenuTextId.NothingHereYet);
            screen.SetFocus(0); // the first mission; the world header above it stays in view
        }
    }
}
