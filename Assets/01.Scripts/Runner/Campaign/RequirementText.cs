using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// Turns an <see cref="UnlockRequirement"/> into the line a locked
    /// mission row prints (<c>REQUIRES: $5,000</c> / <c>REQUIRES: SPEED LV 3</c>).
    /// Lives in the Runner assembly rather than beside the requirement
    /// because naming an upgrade needs the Store's definitions: the category
    /// id is looked up across the <see cref="StoreSettings"/> sections for
    /// its localized label, falling back to the raw id when the store has no
    /// such category. Money goes through <see cref="StatFormat"/>, never
    /// localized, like every stat value.
    /// </summary>
    public static class RequirementText
    {
        /// <summary>The localized requirement line, or empty for null.</summary>
        public static string Describe(UnlockRequirement requirement, MenuTextLibrary texts)
        {
            if (requirement == null || texts == null) return string.Empty;
            return requirement.type switch
            {
                RequirementType.MinMoney =>
                    string.Format(texts.Get(MenuTextId.RequiresMoney), StatFormat.Money(requirement.amount)),
                RequirementType.MinUpgradeLevel =>
                    string.Format(texts.Get(MenuTextId.RequiresUpgrade), CategoryLabel(requirement.categoryId, texts), requirement.level),
                _ => string.Empty
            };
        }

        // The store's localized label for a category id, searched across the
        // three sections; the id itself when nothing matches.
        static string CategoryLabel(string categoryId, MenuTextLibrary texts)
        {
            StoreSettings settings = StoreSettings.Load();
            if (settings != null && !string.IsNullOrEmpty(categoryId))
            {
                for (int k = 0; k < 3; k++)
                {
                    StoreSection section = settings.Section((StoreSectionKind)k);
                    UpgradeDefinition def = section != null ? section.Category(categoryId) : null;
                    if (def != null) return texts.Get(def.label);
                }
            }
            return categoryId ?? string.Empty;
        }
    }
}
