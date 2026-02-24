using TMPro;
using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Displays the current vehicle speed on screen.
    /// Subscribes to RaceEvents.OnSpeedChanged — no direct reference to VehicleController needed.
    ///
    /// Requires TextMeshPro (included in Unity 2020+).
    /// Assign a TMP_Text component in the Inspector.
    /// </summary>
    public class SpeedDisplayUI : MonoBehaviour
    {
        [SerializeField] TMP_Text speedLabel;

        void OnEnable()
        {
            RaceEvents.OnSpeedChanged += UpdateDisplay;
        }

        void OnDisable()
        {
            RaceEvents.OnSpeedChanged -= UpdateDisplay;
        }

        void UpdateDisplay(float speed)
        {
            speedLabel.text = $"{speed:F1} m/s";
        }
    }
}
