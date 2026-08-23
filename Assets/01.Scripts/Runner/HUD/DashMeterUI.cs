using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// World-space power bar showing the dash meter. The whole canvas
    /// hierarchy (Canvas, track, fill, cost tick) is authored in the scene as
    /// a child of the Ship root, so designers adjust position, size and
    /// sprites directly in the editor — this component only DRIVES it: fill
    /// amount and colour from the motor's meter, the tick at the dashCost
    /// fraction, and a per-frame billboard toward the camera so the bar stays
    /// readable at speed. Pulls its rules off the motor (GameManager pushes
    /// the GameSettings asset there in Awake) and disables itself when the
    /// dash is off.
    /// </summary>
    public class DashMeterUI : MonoBehaviour
    {
        [Tooltip("The bar's filled image (Image Type = Filled, Horizontal). Drives fillAmount and colour.")]
        [SerializeField, Required] Image fill;

        [Tooltip("Optional marker moved to the one-dash-cost fraction of the bar.")]
        [SerializeField] RectTransform costTick;

        ShipMotor motor;
        GameSettings settings;

        void Awake()
        {
            motor = GetComponentInParent<ShipMotor>();
        }

        // Start, not Awake: GameManager.Awake must have pushed the settings
        // into the motor first (ConfigureDash).
        void Start()
        {
            settings = motor != null ? motor.DashSettings : null;
            if (settings == null || !settings.dashEnabled || fill == null)
            {
                gameObject.SetActive(false);
                return;
            }

            fill.fillAmount = 0f; // the meter starts every run empty

            // Tick at the one-dash threshold, measured on the fill's width.
            if (costTick != null)
            {
                float width = fill.rectTransform.sizeDelta.x;
                costTick.anchoredPosition = new Vector2(
                    (Mathf.Clamp01(settings.dashCost) - 0.5f) * width, costTick.anchoredPosition.y);
            }
        }

        void LateUpdate()
        {
            if (motor == null || settings == null || fill == null) return;

            float meter = motor.DashMeter;
            fill.fillAmount = meter;

            // Full colour once a dash is banked, dimmed while still charging,
            // and a soft pulse at full so the bar itself says "use me".
            Color color = settings.dashMeterColor;
            if (meter < settings.dashCost)
            {
                color = Color.Lerp(color, Color.black, 0.4f);
                color.a = 0.55f;
            }
            else if (meter >= 1f)
            {
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
                color = Color.Lerp(color, Color.white, 1f - pulse);
            }
            fill.color = color;

            // Billboard in world rotation each frame, so the ship-parenting
            // (and its banking/turning) can never skew the bar.
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
