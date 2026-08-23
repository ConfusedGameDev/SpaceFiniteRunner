using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Central entry point for floating gameplay texts (boost popups, patrol
    /// alerts, any future messages). Singleton — auto-created on first use,
    /// no scene wiring needed. Texts spawn ahead of the ship so they stay
    /// readable at speed; the lead distance and size can be overridden per
    /// call (the GameManager exposes the tuned values for boosts and alerts).
    /// </summary>
    public class FloatingTextSystem : MonoBehaviour
    {
        static FloatingTextSystem instance;

        public static FloatingTextSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<FloatingTextSystem>();
                    if (instance == null)
                        instance = new GameObject("FloatingTextSystem").AddComponent<FloatingTextSystem>();
                }
                return instance;
            }
        }

        [Tooltip("Lead distance ahead of the ship for texts that don't specify one, in meters.")]
        [SerializeField, Min(0f)] float defaultLeadMeters = 60f;

        [Tooltip("Height above the flight line texts spawn at.")]
        [SerializeField] float heightOffset = 8f;

        [Tooltip("Character size for texts that don't specify one.")]
        [SerializeField, Min(0.05f)] float defaultCharacterSize = 2f;

        ShipMotor ship;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        /// <summary>Shows a rising, fading text ahead of the ship.</summary>
        public void DisplayText(string text, Color color, float duration = 1.0f)
            => DisplayText(text, color, duration, defaultLeadMeters, defaultCharacterSize);

        /// <summary>Same, with an explicit lead distance and character size.</summary>
        public void DisplayText(string text, Color color, float duration, float leadMeters, float characterSize)
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();

            Vector3 position = ship != null
                ? ship.transform.position + ship.transform.forward * leadMeters + Vector3.up * heightOffset
                : Vector3.up * heightOffset;

            FloatingWorldText.Spawn(position, text, color, characterSize, duration);
        }
    }
}
