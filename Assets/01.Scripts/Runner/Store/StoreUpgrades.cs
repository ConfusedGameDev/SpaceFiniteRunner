using ConfusedGameDev.FiniteRunner.SaveData;
namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// What gameplay asks the Store: "how much is SPEED multiplied for the
    /// car the player drives?" The profile holds the bought LEVEL per
    /// (model, category); the multiplier that level stands for is read off
    /// the definition asset every time, so retuning a table after a
    /// purchase retunes the saved game. Anything unresolvable — no settings
    /// asset, an unknown category, an empty section — reads as ×1, so a
    /// scene played before the store exists is simply the stock vehicle.
    /// </summary>
    public static class StoreUpgrades
    {
        /// <summary>Multiplier the owned level of <paramref name="categoryId"/> grants on <paramref name="modelId"/>.</summary>
        public static float Multiplier(string modelId, string categoryId)
        {
            StoreSettings settings = StoreSettings.Load();
            if (settings == null) return 1f;
            UpgradeDefinition definition = FindCategory(settings, categoryId);
            if (definition == null) return 1f;
            return definition.MultiplierFor(PlayerStats.UpgradeLevel(modelId, categoryId));
        }

        /// <summary>Multiplier for the section's default model — what the city and the runner use today.</summary>
        public static float Multiplier(StoreSectionKind kind, string categoryId)
        {
            string modelId = DefaultModel(kind);
            return modelId != null ? Multiplier(modelId, categoryId) : 1f;
        }

        /// <summary>
        /// The model id gameplay upgrades apply to in a section — the first
        /// listed. The seam a future model selection replaces with the
        /// profile's chosen one.
        /// </summary>
        public static string DefaultModel(StoreSectionKind kind)
        {
            StoreSettings settings = StoreSettings.Load();
            StoreSection section = settings != null ? settings.Section(kind) : null;
            StoreModel model = section != null ? section.DefaultModel : null;
            return model != null && !string.IsNullOrEmpty(model.modelId) ? model.modelId : null;
        }

        static UpgradeDefinition FindCategory(StoreSettings settings, string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return null;
            for (int k = 0; k < 3; k++)
            {
                StoreSection section = settings.Section((StoreSectionKind)k);
                UpgradeDefinition definition = section != null ? section.Category(categoryId) : null;
                if (definition != null) return definition;
            }
            return null;
        }
    }
}
