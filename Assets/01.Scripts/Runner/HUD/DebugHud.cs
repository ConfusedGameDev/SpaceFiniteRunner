using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Throwaway OnGUI readout for the feel test: speed in km/h, the Light
    /// Speed goal, the countdown and the result. Replaced by real HUD later.
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
            {
                GUILayout.Label($"LIGHT SPEED  {gameManager.LightSpeedKmh:0} km/h", style);
                GUILayout.Label($"TIME    {gameManager.TimeRemaining:0.0} s", style);
            }

            string result = gameManager != null ? gameManager.ResultLabel
                          : motor.HasStopped ? "OUT OF SPEED" : null;
            if (result != null)
            {
                GUILayout.Space(14);
                GUILayout.Label(result, bigStyle);
                GUILayout.Label("press R to run again", style);
            }
            GUILayout.EndArea();

            bool runOver = gameManager != null ? gameManager.RunOver : motor.HasStopped;
            if (runOver &&
                UnityEngine.InputSystem.Keyboard.current is { rKey: { wasPressedThisFrame: true } })
            {
                if (gameManager != null) gameManager.Restart();
                else motor.Launch();
            }
        }
    }
}
