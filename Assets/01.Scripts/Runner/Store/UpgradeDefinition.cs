using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// One upgrade category of the Store — SPEED on the car, DASH POWER on
    /// the ship — as a table of exactly <see cref="UpgradeIds.MaxLevel"/>
    /// rows: what level N costs, the multiplier it applies to the base stat,
    /// and the piece shown in the media panel while it is on offer (a video
    /// or a still; a row with neither shows the category's default media,
    /// and with no default either the panel reads NO SIGNAL). Level 0 is the
    /// stock vehicle and is not a row. Ten explicit rows rather than a cost
    /// formula, because those ten numbers ARE the balancing surface.
    /// </summary>
    [CreateAssetMenu(fileName = "Upgrade", menuName = "FiniteRunner/Store/Upgrade Definition")]
    public class UpgradeDefinition : ScriptableObject
    {
        /// <summary>One purchasable level.</summary>
        [System.Serializable]
        public class UpgradeLevel
        {
            [Tooltip("Price of this level, taken from the wallet.")]
            [MinValue(0)]
            public long cost = 500;

            [Tooltip("Multiplier on the base stat once this level is owned (1 = unchanged).")]
            [PropertyRange(0.25f, 4f)]
            public float multiplier = 1f;

            [Tooltip("Media panel piece while this level is the next to buy. Empty = the category's default.")]
            public VideoClip video;

            [Tooltip("Still shown when there is no video. Empty = the category's default.")]
            [PreviewField(48, ObjectFieldAlignment.Left)]
            public Sprite image;
        }

        [Tooltip("Stable id the profile keys the bought level on — one of the UpgradeIds constants. Never rename.")]
        public string id;

        [Tooltip("Row label, localized.")]
        public MenuTextId label;

        [Title("Default media")]
        [Tooltip("Shown for any level that has no video of its own.")]
        public VideoClip defaultVideo;

        [Tooltip("Shown for any level that has neither a video nor a still of its own.")]
        [PreviewField(64, ObjectFieldAlignment.Left)]
        public Sprite defaultImage;

        [Title("Levels")]
        [Tooltip("Exactly ten rows, level 1 first.")]
        [ListDrawerSettings(ShowIndexLabels = true, DraggableItems = false)]
        [ValidateInput(nameof(HasTenLevels), "A category needs exactly ten levels.")]
        public List<UpgradeLevel> levels = new();

        bool HasTenLevels(List<UpgradeLevel> list) => list != null && list.Count == UpgradeIds.MaxLevel;

        /// <summary>Price of <paramref name="level"/> (1..10); 0 outside the table.</summary>
        public long CostFor(int level)
        {
            int index = level - 1;
            return levels != null && index >= 0 && index < levels.Count && levels[index] != null ? levels[index].cost : 0;
        }

        /// <summary>Multiplier owned at <paramref name="level"/>; level 0 (or a missing row) is ×1.</summary>
        public float MultiplierFor(int level)
        {
            int index = level - 1;
            if (levels == null || index < 0 || index >= levels.Count || levels[index] == null) return 1f;
            float m = levels[index].multiplier;
            return m > 0f ? m : 1f;
        }

        /// <summary>The media panel piece for <paramref name="level"/>, falling back to the defaults.</summary>
        public void MediaFor(int level, out VideoClip video, out Sprite image)
        {
            int index = level - 1;
            UpgradeLevel row = levels != null && index >= 0 && index < levels.Count ? levels[index] : null;
            video = row != null && row.video != null ? row.video : defaultVideo;
            image = row != null && row.image != null ? row.image : defaultImage;
        }
    }
}
