using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// A world: one city scene and the ordered missions played in it.
    /// Completing every mission of a world is what reaches the next one —
    /// the catalog order is the whole rule. The runner is always the same
    /// scene (<see cref="CampaignCatalog.runnerSceneName"/>) playing each
    /// mission's own runner level.
    /// </summary>
    [CreateAssetMenu(fileName = "World", menuName = "FiniteRunner/Campaign/World")]
    public class WorldDefinition : ScriptableObject
    {
        [Tooltip("Header printed over this world's rows on the MISSIONS map.")]
        public string displayName = "World";

        [Tooltip("City scene every mission of this world loads, by name (must be in the build settings).")]
        [Required]
        public string sceneName = "CarTest";

        [Tooltip("Missions in play order. The first not yet completed is the campaign's frontier.")]
        [ListDrawerSettings(ShowFoldout = true), InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public List<MissionDefinition> missions = new();
    }
}
