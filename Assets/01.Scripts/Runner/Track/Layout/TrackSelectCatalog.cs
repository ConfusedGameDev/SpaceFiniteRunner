using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The courses the SELECT COURSE screen offers: one entry per saved
    /// track in <c>04.Data/FiniteRunner/Tracks/</c>, with what the track
    /// asset itself does not carry — a display name, a subtitle and a
    /// difficulty. A build cannot scan a folder, so this Resources asset IS
    /// the folder's runtime face: <c>Tools → FiniteRunner → Sync Track
    /// Catalog</c> (and an asset postprocessor on that folder) adds an entry
    /// for every new track and drops the ones whose track is gone, never
    /// touching the fields a designer edited. Names are authored data like a
    /// mission's displayName — never localized.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_TrackCatalog", menuName = "FiniteRunner/Track Select Catalog")]
    public class TrackSelectCatalog : ScriptableObject
    {
        /// <summary>Resources path of the asset.</summary>
        public const string ResourcePath = "FiniteRunner_TrackCatalog";

        [System.Serializable]
        public class Entry
        {
            [Tooltip("The saved track this course plays.")]
            [Required, AssetsOnly]
            public TrackLayoutAsset track;

            [Tooltip("The course's name on the SELECT COURSE screen. Empty = the asset's name.")]
            public string displayName = "";

            [Tooltip("A second line under the name (the F-Zero 'Twist Road' line). Empty = none.")]
            public string subtitle = "";

            [Tooltip("Difficulty pips shown under the map.")]
            [PropertyRange(1, 5)]
            public int difficulty = 1;

            [Tooltip("Optional picture for the bottom strip. Empty = a small map of the track is drawn instead.")]
            [PreviewField(64), AssetsOnly]
            public Sprite thumbnail;

            /// <summary>True when the entry names a track that has a saved layout.</summary>
            public bool IsValid => track != null && track.IsValid;

            /// <summary>The name the screen prints: the authored one, else the asset's.</summary>
            public string Label => !string.IsNullOrEmpty(displayName) ? displayName : track != null ? track.name : "";

            /// <summary>Inspector list label.</summary>
            public string EditorLabel => string.IsNullOrEmpty(Label) ? "(empty)" : $"{Label} · {new string('★', Mathf.Clamp(difficulty, 1, 5))}";
        }

        [Tooltip("Courses in the order the strip shows them. Synced from the Tracks folder; reorder and edit freely.")]
        [ListDrawerSettings(ShowIndexLabels = true, ShowFoldout = true, DraggableItems = true, ListElementLabelName = nameof(Entry.EditorLabel))]
        public List<Entry> entries = new();

        static TrackSelectCatalog cached;
        static bool warned;

        /// <summary>The index of the entry playing <paramref name="track"/>, or -1.</summary>
        public int IndexOf(TrackLayoutAsset track)
        {
            if (track == null) return -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].track == track) return i;
            return -1;
        }

        /// <summary>The asset from Resources, or null (warned once) when it has not been created yet.</summary>
        public static TrackSelectCatalog Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<TrackSelectCatalog>(ResourcePath);
            if (cached == null && !warned)
            {
                warned = true;
                Debug.LogWarning($"No {nameof(TrackSelectCatalog)} at Resources/{ResourcePath} — run Tools → FiniteRunner → Sync Track Catalog. The SELECT COURSE screen has nothing to list until then.");
            }
            return cached;
        }

        // Domain reload is off in this project: statics survive between plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cached = null;
            warned = false;
        }
    }
}
