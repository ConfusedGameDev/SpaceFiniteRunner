using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Draws the TrackGenerator through Odin (so the Core Settings region —
    /// title group, percentage sliders, auto-rebalancing spawn table — renders
    /// properly) and adds a "Regenerate Track" button.
    /// In play the button is a full RETRY on a new layout: the run's own
    /// <see cref="GameManager.Restart"/> rebuilds the track and relaunches the
    /// physics ship and the patrol — a transform snap would be overwritten by
    /// the ship's body on the next frame. In edit mode it builds an endless
    /// preview (the finite end, laser gates and the loop gate are play-only)
    /// and records the spline as a prefab-instance modification.
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var generator = (TrackGenerator)target;

            GUILayout.Space(10);
            string label = Application.isPlaying ? "Regenerate Track (restarts the run)" : "Regenerate Track (endless preview)";
            if (GUILayout.Button(label, GUILayout.Height(32)))
            {
                if (Application.isPlaying) RegenerateInPlay(generator);
                else RegenerateInEditMode(generator);
            }

            if (generator.LastSeed != 0)
                EditorGUILayout.LabelField("Last seed", generator.LastSeed.ToString());
        }

        static void RegenerateInPlay(TrackGenerator generator)
        {
            // An editor button, not gameplay: finding the run's composition root here is fine.
            var game = Object.FindFirstObjectByType<GameManager>();
            if (game != null)
            {
                game.Restart();
                return;
            }

            // A scene with a track and no game flow (a sandbox): rebuild the track only.
            generator.Generate();
        }

        static void RegenerateInEditMode(TrackGenerator generator)
        {
            var track = generator.Track;
            var container = track != null ? track.Spline : null;
            if (container != null) Undo.RecordObject(container, "Regenerate Track");

            generator.Generate();

            // The track lives on a nested prefab instance (PF_Env → PF_Track):
            // without this the new knots may not be saved as overrides.
            if (container != null) PrefabUtility.RecordPrefabInstancePropertyModifications(container);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
