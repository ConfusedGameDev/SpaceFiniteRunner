using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Layout;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Draws the TrackGenerator through Odin (so the Core Settings region —
    /// title group, percentage sliders, auto-rebalancing spawn table — renders
    /// properly) and adds the authoring buttons. With a layout assigned:
    /// GENERATE LAYOUT (the procedural build recorded into the asset and
    /// closed into a circuit), REBUILD PREVIEW (the asset replayed, one lap
    /// spawned), CLEAR PREVIEW and SAVE LAYOUT; without one, the old
    /// "Regenerate Track" preview of a random endless stretch. Preview
    /// objects are flagged don't-save by the generator, so the scene file
    /// never fills with road pieces; the layout asset is what is saved.
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var generator = (TrackGenerator)target;
            TrackLayout layout = generator.Layout;

            GUILayout.Space(10);
            if (layout == null)
            {
                if (GUILayout.Button("Regenerate Track", GUILayout.Height(32)))
                {
                    generator.Generate();
                    SnapShipToStart();
                    MarkDirty(generator);
                }
                return;
            }

            EditorGUILayout.HelpBox(
                layout.IsAuthored
                    ? $"{layout.name}: {layout.length / 1000f:0.0} km, {layout.knotCount} knots, {layout.pieces.Count} pieces, {layout.items.Count} items, {(layout.closed ? "closed circuit" : "open track")}."
                    : $"{layout.name} has no pieces yet — GENERATE LAYOUT fills it from the procedural rules. Until then play falls back to the endless streamer.",
                layout.IsAuthored ? MessageType.Info : MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Layout", GUILayout.Height(32)))
                {
                    generator.GenerateLayout();
                    SnapShipToStart();
                    EditorUtility.SetDirty(layout);
                    AssetDatabase.SaveAssetIfDirty(layout);
                    MarkDirty(generator);
                }
                using (new EditorGUI.DisabledScope(!layout.IsAuthored))
                {
                    if (GUILayout.Button("Rebuild Preview", GUILayout.Height(32)))
                    {
                        generator.PreviewLayout();
                        SnapShipToStart();
                        MarkDirty(generator);
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!layout.IsAuthored))
                {
                    if (GUILayout.Button(new GUIContent("Capture Spline Edits",
                            "Knots moved with Unity's spline tools live in the preview only. This reads them back into the layout's pieces (moving knots only; adding or removing knots is refused)."),
                            GUILayout.Height(24)))
                    {
                        Undo.RecordObject(layout, "Capture spline edits");
                        if (generator.CaptureSplineIntoLayout())
                        {
                            EditorUtility.SetDirty(layout);
                            generator.PreviewLayout();
                            SnapShipToStart();
                            MarkDirty(generator);
                            SceneView.RepaintAll();
                        }
                    }
                }
                if (GUILayout.Button("Clear Preview", GUILayout.Height(24)))
                {
                    generator.ClearPreview();
                    MarkDirty(generator);
                }
                if (GUILayout.Button("Save Layout", GUILayout.Height(24)))
                {
                    EditorUtility.SetDirty(layout);
                    AssetDatabase.SaveAssets();
                }
            }

            if (!layout.IsAuthored) return;
            EditorGUILayout.HelpBox("The layout asset is the track. Rebuild Preview replays it and Generate Layout rolls a new one, so knots moved with Unity's spline tools are lost unless you click Capture Spline Edits first.", MessageType.None);
            GUILayout.Space(6);
            EditorGUILayout.LabelField("Add at the scene view's pivot", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Pick the Track Layout tool in the scene toolbar to drag handles: a knot's rotation is its turn / grade / bank, sliders stretch pieces, a loop's exit has drift, carry and yaw. Delete removes the selected pickup or feature.", MessageType.None);
            DrawPalette(generator, layout);
        }

        // Pickups and ramps dropped where the scene view is looking, then
        // selected in the tool so their handles are up at once.
        static void DrawPalette(TrackGenerator generator, TrackLayout layout)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var pads = generator.SpawnTable;
                if (pads != null)
                    foreach (var pad in pads)
                    {
                        if (pad == null || pad.definition == null) continue;
                        if (!GUILayout.Button(pad.name)) continue;
                        if (!TrackLayoutTool.PivotOnTrack(generator.Track, out float distance, out float lateral)) continue;
                        Undo.RecordObject(layout, $"Add {pad.name}");
                        layout.items.Add(new TrackItem { kind = TrackItemKind.Pad, entryName = pad.name, distance = distance, lateral = lateral, lane = pad.lane });
                        TrackLayoutTool.SelectItem(layout.items.Count - 1);
                        AfterEdit(generator, layout);
                    }
                if (GUILayout.Button("Coin row"))
                {
                    if (TrackLayoutTool.PivotOnTrack(generator.Track, out float distance, out float lateral))
                    {
                        Undo.RecordObject(layout, "Add coin row");
                        layout.items.Add(new TrackItem { kind = TrackItemKind.CoinRow, distance = distance, lateral = lateral });
                        TrackLayoutTool.SelectItem(layout.items.Count - 1);
                        AfterEdit(generator, layout);
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var features = generator.FeatureTable;
                if (features != null)
                    foreach (var feature in features)
                    {
                        if (feature == null || feature.definition is not Track.Features.JumpDefinition) continue;
                        if (!GUILayout.Button($"Ramp ({feature.name})")) continue;
                        if (!TrackLayoutTool.PivotOnTrack(generator.Track, out float distance, out float lateral)) continue;
                        Undo.RecordObject(layout, "Add ramp");
                        var ramp = generator.InsertRampPiece(distance, feature.name, lateral);
                        if (ramp != null) TrackLayoutTool.SelectPiece(layout.pieces.IndexOf(ramp));
                        AfterEdit(generator, layout);
                    }
            }
        }

        static void AfterEdit(TrackGenerator generator, TrackLayout layout)
        {
            EditorUtility.SetDirty(layout);
            generator.PreviewLayout();
            SceneView.RepaintAll();
        }

        // Snap the ship to the start line so the preview makes sense.
        static void SnapShipToStart()
        {
            var motor = Object.FindFirstObjectByType<ShipMotor>();
            var track = Object.FindFirstObjectByType<TrackManager>();
            if (motor != null && track != null)
            {
                track.GetPose(0f, 0f, out Vector3 pos, out Quaternion rot);
                motor.transform.SetPositionAndRotation(pos, rot);
            }
        }

        static void MarkDirty(TrackGenerator generator)
        {
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
