using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.SaveData
{
    /// <summary>
    /// The recording API gameplay calls — one line per event, so a game
    /// class never touches the profile's fields or the dirty flag itself:
    /// <c>PlayerStats.RecordTotaledCar(...)</c>, <c>PlayerStats.RecordDeath(...)</c>.
    /// Every mutator marks the store dirty (which raises
    /// <see cref="PlayerProfileStore.Changed"/>); the "max" samplers only do
    /// so on a new record, so polling them every frame costs nothing.
    /// Writes to disk happen at the callers' commit points
    /// (<see cref="PlayerProfileStore.SaveIfDirty"/>) and on the
    /// <see cref="PlayerProfileBootstrap"/>'s autosave.
    ///
    /// <c>AddMoney</c> and <c>CompleteBonusObjective</c> are the public
    /// entry points for sources that do not exist yet (the mission reward
    /// banked on level completion is the one real money source today).
    /// </summary>
    public static class PlayerStats
    {
        /// <summary>Speeds above this are collision spikes, not driving — the max-speed record ignores them.</summary>
        public const float PlausibleCarSpeedKmh = 400f;

        /// <summary>
        /// True while the loading curtain is up: the trip runs on a live clock
        /// (it sets timeScale 1) but is not play, so the bootstrap's play-time
        /// tick skips it.
        /// </summary>
        public static bool SuspendPlayTime { get; set; }

        static PlayerProfile P => PlayerProfileStore.Profile;

        // ------------------------------------------------------------- shared

        /// <summary>Total play time — only the <see cref="PlayerProfileBootstrap"/> calls this.</summary>
        public static void AddPlayTime(double seconds)
        {
            if (seconds <= 0d) return;
            P.global.playTimeSeconds += seconds;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>A death (any level reboot); <paramref name="arrested"/> also counts it as an arrest.</summary>
        public static void RecordDeath(bool arrested)
        {
            P.global.deaths++;
            if (arrested) P.global.arrests++;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>An arrest that is not a death — the runner's patrol catching the ship.</summary>
        public static void RecordArrest()
        {
            P.global.arrests++;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>Money banked from any source (mission rewards today; pickups, bonuses tomorrow).</summary>
        public static void AddMoney(long amount)
        {
            if (amount == 0) return;
            P.global.moneyEarned += amount;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>A bonus / optional objective completed (the city's accepted challenges call this the moment one lands).</summary>
        public static void CompleteBonusObjective()
        {
            P.global.bonusObjectivesCompleted++;
            PlayerProfileStore.MarkDirty();
        }

        // --------------------------------------------------------- city chase

        /// <summary>
        /// A car the player totaled. <paramref name="vehicleKey"/> is the
        /// stable identity ("Taxi/Yellow"), <paramref name="vehicleLabel"/>
        /// the words the LOG prints for it ("YELLOW TAXI").
        /// </summary>
        public static void RecordTotaledCar(bool police, string vehicleKey, string vehicleLabel)
        {
            P.global.totaledCars++;
            if (police) P.global.totaledPoliceCars++;
            if (!string.IsNullOrEmpty(vehicleKey))
            {
                var counter = P.Counter(vehicleKey, vehicleLabel);
                counter.count++;
                if (!string.IsNullOrEmpty(vehicleLabel)) counter.label = vehicleLabel;
            }
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>A collectible picked up — counted in total and per id (the LOG lists each id).</summary>
        public static void RecordCollectible(string id)
        {
            P.global.collectiblesFound++;
            if (!string.IsNullOrEmpty(id)) P.CollectibleCounter(id, id.ToUpperInvariant()).count++;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>How many of one collectible id the player has ever picked up.</summary>
        public static int CollectibleCount(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            foreach (var entry in P.collectibles) if (entry.key == id) return entry.count;
            return 0;
        }

        /// <summary>Max-tracks the player car's speed; call every frame, it only records a new best.</summary>
        public static void SampleCarSpeed(float kmh)
        {
            if (kmh <= P.global.maxCarSpeedKmh || kmh > PlausibleCarSpeedKmh || float.IsNaN(kmh)) return;
            P.global.maxCarSpeedKmh = kmh;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>A landed jump: max-tracks both the horizontal distance and the air time.</summary>
        public static void RecordJump(float meters, float airSeconds)
        {
            bool changed = false;
            if (meters > P.global.maxJumpMeters) { P.global.maxJumpMeters = meters; changed = true; }
            if (airSeconds > P.global.maxAirTimeSeconds) { P.global.maxAirTimeSeconds = airSeconds; changed = true; }
            if (changed) PlayerProfileStore.MarkDirty();
        }

        /// <summary>
        /// A city level completed: counts it, banks the reward, remembers it
        /// as the last level and adds it to the progression list. Saves at
        /// once — the caller's scene is about to be unloaded.
        /// </summary>
        public static void RecordLevelCompleted(string levelId, string levelName, string lastObjective,
                                                long reward, int optionalAccepted, int optionalCompleted)
        {
            var p = P;
            p.global.levelsCompleted++;
            p.global.moneyEarned += reward;
            p.lastLevel.levelId = levelId ?? "";
            p.lastLevel.levelName = levelName ?? "";
            p.lastLevel.lastObjective = lastObjective ?? "";
            p.lastLevel.moneyEarned = reward;
            p.lastLevel.optionalAccepted = optionalAccepted;
            p.lastLevel.optionalCompleted = optionalCompleted;
            if (!string.IsNullOrEmpty(levelId) && !p.completedLevelIds.Contains(levelId))
                p.completedLevelIds.Add(levelId);
            PlayerProfileStore.MarkDirty();
            PlayerProfileStore.Save();
        }

        // ------------------------------------------------------ finite runner

        /// <summary>One escape attempt — the first frame the ship actually flies.</summary>
        public static void RecordRunStarted()
        {
            P.runner.escapesAttempted++;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>A run over: an escape counts as completed and min-tracks the time it took.</summary>
        public static void RecordRunEnded(bool escaped, float elapsedSeconds)
        {
            if (escaped)
            {
                P.runner.escapesCompleted++;
                if (elapsedSeconds > 0f && (P.runner.fastestEscapeSeconds <= 0f || elapsedSeconds < P.runner.fastestEscapeSeconds))
                    P.runner.fastestEscapeSeconds = elapsedSeconds;
            }
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>Max-tracks the ship's speed; call every frame while flying.</summary>
        public static void SampleShipSpeed(float kmh)
        {
            if (kmh <= P.runner.maxSpeedKmh || float.IsNaN(kmh)) return;
            P.runner.maxSpeedKmh = kmh;
            PlayerProfileStore.MarkDirty();
        }

        /// <summary>A pad or orb collected: a power-up (boost) or a slow-down.</summary>
        public static void RecordPad(bool boost)
        {
            if (boost) P.runner.powerUpsCollected++;
            else P.runner.slowDownsCollected++;
            PlayerProfileStore.MarkDirty();
        }

        // -------------------------------------------------------- progression

        public static bool IsLevelCompleted(string levelId) =>
            !string.IsNullOrEmpty(levelId) && P.completedLevelIds.Contains(levelId);

        public static bool IsUnlocked(string id) => !string.IsNullOrEmpty(id) && P.unlockedIds.Contains(id);

        /// <summary>Records an unlock; returns true if it was new.</summary>
        public static bool Unlock(string id)
        {
            if (string.IsNullOrEmpty(id) || P.unlockedIds.Contains(id)) return false;
            P.unlockedIds.Add(id);
            PlayerProfileStore.MarkDirty();
            return true;
        }
    }
}
