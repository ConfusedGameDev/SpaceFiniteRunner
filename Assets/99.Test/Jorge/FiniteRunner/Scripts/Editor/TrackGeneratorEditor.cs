using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiniteRunner.EditorTools
{
    /// <summary>
    /// Adds a "Regenerate Track" button to the TrackGenerator inspector so
    /// random layouts can be previewed without entering play mode.
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

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
