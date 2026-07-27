using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Throwaway OnGUI readout for the feel test: speed in km/h, target
    /// speed, distance to goal and the graded result. Replaced by real HUD later.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] GameManager gameManager;

        GUIStyle style;
        GUIStyle bigStyle;

        void OnGUI()
        {
            if (motor == null) return;

            style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.032f),
                fontStyle = FontStyle.Bold
            };
            bigStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.055f),
                fontStyle = FontStyle.Bold
            };

            GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));
            GUILayout.Label($"{motor.CurrentSpeed * 3.6f:0} km/h", bigStyle);
            if (gameManager != null)
                GUILayout.Label($"TARGET  {gameManager.TargetSpeedKmh:0} km/h", style);
            GUILayout.Label($"GOAL    {motor.DistanceToGoal:0} m", style);

            string result = gameManager != null ? gameManager.ResultLabel
                          : motor.HasStopped ? "OUT OF SPEED" : null;
            if (result != null)
            {
                GUILayout.Space(14);
                GUILayout.Label(result, bigStyle);
                GUILayout.Label("press R to run again", style);
            }
            GUILayout.EndArea();

            bool runOver = gameManager != null ? gameManager.RunOver
                         : motor.HasStopped || motor.HasFinished;
            if (runOver &&
                UnityEngine.InputSystem.Keyboard.current is { rKey: { wasPressedThisFrame: true } })
            {
                if (gameManager != null) gameManager.Restart();
                else motor.Launch();
            }
        }
    }
}
