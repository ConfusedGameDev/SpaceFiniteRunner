using ConfusedGameDev.FiniteRunner.FX;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Speed-driven motion blur for the car chase: below the threshold the
    /// image stays at its baseline, past it the URP Motion Blur fades up,
    /// reaching full strength at the top of the speed band — speed reads as
    /// danger without touching the camera itself. It never touches the
    /// Volume: the intensity goes to the <see cref="PostProcessManager"/> as
    /// a request (clamped to the blur's band, dropped while runtime
    /// adjustments are off), rising from the manager's baseline; the blur's
    /// mode and quality are the Volume profile's. Hand-placed under
    /// <c>===LIGHTING===/Filters</c>; finds the player car itself
    /// (Speedometer pattern), so respawns and late spawns need no wiring.
    /// </summary>
    public class SpeedMotionBlur : MonoBehaviour
    {
        [TitleGroup("Blur")]
        [MinMaxSlider(0f, 400f, true)]
        [Tooltip("Speed band in km/h: blur starts at the low end and reaches full intensity at the high end.")]
        public Vector2 speedBandKmh = new Vector2(100f, 200f);

        [TitleGroup("Blur")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Blur intensity at the top of the speed band. The post-processing manager clamps it to the blur band.")]
        public float maxIntensity = 0.6f;

        [TitleGroup("Blur")]
        [PropertyRange(1f, 20f)]
        [Tooltip("How quickly the blur follows speed changes (higher = snappier).")]
        public float responseSharpness = 6f;

        /// <summary>Speed (km/h) where the blur starts fading in.</summary>
        public float ThresholdKmh => speedBandKmh.x;

        /// <summary>Speed (km/h) where the blur reaches maxIntensity.</summary>
        public float FullBlurKmh => speedBandKmh.y;

        CarController player;
        float refreshTimer;
        float blend; // 0 = the baseline, 1 = maxIntensity

        void Update()
        {
            RefreshTarget();
            float target = 0f;
            if (player != null)
            {
                float range = Mathf.Max(1f, FullBlurKmh - ThresholdKmh);
                target = Mathf.Clamp01((player.SpeedKmh - ThresholdKmh) / range);
            }
            blend = Mathf.Lerp(blend, target, 1f - Mathf.Exp(-responseSharpness * Time.deltaTime));
            if (blend < 0.005f && target == 0f) blend = 0f;

            if (blend <= 0f)
            {
                // Below the band: no request, so the baseline shows.
                PostProcessManager.Clear(this, PostEffect.MotionBlur);
                return;
            }
            float baseline = PostProcessManager.Baseline(this, PostEffect.MotionBlur);
            PostProcessManager.Set(this, PostEffect.MotionBlur, Mathf.Lerp(baseline, maxIntensity, blend));
        }

        void RefreshTarget()
        {
            refreshTimer -= Time.deltaTime;
            if (player != null && refreshTimer > 0f) return;
            refreshTimer = 1f;
            player = PlayerCars.Current;
        }

        void OnDisable() => PostProcessManager.Clear(this, PostEffect.MotionBlur);
    }
}
