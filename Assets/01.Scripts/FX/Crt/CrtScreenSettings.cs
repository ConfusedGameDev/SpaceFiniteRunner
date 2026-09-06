using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the CRT screen in one designer asset, re-applied every
    /// frame by <see cref="CrtScreen"/> (no runtime clone, so the inline
    /// inspector and the debug CRT SCREEN page tune the live picture). Each
    /// knob is one physical trait of a tube: the curved glass and its rounded
    /// corners (<see cref="curvature"/>, <see cref="cornerRadius"/>), the
    /// beam that bleeds sideways and loses focus toward the edges
    /// (<see cref="bleed"/>), the three guns drifting apart at the edges
    /// (<see cref="convergence"/>), light glowing through the glass
    /// (<see cref="glow"/>), the painted rows (<see cref="scanlines"/>,
    /// <see cref="scanlineCount"/>), the phosphor stripes (<see cref="mask"/>,
    /// <see cref="maskScale"/>), the refresh (<see cref="flicker"/>,
    /// <see cref="refreshRate"/>) and the dim corners (<see cref="vignette"/>).
    /// The asset is generic (it knows nothing about either game) and also
    /// carries the renderer feature's <see cref="material"/>, written by the
    /// installer, so a driver finds the material without a scene reference.
    /// Lives in Resources like the fog, speed-lines, VHS and PSX assets;
    /// <see cref="Load"/> falls back to an in-memory default.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_CrtScreen", menuName = "FiniteRunner/CRT Screen Settings")]
    public class CrtScreenSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_CrtScreen";

        // ------------------------------------------------------------ renderer
        [TitleGroup("Renderer")]
        [Tooltip("The material the renderer's CrtScreenFeature holds (Hidden/FiniteRunner/CrtScreen). Written by Tools → FiniteRunner → Install CRT Screen Feature; a driver reads it from here when its own reference is empty.")]
        public Material material;

        // ------------------------------------------------------------- picture
        [TitleGroup("Picture")]
        [Tooltip("Master dial. 0 = off (the render pass is skipped entirely), 1 = the full tube. Every trait scales with it, the curvature included, and the result is blended over the clean picture by it. Gameplay scales it further with CrtScreen.SetIntensity, and the player with the VIDEO settings page.")]
        [PropertyRange(0f, 1f)]
        public float intensity = 1f;

        // --------------------------------------------------------------- glass
        [TitleGroup("Glass")]
        [Tooltip("How much the glass bulges: the picture is barrel-warped and the visible area takes the rounded barrel silhouette of a tube, black beyond it. Scanlines and the grille bend with it.")]
        [PropertyRange(0f, 1f)]
        public float curvature = 0.35f;

        [TitleGroup("Glass")]
        [Tooltip("Radius of the rounded corners of the visible picture, as a fraction of the screen height.")]
        [PropertyRange(0f, 0.3f)]
        public float cornerRadius = 0.08f;

        [TitleGroup("Glass")]
        [Tooltip("How much dimmer the corners of the tube are than its centre.")]
        [PropertyRange(0f, 1f)]
        public float vignette = 0.3f;

        // ---------------------------------------------------------------- beam
        [TitleGroup("Beam")]
        [Tooltip("How far each phosphor dot bleeds sideways into its neighbours, in screen pixels at the centre of the tube. The beam loses focus toward the edges, so the bleed grows to 2.5× there — the edges go soft and smeary.")]
        [PropertyRange(0f, 8f), SuffixLabel("px", true)]
        public float bleed = 2.5f;

        [TitleGroup("Beam")]
        [Tooltip("How far the red and blue images drift away from the green at the corners of the tube, in screen pixels — the three guns never land on the same spot. Almost nothing at the centre.")]
        [PropertyRange(0f, 4f), SuffixLabel("px", true)]
        public float convergence = 1f;

        [TitleGroup("Beam")]
        [Tooltip("Halation: how much bright areas glow into their surroundings through the glass.")]
        [PropertyRange(0f, 1f)]
        public float glow = 0.3f;

        // ----------------------------------------------------------- phosphors
        [TitleGroup("Phosphors")]
        [Tooltip("Darkness of the gaps between the painted rows. Bright rows bloom over the gaps, so highlights keep more of their light than shadows.")]
        [PropertyRange(0f, 1f)]
        public float scanlines = 0.45f;

        [TitleGroup("Phosphors")]
        [Tooltip("Rows the beam paints across the picture (480 = NTSC, 576 = PAL). They follow the curvature.")]
        [PropertyRange(100, 1200), SuffixLabel("rows", true)]
        public int scanlineCount = 540;

        [TitleGroup("Phosphors")]
        [Tooltip("Strength of the aperture grille — the vertical red/green/blue phosphor stripes. The lost light is put back, so the picture keeps its brightness.")]
        [PropertyRange(0f, 1f)]
        public float mask = 0.25f;

        [TitleGroup("Phosphors")]
        [Tooltip("Width of one phosphor stripe in screen pixels (a triad is three of them). 1 at 1080p; raise it on a 4K display.")]
        [PropertyRange(1, 4), SuffixLabel("px", true)]
        public int maskScale = 1;

        // ------------------------------------------------------------- refresh
        [TitleGroup("Refresh")]
        [Tooltip("How much the whole frame's brightness breathes with the refresh. Quantised time, so pausing freezes it.")]
        [PropertyRange(0f, 1f)]
        public float flicker = 0.25f;

        [TitleGroup("Refresh")]
        [Tooltip("Refresh rate of the tube the flicker steps at (60 = NTSC, 50 = PAL).")]
        [PropertyRange(24f, 120f), SuffixLabel("Hz", true)]
        public float refreshRate = 60f;

        /// <summary>The shipped asset from Resources, or an in-memory default when none exists.</summary>
        public static CrtScreenSettings Load()
        {
            var asset = Resources.Load<CrtScreenSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static CrtScreenSettings CreateDefault()
        {
            var settings = CreateInstance<CrtScreenSettings>();
            settings.name = "CrtScreenSettings (default)";
            return settings;
        }
    }
}
