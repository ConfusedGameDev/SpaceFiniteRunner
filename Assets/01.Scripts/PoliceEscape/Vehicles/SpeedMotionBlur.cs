using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Speed-driven motion blur for the car chase: below the threshold the
    /// image stays clean, past it the URP Motion Blur override fades in,
    /// reaching full strength at the top of the speed band — speed reads as
    /// danger without touching the camera itself. Works on the scene's global
    /// Volume (auto-found when not wired); the override is added to the
    /// volume's RUNTIME profile copy, so the shared profile asset is never
    /// touched — the same contract as LensDistortionController. Spawned by
    /// CityManager at play start, but a hand-placed one wins so its tuning
    /// survives; finds the player car itself (Speedometer pattern), so
    /// respawns and late spawns need no wiring.
    /// </summary>
    public class SpeedMotionBlur : MonoBehaviour
    {
        [Tooltip("Volume carrying the Motion Blur override. Left empty, the first global Volume in the scene is used (and the override added to its runtime profile if missing).")]
        public Volume volume;

        [TitleGroup("Blur")]
        [MinMaxSlider(0f, 400f, true)]
        [Tooltip("Speed band in km/h: blur starts at the low end and reaches full intensity at the high end.")]
        public Vector2 speedBandKmh = new Vector2(100f, 200f);

        [TitleGroup("Blur")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Blur intensity at the top of the speed band.")]
        public float maxIntensity = 0.6f;

        [TitleGroup("Blur")]
        [PropertyRange(1f, 20f)]
        [Tooltip("How quickly the blur follows speed changes (higher = snappier).")]
        public float responseSharpness = 6f;

        [TitleGroup("Blur")]
        [Tooltip("CameraAndObjects keeps the player car sharp (it barely moves on screen) while the world smears — the chase-cam look. CameraOnly is cheaper but blurs the car too.")]
        public MotionBlurMode mode = MotionBlurMode.CameraAndObjects;

        [TitleGroup("Blur")]
        [Tooltip("URP blur sample quality.")]
        public MotionBlurQuality quality = MotionBlurQuality.Medium;

        /// <summary>Speed (km/h) where the blur starts fading in.</summary>
        public float ThresholdKmh => speedBandKmh.x;

        /// <summary>Speed (km/h) where the blur reaches maxIntensity.</summary>
        public float FullBlurKmh => speedBandKmh.y;

        MotionBlur blur;
        CarController player;
        float refreshTimer;
        float intensity;

        void Awake()
        {
            if (volume == null)
            {
                foreach (Volume candidate in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                {
                    if (!candidate.isGlobal) continue;
                    volume = candidate;
                    break;
                }
            }
            if (volume == null)
            {
                volume = new GameObject("SpeedMotionBlurVolume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            }

            // volume.profile is the instantiated runtime copy — mutating it
            // never dirties the shared profile asset on disk.
            if (!volume.profile.TryGet(out blur))
                blur = volume.profile.Add<MotionBlur>();
            blur.active = true;
            blur.mode.overrideState = true;
            blur.quality.overrideState = true;
            blur.intensity.overrideState = true;
            blur.intensity.value = 0f;
        }

        void Update()
        {
            if (blur == null) return;
            blur.mode.value = mode;
            blur.quality.value = quality;

            RefreshTarget();
            float target = 0f;
            if (player != null)
            {
                float range = Mathf.Max(1f, FullBlurKmh - ThresholdKmh);
                target = maxIntensity * Mathf.Clamp01((player.SpeedKmh - ThresholdKmh) / range);
            }
            intensity = Mathf.Lerp(intensity, target, 1f - Mathf.Exp(-responseSharpness * Time.deltaTime));
            if (intensity < 0.005f && target == 0f) intensity = 0f; // IsActive() gates the pass at exactly 0
            blur.intensity.value = intensity;
        }

        void RefreshTarget()
        {
            refreshTimer -= Time.deltaTime;
            if (player != null && refreshTimer > 0f) return;
            refreshTimer = 1f;
            player = AI.PatrolManager.FindPlayerCar();
        }

        void OnDisable()
        {
            if (blur != null) blur.intensity.value = 0f;
        }
    }
}
