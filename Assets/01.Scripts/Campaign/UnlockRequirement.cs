using System;
using ConfusedGameDev.FiniteRunner.SaveData;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>What a requirement checks. Order is the save format — append only.</summary>
    public enum RequirementType { MinMoney = 0, MinUpgradeLevel = 1 }

    /// <summary>
    /// One extra condition a mission demands before it can be started, on
    /// top of the implicit "every previous mission is complete" (never
    /// authored — the catalog order IS that rule). Enum-typed in the
    /// <c>LevelObjective</c> style: one class, only the chosen type's knobs
    /// shown. Evaluated against the profile through <see cref="PlayerStats"/>
    /// only, so it costs no scene reference:
    /// <list type="bullet">
    /// <item><b>MinMoney</b> — the CURRENT spendable balance
    /// (<see cref="PlayerStats.Balance"/>), so buying upgrades can drop the
    /// player under a threshold they never reached.</item>
    /// <item><b>MinUpgradeLevel</b> — a store level on a model
    /// (<see cref="PlayerStats.UpgradeLevel"/>), ids as the store's
    /// <c>UpgradeIds</c> constants spell them ("car.quadron" / "car.speed").</item>
    /// </list>
    /// Requirements never re-lock: the first time a mission's list passes,
    /// <see cref="CampaignProgress"/> latches its id into the profile.
    /// </summary>
    [Serializable]
    public class UnlockRequirement
    {
        [EnumToggleButtons, HideLabel]
        public RequirementType type = RequirementType.MinMoney;

        [ShowIf("type", RequirementType.MinMoney)]
        [Tooltip("Spendable balance the wallet must hold when the mission is offered.")]
        [PropertyRange(0, 1000000), SuffixLabel("$", true)]
        public long amount = 5000;

        [ShowIf("type", RequirementType.MinUpgradeLevel)]
        [Tooltip("Store model id the level is read on, e.g. car.quadron / ship.nabucodonosor / char.rob.")]
        public string modelId = "car.quadron";

        [ShowIf("type", RequirementType.MinUpgradeLevel)]
        [Tooltip("Store category id, e.g. car.speed / ship.dashPower.")]
        public string categoryId = "car.speed";

        [ShowIf("type", RequirementType.MinUpgradeLevel)]
        [Tooltip("Bought level the category must have reached.")]
        [PropertyRange(1, 10)]
        public int level = 3;

        /// <summary>True when the profile satisfies this requirement right now.</summary>
        public bool IsMet() => type switch
        {
            RequirementType.MinMoney => PlayerStats.Balance >= amount,
            RequirementType.MinUpgradeLevel => PlayerStats.UpgradeLevel(modelId, categoryId) >= level,
            _ => true
        };
    }
}
