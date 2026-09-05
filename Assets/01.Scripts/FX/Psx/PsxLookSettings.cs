using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the PlayStation-1 look in one designer asset, re-applied
    /// every frame by <see cref="PsxLook"/> (no runtime clone, so the inline
    /// inspector and the debug PSX LOOK page tune the live picture). Three
    /// faults of that console: the picture resolved at a virtual resolution
    /// (<see cref="targetHeight"/>), the vertex snap and affine texture swim
    /// approximated per depth-keyed screen block (<see cref="wobble"/>,
    /// <see cref="swim"/>, <see cref="wobbleBlock"/>, <see cref="jitterRate"/>),
    /// and the 15-bit framebuffer under an ordered dither
    /// (<see cref="colorBits"/>, <see cref="dither"/>). The asset is generic
    /// (it knows nothing about either game) and also carries the renderer
    /// feature's <see cref="material"/>, written by the installer, so a
    /// driver finds the material without a scene reference. Lives in
    /// Resources like the fog, speed-lines and VHS assets; <see cref="Load"/>
    /// falls back to an in-memory default.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_PsxLook", menuName = "FiniteRunner/PSX Look Settings")]
    public class PsxLookSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_PsxLook";

        // ------------------------------------------------------------ renderer
        [TitleGroup("Renderer")]
        [Tooltip("The material the renderer's PsxLookFeature holds (Hidden/FiniteRunner/PsxLook). Written by Tools → FiniteRunner → Install PSX Look Feature; a driver reads it from here when its own reference is empty.")]
        public Material material;

        // ------------------------------------------------------------- picture
        [TitleGroup("Picture")]
        [Tooltip("Master dial. 0 = off (the render pass is skipped entirely), 1 = the full effect. The wobble, the swim and the dither scale with it, and the result is blended over the clean picture by it. Gameplay scales it further with PsxLook.SetIntensity.")]
        [PropertyRange(0f, 1f)]
        public float intensity = 1f;

        [TitleGroup("Picture")]
        [Tooltip("Rows of the virtual resolution the picture is resolved at, with square pixels (240 = the PS1's 320×240). The real cell size is an integer number of screen pixels, so the grid never drifts.")]
        [PropertyRange(120, 480), SuffixLabel("rows", true)]
        public int targetHeight = 240;

        // -------------------------------------------------------------- colour
        [TitleGroup("Colour")]
        [Tooltip("Bits per colour channel the picture is quantised to, in gamma space. 5 = the PS1's 15-bit framebuffer; 8 = no banding.")]
        [PropertyRange(3, 8), SuffixLabel("bits", true)]
        public int colorBits = 5;

        [TitleGroup("Colour")]
        [Tooltip("Strength of the 4×4 ordered (Bayer) dither that breaks the banding into the PS1's checker pattern, in quantisation steps. 0 = hard bands.")]
        [PropertyRange(0f, 1f)]
        public float dither = 0.7f;

        // -------------------------------------------------------------- wobble
        [TitleGroup("Wobble")]
        [Tooltip("The vertex snap: how far a block of the picture jumps, in whole virtual pixels, re-rolled every step. Polygon edges dance by this much.")]
        [PropertyRange(0f, 3f), SuffixLabel("px", true)]
        public float wobble = 1f;

        [TitleGroup("Wobble")]
        [Tooltip("The affine swim: how far the texture under a block crawls through one step before snapping back, in virtual pixels (fractional).")]
        [PropertyRange(0f, 2f), SuffixLabel("px", true)]
        public float swim = 0.6f;

        [TitleGroup("Wobble")]
        [Tooltip("Size of a 'polygon' — the block of virtual pixels that moves as one rigid piece. Blocks are also cut by depth, so their seams follow geometry.")]
        [PropertyRange(4, 64), SuffixLabel("px", true)]
        public int wobbleBlock = 16;

        [TitleGroup("Wobble")]
        [Tooltip("Steps per second of the snap and the swim (quantised time, so pausing freezes it). The PS1 ran at 30 or 60; lower is more of a stutter.")]
        [PropertyRange(1f, 60f), SuffixLabel("steps/s", true)]
        public float jitterRate = 15f;

        [TitleGroup("Wobble")]
        [Tooltip("How much calmer far surfaces wobble, per metre of depth. 0 = the whole picture wobbles alike.")]
        [PropertyRange(0f, 0.05f), SuffixLabel("1/m", true)]
        public float wobbleDepthFalloff = 0f;

        /// <summary>The shipped asset from Resources, or an in-memory default when none exists.</summary>
        public static PsxLookSettings Load()
        {
            var asset = Resources.Load<PsxLookSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static PsxLookSettings CreateDefault()
        {
            var settings = CreateInstance<PsxLookSettings>();
            settings.name = "PsxLookSettings (default)";
            return settings;
        }
    }
}
