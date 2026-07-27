using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// The real race HUD (uGUI): big speed readout that shifts color with
    /// how close you are to the target speed and pulses on pad hits, the
    /// target speed, a distance-to-goal bar, and the graded result.
    /// Replaces the throwaway OnGUI DebugHud.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] GameManager gameManager;

        [Header("Widgets")]
        [SerializeField] Text speedText;
        [SerializeField] Text targetText;
        [SerializeField] Text distanceText;
        [SerializeField] Image distanceFill;
        [SerializeField] Text resultText;
        [SerializeField] Text promptText;

        [Header("Speed colors")]
        [Tooltip("Well below the target speed.")]
        [SerializeField] Color slowColor = new(0.31f, 0.76f, 1f);      // blue
        [Tooltip("Within the CLEAR window of the target.")]
        [SerializeField] Color onTargetColor = new(0.48f, 0.83f, 0.32f); // green
        [Tooltip("Well above the target speed.")]
        [SerializeField] Color fastColor = new(1f, 0.35f, 0.25f);      // red

        [Header("Result colors")]
        [SerializeField] Color perfectColor = new(0.48f, 0.83f, 0.32f);
        [SerializeField] Color awesomeColor = new(0.31f, 0.76f, 1f);
        [SerializeField] Color clearColor = new(1f, 0.8f, 0.25f);
        [SerializeField] Color almostColor = new(1f, 0.55f, 0.2f);
        [SerializeField] Color failColor = new(1f, 0.3f, 0.25f);

        [Header("Pad pulse")]
        [SerializeField, Min(1f)] float pulseScale = 1.3f;
        [SerializeField, Min(0.1f)] float pulseDecay = 6f;

        float currentPulse = 1f;

        void OnEnable()
        {
            if (motor != null) motor.PadImpulse += OnPadImpulse;
        }

        void OnDisable()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
        }

        void OnPadImpulse(float _) => currentPulse = pulseScale;

        void Update()
        {
            if (motor == null) return;

            float kmh = motor.CurrentSpeed * 3.6f;
            float target = gameManager != null ? gameManager.TargetSpeedKmh : 0f;

            if (speedText != null)
            {
                speedText.text = $"{kmh:0}";
                speedText.color = SpeedColor(kmh, target);

                currentPulse = Mathf.MoveTowards(currentPulse, 1f, pulseDecay * Time.deltaTime);
                speedText.rectTransform.localScale = Vector3.one * currentPulse;
            }

            if (targetText != null && target > 0f)
                targetText.text = $"TARGET {target:0} KM/H";

            float total = motor.DistanceTravelled + motor.DistanceToGoal;
            float progress = total > 0f ? motor.DistanceTravelled / total : 0f;
            if (distanceFill != null) distanceFill.fillAmount = progress;
            if (distanceText != null) distanceText.text = $"{motor.DistanceToGoal:0} M";

            UpdateResult();

            bool runOver = gameManager != null ? gameManager.RunOver
                         : motor.HasStopped || motor.HasFinished;
            if (runOver && UnityEngine.InputSystem.Keyboard.current is { rKey: { wasPressedThisFrame: true } })
            {
                if (gameManager != null) gameManager.Restart();
                else motor.Launch();
            }
        }

        Color SpeedColor(float kmh, float target)
        {
            if (target <= 0f) return onTargetColor;
            float diff = (kmh - target) / target;
            if (diff < -0.05f)
                return Color.Lerp(slowColor, onTargetColor, Mathf.InverseLerp(-0.5f, -0.05f, diff));
            if (diff > 0.05f)
                return Color.Lerp(onTargetColor, fastColor, Mathf.InverseLerp(0.05f, 0.5f, diff));
            return onTargetColor;
        }

        void UpdateResult()
        {
            string label = gameManager != null ? gameManager.ResultLabel
                         : motor.HasStopped ? "OUT OF SPEED" : null;

            if (resultText != null)
            {
                resultText.text = label ?? "";
                if (label != null) resultText.color = ResultColor(label);
            }
            if (promptText != null)
                promptText.text = label != null ? "PRESS R TO RUN AGAIN" : "";
        }

        Color ResultColor(string label)
        {
            if (label.StartsWith("PERFECT")) return perfectColor;
            if (label.StartsWith("AWESOME")) return awesomeColor;
            if (label.StartsWith("CLEAR")) return clearColor;
            if (label.StartsWith("ALMOST")) return almostColor;
            return failColor;
        }
    }
}
