using System.Collections.Generic;
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
    /// entry points for sources that do not exist yet. The one real money
    /// source today is a MISSION — a city level plus the escape run after
    /// it — paid in full by the runner's Mission Complete panel through
    /// <see cref="RecordMissionCompleted"/> on every completion (replaying
    /// is the intended money farm); the city's <see cref="RecordLevelCompleted"/>
    /// only records the level's rows for that panel.
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
        /// A city level completed: counts it, remembers it as the last level
        /// with every objective row, the accepted challenges (done or
        /// failed), the flat bonus and the rank table, and adds it to the
        /// stats ledger (nothing gates on it — the campaign gates on the
        /// mission records). It banks NO money — the mission is paid by the
        /// runner's Mission Complete panel (<see cref="RecordMissionCompleted"/>),
        /// which needs these rows. Saves at once — the caller's scene is
        /// about to be unloaded.
        /// </summary>
        public static void RecordLevelCompleted(string levelId, string levelName, string lastObjective,
                                                long baseReward, List<ObjectiveResult> objectives,
                                                List<ChallengeResult> challenges, RankTable rank)
        {
            var p = P;
            p.global.levelsCompleted++;
            var last = p.lastLevel;
            last.levelId = levelId ?? "";
            last.levelName = levelName ?? "";
            last.lastObjective = lastObjective ?? "";
            last.baseReward = baseReward;
            last.objectives = objectives != null ? new List<ObjectiveResult>(objectives) : new List<ObjectiveResult>();
            last.challenges = challenges != null ? new List<ChallengeResult>(challenges) : new List<ChallengeResult>();
            last.rank = rank != null ? rank.Clone() : new RankTable();
            last.optionalAccepted = last.challenges.Count;
            last.optionalCompleted = 0;
            foreach (var c in last.challenges) if (c.done) last.optionalCompleted++;
            last.moneyEarned = 0;
            last.missionTotal = 0;
            last.missionRank = "";
            last.banked = false;
            if (!string.IsNullOrEmpty(levelId) && !p.completedLevelIds.Contains(levelId))
                p.completedLevelIds.Add(levelId);
            PlayerProfileStore.MarkDirty();
            PlayerProfileStore.Save();
        }

        /// <summary>
        /// The mission paid, by the runner's Mission Complete panel: banks
        /// the COMPLETE <paramref name="total"/> into the wallet — first
        /// clear or fiftieth, a panel RETRY included; replaying a mission is
        /// the intended way to farm upgrades and money-gated unlocks — and
        /// stamps the total and rank on the last level for the LOG. With a
        /// <paramref name="missionId"/> (a campaign session) it also latches
        /// the mission complete in its <see cref="PlayerProfile.MissionRecord"/>,
        /// whose best total and rank never downgrade. Saves at once: the
        /// panel's answer may leave the scene.
        /// </summary>
        public static void RecordMissionCompleted(string missionId, long total, string rank)
        {
            var p = P;
            var last = p.lastLevel;
            string letter = rank ?? "";
            if (total > 0) p.global.moneyEarned += total;
            last.moneyEarned = total;
            last.missionTotal = total;
            last.missionRank = letter;
            last.banked = true;

            if (!string.IsNullOrEmpty(missionId))
            {
                PlayerProfile.MissionRecord record = FindMission(missionId);
                if (record == null)
                {
                    record = new PlayerProfile.MissionRecord { missionId = missionId };
                    p.missions.Add(record);
                }
                record.completed = true;
                record.timesCompleted++;
                if (total > record.bestTotal) record.bestTotal = total;
                if (RankValue(letter) > RankValue(record.bestRank)) record.bestRank = letter;
            }

            PlayerProfileStore.MarkDirty();
            PlayerProfileStore.Save();
        }

        // The rank letter's order (D < C < B < A < S); an unknown or empty letter sorts below D.
        static int RankValue(string letter) =>
            !string.IsNullOrEmpty(letter) && System.Enum.TryParse(letter, out Rank rank) ? (int)rank : -1;

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

        // -------------------------------------------------------------- store

        /// <summary>Highest level a store upgrade can reach.</summary>
        public const int MaxUpgradeLevel = 10;

        /// <summary>Spendable money: lifetime earnings minus what the store took. Never below zero.</summary>
        public static long Balance => System.Math.Max(0L, P.global.moneyEarned - P.global.moneySpent);

        /// <summary>
        /// Takes <paramref name="amount"/> out of the wallet if it is there;
        /// false (and nothing spent) otherwise. The caller saves — a purchase
        /// is a commit point, like a mission result.
        /// </summary>
        public static bool TrySpend(long amount)
        {
            if (amount <= 0 || amount > Balance) return false;
            P.global.moneySpent += amount;
            PlayerProfileStore.MarkDirty();
            return true;
        }

        /// <summary>Bought level (0 = stock) of a store category on a model.</summary>
        public static int UpgradeLevel(string modelId, string categoryId)
        {
            PlayerProfile.UpgradeEntry entry = FindUpgrade(modelId, categoryId);
            return entry != null ? Mathf.Clamp(entry.level, 0, MaxUpgradeLevel) : 0;
        }

        /// <summary>Writes a store level (clamped to 0..<see cref="MaxUpgradeLevel"/>), creating the entry the first time.</summary>
        public static void SetUpgradeLevel(string modelId, string categoryId, int level)
        {
            if (string.IsNullOrEmpty(modelId) || string.IsNullOrEmpty(categoryId)) return;
            PlayerProfile.UpgradeEntry entry = FindUpgrade(modelId, categoryId);
            if (entry == null)
            {
                entry = new PlayerProfile.UpgradeEntry { modelId = modelId, categoryId = categoryId };
                P.upgrades.Add(entry);
            }
            entry.level = Mathf.Clamp(level, 0, MaxUpgradeLevel);
            PlayerProfileStore.MarkDirty();
        }

        static PlayerProfile.UpgradeEntry FindUpgrade(string modelId, string categoryId)
        {
            List<PlayerProfile.UpgradeEntry> list = P.upgrades;
            for (int i = 0; i < list.Count; i++)
            {
                PlayerProfile.UpgradeEntry e = list[i];
                if (e != null && e.modelId == modelId && e.categoryId == categoryId) return e;
            }
            return null;
        }

        // -------------------------------------------------------- progression

        public static bool IsLevelCompleted(string levelId) =>
            !string.IsNullOrEmpty(levelId) && P.completedLevelIds.Contains(levelId);

        /// <summary>The campaign record of a mission id, or null when it was never completed.</summary>
        public static PlayerProfile.MissionRecord Mission(string missionId) => FindMission(missionId);

        /// <summary>True once the mission has been completed at least once — the campaign's progression gate.</summary>
        public static bool IsMissionCompleted(string missionId)
        {
            PlayerProfile.MissionRecord record = FindMission(missionId);
            return record != null && record.completed;
        }

        /// <summary>True when any campaign mission has been completed — what shows the MISSIONS row.</summary>
        public static bool AnyMissionCompleted
        {
            get
            {
                List<PlayerProfile.MissionRecord> list = P.missions;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].completed) return true;
                return false;
            }
        }

        static PlayerProfile.MissionRecord FindMission(string missionId)
        {
            if (string.IsNullOrEmpty(missionId)) return null;
            List<PlayerProfile.MissionRecord> list = P.missions;
            for (int i = 0; i < list.Count; i++)
            {
                PlayerProfile.MissionRecord m = list[i];
                if (m != null && m.missionId == missionId) return m;
            }
            return null;
        }

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
