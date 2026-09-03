using ConfusedGameDev.FiniteRunner.SaveData;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Collectibles
{
    /// <summary>
    /// The one place a pickup is recorded and acted on, hosted by BOTH
    /// scenes as a hand-placed scene-lifetime system (a root object in the
    /// runner scene, under <c>===SYSTEMS===</c> in the city; Tools →
    /// FiniteRunner → Place Scene Systems and the city's placer put it
    /// there — nothing creates one at play time, and <see cref="Instance"/>
    /// only FINDS it, logging an error once when a scene has none). It
    /// subscribes <see cref="Collectible.Collected"/> in OnEnable/OnDisable
    /// (domain reload is off — never a static initializer), records every
    /// pickup into the profile (<see cref="PlayerStats.RecordCollectible"/>,
    /// the LOG's COLLECTIBLES rows) and then switches on the
    /// <see cref="CollectibleKind"/>: Money adds the value to this run's
    /// <see cref="RunMoney"/>, banks it at once through
    /// <see cref="PlayerStats.AddMoney"/> (a game over keeps what was
    /// collected; the Mission Complete panel still pays the mission's own
    /// reward separately) and raises <see cref="MoneyChanged"/> for the
    /// <c>MoneyHud</c> and the "+$N" popup; an Item needs nothing more here —
    /// the city's LevelManager tallies its objectives off the same
    /// <c>Collected</c> event. A future kind is one more case in that switch.
    /// <see cref="ResetRun"/> zeroes the run counters (the runner's
    /// GameManager.Restart calls it); a scene load resets them by itself.
    /// </summary>
    public class CollectibleManager : MonoBehaviour
    {
        /// <summary>(money collected this run after the change, the amount just added).</summary>
        public static event System.Action<int, int> MoneyChanged;

        static CollectibleManager instance;
        static bool missingLogged;

        /// <summary>
        /// The scene's manager, found once (never created). Null — with one
        /// error per scene load — when the scene has none: place it.
        /// </summary>
        public static CollectibleManager Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = FindAnyObjectByType<CollectibleManager>(FindObjectsInactive.Include);
                if (instance == null && !missingLogged)
                {
                    missingLogged = true;
                    Debug.LogError("CollectibleManager: the scene has no CollectibleManager — pickups are not recorded. Place one (Tools → FiniteRunner → Place Scene Systems, or Tools → Police Escape → Place Scene Systems).");
                }
                return instance;
            }
        }

        /// <summary>Dollars picked up this run (since the scene loaded or the last <see cref="ResetRun"/>).</summary>
        public int RunMoney { get; private set; }

        /// <summary>Pickups of every kind this run.</summary>
        public int RunCollected { get; private set; }

        void OnEnable()
        {
            instance = this;
            missingLogged = false;
            Collectible.Collected += OnCollected;
        }

        void OnDisable()
        {
            Collectible.Collected -= OnCollected;
            if (instance == this) instance = null;
        }

        /// <summary>Start of a new run: the counters go back to zero (the banked money stays banked).</summary>
        public void ResetRun()
        {
            RunMoney = 0;
            RunCollected = 0;
            MoneyChanged?.Invoke(RunMoney, 0);
        }

        void OnCollected(Collectible collectible)
        {
            PlayerStats.RecordCollectible(collectible.Id);
            RunCollected++;

            switch (collectible.Kind)
            {
                case CollectibleKind.Money:
                    int amount = collectible.Value;
                    RunMoney += amount;
                    PlayerStats.AddMoney(amount);
                    MoneyChanged?.Invoke(RunMoney, amount);
                    break;

                case CollectibleKind.Item:
                default:
                    // Counted above; objectives listen to Collectible.Collected themselves.
                    break;
            }
        }
    }
}
