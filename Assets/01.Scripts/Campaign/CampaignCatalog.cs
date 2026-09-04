using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// The one list of everything the campaign contains: worlds in order,
    /// each with its missions in order, plus the two scenes every mission
    /// shares — the runner and the Coming Soon placeholder that stands in
    /// for the world after the last one. Every screen reads it directly
    /// (there is no runtime registry): the Store resolves its frontier off
    /// it, the MISSIONS map lists it, the scene managers route through it.
    /// Loaded from Resources the way <c>StoreSettings</c> is, with the
    /// domain-reload-off static reset.
    /// </summary>
    [CreateAssetMenu(fileName = "CampaignCatalog", menuName = "FiniteRunner/Campaign/Catalog")]
    public class CampaignCatalog : ScriptableObject
    {
        /// <summary>Resources path of the asset.</summary>
        public const string ResourcePath = "Campaign/CampaignCatalog";

        [Title("Worlds")]
        [Tooltip("Worlds in play order; the next opens when every mission of the previous is complete.")]
        [ListDrawerSettings(ShowFoldout = true), InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public List<WorldDefinition> worlds = new();

        [Title("Shared scenes")]
        [Tooltip("The finite runner scene every mission's escape run plays in.")]
        [Required]
        public string runnerSceneName = "FiniteRunner_Test";

        [Tooltip("Scene the Store's START MISSION leads to once every authored mission is complete.")]
        [Required]
        public string comingSoonSceneName = "ComingSoon";

        static CampaignCatalog cached;
        static bool warned;

        /// <summary>One mission of the catalog with its world and its 1-based number across the whole campaign.</summary>
        public readonly struct Entry
        {
            public readonly WorldDefinition world;
            public readonly MissionDefinition mission;
            public readonly int number;

            public Entry(WorldDefinition world, MissionDefinition mission, int number)
            {
                this.world = world;
                this.mission = mission;
                this.number = number;
            }

            /// <summary>False for the default entry — "no mission".</summary>
            public bool IsSet => mission != null;
        }

        /// <summary>Every mission in catalog order, null slots skipped, numbered from 1.</summary>
        public IEnumerable<Entry> AllMissions()
        {
            int number = 0;
            for (int w = 0; w < worlds.Count; w++)
            {
                WorldDefinition world = worlds[w];
                if (world == null) continue;
                for (int m = 0; m < world.missions.Count; m++)
                {
                    MissionDefinition mission = world.missions[m];
                    if (mission == null) continue;
                    yield return new Entry(world, mission, ++number);
                }
            }
        }

        /// <summary>The catalog entry of <paramref name="mission"/>, or the default entry when it is not listed.</summary>
        public Entry Find(MissionDefinition mission)
        {
            if (mission != null)
                foreach (Entry entry in AllMissions())
                    if (entry.mission == mission) return entry;
            return default;
        }

        /// <summary>The asset from Resources, or null (warned once) when it has not been created yet.</summary>
        public static CampaignCatalog Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<CampaignCatalog>(ResourcePath);
            if (cached == null && !warned)
            {
                warned = true;
                Debug.LogWarning($"No {nameof(CampaignCatalog)} at Resources/{ResourcePath} — run Tools → FiniteRunner → Create Campaign Assets. The Store falls back to its fixed next scene until then.");
            }
            return cached;
        }

        // Domain reload is off in this project: statics survive between plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cached = null;
            warned = false;
        }
    }
}
