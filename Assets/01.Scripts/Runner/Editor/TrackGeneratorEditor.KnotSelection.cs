using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Knot multi-selection for Edit Track (the Knots tab).
    /// <list type="bullet">
    /// <item><b>Click</b> a knot: it alone. <b>Ctrl/Cmd+click</b>: add or
    /// remove it. <b>Shift+click</b>: every editable knot from the last one
    /// clicked to this one.</item>
    /// <item><b>Drag on empty space</b>: a box; the editable knots inside it
    /// are selected (Shift or Ctrl/Cmd adds to the selection). A click on
    /// empty space clears it, and so does Esc. The editor owns empty-space
    /// clicks while the Knots tab is up, so a stray click no longer drops the
    /// TrackGenerator's inspector (and with it the edit).</item>
    /// <item>Two or more knots share one handle at their centre: its arrows
    /// move them all by the same offset, its disc adds the same bank to each
    /// (a feature's knot keeps bank 0). The inspector sets the bank of all of
    /// them, moves them by an offset, or deletes them.</item>
    /// </list>
    /// Locked knots (the start, the final run-up, any knot on a track with
    /// loops or tubes) are never added by Ctrl/Shift or the box: a plain
    /// click still selects one to read it. <see cref="selectedKnot"/> stays the
    /// primary (the last one clicked) — the single-knot panel and its handles
    /// are exactly what they were when one knot is selected.
    /// </summary>
    public partial class TrackGeneratorEditor
    {
        static readonly Color MarqueeFill = new(1f, 0.9f, 0.1f, 0.08f);
        static readonly Color MarqueeEdge = new(1f, 0.9f, 0.1f, 0.8f);
        const float MarqueeMinPixels = 4f;

        readonly List<int> knotSelection = new(); // every selected knot, in the order they were added; holds selectedKnot when >= 0

        bool marqueeActive;
        Vector2 marqueeStart, marqueeEnd;
        bool marqueeAdditive;
        Vector3 groupMoveOffset; // the inspector's "Move by" field

        int SelectedKnotCount => knotSelection.Count;

        // ---------------------------------------------------------- selection
        void ClearKnotSelection()
        {
            knotSelection.Clear();
            selectedKnot = -1;
        }

        void SelectOnlyKnot(int i)
        {
            knotSelection.Clear();
            if (i >= 0) knotSelection.Add(i);
            selectedKnot = i;
        }

        void ToggleKnot(TrackLayout layout, int i)
        {
            if (knotSelection.Remove(i))
            {
                if (selectedKnot == i) selectedKnot = knotSelection.Count > 0 ? knotSelection[^1] : -1;
                return;
            }
            if (KnotLocked(layout, i, out _) && knotSelection.Count > 0) return; // a locked knot only ever stands alone
            DropLockedFromSelection(layout);
            knotSelection.Add(i);
            selectedKnot = i;
        }

        void SelectKnotRange(TrackLayout layout, int to)
        {
            int from = selectedKnot >= 0 ? selectedKnot : to;
            DropLockedFromSelection(layout);
            for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                if (!KnotLocked(layout, i, out _) && !knotSelection.Contains(i)) knotSelection.Add(i);
            if (knotSelection.Contains(to)) selectedKnot = to;
            else if (knotSelection.Count > 0) selectedKnot = knotSelection[^1];
        }

        // Growing a selection: a locked knot selected alone to be read goes.
        void DropLockedFromSelection(TrackLayout layout)
        {
            knotSelection.RemoveAll(k => KnotLocked(layout, k, out _));
            if (selectedKnot >= 0 && !knotSelection.Contains(selectedKnot)) selectedKnot = knotSelection.Count > 0 ? knotSelection[^1] : -1;
        }

        // After an undo or a delete: forget indices that no longer exist.
        void PruneKnotSelection(TrackLayout layout)
        {
            knotSelection.RemoveAll(k => k < 0 || k >= layout.knots.Count);
            if (selectedKnot >= layout.knots.Count) selectedKnot = layout.knots.Count - 1;
            if (selectedKnot >= 0 && !knotSelection.Contains(selectedKnot)) knotSelection.Add(selectedKnot);
        }

        // A knot button was clicked: the modifiers say what it means.
        void ClickKnot(TrackLayout layout, int i)
        {
            Event e = Event.current;
            if (e.shift) SelectKnotRange(layout, i);
            else if (EditorGUI.actionKey) ToggleKnot(layout, i);
            else SelectOnlyKnot(i);
            Repaint();
        }

        // The selected knots that can be edited, ascending.
        List<int> EditableSelection(TrackLayout layout)
        {
            var list = new List<int>();
            foreach (int k in knotSelection)
                if (k >= 0 && k < layout.knots.Count && !KnotLocked(layout, k, out _)) list.Add(k);
            list.Sort();
            return list;
        }

        // ------------------------------------------------------------ marquee
        // Owns empty-space clicks in the Knots tab: a drag is a box select, a
        // click clears. Call before the knot buttons so they win a click on a knot.
        void KnotMarqueeGUI(TrackLayout layout, Transform container, Vector3 eye, float radiusSqr)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseDown:
                    if (e.button != 0 || e.alt || HandleUtility.nearestControl != id) break;
                    GUIUtility.hotControl = id;
                    marqueeActive = true;
                    marqueeStart = marqueeEnd = e.mousePosition;
                    marqueeAdditive = e.shift || EditorGUI.actionKey;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    marqueeEnd = e.mousePosition;
                    e.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    marqueeActive = false;
                    e.Use();
                    if ((marqueeEnd - marqueeStart).magnitude < MarqueeMinPixels)
                    {
                        if (!marqueeAdditive) ClearKnotSelection(); // a click on nothing
                    }
                    else BoxSelectKnots(layout, container, eye, radiusSqr, MarqueeRect(), marqueeAdditive);
                    Repaint();
                    SceneView.RepaintAll();
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && knotSelection.Count > 0 && GUIUtility.hotControl == 0)
                    {
                        ClearKnotSelection();
                        e.Use();
                        Repaint();
                        SceneView.RepaintAll();
                    }
                    break;

                case EventType.Repaint:
                    if (!marqueeActive) break;
                    Handles.BeginGUI();
                    Rect rect = MarqueeRect();
                    EditorGUI.DrawRect(rect, MarqueeFill);
                    Handles.color = MarqueeEdge;
                    Handles.DrawSolidRectangleWithOutline(rect, Color.clear, MarqueeEdge);
                    Handles.EndGUI();
                    break;
            }
        }

        Rect MarqueeRect() => Rect.MinMaxRect(
            Mathf.Min(marqueeStart.x, marqueeEnd.x), Mathf.Min(marqueeStart.y, marqueeEnd.y),
            Mathf.Max(marqueeStart.x, marqueeEnd.x), Mathf.Max(marqueeStart.y, marqueeEnd.y));

        void BoxSelectKnots(TrackLayout layout, Transform container, Vector3 eye, float radiusSqr, Rect rect, bool additive)
        {
            if (!additive) ClearKnotSelection();
            else DropLockedFromSelection(layout);
            Camera camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            for (int i = 0; i < layout.knots.Count; i++)
            {
                if (KnotLocked(layout, i, out _) || knotSelection.Contains(i)) continue;
                Vector3 world = container.TransformPoint(layout.knots[i].position);
                if ((world - eye).sqrMagnitude > radiusSqr) continue; // only the knots drawn
                if (camera != null && camera.WorldToViewportPoint(world).z <= 0f) continue; // behind the camera
                if (!rect.Contains(HandleUtility.WorldToGUIPoint(world))) continue;
                knotSelection.Add(i);
                selectedKnot = i;
            }
        }

        // ------------------------------------------------------- group handle
        // Two or more knots: one handle at their centre moves and banks them all.
        void GroupKnotHandles(TrackGenerator generator, TrackLayout layout, Transform container)
        {
            List<int> group = EditableSelection(layout);
            if (group.Count == 0) return;

            Vector3 centre = Vector3.zero, forward = Vector3.zero;
            foreach (int k in group)
            {
                centre += container.TransformPoint(layout.knots[k].position);
                forward += container.rotation * (layout.knots[k].rotation * Vector3.forward);
            }
            centre /= group.Count;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            Quaternion frame = Quaternion.LookRotation(forward.normalized, Vector3.up);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(centre, Tools.pivotRotation == PivotRotation.Local ? frame : Quaternion.identity);
            Handles.color = SelectedColor;
            Quaternion turned = Handles.Disc(frame, centre, frame * Vector3.forward, HandleUtility.GetHandleSize(centre) * 1.6f, false, 1f);
            if (!EditorGUI.EndChangeCheck()) return;

            Vector3 axis = frame * Vector3.forward;
            float bankDelta = Vector3.SignedAngle(frame * Vector3.up, turned * Vector3.up, axis);
            Vector3 offset = Quaternion.Inverse(container.rotation) * (moved - centre);

            Undo.RecordObject(working, "Edit Track Knots");
            MoveKnots(layout, group, offset, bankDelta, null);
            // Live: the stretch from the first to the last of them, with a knot's margin either side.
            EditorUtility.SetDirty(working);
            var distances = layout.knotDistances;
            generator.ApplyEditLive(working, distances[Mathf.Max(0, group[0] - 2)], distances[Mathf.Min(distances.Count - 1, group[^1] + 2)]);
            SceneView.RepaintAll();
            rebuildPending = true;
        }

        // ------------------------------------------------------- group panel
        void DrawKnotGroup(TrackGenerator generator, TrackLayout layout)
        {
            List<int> group = EditableSelection(layout);
            EditorGUILayout.Space(4);
            string range = group.Count > 0 ? $"knots {group[0]}–{group[^1]}" : "none editable";
            EditorGUILayout.LabelField($"{knotSelection.Count} knots selected", range, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Frame")) FrameKnots(generator, layout, group);
                if (GUILayout.Button("Select All Editable")) SelectAllEditable(layout);
                if (GUILayout.Button("Clear")) { ClearKnotSelection(); SceneView.RepaintAll(); }
            }
            if (group.Count == 0) return;

            // Bank: one value for all (mixed shows "—"); a feature's knot keeps 0.
            bool anyBankable = false, mixed = false;
            float firstBank = float.NaN;
            foreach (int k in group)
            {
                if (layout.knots[k].mode == TangentMode.Continuous) continue;
                float b = BankOf(layout.knots[k].rotation);
                if (!anyBankable) { firstBank = b; anyBankable = true; }
                else if (Mathf.Abs(b - firstBank) > 0.05f) mixed = true;
            }
            using (new EditorGUI.DisabledScope(!anyBankable))
            {
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                float bank = EditorGUILayout.Slider("Bank (°), all", anyBankable ? firstBank : 0f, -89f, 89f);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(working, "Bank Track Knots");
                    MoveKnots(layout, group, Vector3.zero, 0f, bank);
                    Commit(generator, true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                groupMoveOffset = EditorGUILayout.Vector3Field("Move by (m)", groupMoveOffset);
                using (new EditorGUI.DisabledScope(groupMoveOffset == Vector3.zero))
                    if (GUILayout.Button("Apply", GUILayout.Width(60)))
                    {
                        Undo.RecordObject(working, "Move Track Knots");
                        Transform container = generator.Track.Spline.transform;
                        MoveKnots(layout, group, Quaternion.Inverse(container.rotation) * groupMoveOffset, 0f, null);
                        Commit(generator, true);
                    }
            }

            int deletable = 0;
            foreach (int k in group) if (layout.knots[k].mode != TangentMode.Continuous) deletable++;
            using (new EditorGUI.DisabledScope(deletable == 0))
                if (GUILayout.Button(deletable == group.Count ? $"Delete {deletable} Knots" : $"Delete {deletable} Knots (feature knots kept)"))
                    DeleteKnots(generator, layout, group);
        }

        void SelectAllEditable(TrackLayout layout)
        {
            knotSelection.Clear();
            for (int i = 0; i < layout.knots.Count; i++)
                if (!KnotLocked(layout, i, out _)) knotSelection.Add(i);
            selectedKnot = knotSelection.Count > 0 ? knotSelection[^1] : -1;
            SceneView.RepaintAll();
        }

        static void FrameKnots(TrackGenerator generator, TrackLayout layout, List<int> group)
        {
            if (group.Count == 0 || SceneView.lastActiveSceneView == null) return;
            Transform container = generator.Track.Spline.transform;
            var bounds = new Bounds(container.TransformPoint(layout.knots[group[0]].position), Vector3.one * 100f);
            foreach (int k in group) bounds.Encapsulate(container.TransformPoint(layout.knots[k].position));
            bounds.Expand(150f);
            SceneView.lastActiveSceneView.Frame(bounds, false);
        }

        // ------------------------------------------------------- group edits
        // Every knot of `group` (editable, ascending) moved by `offset` (container
        // space) and either banked by `bankDelta` or set to `setBank`; then each
        // one and its unselected neighbours are re-aimed, as a single move does.
        void MoveKnots(TrackLayout layout, List<int> group, Vector3 offset, float bankDelta, float? setBank)
        {
            var banks = new Dictionary<int, float>();
            foreach (int k in group) banks[k] = BankOf(layout.knots[k].rotation);

            foreach (int k in group)
            {
                TrackLayout.Knot knot = layout.knots[k];
                knot.position += offset;
                layout.knots[k] = knot;
            }
            var reframed = new HashSet<int>();
            foreach (int k in group)
            {
                float bank = setBank ?? banks[k] + bankDelta;
                ReframeKnot(layout, k, layout.knots[k].mode == TangentMode.Continuous ? 0f : Mathf.Clamp(bank, -89f, 89f));
                reframed.Add(k);
            }
            foreach (int k in group)
                for (int n = k - 1; n <= k + 1; n += 2)
                {
                    if (n < 0 || n >= layout.knots.Count || reframed.Contains(n) || KnotLocked(layout, n, out _)) continue;
                    ReframeKnot(layout, n, BankOf(layout.knots[n].rotation));
                    reframed.Add(n);
                }
        }

        void DeleteKnots(TrackGenerator generator, TrackLayout layout, List<int> group)
        {
            Undo.RecordObject(working, "Delete Track Knots");
            var removed = new List<int>();
            foreach (int k in group)
                if (layout.knots[k].mode != TangentMode.Continuous) removed.Add(k);
            for (int r = removed.Count - 1; r >= 0; r--) // descending, so the indices still to go hold
            {
                layout.knots.RemoveAt(removed[r]);
                layout.knotDistances.RemoveAt(removed[r]);
            }
            // The knots either side of every gap take the new chord (and only
            // those: a feature's knot further on keeps its tangent).
            var gapSides = new HashSet<int>();
            for (int r = 0; r < removed.Count; r++)
            {
                int gap = removed[r] - r; // where the removed knot's successor now sits
                gapSides.Add(gap - 1);
                gapSides.Add(gap);
            }
            foreach (int i in gapSides)
                if (i > 0 && i < layout.knots.Count && !KnotLocked(layout, i, out _))
                    ReframeKnot(layout, i, BankOf(layout.knots[i].rotation));
            ClearKnotSelection();
            Commit(generator, true);
        }
    }
}
