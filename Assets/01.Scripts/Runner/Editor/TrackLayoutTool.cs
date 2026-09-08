using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Layout;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// The scene-view editor of an authored circuit: select the Track, pick
    /// the tool from the toolbar, and the circuit is drawn as its centre and
    /// edge lines with a pick button on every piece and pickup. The selected
    /// one gets its handles — a rotation handle on a straight's knot (turn,
    /// grade and bank, "the curvature in x, y and z") and a slider for its
    /// chord; a radius handle on a loop or a tube; drift and carry sliders and
    /// a yaw disc at a loop's exit ("adjust the exit to make a wider loop");
    /// a length slider on a tube; a lateral slider on a ramp; a free-move
    /// handle on a pickup, projected back onto the track. Delete removes the
    /// selected pickup or feature piece. Every edit is Undo-recorded on the
    /// layout asset, the circuit's lines redraw at once off a spline-only
    /// rebuild, and the full preview (road, pickups) follows once the drag
    /// settles. The inspector's palette adds pickups and ramps at the scene
    /// view's pivot. Nothing here touches the scene file: previews are
    /// don't-save and the asset is what is edited.
    /// </summary>
    [EditorTool("Track Layout", typeof(TrackGenerator))]
    public class TrackLayoutTool : EditorTool
    {
        const float LineStep = 15f;          // metres between line samples
        const float FullPreviewDelay = 0.4f; // seconds after the last edit
        static readonly Color CentreColor = new(1f, 0.8f, 0.2f, 0.9f);
        static readonly Color EdgeColor = new(0.3f, 0.8f, 1f, 0.5f);
        static readonly Color StraightColor = new(0.8f, 0.8f, 0.8f);
        static readonly Color LoopColor = new(0.9f, 0.3f, 1f);
        static readonly Color TubeColor = new(0.3f, 1f, 0.9f);
        static readonly Color RampColor = new(1f, 0.8f, 0.2f);
        static readonly Color ItemColor = new(0.4f, 1f, 0.5f);

        // Selection survives tool re-instantiation.
        static int selectedPiece = -1;
        static int selectedItem = -1;
        static double fullPreviewAt = -1;
        static TrackGenerator pendingPreview;
        static bool ticking;

        GUIContent icon;
        public override GUIContent toolbarIcon => icon ??= new GUIContent("Track", "Track Layout: edit the authored circuit's pieces and pickups in the scene view");

        public override void OnActivated()
        {
            var generator = target as TrackGenerator;
            if (generator != null && generator.Layout != null && generator.Layout.IsAuthored)
                generator.PreviewLayout();
            EnsureTicking();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            var generator = target as TrackGenerator;
            if (generator == null) return;
            TrackLayout layout = generator.Layout;
            TrackManager track = generator.Track;
            if (layout == null || track == null || !layout.IsAuthored) return;
            if (track.Length <= 0f) generator.PreviewSpline();

            DrawTrack(track);
            HandleKeys(generator, layout);
            DrawPieces(generator, layout, track);
            DrawItems(generator, layout, track);
        }

        // ------------------------------------------------------------ track

        static void DrawTrack(TrackManager track)
        {
            float length = track.Length;
            int n = Mathf.Max(2, Mathf.CeilToInt(length / LineStep) + 1);
            var centre = new Vector3[n];
            var left = new Vector3[n];
            var right = new Vector3[n];
            float half = track.HalfWidth;
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Min(i * LineStep, length - 0.01f);
                track.GetPoseAtDistance(d, 0f, out centre[i], out _);
                track.GetPoseAtDistance(d, -half, out left[i], out _);
                track.GetPoseAtDistance(d, half, out right[i], out _);
            }
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = EdgeColor;
            Handles.DrawPolyLine(left);
            Handles.DrawPolyLine(right);
            Handles.color = CentreColor;
            Handles.DrawPolyLine(centre);
        }

        // ------------------------------------------------------------ pieces

        void DrawPieces(TrackGenerator generator, TrackLayout layout, TrackManager track)
        {
            for (int i = 0; i < layout.pieces.Count; i++)
            {
                TrackPiece piece = layout.pieces[i];
                if (piece == null) continue;
                bool selected = selectedPiece == i;

                // Where the piece is picked: a straight at its knot (its end), a feature at its spot.
                float pickDistance = piece.kind == TrackPieceKind.Straight ? piece.distance + piece.chord : piece.distance;
                track.GetPoseAtDistance(pickDistance, 0f, out Vector3 pos, out Quaternion rot);
                float size = HandleUtility.GetHandleSize(pos) * (selected ? 0.35f : 0.22f);

                Handles.color = PieceColor(piece.kind);
                if (Handles.Button(pos, rot, size, size * 1.3f, Handles.CubeHandleCap))
                {
                    selectedPiece = i;
                    selectedItem = -1;
                }
                if (selected) Handles.Label(pos + Vector3.up * size * 4f, piece.Label);

                if (!selected) continue;
                switch (piece.kind)
                {
                    case TrackPieceKind.Straight: StraightHandles(generator, layout, piece, pos); break;
                    case TrackPieceKind.Loop: LoopHandles(generator, layout, piece, track, pos, rot); break;
                    case TrackPieceKind.Tube: TubeHandles(generator, layout, piece, track); break;
                    case TrackPieceKind.Ramp: RampHandles(generator, layout, piece, track); break;
                }
            }
        }

        // The knot's rotation handle IS the piece's curvature in x, y and z:
        // its forward is the segment's heading and grade, its roll the bank.
        static void StraightHandles(TrackGenerator generator, TrackLayout layout, TrackPiece piece, Vector3 knotPos)
        {
            Quaternion knotRot = TrackGenerator.KnotRotation(TrackGenerator.KnotDirection(piece.heading, piece.pitch), piece.bank);
            float size = HandleUtility.GetHandleSize(knotPos);

            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(knotRot, knotPos);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Turn track piece");
                Vector3 f = newRot * Vector3.forward;
                piece.heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                piece.pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg, -30f, 30f);
                Vector3 upRef = Quaternion.LookRotation(f, Vector3.up) * Vector3.up;
                piece.bank = Mathf.Clamp(Vector3.SignedAngle(upRef, newRot * Vector3.up, f), -89f, 89f);
                Changed(generator, layout);
            }

            Vector3 forward = knotRot * Vector3.forward;
            Handles.color = StraightColor;
            EditorGUI.BeginChangeCheck();
            Vector3 dragged = Handles.Slider(knotPos, forward, size * 1.6f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Stretch track piece");
                piece.chord = Mathf.Clamp(piece.chord + Vector3.Dot(dragged - knotPos, forward), 50f, 1000f);
                Changed(generator, layout);
            }
        }

        static void LoopHandles(TrackGenerator generator, TrackLayout layout, TrackPiece piece, TrackManager track, Vector3 entryPos, Quaternion entryRot)
        {
            Vector3 forward = entryRot * Vector3.forward;
            Vector3 up = entryRot * Vector3.up;
            Vector3 right = entryRot * Vector3.right;

            // Size: the first turn's ring.
            Vector3 ringCentre = entryPos + up * piece.radius;
            Handles.color = LoopColor;
            EditorGUI.BeginChangeCheck();
            float radius = Handles.RadiusHandle(Quaternion.LookRotation(forward, up), ringCentre, piece.radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Resize loop");
                piece.radius = Mathf.Clamp(radius, 40f, 250f);
                Changed(generator, layout);
            }

            // Exit: where the track continues. Drift sideways, carry forward, yaw the heading.
            if (!generator.TryGetLoopSection(piece, out LoopSection section)) return;
            section.GetExitPose(0f, out Vector3 exitPos, out Quaternion exitRot);
            float size = HandleUtility.GetHandleSize(exitPos);
            Handles.Label(exitPos + up * size * 0.5f, $"exit  drift {piece.lateralDrift:0}  carry {piece.forwardCarry:0}  yaw {piece.exitYaw:0}°  ×{piece.turns}");

            Handles.color = LoopColor;
            EditorGUI.BeginChangeCheck();
            Vector3 drift = Handles.Slider(exitPos, right, size * 1.4f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Drift loop exit");
                piece.lateralDrift = Mathf.Clamp(piece.lateralDrift + Vector3.Dot(drift - exitPos, right), -600f, 600f);
                Changed(generator, layout);
            }

            EditorGUI.BeginChangeCheck();
            Vector3 carry = Handles.Slider(exitPos, forward, size * 1.4f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Carry loop exit");
                piece.forwardCarry = Mathf.Clamp(piece.forwardCarry + Vector3.Dot(carry - exitPos, forward), 0f, 1000f);
                Changed(generator, layout);
            }

            EditorGUI.BeginChangeCheck();
            Quaternion yawed = Handles.Disc(exitRot, exitPos, up, size * 1.2f, false, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Yaw loop exit");
                piece.exitYaw = Mathf.Clamp(Vector3.SignedAngle(forward, yawed * Vector3.forward, up), -60f, 60f);
                Changed(generator, layout);
            }
        }

        static void TubeHandles(TrackGenerator generator, TrackLayout layout, TrackPiece piece, TrackManager track)
        {
            float mid = piece.distance + piece.length * 0.5f;
            track.GetPoseAtDistance(mid, 0f, out Vector3 top, out Quaternion rot);
            Vector3 up = rot * Vector3.up;
            Vector3 axis = top - up * piece.tubeRadius; // the pipe's axis runs one radius under the flight line

            Handles.color = TubeColor;
            EditorGUI.BeginChangeCheck();
            float radius = Handles.RadiusHandle(rot, axis, piece.tubeRadius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Resize tube");
                piece.tubeRadius = Mathf.Clamp(radius, 20f, 150f);
                Changed(generator, layout);
            }

            track.GetPoseAtDistance(piece.distance + piece.length, 0f, out Vector3 endPos, out Quaternion endRot);
            Vector3 forward = endRot * Vector3.forward;
            float size = HandleUtility.GetHandleSize(endPos);
            Handles.Label(endPos + up * size * 0.5f, $"tube end  {piece.length:0} m  R{piece.tubeRadius:0}");
            EditorGUI.BeginChangeCheck();
            Vector3 dragged = Handles.Slider(endPos, forward, size * 1.6f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Stretch tube");
                piece.length = Mathf.Clamp(piece.length + Vector3.Dot(dragged - endPos, forward), 200f, 8000f);
                Changed(generator, layout);
            }
        }

        static void RampHandles(TrackGenerator generator, TrackLayout layout, TrackPiece piece, TrackManager track)
        {
            track.GetPoseAtDistance(piece.distance, piece.lateral, out Vector3 pos, out Quaternion rot);
            Vector3 right = rot * Vector3.right;
            float size = HandleUtility.GetHandleSize(pos);
            Handles.color = RampColor;
            EditorGUI.BeginChangeCheck();
            Vector3 dragged = Handles.Slider(pos, right, size * 1.4f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Move ramp");
                piece.lateral = Mathf.Clamp(piece.lateral + Vector3.Dot(dragged - pos, right), -60f, 60f);
                Changed(generator, layout);
            }
        }

        // ------------------------------------------------------------- items

        static void DrawItems(TrackGenerator generator, TrackLayout layout, TrackManager track)
        {
            for (int i = 0; i < layout.items.Count; i++)
            {
                TrackItem item = layout.items[i];
                if (item == null) continue;
                bool selected = selectedItem == i;
                track.GetPoseAtDistance(item.distance, item.lateral, out Vector3 pos, out Quaternion rot);
                float size = HandleUtility.GetHandleSize(pos) * (selected ? 0.3f : 0.15f);

                Handles.color = ItemColor;
                if (!selected)
                {
                    if (Handles.Button(pos, rot, size, size * 1.4f, Handles.SphereHandleCap))
                    {
                        selectedItem = i;
                        selectedPiece = -1;
                    }
                    continue;
                }

                Handles.Label(pos + rot * Vector3.up * size * 3f, item.Label);
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(pos, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(layout, "Move pickup");
                    item.distance = track.NearestDistance(moved, out float lateral);
                    item.lateral = Mathf.Clamp(lateral, -200f, 200f);
                    ChangedItemsOnly(generator, layout);
                }
            }
        }

        // ------------------------------------------------------------- keys

        static void HandleKeys(TrackGenerator generator, TrackLayout layout)
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.Delete) return;
            if (selectedItem >= 0 && selectedItem < layout.items.Count)
            {
                Undo.RecordObject(layout, "Delete pickup");
                layout.items.RemoveAt(selectedItem);
                selectedItem = -1;
                e.Use();
                ChangedItemsOnly(generator, layout);
            }
            else if (selectedPiece >= 0 && selectedPiece < layout.pieces.Count
                     && layout.pieces[selectedPiece].kind != TrackPieceKind.Straight)
            {
                // A feature can go; a straight is the road itself.
                Undo.RecordObject(layout, "Delete track feature");
                layout.pieces.RemoveAt(selectedPiece);
                selectedPiece = -1;
                e.Use();
                Changed(generator, layout);
            }
        }

        // ---------------------------------------------------------- rebuilds

        // A piece edit: the spline is replayed at once (the lines follow the
        // drag), the full preview once the drag has settled.
        static void Changed(TrackGenerator generator, TrackLayout layout)
        {
            EditorUtility.SetDirty(layout);
            generator.PreviewSpline();
            ScheduleFullPreview(generator);
            SceneView.RepaintAll();
        }

        // A pickup edit changes no spline: just the preview objects, settled.
        static void ChangedItemsOnly(TrackGenerator generator, TrackLayout layout)
        {
            EditorUtility.SetDirty(layout);
            ScheduleFullPreview(generator);
            SceneView.RepaintAll();
        }

        static void ScheduleFullPreview(TrackGenerator generator)
        {
            pendingPreview = generator;
            fullPreviewAt = EditorApplication.timeSinceStartup + FullPreviewDelay;
            EnsureTicking();
        }

        static void EnsureTicking()
        {
            if (ticking) return;
            ticking = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (fullPreviewAt < 0 || EditorApplication.timeSinceStartup < fullPreviewAt) return;
            fullPreviewAt = -1;
            if (pendingPreview != null && !Application.isPlaying)
            {
                pendingPreview.PreviewLayout();
                SceneView.RepaintAll();
            }
            pendingPreview = null;
        }

        static Color PieceColor(TrackPieceKind kind) => kind switch
        {
            TrackPieceKind.Loop => LoopColor,
            TrackPieceKind.Tube => TubeColor,
            TrackPieceKind.Ramp => RampColor,
            _ => StraightColor,
        };

        /// <summary>The inspector palette's insertion point: the scene view's pivot projected onto the track.</summary>
        public static bool PivotOnTrack(TrackManager track, out float distance, out float lateral)
        {
            distance = 0f;
            lateral = 0f;
            var view = SceneView.lastActiveSceneView;
            if (view == null || track == null || track.Length <= 0f) return false;
            distance = track.NearestDistance(view.pivot, out lateral);
            lateral = Mathf.Clamp(lateral, -track.HalfWidth, track.HalfWidth);
            return true;
        }

        /// <summary>Selects a piece (for the palette after inserting a ramp).</summary>
        public static void SelectPiece(int index)
        {
            selectedPiece = index;
            selectedItem = -1;
        }

        public static void SelectItem(int index)
        {
            selectedItem = index;
            selectedPiece = -1;
        }
    }
}
