using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// The player's city — one persisted seed, stored in PlayerPrefs so the
    /// same streets come back on every launch, in the editor and in a build
    /// alike.
    ///
    /// The design rule this enforces: <b>generation is deterministic, so the
    /// seed is the entire save file</b>. Roads, features and buildings are all
    /// a pure function of (seed, coordinates) via
    /// <see cref="DeterministicHash"/>, so nothing about the layout ever needs
    /// writing to disk — persist the seed and the city rebuilds itself
    /// identically. A marker or a route saved against a city that reshuffled
    /// on the next Play would point at a road that no longer exists, which is
    /// why the seed is pinned here rather than rolled in
    /// <see cref="CityManager.Awake"/>.
    ///
    /// Deliberately NOT on <see cref="CityGenerationSettings"/>: that asset is
    /// design data shipped with the build and shared by the whole project,
    /// while this belongs to whoever is playing. Same split, and the same
    /// PlayerPrefs write-through shape, as FiniteRunner's UserSettings.
    ///
    /// Only an explicit "new city" action ever changes the stored seed —
    /// see <see cref="RollNewSeed"/>.
    /// </summary>
    public static class CitySaveData
    {
        const string SeedKey = "city.seed";
        const string HasSeedKey = "city.seed.set";

        static int seed;
        static bool hasSeed;
        static bool loaded;

        /// <summary>True once a city has been generated and pinned at least once.</summary>
        public static bool HasSeed
        {
            get { EnsureLoaded(); return hasSeed; }
        }

        /// <summary>The pinned city seed. Setting it writes through to disk immediately.</summary>
        public static int Seed
        {
            get { EnsureLoaded(); return seed; }
            set
            {
                EnsureLoaded();
                seed = value;
                hasSeed = true;
                PlayerPrefs.SetInt(SeedKey, seed);
                PlayerPrefs.SetInt(HasSeedKey, 1);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Roll a brand-new city and pin it. The one call that is allowed to
        /// change the player's city — everything else reads <see cref="Seed"/>.
        /// </summary>
        public static int RollNewSeed()
        {
            Seed = Random.Range(int.MinValue / 2, int.MaxValue / 2);
            return seed;
        }

        /// <summary>Forget the pinned city, so the next launch rolls a fresh one.</summary>
        public static void ClearSeed()
        {
            EnsureLoaded();
            seed = 0;
            hasSeed = false;
            PlayerPrefs.DeleteKey(SeedKey);
            PlayerPrefs.DeleteKey(HasSeedKey);
            PlayerPrefs.Save();
        }

        // Domain reload is disabled in this project, so these statics outlive a
        // play session. That is harmless here — the cache only ever mirrors what
        // PlayerPrefs already holds — but it does mean the load must be lazy
        // rather than done in a static constructor tied to a session.
        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            hasSeed = PlayerPrefs.GetInt(HasSeedKey, 0) != 0;
            seed = PlayerPrefs.GetInt(SeedKey, 0);
        }
    }
}
