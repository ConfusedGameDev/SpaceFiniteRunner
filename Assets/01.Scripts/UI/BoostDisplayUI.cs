using TMPro;
using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Shows remaining player-triggered boosts on screen.
    /// Subscribes to RaceEvents.OnBoostsChanged — no direct VehicleController reference needed.
    ///
    /// Assign a TMP_Text component in the Inspector.
    /// </summary>
    public class BoostDisplayUI : MonoBehaviour
    {
        [SerializeField] TMP_Text boostLabel;

        void OnEnable()
        {
            RaceEvents.OnBoostsChanged += UpdateDisplay;
        }

        void OnDisable()
        {
            RaceEvents.OnBoostsChanged -= UpdateDisplay;
        }

        // Displays whole charges as solid pips and a half charge as a partial pip.
        // e.g. 2.5 charges → "▓▓▒"
        void UpdateDisplay(float remaining)
        {
            int   full    = Mathf.FloorToInt(remaining);
            bool  hasHalf = (remaining - full) >= 0.25f;

            var sb = new System.Text.StringBuilder("Boosts ");
            for (int i = 0; i < full; i++) sb.Append('▓');
            if (hasHalf) sb.Append('▒');
            boostLabel.text = sb.ToString();
        }
    }
}
