using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the VHS tape look in one designer asset, re-applied
    /// every frame by <see cref="VhsTape"/> (no runtime clone, so the inline
    /// inspector and the debug VHS TAPE page tune the live picture). Each
    /// knob is one real fault of the format — chroma recorded at a fraction
    /// of the luma bandwidth (bleed + lag), per-row timing errors (jitter),
    /// the crawling tracking band, the head switch at the bottom of the
    /// frame, tape noise, the CRT's scanlines, the washed-out tone curve —
    /// so a designer can dial a pristine studio dub or a rental that has
    /// been through a hundred decks. The asset is generic (it knows nothing
    /// about either game) and also carries the renderer feature's
    /// <see cref="material"/>, written by the installer, so a driver finds
    /// the material without a scene reference. Lives in Resources like the
    /// fog and speed-lines assets; <see cref="Load"/> falls back to an
    /// in-memory default.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_VhsTape", menuName = "FiniteRunner/VHS Tape Settings")]
    public class VhsTapeSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_VhsTape";

        // ------------------------------------------------------------ renderer
        [TitleGroup("Renderer")]
        [Tooltip("The material the renderer's VhsTapeFeature holds (Hidden/FiniteRunner/VhsTape). Written by Tools → FiniteRunner → Install VHS Tape Feature; a driver reads it from here when its own reference is empty.")]
        public Material material;

        // ---------------------------------------------------------------- tape
        [TitleGroup("Tape")]
        [Tooltip("Master dial. 0 = off (the render pass is skipped entirely), 1 = the full effect. Every fault below scales with it, and the result is blended over the clean picture by it. Gameplay scales it further with VhsTape.SetIntensity.")]
        [PropertyRange(0f, 1f)]
        public float intensity = 0.8f;

        [TitleGroup("Tape")]
        [Tooltip("Steps per second of every random element (grain, jitter, dropouts, the band's crawl). VHS is 25/30 fields a second — the noise steps at tape rate rather than shimmering at the game's frame rate. Pausing freezes it.")]
        [PropertyRange(1f, 60f), SuffixLabel("steps/s", true)]
        public float frameRate = 24f;

        // -------------------------------------------------------------- colour
        [TitleGroup("Colour")]
        [Tooltip("How far the colour smears sideways, in pixels — the chroma is recorded at a fraction of the luma bandwidth. 0 = colour as sharp as the picture.")]
        [PropertyRange(0f, 40f), SuffixLabel("px", true)]
        public float chromaBleed = 14f;

        [TitleGroup("Colour")]
        [Tooltip("How far the colour trails the luma, in pixels — the fringe on every hard edge.")]
        [PropertyRange(0f, 12f), SuffixLabel("px", true)]
        public float chromaLag = 3f;

        [TitleGroup("Colour")]
        [Tooltip("Vertical softness of the luma, in pixels — the tape's own resolution.")]
        [PropertyRange(0f, 4f), SuffixLabel("px", true)]
        public float lumaSoftness = 0.8f;

        [TitleGroup("Colour")]
        [Tooltip("Washed-out tone: lifted blacks, crushed whites, tired colour. 0 = the picture's own contrast.")]
        [PropertyRange(0f, 1f)]
        public float wash = 0.35f;

        // ------------------------------------------------------------ geometry
        [TitleGroup("Geometry")]
        [Tooltip("Sideways timing error per pair of rows plus a slow sway of the whole frame, in pixels.")]
        [PropertyRange(0f, 12f), SuffixLabel("px", true)]
        public float jitter = 2f;

        [TitleGroup("Geometry")]
        [Tooltip("Strength of the tracking band — the strip of torn, noisy, colourless rows crawling down the frame. Also widens it. 0 = a deck that tracks perfectly; a gameplay burst (VhsTape.TrackingPulse) adds to it.")]
        [PropertyRange(0f, 1f)]
        public float tracking = 0.35f;

        [TitleGroup("Geometry")]
        [Tooltip("How fast the tracking band crawls down the frame, in screen heights per second.")]
        [PropertyRange(0f, 2f), SuffixLabel("screens/s", true)]
        public float trackingSpeed = 0.12f;

        [TitleGroup("Geometry")]
        [Tooltip("Height of the tracking band at strength 0, in screen heights (it widens with strength).")]
        [PropertyRange(0f, 0.5f), SuffixLabel("screens", true)]
        public float trackingHeight = 0.06f;

        [TitleGroup("Geometry")]
        [Tooltip("Head-switch noise: the last rows of every frame skew hard sideways and fill with noise as the heads swap. 0 = off.")]
        [PropertyRange(0f, 1f)]
        public float headSwitch = 0.5f;

        [TitleGroup("Geometry")]
        [Tooltip("Height of the head-switch strip at the bottom of the frame, in screen heights.")]
        [PropertyRange(0f, 0.2f), SuffixLabel("screens", true)]
        public float headSwitchHeight = 0.035f;

        // --------------------------------------------------------------- noise
        [TitleGroup("Noise")]
        [Tooltip("Tape noise: fine grain, sparse white dropout dashes and a little brightness flicker, all at once.")]
        [PropertyRange(0f, 1f)]
        public float noise = 0.3f;

        [TitleGroup("Noise")]
        [Tooltip("Darkening of alternate rows, like a CRT's scanlines. 0 = off.")]
        [PropertyRange(0f, 1f)]
        public float scanlines = 0.3f;

        [TitleGroup("Noise")]
        [Tooltip("Number of scanlines over the screen height. 480 is an NTSC frame.")]
        [PropertyRange(100, 1200)]
        public int scanlineCount = 480;

        [TitleGroup("Noise")]
        [Tooltip("Corner darkening of an old tube.")]
        [PropertyRange(0f, 1f)]
        public float vignette = 0.35f;

        /// <summary>The shipped asset from Resources, or an in-memory default when none exists.</summary>
        public static VhsTapeSettings Load()
        {
            var asset = Resources.Load<VhsTapeSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static VhsTapeSettings CreateDefault()
        {
            var settings = CreateInstance<VhsTapeSettings>();
            settings.name = "VhsTapeSettings (default)";
            return settings;
        }
    }
}
