using System;
using System.Collections.Generic;

namespace ConfusedGameDev.FiniteRunner.SaveData
{
    /// <summary>
    /// Everything the game remembers about the player between sessions: the
    /// lifetime statistics the LOG screen lists, the last level's summary,
    /// the finite-runner records, and the progression lists (completed
    /// levels, unlocked items) the level flow and a future end screen read.
    ///
    /// This is the JSON on disk, so it follows JsonUtility's rules: public
    /// fields only, nested classes marked <see cref="SerializableAttribute"/>,
    /// <c>List&lt;T&gt;</c> instead of dictionaries (the per-vehicle kill
    /// counts are a list of <see cref="CountEntry"/>). It knows nothing about
    /// either game — the per-vehicle keys are strings the recorder builds,
    /// so this assembly never sees a <c>VehicleKind</c>.
    ///
    /// <b>Adding a stat</b> is one field here (plus a <see cref="PlayerStats"/>
    /// call for whoever records it and one row line in the LOG builder).
    /// Bump <see cref="CurrentVersion"/> only when an existing field changes
    /// meaning — a new field simply loads as its default from an older file.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public const int CurrentVersion = 2; // 2: lastLevel carries the objective rows; money is banked by the runner's Mission Complete panel

        public int version = CurrentVersion;

        public GlobalStats global = new();
        public LastLevelStats lastLevel = new();
        public RunnerStats runner = new();

        /// <summary>Totaled cars per vehicle: key "Kind/Paint", label as the LOG prints it ("RED TRUCK").</summary>
        public List<CountEntry> totaledByVehicle = new();

        /// <summary>Collectibles picked up per id: key = the Collectible's id, label as the LOG prints it.</summary>
        public List<CountEntry> collectibles = new();

        /// <summary>Ids (LevelDefinition asset names) of every level completed at least once — the progression gate.</summary>
        public List<string> completedLevelIds = new();

        /// <summary>Ids handed to <see cref="PlayerStats.Unlock"/> — the future unlock system's ledger.</summary>
        public List<string> unlockedIds = new();

        /// <summary>Lifetime totals across both games.</summary>
        [Serializable]
        public class GlobalStats
        {
            public double playTimeSeconds;
            public int levelsCompleted;
            public int deaths;
            public int arrests;
            public float maxCarSpeedKmh;
            public float maxJumpMeters;
            public float maxAirTimeSeconds;
            public int totaledCars;
            public int totaledPoliceCars;
            public long moneyEarned;
            public int bonusObjectivesCompleted;
            public int collectiblesFound;
        }

        /// <summary>
        /// The most recently completed city level — and the hand-off to the
        /// runner's Mission Complete panel, which is the one place a mission
        /// (city level + escape run) is paid. The city writes the rows and the
        /// rank table at completion; the runner adds the run's own rows,
        /// totals everything, banks it and stamps <see cref="missionTotal"/> /
        /// <see cref="missionRank"/>. <see cref="banked"/> is what stops a
        /// retried run from paying the city's share twice.
        /// </summary>
        [Serializable]
        public class LastLevelStats
        {
            public string levelId = "";
            public string levelName = "";
            public string lastObjective = "";
            /// <summary>Money this mission has paid so far (the banked total; 0 until the runner finishes it).</summary>
            public long moneyEarned;
            public int optionalAccepted;
            public int optionalCompleted;
            /// <summary>The level's flat bonus, paid on top of the objective rewards.</summary>
            public long baseReward;
            /// <summary>The level's objectives, in order, with the money each paid.</summary>
            public List<ObjectiveResult> objectives = new();
            /// <summary>The challenges the player ACCEPTED, done or failed — never the declined ones.</summary>
            public List<ChallengeResult> challenges = new();
            /// <summary>The rank thresholds the level authored, applied by the runner to the mission total.</summary>
            public RankTable rank = new();
            /// <summary>The mission total the runner banked (0 until it did).</summary>
            public long missionTotal;
            /// <summary>The rank letter that total earned ("" until banked).</summary>
            public string missionRank = "";
            /// <summary>True once the runner has paid this mission at least once.</summary>
            public bool banked;
        }

        /// <summary>The finite runner's records.</summary>
        [Serializable]
        public class RunnerStats
        {
            public int escapesAttempted;
            public int escapesCompleted;
            public float maxSpeedKmh;
            /// <summary>Best launch-to-light-speed time in seconds; 0 = no escape yet.</summary>
            public float fastestEscapeSeconds;
            public int powerUpsCollected;
            public int slowDownsCollected;
        }

        /// <summary>One string-keyed counter (JsonUtility has no dictionaries).</summary>
        [Serializable]
        public class CountEntry
        {
            public string key;
            public string label;
            public int count;
        }

        /// <summary>The totaled-vehicle counter for <paramref name="key"/>, created on first use.</summary>
        public CountEntry Counter(string key, string label) => Counter(totaledByVehicle, key, label);

        /// <summary>The collectible counter for <paramref name="id"/>, created on first use.</summary>
        public CountEntry CollectibleCounter(string id, string label) => Counter(collectibles, id, label);

        static CountEntry Counter(List<CountEntry> list, string key, string label)
        {
            foreach (var entry in list)
                if (entry.key == key) return entry;
            var created = new CountEntry { key = key, label = label, count = 0 };
            list.Add(created);
            return created;
        }

        /// <summary>
        /// Fills in whatever an older or hand-edited file left out, so no
        /// consumer ever meets a null list or section.
        /// </summary>
        public void Sanitize()
        {
            global ??= new GlobalStats();
            lastLevel ??= new LastLevelStats();
            runner ??= new RunnerStats();
            totaledByVehicle ??= new List<CountEntry>();
            collectibles ??= new List<CountEntry>();
            completedLevelIds ??= new List<string>();
            unlockedIds ??= new List<string>();
            lastLevel.levelId ??= "";
            lastLevel.levelName ??= "";
            lastLevel.lastObjective ??= "";
            lastLevel.objectives ??= new List<ObjectiveResult>();
            lastLevel.challenges ??= new List<ChallengeResult>();
            lastLevel.rank ??= new RankTable();
            lastLevel.missionRank ??= "";
            lastLevel.objectives.RemoveAll(o => o == null);
            lastLevel.challenges.RemoveAll(c => c == null);
            totaledByVehicle.RemoveAll(e => e == null || string.IsNullOrEmpty(e.key));
            collectibles.RemoveAll(e => e == null || string.IsNullOrEmpty(e.key));
        }
    }
}
