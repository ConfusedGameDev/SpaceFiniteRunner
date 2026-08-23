using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Disk-backed home of the pause menu's debug tweaks: track width,
    /// straightness, and per-entry spawn probability / boost multiplier.
    /// Every debug slider change is captured here and, in the editor, the
    /// asset is flushed to disk at commit points (reload scene, resume) — so
    /// the tuned values survive scene reloads, play-mode exits and editor
    /// restarts. While <see cref="applyOnLoad"/> is on, the values override
    /// the scene's authored Core Settings on every play-mode Generate; untick
    /// it on the asset to fall back to the scene's own values.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_TrackDebug", menuName = "FiniteRunner/Track Debug Settings")]
    public class TrackDebugSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_TrackDebug";

        [System.Serializable]
        public class EntryValues
        {
            public string name;
            public float probability;
            public float multiplier = 1f;
        }

        [Tooltip("When on, these saved values override the scene's Core Settings on every play-mode Generate. Turned on the first time the debug menu saves; untick to return to the scene's authored values.")]
        public bool applyOnLoad;
        public float trackWidth = 60f;
        public float straightness = 100f;
        public List<EntryValues> entries = new();

        static TrackDebugSettings cached;

        /// <summary>
        /// The asset, or a throwaway instance if none is in a Resources folder
        /// (the menu stays usable; tweaks just die with the session).
        /// </summary>
        public static TrackDebugSettings Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<TrackDebugSettings>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(TrackDebugSettings)} at Resources/{ResourcePath} — " +
                                 "debug tweaks will not survive this session.");
                cached = CreateInstance<TrackDebugSettings>();
            }
            return cached;
        }

        /// <summary>Snapshots the generator's current Core Settings and arms the override.</summary>
        public void CaptureFrom(TrackGenerator generator)
        {
            applyOnLoad = true;
            trackWidth = generator.TrackWidth;
            straightness = generator.Straightness;

            entries.Clear();
            var table = generator.SpawnTable;
            if (table != null)
                foreach (var e in table)
                    entries.Add(new EntryValues { name = e.name, probability = e.probability, multiplier = e.multiplier });

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>Writes the saved values onto the generator. Entries match by name, then by index.</summary>
        public void ApplyTo(TrackGenerator generator)
        {
            if (!applyOnLoad) return;
            generator.TrackWidth = trackWidth;
            generator.Straightness = straightness;

            var table = generator.SpawnTable;
            if (table == null) return;
            for (int i = 0; i < table.Length; i++)
            {
                var saved = entries.Find(e => e.name == table[i].name)
                            ?? (i < entries.Count ? entries[i] : null);
                if (saved == null) continue;
                table[i].probability = saved.probability;
                table[i].multiplier = saved.multiplier;
            }
        }

        /// <summary>
        /// Writes the asset to disk (editor only — builds keep changes for the
        /// app session). Called at commit points, not on every slider tick.
        /// </summary>
        public void Flush()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#endif
        }
    }
}
