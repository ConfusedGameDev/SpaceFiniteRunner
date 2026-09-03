using System;
using System.IO;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.SaveData
{
    /// <summary>
    /// The one place the <see cref="PlayerProfile"/> is read from and written
    /// to disk: <c>Application.persistentDataPath/profile.json</c>. JSON in
    /// the persistent folder rather than a ScriptableObject, because asset
    /// writes only exist in the editor — every <c>*DebugSettings.Flush</c> in
    /// this project is <c>#if UNITY_EDITOR</c> and a build keeps nothing.
    ///
    /// Loads lazily on first access (statics survive play sessions with
    /// domain reload off, so <see cref="Invalidate"/> drops the cache at the
    /// start of every play and after the editor deletes the file). Writes
    /// are atomic — the JSON lands in a <c>.tmp</c> next to the file and is
    /// swapped in, keeping the previous version as <c>.bak</c> — and a file
    /// that will not parse is quarantined as <c>profile.corrupt-*.json</c>
    /// rather than thrown away, with a fresh profile taking its place. No
    /// I/O failure ever reaches gameplay: everything is caught and logged.
    ///
    /// Gameplay never talks to this class directly for recording — that is
    /// <see cref="PlayerStats"/>; this owns the file, the dirty flag and the
    /// <see cref="Changed"/> event a screen can redraw on.
    /// </summary>
    public static class PlayerProfileStore
    {
        public const string FileName = "profile.json";

        static PlayerProfile profile;
        static bool dirty;

        /// <summary>Raised after any recorded change (and after a reset). Fires on the main thread.</summary>
        public static event Action Changed;

        /// <summary>Full path of the save file.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>The live profile, loaded on first access.</summary>
        public static PlayerProfile Profile
        {
            get
            {
                EnsureLoaded();
                return profile;
            }
        }

        /// <summary>True when something was recorded since the last <see cref="Save"/>.</summary>
        public static bool IsDirty => dirty;

        // Domain reload is disabled in this project: a static cache from the
        // previous play session (or from an inspector preview) would be
        // re-saved over whatever is on disk. Every play starts from the file.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            profile = null;
            dirty = false;
            Changed = null;
        }

        /// <summary>Drops the in-memory profile so the next access re-reads the file. Unsaved changes are lost — call <see cref="SaveIfDirty"/> first if they matter.</summary>
        public static void Invalidate()
        {
            profile = null;
            dirty = false;
        }

        /// <summary>Flags the profile as changed and tells listeners. Every <see cref="PlayerStats"/> mutator ends here.</summary>
        public static void MarkDirty()
        {
            EnsureLoaded();
            dirty = true;
            Changed?.Invoke();
        }

        /// <summary>Writes the profile if anything changed since the last write — the cheap call for commit points.</summary>
        public static void SaveIfDirty()
        {
            if (dirty) Save();
        }

        /// <summary>Writes the profile to disk now (atomic swap, previous file kept as .bak). Never throws.</summary>
        public static void Save()
        {
            EnsureLoaded();
            string path = FilePath;
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, JsonUtility.ToJson(profile, prettyPrint: true));
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, bak); }
                    catch (PlatformNotSupportedException) { File.Copy(tmp, path, overwrite: true); File.Delete(tmp); }
                }
                else
                {
                    File.Move(tmp, path);
                }
                dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveData] could not write {path}: {e.Message}");
            }
        }

        /// <summary>Starts a brand-new profile and writes it — the editor's Delete Save and any future in-game reset go through here.</summary>
        public static void ResetAll()
        {
            profile = new PlayerProfile();
            dirty = true;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Removes the save file and its .tmp/.bak siblings from disk and forgets the cached profile.</summary>
        public static void DeleteFile()
        {
            string path = FilePath;
            foreach (var candidate in new[] { path, path + ".tmp", path + ".bak" })
            {
                try { if (File.Exists(candidate)) File.Delete(candidate); }
                catch (Exception e) { Debug.LogWarning($"[SaveData] could not delete {candidate}: {e.Message}"); }
            }
            Invalidate();
        }

        static void EnsureLoaded()
        {
            if (profile != null) return;

            string path = FilePath;
            PlayerProfile loaded = null;
            if (File.Exists(path))
            {
                try
                {
                    loaded = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
                    if (loaded == null) throw new InvalidDataException("empty or not an object");
                }
                catch (Exception e)
                {
                    loaded = null;
                    Quarantine(path, e);
                }
            }

            profile = loaded ?? new PlayerProfile();
            profile.Sanitize();
            Migrate(profile);
            dirty = false;
        }

        // The save is the player's history: a file that will not parse is
        // set aside under a dated name (never deleted) and a fresh profile
        // starts — the next Save writes beside it, not over it.
        static void Quarantine(string path, Exception cause)
        {
            string target = Path.Combine(Path.GetDirectoryName(path),
                $"profile.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Move(path, target);
                Debug.LogWarning($"[SaveData] {FileName} could not be read ({cause.Message}) — moved to {Path.GetFileName(target)} and starting a fresh profile.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveData] {FileName} could not be read ({cause.Message}) and could not be moved aside ({e.Message}) — starting a fresh profile in memory.");
            }
        }

        // One case per past version that needs its fields reinterpreted; a
        // file that only lacks new fields needs nothing (they load as defaults).
        static void Migrate(PlayerProfile p)
        {
            if (p.version >= PlayerProfile.CurrentVersion) { p.version = PlayerProfile.CurrentVersion; return; }
            switch (p.version)
            {
                case 1:
                    // Version 1 banked a level's money at city completion; the
                    // runner's Mission Complete panel banks it now. A v1 last
                    // level was therefore already paid — mark it so a run
                    // finished on top of it never pays the city's share again.
                    p.lastLevel.banked = true;
                    p.lastLevel.missionTotal = p.lastLevel.moneyEarned;
                    break;
            }
            p.version = PlayerProfile.CurrentVersion;
            dirty = true;
        }
    }
}
