using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.TimeTrial;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// A preview checkpoint gate's inspector (TimeTrialPRD D15): select a gate
    /// under TimeTrialCheckpoints and edit THAT checkpoint in the saved track —
    /// its distance and its five overrides — without opening Edit Track.
    /// Every change is undoable and goes into the track asset, which stays
    /// unsaved (Save here, Edit Track's Save, or File → Save Project) until
    /// you save it. An override only rewrites the gate's label; a move rebuilds
    /// the preview and selects the gate again at its new spot.
    /// </summary>
    [CustomEditor(typeof(CheckpointGate))]
    public class CheckpointGateEditor : Editor
    {
        TrackGenerator undoOwner;
        int undoIndex = -1;

        void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;
        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        public override void OnInspectorGUI()
        {
            var gate = (CheckpointGate)target;
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("A run's gate, drawn by the TimeTrialSystem. Checkpoints are edited on the saved track, in edit mode.", MessageType.None);
                return;
            }

            TrackGenerator generator = gate.Owner;
            TrackLayoutAsset asset = generator != null ? generator.LoadedTrack : null;
            int index = gate.RecordIndex;
            if (asset == null || index < 0 || index >= asset.Layout.placements.Count
                || asset.Layout.placements[index].kind != TrackPlacementKind.Checkpoint)
            {
                EditorGUILayout.HelpBox("Not linked to a saved track's checkpoint any more. Preview the saved track again (or Edit Track) and select the gate.", MessageType.Info);
                return;
            }
            undoOwner = generator;
            undoIndex = index;

            TrackLayout layout = asset.Layout;
            TrackPlacement p = layout.placements[index];
            TimeTrialDefinition trial = CheckpointPreviewGates.Trial;
            Ordinal(layout, index, out int ordinal, out int count, out int previous, out int next);

            EditorGUILayout.LabelField($"Checkpoint {ordinal} / {count}", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool unsaved = EditorUtility.IsDirty(asset);
                EditorGUILayout.LabelField($"on '{asset.name}'" + (unsaved ? "  ● unsaved" : ""));
                using (new EditorGUI.DisabledScope(!unsaved))
                    if (GUILayout.Button("Save Track", GUILayout.Width(90))) AssetDatabase.SaveAssetIfDirty(asset);
            }

            EditorGUI.BeginChangeCheck();
            CheckpointPreviewGates.DrawTrialField();
            if (EditorGUI.EndChangeCheck()) Rebuild(generator, asset, index); // the new look and defaults

            // A move rebuilds the preview: only on Enter / focus out, never per keystroke.
            float limit = layout.endZoneStart >= 0f ? layout.endZoneStart : layout.endDistance;
            float distance = EditorGUILayout.DelayedFloatField(new GUIContent("Distance (m)", "Where the checkpoint stands, metres from the start (before the final run-up)."), p.distance);
            if (!Mathf.Approximately(distance, p.distance))
            {
                Undo.RecordObject(asset, "Move Checkpoint");
                p.distance = Mathf.Clamp(distance, 1f, limit - 1f);
                layout.placements[index] = p;
                EditorUtility.SetDirty(asset);
                Rebuild(generator, asset, index);
                GUIUtility.ExitGUI();
            }

            EditorGUI.BeginChangeCheck();
            TrackPlacement edited = CheckpointPreviewGates.DrawOverrides(layout, p, trial);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Edit Checkpoint");
                layout.placements[index] = edited;
                EditorUtility.SetDirty(asset);
                CheckpointPreviewGates.Relabel(gate, layout, edited, trial);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(previous < 0))
                    if (GUILayout.Button("◀ Previous")) SelectGate(generator, previous, true);
                if (GUILayout.Button("Frame")) SceneView.lastActiveSceneView?.Frame(new Bounds(gate.transform.position, Vector3.one * 150f), false);
                using (new EditorGUI.DisabledScope(next < 0))
                    if (GUILayout.Button("Next ▶")) SelectGate(generator, next, true);
            }
            if (GUILayout.Button("Delete Checkpoint"))
            {
                Undo.RecordObject(asset, "Delete Checkpoint");
                layout.placements.RemoveAt(index);
                EditorUtility.SetDirty(asset);
                generator.ApplyEdit(asset, true);
                Selection.activeGameObject = generator.CheckpointParent != null ? generator.CheckpointParent.gameObject : null;
                GUIUtility.ExitGUI();
            }
        }

        // The checkpoint's place in road order, how many there are, and its neighbours' record indices.
        static void Ordinal(TrackLayout layout, int index, out int ordinal, out int count, out int previous, out int next)
        {
            float at = layout.placements[index].distance;
            ordinal = 1; count = 0; previous = next = -1;
            float before = float.NegativeInfinity, after = float.PositiveInfinity;
            for (int i = 0; i < layout.placements.Count; i++)
            {
                TrackPlacement p = layout.placements[i];
                if (p.kind != TrackPlacementKind.Checkpoint) continue;
                count++;
                if (i == index) continue;
                if (p.distance < at) { ordinal++; if (p.distance > before) { before = p.distance; previous = i; } }
                else if (p.distance < after) { after = p.distance; next = i; }
            }
        }

        // The preview rebuilt from the asset, then the gate showing `index` selected again.
        static void Rebuild(TrackGenerator generator, TrackLayoutAsset asset, int index)
        {
            generator.ApplyEdit(asset, true);
            EditorApplication.delayCall += () => SelectGate(generator, index, false);
        }

        static void SelectGate(TrackGenerator generator, int index, bool frame)
        {
            if (generator == null || generator.CheckpointParent == null) return;
            foreach (Transform child in generator.CheckpointParent)
                if (child.TryGetComponent(out CheckpointGate gate) && gate.Owner == generator && gate.RecordIndex == index)
                {
                    Selection.activeGameObject = gate.gameObject;
                    if (frame) SceneView.lastActiveSceneView?.Frame(new Bounds(gate.transform.position, Vector3.one * 150f), false);
                    return;
                }
        }

        // An undone edit made here: the preview follows the asset back.
        void OnUndoRedo()
        {
            if (Application.isPlaying || undoOwner == null || undoOwner.LoadedTrack == null) return;
            Rebuild(undoOwner, undoOwner.LoadedTrack, undoIndex);
        }
    }
}
