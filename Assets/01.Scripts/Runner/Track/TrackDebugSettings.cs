using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Disk-backed home of the pause menu's debug tweaks: track width,
    /// straightness, per-spawner density, and the speed-orb tiers' spawn
    /// probability / boost multiplier.
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

        [System.Serializable]
        public class SpawnerValues
        {
            public string name;
            public float density = 1f;
        }

        [Tooltip("When on, these saved values override the scene's Core Settings on every play-mode Generate. Turned on the first time the debug menu saves; untick to return to the scene's authored values.")]
        public bool applyOnLoad;
        public float trackWidth = 60f;
        public float straightness = 100f;
        public List<EntryValues> entries = new();

        // The shape clone's elevation knobs (TrackShapeSettings), captured and
        // re-applied like straightness: the asset on disk is never touched.
        public bool elevationEnabled = true;
        public float elevationBand = 60f;
        public float maxGrade = 6f;
        public float maxGradeStepPerKnot = 3f;
        public float baselinePull = 0.5f;
        // Keep these defaults equal to the TrackShapeSettings asset's: the
        // shipped debug asset has applyOnLoad on, so a key it lacks applies
        // the default here on every play-mode Generate.
        public bool bankEnabled = true;
        public float maxBankAngle = 80f;
        public float bankPerDegreeOfTurn = 4f;
        public float maxBankStepPerKnot = 45f;
        public float levelLeadDistance = 200f;
        // -1 = never captured: leave the shape asset's value alone (the armed
        // debug asset predates these keys — a real default here would
        // silently override whatever the TrackShape asset says).
        public float unbankedSweepChance = -1f;
        public float openStraightChance = -1f;
        // Track length override, metres. -1 = none (never captured, or the row
        // set back to 0): the run keeps its level's / GameSettings' length.
        public float trackLength = -1f;
        // Per-spawner density multiplier (0 = none, 1 = the authored spacing),
        // matched by the spawner's display name. A spawner not listed keeps 1.
        public List<SpawnerValues> densities = new();

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

            var shape = generator.Shape;
            elevationEnabled = shape.elevationEnabled;
            elevationBand = shape.elevationBand;
            maxGrade = shape.maxGrade;
            maxGradeStepPerKnot = shape.maxGradeStepPerKnot;
            baselinePull = shape.baselinePull;
            bankEnabled = shape.bankEnabled;
            maxBankAngle = shape.maxBankAngle;
            bankPerDegreeOfTurn = shape.bankPerDegreeOfTurn;
            maxBankStepPerKnot = shape.maxBankStepPerKnot;
            levelLeadDistance = shape.levelLeadDistance;
            unbankedSweepChance = shape.unbankedSweepChance;
            openStraightChance = shape.openStraightChance;
            trackLength = generator.TrackLengthOverride;

            densities.Clear();
            foreach (var spawner in generator.Spawners)
                if (spawner != null)
                    densities.Add(new SpawnerValues { name = spawner.displayName, density = spawner.Density });

            entries.Clear();
            var tiers = generator.GetSpawner<SpeedOrbSpawner>()?.Tiers;
            if (tiers != null)
                foreach (var e in tiers)
                    entries.Add(new EntryValues { name = e.name, probability = e.probability, multiplier = e.multiplier });

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>Writes the saved values onto the generator's runtime spawners. Spawners and orb tiers match by name; a saved name with nothing to match (a retired brake entry) is ignored.</summary>
        public void ApplyTo(TrackGenerator generator)
        {
            if (!applyOnLoad) return;
            generator.TrackWidth = trackWidth;
            generator.Straightness = straightness;

            var shape = generator.Shape;
            shape.elevationEnabled = elevationEnabled;
            shape.elevationBand = elevationBand;
            shape.maxGrade = maxGrade;
            shape.maxGradeStepPerKnot = maxGradeStepPerKnot;
            shape.baselinePull = baselinePull;
            shape.bankEnabled = bankEnabled;
            shape.maxBankAngle = maxBankAngle;
            shape.bankPerDegreeOfTurn = bankPerDegreeOfTurn;
            shape.maxBankStepPerKnot = maxBankStepPerKnot;
            shape.levelLeadDistance = levelLeadDistance;
            if (unbankedSweepChance >= 0f) shape.unbankedSweepChance = unbankedSweepChance;
            if (openStraightChance >= 0f) shape.openStraightChance = openStraightChance;
            if (trackLength > 0f) generator.TrackLengthOverride = trackLength;

            foreach (var spawner in generator.Spawners)
            {
                if (spawner == null) continue;
                var saved = densities.Find(d => d.name == spawner.displayName);
                if (saved != null) spawner.Density = saved.density;
            }

            var tiers = generator.GetSpawner<SpeedOrbSpawner>()?.Tiers;
            if (tiers == null) return;
            foreach (var tier in tiers)
            {
                var saved = entries.Find(e => e.name == tier.name);
                if (saved == null) continue;
                tier.probability = saved.probability;
                tier.multiplier = saved.multiplier;
            }
            // A saved table from before an entry was retired no longer sums to 100.
            float[] noHistory = null;
            WeightedTable.Normalize(tiers, ref noHistory);
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
