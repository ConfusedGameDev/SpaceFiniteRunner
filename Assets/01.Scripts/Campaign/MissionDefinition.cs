using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// One mission of the campaign: a city level and the escape run that
    /// follows it, cleared (and paid) together by the runner's Mission
    /// Complete panel. The unit of progression, payment and replay.
    ///
    /// <see cref="id"/> is an authored string, never the asset name, so a
    /// renamed asset keeps its place in every profile — it keys the
    /// <c>MissionRecord</c> and the unlock ledger. The two level slots are
    /// typed through the marker bases so the catalog never references a game
    /// assembly. <see cref="requirements"/> are the EXTRA gates; "the
    /// previous mission is complete" is implicit in the catalog order.
    /// </summary>
    [CreateAssetMenu(fileName = "Mission", menuName = "FiniteRunner/Campaign/Mission")]
    public class MissionDefinition : ScriptableObject
    {
        [Title("Identity")]
        [Tooltip("Stable save key (e.g. m1_downtown). Never change it once a build shipped.")]
        [Required]
        public string id = "";

        [Tooltip("Name printed on the Store's START MISSION row and the MISSIONS map.")]
        public string displayName = "Mission";

        [Title("Levels")]
        [Tooltip("The city LevelDefinition this mission plays first.")]
        [Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public CityLevelAsset cityLevel;

        [Tooltip("The RunnerLevelDefinition the escape run plays after the city.")]
        [Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public RunnerLevelAsset runnerLevel;

        [Title("Unlock")]
        [Tooltip("Extra conditions on top of the previous mission being complete. Empty = unlocks the moment it becomes the frontier.")]
        [ListDrawerSettings(ShowFoldout = true)]
        public List<UnlockRequirement> requirements = new();

        /// <summary>The name the menus print — the display name, or the asset name while it is blank.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}
