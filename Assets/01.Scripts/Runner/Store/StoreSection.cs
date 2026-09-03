using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>The three tabs of the Store. The order is the tab order.</summary>
    public enum StoreSectionKind { Car = 0, Ship = 1, Character = 2 }

    /// <summary>
    /// One thing the player can own in a section — a car, a ship, a
    /// character: its profile id, the name on the model row and the prefab
    /// the stage shows, with how to seat it in front of the camera.
    /// </summary>
    [System.Serializable]
    public class StoreModel
    {
        [Tooltip("Stable id the profile keys this model's upgrade levels on — one of the UpgradeIds constants. Never rename.")]
        public string modelId;

        [Tooltip("Name on the model row (< QUADRON >). Not localized: it is a proper name.")]
        public string displayName;

        [Tooltip("What the stage instances for the preview.")]
        [AssetsOnly]
        public GameObject prefab;

        [Tooltip("Uniform scale the preview instance gets.")]
        [PropertyRange(0.05f, 5f)]
        public float previewScale = 1f;

        [Tooltip("Offset from the stage slot, so the model sits centred under the camera.")]
        public Vector3 previewOffset;
    }

    /// <summary>
    /// One tab of the Store: which kind it is, its localized title, the
    /// models it lists (one today — the model row's seam for more) and the
    /// upgrade categories drawn under the model, in row order. Gameplay reads
    /// a section only to resolve a category's multiplier for the section's
    /// default model.
    /// </summary>
    [CreateAssetMenu(fileName = "StoreSection", menuName = "FiniteRunner/Store/Store Section")]
    public class StoreSection : ScriptableObject
    {
        public StoreSectionKind kind;

        [Tooltip("Tab title (STORE — CAR).")]
        public MenuTextId title;

        [Tooltip("Models the section offers; the first is the default gameplay uses.")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        public List<StoreModel> models = new();

        [Tooltip("Upgrade rows, top to bottom.")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        public List<UpgradeDefinition> categories = new();

        /// <summary>The model gameplay upgrades apply to — the first listed. Null when the section is empty.</summary>
        public StoreModel DefaultModel => models != null && models.Count > 0 ? models[0] : null;

        /// <summary>The category with the given id, or null.</summary>
        public UpgradeDefinition Category(string categoryId)
        {
            if (categories == null) return null;
            for (int i = 0; i < categories.Count; i++)
                if (categories[i] != null && categories[i].id == categoryId) return categories[i];
            return null;
        }
    }
}
