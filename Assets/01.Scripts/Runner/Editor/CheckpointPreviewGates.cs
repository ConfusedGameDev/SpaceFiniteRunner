using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.TimeTrial;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// The edit-mode picture of a saved track's time trial checkpoints
    /// (TimeTrialPRD D15). Registered as <see cref="TrackGenerator.CheckpointPreview"/>,
    /// so the generator builds a <see cref="CheckpointGate"/> for every
    /// Checkpoint record exactly as it builds a pad — under its
    /// TimeTrialCheckpoints parent, rebuilt with every edit, never saved into
    /// the scene — each one linked to its record, so selecting a gate edits
    /// that checkpoint (<see cref="CheckpointGateEditor"/>).
    ///
    /// Also the one home of what both checkpoint editors share: the trial whose
    /// look and defaults the preview uses (picked once, remembered), the
    /// floating label's preview text, and the five override rows.
    /// </summary>
    [InitializeOnLoad]
    public static class CheckpointPreviewGates
    {
        const string TrialPref = "FiniteRunner.TrackEditor.CheckpointTrial";

        static TimeTrialDefinition picked;
        static TimeTrialDefinition fallback; // the trial's code defaults, when the project has none

        static CheckpointPreviewGates() => TrackGenerator.CheckpointPreview = Build;

        /// <summary>The picked trial asset (remembered across sessions); null = none picked.</summary>
        public static TimeTrialDefinition Picked
        {
            get
            {
                if (picked == null)
                {
                    string path = EditorPrefs.GetString(TrialPref, "");
                    if (!string.IsNullOrEmpty(path)) picked = AssetDatabase.LoadAssetAtPath<TimeTrialDefinition>(path);
                }
                return picked;
            }
            set
            {
                picked = value;
                EditorPrefs.SetString(TrialPref, value != null ? AssetDatabase.GetAssetPath(value) : "");
            }
        }

        /// <summary>The trial the previews and Lay Auto Checkpoints read: the picked one, else the first Arcade asset, else the code defaults.</summary>
        public static TimeTrialDefinition Trial
        {
            get
            {
                if (Picked != null) return picked;
                foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(TimeTrialDefinition)))
                {
                    var found = AssetDatabase.LoadAssetAtPath<TimeTrialDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (found != null && found.IsArcade) { picked = found; return found; }
                }
                if (fallback == null)
                {
                    fallback = ScriptableObject.CreateInstance<TimeTrialDefinition>();
                    fallback.hideFlags = HideFlags.HideAndDontSave;
                    fallback.mode = TimeTrialMode.Arcade;
                }
                return fallback;
            }
        }

        /// <summary>The trial picker both editors draw.</summary>
        public static void DrawTrialField()
        {
            TimeTrialDefinition shown = Trial;
            EditorGUI.BeginChangeCheck();
            var chosen = (TimeTrialDefinition)EditorGUILayout.ObjectField(
                new GUIContent("Trial", "The time trial whose gate look and defaults the preview shows, and whose auto count, first distance and clearance Lay Auto Checkpoints reads."),
                shown == fallback ? null : shown, typeof(TimeTrialDefinition), false);
            if (EditorGUI.EndChangeCheck()) Picked = chosen;
        }

        // --------------------------------------------------------------- build
        static GameObject Build(TrackGenerator generator, TrackPlacement p)
        {
            TimeTrialDefinition trial = Trial;
            var gate = CheckpointGate.Create(generator.CheckpointParent, $"Checkpoint_{p.distance:00000}", trial.gatePrefab);
            gate.Place(generator.Track, p.distance, trial.gateHeight, trial.gateThickness, trial.labelGap, trial.labelSize);
            gate.SetColor(trial.gatePendingColor);
            TrackLayoutAsset asset = generator.LoadedTrack;
            gate.BindRecord(generator, asset != null ? IndexOf(asset.Layout, p.distance) : -1);
            Relabel(gate, asset != null ? asset.Layout : null, p, trial);
            return gate.gameObject;
        }

        static int IndexOf(TrackLayout layout, float distance)
        {
            for (int i = 0; i < layout.placements.Count; i++)
                if (layout.placements[i].kind == TrackPlacementKind.Checkpoint && Mathf.Abs(layout.placements[i].distance - distance) < 0.01f)
                    return i;
            return -1;
        }

        /// <summary>
        /// The preview's floating text: the award (fixed, or "MAX" — the most a
        /// pace-sized one pays, at the trial's pace floor), the clean bonus and
        /// dirty penalty, and any kill / life override of the stretch to it.
        /// </summary>
        public static void Relabel(CheckpointGate gate, TrackLayout layout, TrackPlacement p, TimeTrialDefinition trial)
        {
            if (!trial.showGateLabels) { gate.SetLabel(null); return; }
            TimeTrialCheckpoint cp = TimeTrialCheckpoint.FromPlacement(p);
            bool fixedAward = cp.Has(TimeTrialCheckpoint.Overrides.Award);
            float award = fixedAward ? cp.award : layout != null ? PaceFloorAward(layout, trial, p.distance) : 0f;
            string awardText = CheckpointGate.Seconds(award) + (fixedAward ? "" : " MAX");

            string extra = $"CLEAN {CheckpointGate.Seconds(cp.CleanBonusOr(trial))}";
            float dirty = cp.DirtyPenaltyOr(trial);
            if (dirty > 0f) extra += $"   DIRTY {CheckpointGate.Seconds(-dirty)}";
            if (cp.Has(TimeTrialCheckpoint.Overrides.KillBonus) || cp.Has(TimeTrialCheckpoint.Overrides.LifePenalty))
                extra += $"\nKILL {CheckpointGate.Seconds(cp.KillBonusOr(trial))}   LIFE {CheckpointGate.Seconds(-cp.LifePenaltyOr(trial))}";
            gate.SetLabel(CheckpointGate.Compose(awardText, trial.labelAwardColor, extra, trial.labelCleanColor));
        }

        /// <summary>The award a checkpoint at <paramref name="distance"/> pays at the trial's pace floor — the most a pace-sized one can — to the next checkpoint or the end.</summary>
        public static float PaceFloorAward(TrackLayout layout, TimeTrialDefinition trial, float distance)
        {
            float next = layout.endDistance;
            foreach (var p in layout.placements)
                if (p.kind == TrackPlacementKind.Checkpoint && p.distance > distance + 0.01f && p.distance < next) next = p.distance;
            float pace = Mathf.Max(trial.paceFloorKmh / 3.6f, 1f);
            return Mathf.Round(Mathf.Clamp((next - distance) / pace + trial.graceSeconds, trial.MinAward, trial.MaxAward) * trial.awardScale * 10f) / 10f;
        }

        // ---------------------------------------------------------- overrides
        /// <summary>The five override rows for a checkpoint record; returns the record as edited.</summary>
        public static TrackPlacement DrawOverrides(TrackLayout layout, TrackPlacement p, TimeTrialDefinition trial)
        {
            TimeTrialCheckpoint cp = TimeTrialCheckpoint.FromPlacement(p);

            EditorGUILayout.LabelField("Overrides (unticked = the trial's value)", EditorStyles.miniBoldLabel);
            cp.award = OverrideRow("Time award (s)", "A fixed award in place of the pace-sized one (shown: the most the pace-sized one pays here).",
                ref cp.set, TimeTrialCheckpoint.Overrides.Award, cp.award, PaceFloorAward(layout, trial, p.distance), 120f);
            cp.cleanBonus = OverrideRow("Clean bonus (s)", "Added on crossing with no hit since the last checkpoint.",
                ref cp.set, TimeTrialCheckpoint.Overrides.CleanBonus, cp.cleanBonus, trial.cleanCheckpointBonus, 20f);
            cp.dirtyPenalty = OverrideRow("Dirty penalty (s)", "Taken away on crossing after a hit since the last checkpoint.",
                ref cp.set, TimeTrialCheckpoint.Overrides.DirtyPenalty, cp.dirtyPenalty, trial.dirtyCheckpointPenalty, 20f);
            cp.killBonus = OverrideRow("Patrol kill bonus (s)", "Added for a patrol kill on the stretch leading up to this checkpoint.",
                ref cp.set, TimeTrialCheckpoint.Overrides.KillBonus, cp.killBonus, trial.patrolKillBonus, 30f);
            cp.lifePenalty = OverrideRow("Life penalty (s)", "Taken away for a life lost on the stretch leading up to this checkpoint.",
                ref cp.set, TimeTrialCheckpoint.Overrides.LifePenalty, cp.lifePenalty, trial.lifePenaltySeconds, 30f);

            cp.distance = p.distance;
            return cp.ToPlacement();
        }

        // One override: a tick, then its value — the trial's, greyed, while unticked.
        static float OverrideRow(string label, string tooltip, ref TimeTrialCheckpoint.Overrides set,
                                 TimeTrialCheckpoint.Overrides flag, float value, float fallbackValue, float max)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool was = (set & flag) != 0;
                bool on = EditorGUILayout.ToggleLeft(new GUIContent(label, tooltip), was, GUILayout.Width(EditorGUIUtility.labelWidth));
                if (on && !was) value = fallbackValue; // start from what applied until now
                set = on ? set | flag : set & ~flag;
                using (new EditorGUI.DisabledScope(!on))
                {
                    float shown = EditorGUILayout.Slider(on ? value : fallbackValue, 0f, Mathf.Max(max, on ? value : fallbackValue));
                    if (on) value = shown;
                }
            }
            return value;
        }
    }
}
