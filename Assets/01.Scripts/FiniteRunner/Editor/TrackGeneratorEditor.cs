using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiniteRunner.EditorTools
{
    /// <summary>
    /// Draws the TrackGenerator through Odin (so the Core Settings region —
    /// title group, percentage sliders, auto-rebalancing spawn table — renders
    /// properly) and adds a "Regenerate Track" button so random layouts can be
    /// previewed without entering play mode.
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GUILayout.Space(10);
            if (GUILayout.Button("Regenerate Track", GUILayout.Height(32)))
            {
                var generator = (TrackGenerator)target;
                generator.Generate();

                // Snap the ship to the new start line so the preview makes sense.
                var motor = Object.FindFirstObjectByType<ShipMotor>();
                var track = Object.FindFirstObjectByType<TrackManager>();
                if (motor != null && track != null)
                {
                    track.GetPose(0f, 0f, out Vector3 pos, out Quaternion rot);
                    motor.transform.SetPositionAndRotation(pos, rot);
                }

                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            }
        }
    }
}
