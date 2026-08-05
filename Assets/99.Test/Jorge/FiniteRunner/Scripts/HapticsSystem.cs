using UnityEngine;
using UnityEngine.InputSystem;

namespace FiniteRunner
{
    /// <summary>
    /// Central gamepad rumble control. Singleton — auto-created on first use,
    /// no scene wiring needed. Two channels, combined per frame onto the
    /// current gamepad's motors:
    /// - Pulse: one-shot rumble (power-up hits, getting caught).
    /// - Chase: continuous intensity refreshed every frame by the patrol
    ///   while it is close; it fades out automatically when not refreshed,
    ///   so stale values can never leave the pad buzzing.
    /// Motors are reset when the object is disabled or the app quits.
    /// </summary>
    public class HapticsSystem : MonoBehaviour
    {
        static HapticsSystem instance;

        public static HapticsSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<HapticsSystem>();
                    if (instance == null)
                        instance = new GameObject("HapticsSystem").AddComponent<HapticsSystem>();
                }
                return instance;
            }
        }

        [Tooltip("Low-frequency motor speed the chase channel reaches at full intensity.")]
        [SerializeField, Range(0f, 1f)] float chaseMaxLow = 0.45f;

        [Tooltip("High-frequency motor speed the chase channel reaches at full intensity.")]
        [SerializeField, Range(0f, 1f)] float chaseMaxHigh = 0.1f;

        [Tooltip("Seconds the chase channel keeps rumbling after the last refresh before fading out.")]
        [SerializeField, Min(0.05f)] float chaseTimeout = 0.25f;

        float pulseLow;
        float pulseHigh;
        float pulseTimeLeft;

        float chaseIntensity;
        float chaseRefreshedAgo = float.MaxValue;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        /// <summary>One-shot rumble. Overlapping pulses keep the strongest value and longest time.</summary>
        public void Pulse(float lowFrequency, float highFrequency, float duration)
        {
            pulseLow = Mathf.Max(pulseLow, Mathf.Clamp01(lowFrequency));
            pulseHigh = Mathf.Max(pulseHigh, Mathf.Clamp01(highFrequency));
            pulseTimeLeft = Mathf.Max(pulseTimeLeft, duration);
        }

        /// <summary>
        /// Continuous proximity rumble, 0..1. Call every frame while the danger
        /// lasts — it decays on its own shortly after the calls stop.
        /// </summary>
        public void SetChaseIntensity(float intensity01)
        {
            chaseIntensity = Mathf.Clamp01(intensity01);
            chaseRefreshedAgo = 0f;
        }

        void Update()
        {
            // Unscaled so rumble still decays while the pause menu holds timeScale at 0.
            float dt = Time.unscaledDeltaTime;

            if (pulseTimeLeft > 0f)
            {
                pulseTimeLeft -= dt;
                if (pulseTimeLeft <= 0f) { pulseLow = 0f; pulseHigh = 0f; }
            }

            chaseRefreshedAgo += dt;
            float chase = chaseRefreshedAgo <= chaseTimeout ? chaseIntensity : 0f;

            var pad = Gamepad.current;
            if (pad == null) return;

            float low = Mathf.Max(pulseTimeLeft > 0f ? pulseLow : 0f, chase * chaseMaxLow);
            float high = Mathf.Max(pulseTimeLeft > 0f ? pulseHigh : 0f, chase * chaseMaxHigh);
            pad.SetMotorSpeeds(low, high);
        }

        void OnDisable() => Gamepad.current?.ResetHaptics();

        void OnApplicationQuit() => Gamepad.current?.ResetHaptics();
    }
}
