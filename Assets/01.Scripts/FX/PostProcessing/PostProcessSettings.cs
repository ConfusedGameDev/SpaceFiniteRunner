using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The rules of the <see cref="PostProcessManager"/>: whether gameplay may
    /// move the picture at all, whether it may move each effect, and how far.
    /// This asset is the ONE source of truth for the range of every effect —
    /// a system asks for an absolute value and the manager clamps it to the
    /// band here. With a switch off the effect sits exactly at its baseline
    /// (the VolumeProfile asset for the Volume overrides, the driver's own
    /// settings asset for the full-screen drivers), which is what makes the
    /// look tunable while the game plays. Read live, never cloned; gameplay
    /// never writes it. Lives in Resources; <see cref="Load"/> falls back to
    /// an in-memory default that allows everything.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_PostProcessing", menuName = "FiniteRunner/Post Processing Settings")]
    public class PostProcessSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_PostProcessing";

        [Tooltip("Master switch. Off = every effect below holds its baseline, whatever gameplay asks for.")]
        public bool runtimeAdjustments = true;

        // ------------------------------------------------------ volume overrides
        [ToggleGroup("lensRuntime", "Lens distortion (boost kick, Light Speed warp)")]
        public bool lensRuntime = true;
        [ToggleGroup("lensRuntime")]
        [Tooltip("Band a runtime request is clamped to (the override's intensity).")]
        [MinMaxSlider(-1f, 1f, true)]
        public Vector2 lensRange = new(-1f, 1f);

        [ToggleGroup("motionBlurRuntime", "Motion blur (speed blur)")]
        public bool motionBlurRuntime = true;
        [ToggleGroup("motionBlurRuntime")]
        [Tooltip("Band a runtime request is clamped to (the override's intensity).")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 motionBlurRange = new(0f, 1f);

        [ToggleGroup("motionBlurClampRuntime", "Motion blur clamp")]
        public bool motionBlurClampRuntime = true;
        [ToggleGroup("motionBlurClampRuntime")]
        [Tooltip("Band a runtime request is clamped to (the longest blur streak, as a fraction of the screen).")]
        [MinMaxSlider(0f, 0.2f, true)]
        public Vector2 motionBlurClampRange = new(0f, 0.2f);

        [ToggleGroup("bloomRuntime", "Bloom")]
        public bool bloomRuntime = true;
        [ToggleGroup("bloomRuntime")]
        [Tooltip("Band a runtime request is clamped to (the override's intensity).")]
        [MinMaxSlider(0f, 10f, true)]
        public Vector2 bloomRange = new(0f, 10f);

        [ToggleGroup("vignetteRuntime", "Vignette")]
        public bool vignetteRuntime = true;
        [ToggleGroup("vignetteRuntime")]
        [Tooltip("Band a runtime request is clamped to (the override's intensity).")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 vignetteRange = new(0f, 1f);

        [ToggleGroup("chromaticRuntime", "Chromatic aberration")]
        public bool chromaticRuntime = true;
        [ToggleGroup("chromaticRuntime")]
        [Tooltip("Band a runtime request is clamped to (the override's intensity).")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 chromaticRange = new(0f, 1f);

        [ToggleGroup("filmGrainRuntime", "Film grain")]
        public bool filmGrainRuntime = true;
        [ToggleGroup("filmGrainRuntime")]
        [Tooltip("Band a runtime request is clamped to (the override's intensity).")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 filmGrainRange = new(0f, 1f);

        // ---------------------------------------------------- full-screen drivers
        [ToggleGroup("glitchRuntime", "Glitch (pulses, holds, damage floor)")]
        public bool glitchRuntime = true;
        [ToggleGroup("glitchRuntime")]
        [Tooltip("Band the glitch intensity is clamped to while gameplay drives it.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 glitchRange = new(0f, 1f);

        [ToggleGroup("distanceFogRuntime", "Distance fog (gameplay ramp)")]
        public bool distanceFogRuntime = true;
        [ToggleGroup("distanceFogRuntime")]
        [Tooltip("Band the fog intensity is clamped to while gameplay ramps it off its asset value.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 distanceFogRange = new(0f, 1f);

        [ToggleGroup("speedLinesRuntime", "Speed lines")]
        public bool speedLinesRuntime = true;
        [ToggleGroup("speedLinesRuntime")]
        [Tooltip("Band the speed-line intensity is clamped to while the speed or a boost drives it.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 speedLinesRange = new(0f, 1f);

        [ToggleGroup("vhsRuntime", "VHS tape (gameplay ramp)")]
        public bool vhsRuntime = true;
        [ToggleGroup("vhsRuntime")]
        [Tooltip("Band the tape intensity is clamped to while gameplay ramps it off its asset value. The player's VIDEO dial multiplies in afterwards.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 vhsRange = new(0f, 1f);

        [ToggleGroup("psxRuntime", "PSX look (gameplay ramp)")]
        public bool psxRuntime = true;
        [ToggleGroup("psxRuntime")]
        [Tooltip("Band the console intensity is clamped to while gameplay ramps it off its asset value. The player's VIDEO dial multiplies in afterwards.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 psxRange = new(0f, 1f);

        [ToggleGroup("crtRuntime", "CRT screen (gameplay ramp)")]
        public bool crtRuntime = true;
        [ToggleGroup("crtRuntime")]
        [Tooltip("Band the tube intensity is clamped to while gameplay ramps it off its asset value. The player's VIDEO dial multiplies in afterwards.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 crtRange = new(0f, 1f);

        [ToggleGroup("hyperspaceRuntime", "Hyperspace sky")]
        public bool hyperspaceRuntime = true;
        [ToggleGroup("hyperspaceRuntime")]
        [Tooltip("Band the sky crossfade is clamped to while Light Speed holds it in.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 hyperspaceRange = new(0f, 1f);

        [ToggleGroup("rainAtmosphereRuntime", "Rain atmosphere (scene fog and ambient)")]
        public bool rainAtmosphereRuntime = true;
        [ToggleGroup("rainAtmosphereRuntime")]
        [Tooltip("Band the rain's fog and ambient dimming is clamped to.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 rainAtmosphereRange = new(0f, 1f);

        /// <summary>True when gameplay may move <paramref name="effect"/>: the master switch and its own.</summary>
        public bool Allowed(PostEffect effect) => runtimeAdjustments && effect switch
        {
            PostEffect.LensDistortion => lensRuntime,
            PostEffect.MotionBlur => motionBlurRuntime,
            PostEffect.MotionBlurClamp => motionBlurClampRuntime,
            PostEffect.Bloom => bloomRuntime,
            PostEffect.Vignette => vignetteRuntime,
            PostEffect.ChromaticAberration => chromaticRuntime,
            PostEffect.FilmGrain => filmGrainRuntime,
            PostEffect.Glitch => glitchRuntime,
            PostEffect.DistanceFog => distanceFogRuntime,
            PostEffect.SpeedLines => speedLinesRuntime,
            PostEffect.VhsTape => vhsRuntime,
            PostEffect.PsxLook => psxRuntime,
            PostEffect.CrtScreen => crtRuntime,
            PostEffect.HyperspaceSky => hyperspaceRuntime,
            PostEffect.RainAtmosphere => rainAtmosphereRuntime,
            _ => true,
        };

        /// <summary>The band a runtime request for <paramref name="effect"/> is clamped to (x = low end, y = high end).</summary>
        public Vector2 Range(PostEffect effect) => effect switch
        {
            PostEffect.LensDistortion => lensRange,
            PostEffect.MotionBlur => motionBlurRange,
            PostEffect.MotionBlurClamp => motionBlurClampRange,
            PostEffect.Bloom => bloomRange,
            PostEffect.Vignette => vignetteRange,
            PostEffect.ChromaticAberration => chromaticRange,
            PostEffect.FilmGrain => filmGrainRange,
            PostEffect.Glitch => glitchRange,
            PostEffect.DistanceFog => distanceFogRange,
            PostEffect.SpeedLines => speedLinesRange,
            PostEffect.VhsTape => vhsRange,
            PostEffect.PsxLook => psxRange,
            PostEffect.CrtScreen => crtRange,
            PostEffect.HyperspaceSky => hyperspaceRange,
            PostEffect.RainAtmosphere => rainAtmosphereRange,
            _ => new Vector2(float.MinValue, float.MaxValue),
        };

        /// <summary><paramref name="value"/> clamped to the band of <paramref name="effect"/>.</summary>
        public float Clamp(PostEffect effect, float value)
        {
            Vector2 range = Range(effect);
            return Mathf.Clamp(value, range.x, Mathf.Max(range.x, range.y));
        }

        /// <summary>The shipped asset from Resources, or an in-memory default when none exists.</summary>
        public static PostProcessSettings Load()
        {
            var asset = Resources.Load<PostProcessSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static PostProcessSettings CreateDefault()
        {
            var settings = CreateInstance<PostProcessSettings>();
            settings.name = "PostProcessSettings (default)";
            return settings;
        }
    }
}
