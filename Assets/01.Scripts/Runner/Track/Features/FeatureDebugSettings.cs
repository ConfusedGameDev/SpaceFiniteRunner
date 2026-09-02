using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// Disk-backed home of the pause menu's FEATURES debug tweaks: the
    /// generator's feature spacing band, each feature entry's probability /
    /// minimum spacing / boost multiplier, and the jump definition's knobs.
    /// Same contract as <see cref="TrackDebugSettings"/>: every slider change
    /// is captured here, the asset is flushed to disk at the menu's commit
    /// points, and while <see cref="applyOnLoad"/> is on the values are
    /// stamped onto the generator and its definition clones on every
    /// play-mode Generate — so a tuned ramp survives reloads, play-mode exits
    /// and editor restarts. Untick it on the asset to return to the authored
    /// values.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_FeatureDebug", menuName = "FiniteRunner/Feature Debug Settings")]
    public class FeatureDebugSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_FeatureDebug";

        [System.Serializable]
        public class EntryValues
        {
            public string name;
            public float probability;
            public float minSpacing;
            public float multiplier = 1f;
        }

        [System.Serializable]
        public class JumpValues
        {
            public float widthFraction = 0.25f;
            public float length = 60f;
            public float rampAngle = 20f;
            public float airDistancePerSpeed = 0.6f;
            public float maxAirDistance = 600f;
            public float airControlFactor = 0.5f;
            public float sideHitSpeedLoss = 0.15f;
        }

        [Tooltip("When on, these saved values override the scene's feature table and the jump definition on every play-mode Generate. Turned on the first time the debug menu saves; untick to return to the authored values.")]
        public bool applyOnLoad;
        public Vector2 featureSpacing = new(600f, 1200f);
        public List<EntryValues> entries = new();
        public JumpValues jump = new();

        static FeatureDebugSettings cached;

        /// <summary>The asset, or a throwaway instance if none is in a Resources folder (tweaks then die with the session).</summary>
        public static FeatureDebugSettings Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<FeatureDebugSettings>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(FeatureDebugSettings)} at Resources/{ResourcePath} — " +
                                 "feature debug tweaks will not survive this session.");
                cached = CreateInstance<FeatureDebugSettings>();
            }
            return cached;
        }

        /// <summary>Snapshots the generator's feature table and the live jump clone, and arms the override.</summary>
        public void CaptureFrom(TrackGenerator generator)
        {
            applyOnLoad = true;
            featureSpacing = generator.FeatureSpacing;

            entries.Clear();
            var table = generator.FeatureTable;
            if (table != null)
                foreach (var e in table)
                {
                    entries.Add(new EntryValues { name = e.name, probability = e.probability, minSpacing = e.minSpacing, multiplier = e.multiplier });
                    if (e.Runtime is JumpDefinition j) CaptureJump(j);
                }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        void CaptureJump(JumpDefinition j)
        {
            jump.widthFraction = j.widthFraction;
            jump.length = j.length;
            jump.rampAngle = j.rampAngle;
            jump.airDistancePerSpeed = j.airDistancePerSpeed;
            jump.maxAirDistance = j.airDistanceRange.y;
            jump.airControlFactor = j.airControlFactor;
            jump.sideHitSpeedLoss = j.sideHitSpeedLoss;
        }

        /// <summary>Writes the saved values onto the generator's table and definition CLONES. Entries match by name, then by index.</summary>
        public void ApplyTo(TrackGenerator generator)
        {
            if (!applyOnLoad) return;
            generator.FeatureSpacing = featureSpacing;

            var table = generator.FeatureTable;
            if (table == null) return;
            for (int i = 0; i < table.Length; i++)
            {
                var saved = entries.Find(e => e.name == table[i].name)
                            ?? (i < entries.Count ? entries[i] : null);
                if (saved != null)
                {
                    table[i].probability = saved.probability;
                    table[i].minSpacing = saved.minSpacing;
                    table[i].multiplier = saved.multiplier;
                }
                if (table[i].Runtime is JumpDefinition j) ApplyJump(j);
            }
        }

        void ApplyJump(JumpDefinition j)
        {
            j.widthFraction = jump.widthFraction;
            j.length = jump.length;
            j.rampAngle = jump.rampAngle;
            j.airDistancePerSpeed = jump.airDistancePerSpeed;
            j.airDistanceRange = new Vector2(Mathf.Min(j.airDistanceRange.x, jump.maxAirDistance), jump.maxAirDistance);
            j.airControlFactor = jump.airControlFactor;
            j.sideHitSpeedLoss = jump.sideHitSpeedLoss;
        }

        /// <summary>Writes the asset to disk (editor only). Called at commit points, not on every slider tick.</summary>
        public void Flush()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#endif
        }
    }
}
