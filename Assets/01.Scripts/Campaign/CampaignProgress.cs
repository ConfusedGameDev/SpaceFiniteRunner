using ConfusedGameDev.FiniteRunner.SaveData;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// The campaign's rules, read off the catalog and the profile — the one
    /// place "which mission is next" and "may it be played" are decided, so
    /// the Store and the MISSIONS map can never disagree:
    /// <list type="bullet">
    /// <item>The <b>frontier</b> is the first mission in catalog order that
    /// is not completed; unset once every mission is.</item>
    /// <item>A mission is <b>playable</b> when it is completed (a replay) or
    /// it is the frontier and its requirements pass.</item>
    /// <item>Requirements <b>latch</b>: the first time a mission's list
    /// passes, its id goes into the profile's unlock ledger and it never
    /// re-locks, even if the balance later drops under a threshold.</item>
    /// </list>
    /// Nothing here loads a scene or touches a session — callers do.
    /// </summary>
    public static class CampaignProgress
    {
        /// <summary>The first mission not yet completed, or the default entry when the campaign is exhausted or there is no catalog.</summary>
        public static CampaignCatalog.Entry Frontier(CampaignCatalog catalog)
        {
            if (catalog == null) return default;
            foreach (CampaignCatalog.Entry entry in catalog.AllMissions())
                if (!PlayerStats.IsMissionCompleted(entry.mission.id))
                    return entry;
            return default;
        }

        /// <summary>True when the mission's requirements pass now or passed once before; a fresh pass is latched into the profile.</summary>
        public static bool RequirementsMet(MissionDefinition mission)
        {
            if (mission == null) return false;
            if (PlayerStats.IsUnlocked(mission.id)) return true;
            if (FirstUnmet(mission) != null) return false;
            PlayerStats.Unlock(mission.id); // latched: the next commit point saves it
            return true;
        }

        /// <summary>The first requirement the profile does not satisfy — what a locked row prints — or null when they all pass (or were latched).</summary>
        public static UnlockRequirement FirstUnmet(MissionDefinition mission)
        {
            if (mission == null) return null;
            if (PlayerStats.IsUnlocked(mission.id)) return null;
            for (int i = 0; i < mission.requirements.Count; i++)
            {
                UnlockRequirement requirement = mission.requirements[i];
                if (requirement != null && !requirement.IsMet()) return requirement;
            }
            return null;
        }

        /// <summary>True when the mission may be started: already completed, or the frontier with its requirements met.</summary>
        public static bool IsPlayable(CampaignCatalog catalog, MissionDefinition mission)
        {
            if (mission == null) return false;
            if (PlayerStats.IsMissionCompleted(mission.id)) return true;
            CampaignCatalog.Entry frontier = Frontier(catalog);
            return frontier.mission == mission && RequirementsMet(mission);
        }

        /// <summary>True when every mission of <paramref name="world"/> is completed.</summary>
        public static bool IsWorldComplete(WorldDefinition world)
        {
            if (world == null) return false;
            for (int i = 0; i < world.missions.Count; i++)
            {
                MissionDefinition mission = world.missions[i];
                if (mission != null && !PlayerStats.IsMissionCompleted(mission.id)) return false;
            }
            return true;
        }

        /// <summary>True when at least one mission of the catalog has been completed — what shows the MISSIONS row.</summary>
        public static bool AnyCompleted(CampaignCatalog catalog)
        {
            if (catalog == null) return false;
            foreach (CampaignCatalog.Entry entry in catalog.AllMissions())
                if (PlayerStats.IsMissionCompleted(entry.mission.id)) return true;
            return false;
        }
    }
}
