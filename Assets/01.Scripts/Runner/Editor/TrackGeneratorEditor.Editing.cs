using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Road editing on a saved track (TrackAuthoringPRD M4), in the Scene
    /// view. Edits go straight into the <see cref="TrackLayoutAsset"/> (with
    /// Undo) and the generator re-derives what they moved
    /// (<see cref="TrackGenerator.ApplyEdit"/>): knot distances, length, the
    /// run-up, the end ramps. Every other placement keeps its distance, so it
    /// slides with the road.
    /// <list type="bullet">
    /// <item><b>Knots</b>: click one to select it; its arrows move it (up and
    /// down is the grade), its disc banks it. Neighbours keep their own bank.
    /// A knot a feature stands on (an explicit-tangent spot) keeps bank 0.</item>
    /// <item><b>Locked</b>: the first knot and the final run-up (D9: it stays a
    /// straight, walled, level run to the ramps), and every knot on a track
    /// with loops or tubes — moving road before a section would shift it.</item>
    /// <item><b>Spans</b>: the dots at the ends of flat sweeps (orange) and
    /// open straights (red) slide along the road; the inspector adds and
    /// removes them at the selected knot.</item>
    /// </list>
    /// While a handle is dragged the road is reloaded and only the stretch it
    /// reshapes is rebuilt, live (<see cref="TrackGenerator.ApplyEditLive(TrackLayoutAsset, int)"/>);
    /// the whole preview is rebuilt on release.
    /// <b>Saving is explicit</b>: Edit Track saves anything pending and
    /// snapshots the track; nothing is written while editing; Save writes,
    /// Revert goes back to the snapshot, and Stop Editing with unsaved changes
    /// asks Save / Discard / Keep Editing. Every change is undoable (Discard
    /// too), and an undo redraws the track on show even after editing stops.
    /// </summary>
    public partial class TrackGeneratorEditor
    {
        static readonly Color KnotColor = new(1f, 1f, 1f, 0.9f);
        static readonly Color SpotKnotColor = new(0.3f, 1f, 1f, 0.9f);
        static readonly Color LockedKnotColor = new(0.5f, 0.5f, 0.5f, 0.6f);
        static readonly Color SelectedColor = new(1f, 0.9f, 0.1f, 1f);
        static readonly Color FlatSpanColor = new(1f, 0.55f, 0.05f, 1f);
        static readonly Color OpenSpanColor = new(1f, 0.15f, 0.15f, 1f);

        bool editingTrack;
        bool undoHooked;
        string snapshot; // the track as it was when editing began (Discard goes back to it)
        int selectedKnot = -1;
        bool rebuildPending; // the road changed during a drag: build the preview on release

        // ---------------------------------------------------------------- panel
        void DrawEditPanel(TrackGenerator generator)
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField("Edit Track", EditorStyles.boldLabel);
            bool canEdit = working != null && working.IsValid;
            using (new EditorGUI.DisabledScope(!canEdit))
            {
                bool on = GUILayout.Toggle(editingTrack, editingTrack ? "Stop Editing" : "Edit Track", "Button", GUILayout.Height(26));
                if (on != editingTrack) SetEditing(generator, on);
            }
            if (!undoHooked)
            {
                // For the inspector's whole life, not just while editing: an undo after
                // Stop Editing still has to redraw the track on show.
                Undo.undoRedoPerformed += OnUndoRedo;
                undoHooked = true;
            }
            if (!editingTrack || !canEdit) return;

            bool unsaved = EditorUtility.IsDirty(working);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(unsaved ? "● Unsaved changes" : "Saved", unsaved ? EditorStyles.boldLabel : EditorStyles.label);
                using (new EditorGUI.DisabledScope(!unsaved))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(70))) SaveEdits();
                    if (GUILayout.Button("Revert", GUILayout.Width(70))) DiscardEdits(generator);
                }
            }

            TrackLayout layout = working.Layout;
            if (layout.sections.Count > 0)
                EditorGUILayout.HelpBox("This track has loops or tubes: knot editing is off (moving road before a section would shift it). Spans can still be edited.", MessageType.Warning);
            EditorGUILayout.HelpBox("Click a knot to select it. Arrows move it (up/down = grade), the disc banks it. Drag the dots at the ends of flat sweeps (orange) and open straights (red) along the road. Grey knots are locked (the start and the final run-up). Ctrl/Cmd+Z undoes.", MessageType.None);

            if (selectedKnot < 0 || selectedKnot >= layout.knots.Count) return;
            DrawSelectedKnot(generator, layout);
        }

        void SetEditing(TrackGenerator generator, bool on)
        {
            if (!on && !ConfirmStopEditing(generator)) return; // Keep Editing

            editingTrack = on;
            selectedKnot = -1;
            SceneView.duringSceneGui -= OnSceneEditing;
            if (on)
            {
                // Start from what is on disk: anything pending is saved first, then remembered.
                AssetDatabase.SaveAssetIfDirty(working);
                snapshot = JsonUtility.ToJson(working.Layout);
                generator.PreviewSavedTrack(working);
                SceneView.duringSceneGui += OnSceneEditing;
                status = $"Editing '{working.name}'. Nothing is saved until you press Save (or answer Save on Stop Editing).";
            }
            else snapshot = null;
            SceneView.RepaintAll();
        }

        // Stop Editing with unsaved changes: Save, Discard (back to how it was when
        // editing began), or Keep Editing (false).
        bool ConfirmStopEditing(TrackGenerator generator)
        {
            if (working == null || !EditorUtility.IsDirty(working)) return true;
            int choice = EditorUtility.DisplayDialogComplex("Save track changes?",
                $"'{working.name}' has unsaved changes.", "Save", "Keep Editing", "Discard");
            switch (choice)
            {
                case 0: SaveEdits(); return true;
                case 2: DiscardEdits(generator); return true;
                default: return false;
            }
        }

        void SaveEdits()
        {
            AssetDatabase.SaveAssetIfDirty(working);
            snapshot = JsonUtility.ToJson(working.Layout);
            status = $"Saved '{working.name}'.";
        }

        // Back to the snapshot — undoable, like every edit — and written back, so
        // the asset on disk and in memory agree again.
        void DiscardEdits(TrackGenerator generator)
        {
            if (snapshot == null) return;
            Undo.RecordObject(working, "Discard Track Changes");
            working.SetLayout(JsonUtility.FromJson<TrackLayout>(snapshot), working.shape, working.spawnSet);
            EditorUtility.SetDirty(working);
            AssetDatabase.SaveAssetIfDirty(working);
            selectedKnot = -1;
            generator.ApplyEdit(working, true);
            status = $"Discarded the changes to '{working.name}'.";
            SceneView.RepaintAll();
        }

        // (No OnDisable here: it would hide OdinEditor's. Both handlers let go
        // of themselves once this inspector is gone.)
        void OnUndoRedo()
        {
            if (this == null || target == null)
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
                return;
            }
            var generator = (TrackGenerator)target;
            // Redraw whenever the track on show is the one being undone (editing or not).
            if (working == null || generator.LoadedTrack != working) return;
            if (selectedKnot >= working.Layout.knots.Count) selectedKnot = working.Layout.knots.Count - 1; // an undone insert
            generator.ApplyEdit(working, true);
            SceneView.RepaintAll();
            Repaint();
        }

        void DrawSelectedKnot(TrackGenerator generator, TrackLayout layout)
        {
            int i = selectedKnot;
            TrackLayout.Knot knot = layout.knots[i];
            bool locked = KnotLocked(layout, i, out string why);
            Transform container = generator.Track.Spline.transform;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Knot {i} / {layout.knots.Count - 1}", $"{layout.knotDistances[i] / 1000f:0.000} km{(locked ? $"  (locked: {why})" : "")}");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ Previous")) SelectKnot(generator, layout, Mathf.Max(0, i - 1));
                if (GUILayout.Button("Frame")) FrameKnot(generator, layout, i);
                if (GUILayout.Button("Next ▶")) SelectKnot(generator, layout, Mathf.Min(layout.knots.Count - 1, i + 1));
            }

            using (new EditorGUI.DisabledScope(locked))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 world = EditorGUILayout.Vector3Field("Position", container.TransformPoint(knot.position));
                float bank = knot.mode == TangentMode.Continuous
                    ? 0f
                    : EditorGUILayout.Slider("Bank (°)", BankOf(knot.rotation), -89f, 89f);
                if (knot.mode == TangentMode.Continuous) EditorGUILayout.LabelField("Bank", "0 — a feature stands on this knot");
                // What the road actually leans there: the spline re-leans a knot to its curve, so a
                // sharply bent knot can read a few degrees off the bank asked for.
                EditorGUILayout.LabelField("Road bank (°)", generator.Track.GetBankAtDistance(layout.knotDistances[i]).ToString("0.0"));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(working, "Edit Track Knot");
                    MoveKnot(layout, i, container.InverseTransformPoint(world), bank);
                    Commit(generator, true);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nextLocked = i + 1 >= layout.knots.Count || KnotLocked(layout, i + 1, out _);
                    using (new EditorGUI.DisabledScope(nextLocked))
                        if (GUILayout.Button("Insert Knot After")) InsertKnotAfter(generator, layout, i);
                    using (new EditorGUI.DisabledScope(knot.mode == TangentMode.Continuous))
                        if (GUILayout.Button("Delete Knot")) DeleteKnot(generator, layout, i);
                }
            }

            // Spans at this knot's segment (i → i+1).
            if (i + 1 < layout.knots.Count)
            {
                float from = layout.knotDistances[i], to = layout.knotDistances[i + 1];
                EditorGUILayout.LabelField("Segment", $"{from:0} – {to:0} m");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Straight")) AddSpan(generator, layout.openStretches, from, to, 0, "Add Open Straight");
                    if (GUILayout.Button("Flat Sweep ◀ out")) AddSpan(generator, layout.flatSweeps, from, to, -1, "Add Flat Sweep");
                    if (GUILayout.Button("Flat Sweep out ▶")) AddSpan(generator, layout.flatSweeps, from, to, 1, "Add Flat Sweep");
                }
                if (GUILayout.Button("Remove Spans Here")) RemoveSpans(generator, layout, from, to);
            }
        }

        // --------------------------------------------------------------- scene
        void OnSceneEditing(SceneView view)
        {
            if (this == null || target == null || !editingTrack || working == null || !working.IsValid)
            {
                SceneView.duringSceneGui -= OnSceneEditing;
                return;
            }
            var generator = (TrackGenerator)target;
            TrackManager track = generator.Track;
            if (track == null || track.Spline == null || view.camera == null) return;

            TrackLayout layout = working.Layout;
            Transform container = track.Spline.transform;
            Vector3 eye = view.camera.transform.position;
            float radiusSqr = Mathf.Pow(Mathf.Max(200f, TrackGizmoSettings.DetailRadius), 2f);

            // Knots: a button each; the selected one gets its handles.
            for (int i = 0; i < layout.knots.Count; i++)
            {
                Vector3 world = container.TransformPoint(layout.knots[i].position);
                if ((world - eye).sqrMagnitude > radiusSqr && i != selectedKnot) continue;
                bool locked = KnotLocked(layout, i, out _);
                float size = HandleUtility.GetHandleSize(world) * 0.15f;
                Handles.color = i == selectedKnot ? SelectedColor
                    : locked ? LockedKnotColor
                    : layout.knots[i].mode == TangentMode.Continuous ? SpotKnotColor
                    : KnotColor;
                if (Handles.Button(world, Quaternion.identity, size, size * 1.2f, Handles.SphereHandleCap))
                {
                    selectedKnot = i;
                    Repaint();
                }
            }
            if (selectedKnot >= 0 && selectedKnot < layout.knots.Count && !KnotLocked(layout, selectedKnot, out _))
                KnotHandles(generator, layout, container, selectedKnot);

            SpanHandles(generator, track, layout.flatSweeps, FlatSpanColor, "Move Flat Sweep", eye, radiusSqr);
            SpanHandles(generator, track, layout.openStretches, OpenSpanColor, "Move Open Straight", eye, radiusSqr);

            // Released: build the preview once, and save.
            if (rebuildPending && GUIUtility.hotControl == 0)
            {
                rebuildPending = false;
                Commit(generator, true);
            }
        }

        void KnotHandles(TrackGenerator generator, TrackLayout layout, Transform container, int i)
        {
            TrackLayout.Knot knot = layout.knots[i];
            Vector3 world = container.TransformPoint(knot.position);
            Quaternion rotation = container.rotation * knot.rotation;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Tools.pivotRotation == PivotRotation.Local ? rotation : Quaternion.identity);
            float bank = BankOf(knot.rotation);
            if (knot.mode != TangentMode.Continuous)
            {
                Handles.color = SelectedColor;
                Vector3 forward = rotation * Vector3.forward;
                Quaternion turned = Handles.Disc(rotation, world, forward, HandleUtility.GetHandleSize(world) * 1.6f, false, 1f);
                bank = BankOf(Quaternion.Inverse(container.rotation) * turned);
            }
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(working, "Edit Track Knot");
            MoveKnot(layout, i, container.InverseTransformPoint(moved), bank);
            // Live: the road and what stands on the reshaped stretch follow the drag.
            EditorUtility.SetDirty(working);
            generator.ApplyEditLive(working, i);
            SceneView.RepaintAll();
            rebuildPending = true;
        }

        void SpanHandles(TrackGenerator generator, TrackManager track, System.Collections.Generic.List<TrackLayout.Span> spans, Color color, string undoName, Vector3 eye, float radiusSqr)
        {
            for (int s = 0; s < spans.Count; s++)
            {
                TrackLayout.Span span = spans[s];
                for (int end = 0; end < 2; end++)
                {
                    float distance = end == 0 ? span.start : span.end;
                    track.GetFrameAtDistance(distance, out Vector3 centre, out _, out Vector3 up, out Vector3 right);
                    int side = span.outerSide != 0 ? span.outerSide : 1;
                    Vector3 dot = centre + right * (track.RoadHalfWidth * side) + up * 4f;
                    if ((dot - eye).sqrMagnitude > radiusSqr) continue;

                    Handles.color = color;
                    float size = HandleUtility.GetHandleSize(dot) * 0.12f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(dot, size, Vector3.zero, Handles.DotHandleCap);
                    if (!EditorGUI.EndChangeCheck()) continue;

                    float d = ProjectDistance(track, moved, distance);
                    Undo.RecordObject(working, undoName);
                    if (end == 0) span.start = Mathf.Min(d, span.end - 10f);
                    else span.end = Mathf.Max(d, span.start + 10f);
                    spans[s] = span;
                    // Live: re-stamp the walls between where the end was and where it is now.
                    EditorUtility.SetDirty(working);
                    float changed = end == 0 ? span.start : span.end;
                    generator.ApplyEditLive(working, Mathf.Min(distance, changed) - 40f, Mathf.Max(distance, changed) + 40f);
                    SceneView.RepaintAll();
                    rebuildPending = true;
                }
            }
        }

        // -------------------------------------------------------------- edits
        // A knot moved and/or banked, in the container's space. The knot's
        // rotation follows the chord through its neighbours (the builder's
        // own frame for a knot), carrying the bank; a feature's knot keeps
        // bank 0 and its tangent turns with it. The neighbours' frames are
        // re-aimed too, each keeping its own bank.
        void MoveKnot(TrackLayout layout, int i, Vector3 position, float bank)
        {
            TrackLayout.Knot knot = layout.knots[i];
            knot.position = position;
            layout.knots[i] = knot;
            ReframeKnot(layout, i, knot.mode == TangentMode.Continuous ? 0f : bank);
            if (i > 0 && !KnotLocked(layout, i - 1, out _)) ReframeKnot(layout, i - 1, BankOf(layout.knots[i - 1].rotation));
            if (i + 1 < layout.knots.Count && !KnotLocked(layout, i + 1, out _)) ReframeKnot(layout, i + 1, BankOf(layout.knots[i + 1].rotation));
        }

        static void ReframeKnot(TrackLayout layout, int i, float bank)
        {
            TrackLayout.Knot knot = layout.knots[i];
            Vector3 previous = layout.knots[Mathf.Max(0, i - 1)].position;
            Vector3 next = layout.knots[Mathf.Min(layout.knots.Count - 1, i + 1)].position;
            Vector3 direction = next - previous;
            if (direction.sqrMagnitude < 1e-4f) direction = knot.rotation * Vector3.forward;
            direction.Normalize();

            Quaternion rotation = WithBank(direction, knot.mode == TangentMode.Continuous ? 0f : bank);
            if (knot.mode == TangentMode.Continuous)
            {
                // A feature's knot: its explicit tangent points down the new chord, same length.
                float length = knot.tangentOut.magnitude;
                Vector3 local = Quaternion.Inverse(rotation) * (direction * length);
                knot.tangentOut = local;
                knot.tangentIn = -local;
            }
            knot.rotation = rotation;
            layout.knots[i] = knot;
        }

        void InsertKnotAfter(TrackGenerator generator, TrackLayout layout, int i)
        {
            TrackManager track = generator.Track;
            float middle = (layout.knotDistances[i] + layout.knotDistances[i + 1]) * 0.5f;
            track.GetFrameAtDistance(middle, out Vector3 centre, out _, out _, out _);
            // The pose on the road is on the flight line; the knot is the spline itself (lateral 0).
            Vector3 position = track.Spline.transform.InverseTransformPoint(centre);
            float bank = (BankOf(layout.knots[i].rotation) + BankOf(layout.knots[i + 1].rotation)) * 0.5f;

            Undo.RecordObject(working, "Insert Track Knot");
            layout.knots.Insert(i + 1, new TrackLayout.Knot { position = position, rotation = Quaternion.identity, mode = TangentMode.AutoSmooth });
            layout.knotDistances.Insert(i + 1, middle); // re-derived by ApplyEdit; keeps the list index-aligned
            ReframeKnot(layout, i + 1, bank);
            selectedKnot = i + 1;
            Commit(generator, true);
        }

        void DeleteKnot(TrackGenerator generator, TrackLayout layout, int i)
        {
            Undo.RecordObject(working, "Delete Track Knot");
            layout.knots.RemoveAt(i);
            layout.knotDistances.RemoveAt(i);
            if (i > 0) ReframeKnot(layout, i - 1, BankOf(layout.knots[i - 1].rotation));
            if (i < layout.knots.Count && !KnotLocked(layout, i, out _)) ReframeKnot(layout, i, BankOf(layout.knots[i].rotation));
            selectedKnot = Mathf.Min(i, layout.knots.Count - 1);
            Commit(generator, true);
        }

        void AddSpan(TrackGenerator generator, System.Collections.Generic.List<TrackLayout.Span> spans, float from, float to, int outerSide, string undoName)
        {
            Undo.RecordObject(working, undoName);
            spans.Add(new TrackLayout.Span(from, to, outerSide));
            spans.Sort((a, b) => a.start.CompareTo(b.start));
            Commit(generator, true);
        }

        void RemoveSpans(TrackGenerator generator, TrackLayout layout, float from, float to)
        {
            Undo.RecordObject(working, "Remove Spans");
            layout.flatSweeps.RemoveAll(s => s.start < to && s.end > from);
            layout.openStretches.RemoveAll(s => s.start < to && s.end > from);
            Commit(generator, true);
        }

        // Reloads the edited track (road only while dragging). Nothing is written
        // to disk here: Save, or Stop Editing's prompt, does that.
        void Commit(TrackGenerator generator, bool build)
        {
            EditorUtility.SetDirty(working);
            generator.ApplyEdit(working, build);
            SceneView.RepaintAll();
            Repaint();
        }

        // ------------------------------------------------------------- helpers
        void SelectKnot(TrackGenerator generator, TrackLayout layout, int i)
        {
            selectedKnot = i;
            FrameKnot(generator, layout, i);
        }

        static void FrameKnot(TrackGenerator generator, TrackLayout layout, int i)
        {
            Vector3 world = generator.Track.Spline.transform.TransformPoint(layout.knots[i].position);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(world, Vector3.one * 300f), false);
        }

        // The start, the final run-up (D9) and every knot of a track with sections are not editable.
        static bool KnotLocked(TrackLayout layout, int i, out string why)
        {
            why = null;
            if (layout.sections.Count > 0) { why = "track has loops or tubes"; return true; }
            if (i == 0) { why = "start"; return true; }
            if (layout.endZoneStart >= 0f && i < layout.knotDistances.Count && layout.knotDistances[i] >= layout.endZoneStart - 0.5f)
            {
                why = "final run-up";
                return true;
            }
            return false;
        }

        // A knot's bank, degrees, right edge up positive: its up against the level up round its forward.
        static float BankOf(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f) return 0f;
            Vector3 level = Quaternion.LookRotation(forward, Vector3.up) * Vector3.up;
            return Vector3.SignedAngle(level, rotation * Vector3.up, forward);
        }

        // The builder's own knot frame: look down the direction, then roll by the bank.
        static Quaternion WithBank(Vector3 direction, float bank) =>
            Quaternion.AngleAxis(bank, direction) * Quaternion.LookRotation(direction, Vector3.up);

        // The track distance nearest a world point, from a starting guess: Newton steps along the road.
        static float ProjectDistance(TrackManager track, Vector3 world, float hint)
        {
            float d = hint;
            for (int k = 0; k < 10; k++)
            {
                track.GetPoseAtDistance(d, 0f, out Vector3 position, out Quaternion rotation);
                float along = Vector3.Dot(world - position, rotation * Vector3.forward);
                if (Mathf.Abs(along) < 0.05f) break;
                d = Mathf.Clamp(d + Mathf.Clamp(along, -200f, 200f), 0f, track.Length);
            }
            return d;
        }
    }
}
