using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The race HUD (uGUI): big speed readout that heats up as you approach
    /// Light Speed and pulses on pad hits, the Light Speed goal, a countdown
    /// bar (time is the limit, not distance), the win/lose result, and
    /// floating "+boost" text spawned at the ship on every booster hit.
    /// Its own "PRESS R TO RUN AGAIN" shortcut only covers a WIN: a loss ends
    /// on the <see cref="GameOverScreen"/>, which owns that answer.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] GameManager gameManager;

        [Header("Widgets")]
        [SerializeField] Text speedText;
        [SerializeField] Text targetText;
        [FormerlySerializedAs("distanceText")]
        [SerializeField] Text timeText;
        [FormerlySerializedAs("distanceFill")]
        [SerializeField] Image timeFill;
        [SerializeField] Text resultText;
        [SerializeField] Text promptText;

        [Header("Speed colors")]
        [Tooltip("Far below Light Speed.")]
        [SerializeField] Color slowColor = new(0.31f, 0.76f, 1f);      // blue
        [Tooltip("Making good progress toward Light Speed.")]
        [SerializeField] Color onTargetColor = new(0.48f, 0.83f, 0.32f); // green
        [Tooltip("Closing in on Light Speed.")]
        [SerializeField] Color fastColor = new(1f, 0.35f, 0.25f);      // hot

        [Header("Result colors")]
        [FormerlySerializedAs("perfectColor")]
        [SerializeField] Color winColor = new(0.48f, 0.83f, 0.32f);
        [SerializeField] Color failColor = new(1f, 0.3f, 0.25f);

        [Header("Countdown")]
        [Tooltip("The timer text tints with the fail color below this many seconds.")]
        [SerializeField, Min(0f)] float lowTimeWarning = 10f;
        [SerializeField] Color timeColor = Color.white;

        [Header("Pad pulse")]
        [SerializeField, Min(1f)] float pulseScale = 1.3f;
        [SerializeField, Min(0.1f)] float pulseDecay = 6f;

        [Header("Boost floating text")]
        [SerializeField] bool spawnBoostText = true;
        [SerializeField] Color boostTextColor = new(0.48f, 1f, 0.4f);
        [SerializeField, Min(0.05f)] float boostTextSize = 0.6f;

        float currentPulse = 1f;

        void OnEnable()
        {
            if (motor != null) motor.PadImpulse += OnPadImpulse;
        }

        void OnDisable()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
        }

        void OnPadImpulse(float magnitude)
        {
            currentPulse = pulseScale;

            // Juice: floating text for every booster hit, spawned ahead of the
            // ship (GameSettings.boostTextLeadMeters) so it isn't left behind
            // instantly at speed.
            if (spawnBoostText && magnitude > 0f && gameManager != null)
                FloatingTextSystem.Instance.DisplayText(
                    $"+{magnitude:0}", boostTextColor, 1f,
                    gameManager.BoostTextLeadMeters, boostTextSize);
        }

        void Update()
        {
            if (motor == null) return;

            float kmh = motor.CurrentSpeed * 3.6f;
            float lightSpeed = gameManager != null ? gameManager.LightSpeedKmh : 0f;

            if (speedText != null)
            {
                speedText.text = $"{kmh:0}";
                speedText.color = SpeedColor(kmh, lightSpeed);

                currentPulse = Mathf.MoveTowards(currentPulse, 1f, pulseDecay * Time.deltaTime);
                speedText.rectTransform.localScale = Vector3.one * currentPulse;
            }

            if (targetText != null && lightSpeed > 0f)
                targetText.text = $"LIGHT SPEED  {lightSpeed:0} KM/H";

            UpdateCountdown();
            UpdateResult();

            bool runOver = gameManager != null ? gameManager.RunOver : motor.HasStopped;
            // South only on gamepad — Start is reserved for the pause menu.
            bool restartPressed =
                UnityEngine.InputSystem.Keyboard.current is { rKey: { wasPressedThisFrame: true } } ||
                UnityEngine.InputSystem.Gamepad.current is { buttonSouth: { wasPressedThisFrame: true } };
            if (runOver && restartPressed && !GameOverOwnsRetry)
            {
                if (gameManager != null) gameManager.Restart();
                else motor.Launch();
            }
        }

        /// <summary>
        /// True from the moment a run is lost until the GAME OVER screen has
        /// been answered. The screen is raised only after the patrol's parting
        /// line finishes, so this also covers the seconds before it appears —
        /// the HUD must not offer a second, quieter way out of the same moment.
        /// </summary>
        bool GameOverOwnsRetry => GameOverScreen.IsOpen
                                  || (gameManager != null && gameManager.RunOver && !gameManager.HasWon);

        void UpdateCountdown()
        {
            if (gameManager == null) return;

            float remaining = gameManager.TimeRemaining;
            if (timeText != null)
            {
                timeText.text = $"{remaining:0.0} S";
                timeText.color = remaining <= lowTimeWarning ? failColor : timeColor;
            }
            if (timeFill != null)
                timeFill.fillAmount = gameManager.TimeLimit > 0f ? remaining / gameManager.TimeLimit : 0f;
        }

        // The readout heats up as speed climbs toward Light Speed: blue when
        // slow, green mid-climb, hot near the goal. Faster is always better now.
        Color SpeedColor(float kmh, float lightSpeed)
        {
            if (lightSpeed <= 0f) return onTargetColor;
            float progress = Mathf.Clamp01(kmh / lightSpeed);
            return progress < 0.6f
                ? Color.Lerp(slowColor, onTargetColor, progress / 0.6f)
                : Color.Lerp(onTargetColor, fastColor, (progress - 0.6f) / 0.4f);
        }

        void UpdateResult()
        {
            string label = gameManager != null ? gameManager.ResultLabel
                         : motor.HasStopped ? "OUT OF SPEED" : null;

            if (resultText != null)
            {
                resultText.text = label ?? "";
                if (label != null)
                    resultText.color = gameManager != null && gameManager.HasWon ? winColor : failColor;
            }
            if (promptText != null)
                promptText.text = label != null && !GameOverOwnsRetry ? "PRESS R TO RUN AGAIN" : "";
        }
    }
}
