using System.IO;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Draws the TrackGenerator through Odin (so the Core Settings region —
    /// title group, percentage sliders, auto-rebalancing spawn table — renders
    /// properly) and adds the track authoring tools (TrackAuthoringPRD M2).
    /// <b>Edit mode</b>: Generate Track decides a whole finite track with the
    /// scene's run rules, exactly as play would; Save Track As… writes it to a
    /// <see cref="TrackLayoutAsset"/>; Set as Current Track puts that asset on
    /// the scene's level, so Play loads exactly that track; Clear Current
    /// Track goes back to generating one per run; Preview loads a saved track
    /// into the scene. Previews are never saved into the scene file.
    /// <b>Play mode</b>: Regenerate Track is a full RETRY — the run's own
    /// <see cref="GameManager.Restart"/> rebuilds the track and relaunches the
    /// physics ship and the patrol (a transform snap would be overwritten by
    /// the ship's body on the next frame).
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : OdinEditor
    {
        const string TracksFolder = "Assets/04.Data/FiniteRunner/Tracks";

        TrackLayoutAsset working; // the asset the buttons act on
        string status;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var generator = (TrackGenerator)target;

            GUILayout.Space(10);
            if (Application.isPlaying) DrawPlayMode(generator);
            else DrawAuthoring(generator);

            GUILayout.Space(6);
            DrawGizmoSettings();
        }

        // The Scene-view drawing of the track (TrackGizmos): per-user switches.
        static void DrawGizmoSettings()
        {
            EditorGUILayout.LabelField("Scene View", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool show = EditorGUILayout.Toggle("Draw track", TrackGizmoSettings.Show);
            using (new EditorGUI.DisabledScope(!show))
            {
                float radius = EditorGUILayout.Slider("Detail radius (m)", TrackGizmoSettings.DetailRadius, 500f, 20000f);
                bool placements = EditorGUILayout.Toggle("Placements", TrackGizmoSettings.Placements);
                bool labels = EditorGUILayout.Toggle("Labels", TrackGizmoSettings.Labels);
                if (EditorGUI.EndChangeCheck())
                {
                    TrackGizmoSettings.Show = show;
                    TrackGizmoSettings.DetailRadius = radius;
                    TrackGizmoSettings.Placements = placements;
                    TrackGizmoSettings.Labels = labels;
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.HelpBox("Red edge = open (a drop) · orange = flat sweep · cyan = loop/tube · magenta = final run-up · white = walls. Ticks every 500 m.", MessageType.None);
        }

        // ------------------------------------------------------------------ play
        static void DrawPlayMode(TrackGenerator generator)
        {
            EditorGUILayout.LabelField("Track", generator.LoadedTrack != null
                ? $"saved: {generator.LoadedTrack.name}"
                : $"generated, seed {generator.LastSeed}");

            if (!GUILayout.Button("Regenerate Track (restarts the run)", GUILayout.Height(32))) return;
            // An editor button, not gameplay: finding the run's composition root here is fine.
            var game = Object.FindFirstObjectByType<GameManager>();
            if (game != null) game.Restart();
            else generator.Generate(); // a scene with a track and no game flow (a sandbox): rebuild the track only
        }

        // ------------------------------------------------------------- authoring
        void DrawAuthoring(TrackGenerator generator)
        {
            var game = Object.FindFirstObjectByType<GameManager>();
            RunnerLevelDefinition level = game != null ? game.Level : null;

            EditorGUILayout.LabelField("Track Authoring", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Level", level, typeof(RunnerLevelDefinition), false);
            if (level == null)
            {
                EditorGUILayout.HelpBox("No GameManager with a level in this scene: Generate Track needs the run's rules (length, run-up, end ramps), and Set as Current Track needs the level.", MessageType.Warning);
                return;
            }

            if (working == null) working = level.track;
            working = (TrackLayoutAsset)EditorGUILayout.ObjectField("Track asset", working, typeof(TrackLayoutAsset), false);
            EditorGUILayout.LabelField("Level plays", level.track != null ? level.track.name : "a generated track (none set)");

            bool generated = generator.IsFinite && generator.LoadedTrack == null && generator.Track != null && generator.Track.HasEnd;

            if (GUILayout.Button("Generate Track", GUILayout.Height(28)))
            {
                Generate(generator, game);
                generated = true;
            }

            using (new EditorGUI.DisabledScope(!generated))
                if (GUILayout.Button("Save Track As…")) SaveAs(generator, level);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(working == null || level.track == working))
                    if (GUILayout.Button("Set as Current Track")) SetCurrent(level, working);
                using (new EditorGUI.DisabledScope(level.track == null))
                    if (GUILayout.Button("Clear Current Track")) SetCurrent(level, null);
            }

            using (new EditorGUI.DisabledScope(working == null || !working.IsValid))
                if (GUILayout.Button("Preview Saved Track")) Preview(generator, working);

            if (GUILayout.Button("Regenerate (endless preview)")) EndlessPreview(generator);

            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        void Generate(TrackGenerator generator, GameManager game)
        {
            generator.GenerateForBake(game);
            MarkChanged(generator);
            status = generator.Track.HasEnd
                ? $"Generated: {generator.Track.EndDistance / 1000f:0.0} km, {generator.Placements.Count} placements, seed {generator.LastSeed}. Not saved yet — Save Track As… to keep it."
                : "Generated, but the track has no end — is the GameManager's track length 0?";
        }

        void SaveAs(TrackGenerator generator, RunnerLevelDefinition level)
        {
            if (!AssetDatabase.IsValidFolder(TracksFolder))
            {
                Directory.CreateDirectory(TracksFolder);
                AssetDatabase.Refresh();
            }
            string suggested = working != null ? working.name : $"Track_{level.name}_{generator.LastSeed}";
            string path = EditorUtility.SaveFilePanelInProject("Save Track", suggested, "asset", "Where to save the generated track.", TracksFolder);
            if (string.IsNullOrEmpty(path)) return;

            var asset = AssetDatabase.LoadAssetAtPath<TrackLayoutAsset>(path);
            if (asset != null && asset.IsValid
                && !EditorUtility.DisplayDialog("Overwrite track?",
                    $"'{asset.name}' already holds a track. Replace it with the one just generated? Any edits made to it are lost.",
                    "Replace", "Cancel"))
                return;

            bool created = asset == null;
            if (created) asset = ScriptableObject.CreateInstance<TrackLayoutAsset>();
            else Undo.RecordObject(asset, "Save Track");
            asset.SetLayout(generator.CaptureLayout(), generator.ShapeAsset, generator.SpawnSet);
            if (created) AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            working = asset;
            status = $"Saved '{asset.name}'. Set as Current Track to play it.";
            EditorGUIUtility.PingObject(asset);
        }

        void SetCurrent(RunnerLevelDefinition level, TrackLayoutAsset asset)
        {
            Undo.RecordObject(level, asset != null ? "Set Current Track" : "Clear Current Track");
            level.track = asset;
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            status = asset != null
                ? $"'{level.name}' now plays '{asset.name}'. Press Play to test it."
                : $"'{level.name}' generates a track for every run again.";
        }

        void Preview(TrackGenerator generator, TrackLayoutAsset asset)
        {
            generator.PreviewSavedTrack(asset);
            MarkChanged(generator);
            status = $"Previewing '{asset.name}' ({asset.Layout.endDistance / 1000f:0.0} km).";
        }

        void EndlessPreview(TrackGenerator generator)
        {
            generator.Generate();
            MarkChanged(generator);
            status = "Endless preview (no end, no gates): the old quick look. Generate Track makes a real one.";
        }

        // A preview is not a scene edit: play rebuilds (or loads) the track, so
        // nothing is recorded as a prefab override or marks the scene dirty.
        static void MarkChanged(TrackGenerator generator) => SceneView.RepaintAll();
    }
}
