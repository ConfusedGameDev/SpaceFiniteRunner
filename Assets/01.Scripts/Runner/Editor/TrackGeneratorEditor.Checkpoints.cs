using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.TimeTrial;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Arcade time trial checkpoints on a saved track (TimeTrialPRD D7, D15),
    /// in the Placements tab of Edit Track.
    /// <list type="bullet">
    /// <item><b>Lay Auto Checkpoints</b> writes the very layout a run would
    /// lay on its own (<see cref="TimeTrialCheckpoint.LayAuto"/>, read off the
    /// chosen <see cref="TimeTrialDefinition"/>) into the track as Checkpoint
    /// records, replacing any there — from then on they are authored: drag
    /// them, add, duplicate or delete like any placement.</item>
    /// <item>A selected checkpoint edits its <b>overrides</b>: a fixed time
    /// award, the clean bonus, the dirty penalty, and the patrol kill bonus and
    /// life penalty of the stretch leading up to it. Unticked, the trial's
    /// value applies (shown greyed).</item>
    /// <item>Every checkpoint also stands as a gate under the scene's
    /// TimeTrialCheckpoints object (<see cref="CheckpointPreviewGates"/>):
    /// selecting one there edits the same record (<see cref="CheckpointGateEditor"/>).</item>
    /// </list>
    /// </summary>
    public partial class TrackGeneratorEditor
    {
        // -------------------------------------------------------------- panel
        void DrawCheckpointTools(TrackGenerator generator, TrackLayout layout)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Time trial checkpoints (Arcade)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            CheckpointPreviewGates.DrawTrialField();
            if (EditorGUI.EndChangeCheck()) generator.ApplyEdit(working, true); // the gates take the new look and defaults; the track itself is unchanged
            if (generator.CheckpointParent == null)
                EditorGUILayout.HelpBox("No TimeTrialCheckpoints parent on this track, so no gates are previewed in the scene (the lines still draw). Run Tools → FiniteRunner → Install Time Trials to add it next to Pads.", MessageType.Info);
            string unused = CheckpointsUnusedReason();
            if (unused != null) EditorGUILayout.HelpBox(unused, MessageType.Warning);

            int count = 0;
            foreach (var p in layout.placements)
                if (p.kind == TrackPlacementKind.Checkpoint) count++;
            EditorGUILayout.LabelField(count == 0
                ? "None on this track: a run lays them automatically."
                : $"{count} on this track: a run uses these, as placed.", EditorStyles.miniLabel);

            // Each one, in road order, with Go (frames it in the Scene view).
            if (count > 0)
            {
                var order = new List<int>();
                for (int i = 0; i < layout.placements.Count; i++)
                    if (layout.placements[i].kind == TrackPlacementKind.Checkpoint) order.Add(i);
                order.Sort((a, b) => layout.placements[a].distance.CompareTo(layout.placements[b].distance));
                for (int n = 0; n < order.Count; n++)
                {
                    TrackPlacement p = layout.placements[order[n]];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string mark = p.variant != 0 ? "  (overrides)" : "";
                        EditorGUILayout.LabelField($"{n + 1}.  {p.distance / 1000f:0.000} km{mark}");
                        if (GUILayout.Button(order[n] == selectedPlacement ? "Selected" : "Go", GUILayout.Width(70)))
                            SelectPlacement(generator, layout, order[n]);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(count == 0 ? "Lay Auto Checkpoints" : "Lay Auto Checkpoints Again")
                    && (count == 0 || EditorUtility.DisplayDialog("Lay checkpoints again?",
                        $"Replace the {count} checkpoint(s) on this track — and their overrides — with a fresh auto layout? Undo brings them back.",
                        "Replace", "Cancel")))
                    LayAutoCheckpoints(generator, layout);
                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button("Clear Checkpoints", GUILayout.Width(130)))
                    {
                        Undo.RecordObject(working, "Clear Checkpoints");
                        RemoveCheckpoints(generator, layout);
                        Commit(generator, true);
                    }
            }
        }

        // Why Play will not use these checkpoints with the scene's level, or null when it will.
        string CheckpointsUnusedReason()
        {
            var game = Object.FindFirstObjectByType<GameManager>();
            RunnerLevelDefinition level = game != null ? game.Level : null;
            if (level == null || working == null) return null;
            if (level.track != working)
                return $"The scene's level '{level.name}' does not play this track, so Play will not use these checkpoints. Set as Current Track, or put the track on an Arcade level.";
            if (level.race != null)
                return $"'{level.name}' is a race level: a race wins over a time trial, so Play runs no checkpoints. Clear its Race to play the trial.";
            if (level.timeTrial == null)
                return $"'{level.name}' has no Time Trial, so Play runs no checkpoints. Set its Time Trial to an Arcade one.";
            if (!level.timeTrial.IsArcade)
                return $"'{level.name}' plays '{level.timeTrial.name}', a Beat the Clock trial — it has no checkpoints. Set its Time Trial to an Arcade one.";
            return null;
        }

        void LayAutoCheckpoints(TrackGenerator generator, TrackLayout layout)
        {
            TimeTrialDefinition trial = CheckpointPreviewGates.Trial;
            float limit = layout.endZoneStart >= 0f ? layout.endZoneStart : layout.endDistance;
            Undo.RecordObject(working, "Lay Auto Checkpoints");
            RemoveCheckpoints(generator, layout);
            var spots = new List<float>();
            TimeTrialCheckpoint.LayAuto(generator.Track, generator, layout.placements, trial, limit, spots);
            foreach (float spot in spots) AddPlacement(generator, layout, TimeTrialCheckpoint.At(spot).ToPlacement());
            Commit(generator, true);
            if (spots.Count == 0)
                EditorUtility.DisplayDialog("No checkpoints laid",
                    $"'{trial.name}' found no free spot between {trial.firstCheckpointMeters:0} m and the final run-up ({limit:0} m) clear of features, ramps and laser gates. Lower First Checkpoint Meters or Feature Clearance on the trial.", "OK");
            Debug.Log($"[TrackEditor] laid {spots.Count} checkpoint(s) on '{working.name}' from '{trial.name}'.", working);
        }

        void RemoveCheckpoints(TrackGenerator generator, TrackLayout layout)
        {
            for (int i = layout.placements.Count - 1; i >= 0; i--)
                if (layout.placements[i].kind == TrackPlacementKind.Checkpoint) RemovePlacement(generator, layout, i);
        }

        // ------------------------------------------------------ the selected one
        // Its overrides; returns the record as edited (the caller commits a change).
        TrackPlacement DrawCheckpointOverrides(TrackGenerator generator, TrackLayout layout, TrackPlacement p) =>
            CheckpointPreviewGates.DrawOverrides(layout, p, CheckpointPreviewGates.Trial);
    }
}
