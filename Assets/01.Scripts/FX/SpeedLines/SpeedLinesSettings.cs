using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the manga speed lines in one designer asset, re-applied
    /// every frame by <see cref="SpeedLines"/> (no runtime clone, so the
    /// inline inspector and the debug SPEED LINES page tune the live
    /// picture). The asset is GENERIC — it knows nothing about ships, cars or
    /// Light Speed: the trigger band is a fraction of whatever reference
    /// speed the owner hands the driver, and the camera-mode multipliers are
    /// indexed 0 / 1 / 2 (Far / Close / First person) because the FX assembly
    /// cannot see the camera rig's enum. It also carries the renderer
    /// feature's <see cref="material"/>, written by the installer, so a
    /// driver created at play time can find the material without a scene
    /// reference. Lives in Resources like the fog asset; <see cref="Load"/>
    /// falls back to an in-memory default.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_SpeedLines", menuName = "FiniteRunner/Speed Lines Settings")]
    public class SpeedLinesSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_SpeedLines";

        // ------------------------------------------------------------ renderer
        [TitleGroup("Renderer")]
        [Tooltip("The material the renderer's SpeedLinesFeature holds (Hidden/FiniteRunner/SpeedLines). Written by Tools → FiniteRunner → Install Speed Lines Feature; a driver spawned at play time reads it from here.")]
        public Material material;

        // ------------------------------------------------------------- trigger
        [TitleGroup("Trigger")]
        [Tooltip("Master dial. 0 = off (the render pass is skipped entirely), 1 = the full effect. Gameplay scales it further with SpeedLines.SetIntensity.")]
        [PropertyRange(0f, 1f)]
        public float intensity = 1f;

        [TitleGroup("Trigger")]
        [Tooltip("Speed band as a fraction of the reference speed the owner hands the driver (the runner: Light Speed). Lines start at X and reach full strength at Y.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 speedBand = new(0.5f, 1f);

        [TitleGroup("Trigger")]
        [Tooltip("How fast the intensity follows the speed (exponential response, 1/s). Low = a slow build that survives a brake pad; high = snaps.")]
        [PropertyRange(1f, 20f), SuffixLabel("1/s", true)]
        public float responseSharpness = 4f;

        [TitleGroup("Trigger")]
        [Tooltip("How fast the convergence point follows the target's screen position (1/s).")]
        [PropertyRange(1f, 30f), SuffixLabel("1/s", true)]
        public float focusResponse = 8f;

        // ---------------------------------------------------------------- look
        [TitleGroup("Look")]
        [Tooltip("Line colour; alpha scales the whole overlay.")]
        public Color color = Color.white;

        [TitleGroup("Look")]
        [Tooltip("Angular cells of the coarse (wide) layer. Fixed on purpose — density × intensity decides how many hold a line, so cells never drift between flicker frames.")]
        [PropertyRange(8, 200)]
        public int lineCount = 48;

        [TitleGroup("Look")]
        [Tooltip("Angular cells of the fine (thin) layer drawn under the coarse one.")]
        [PropertyRange(16, 400)]
        public int fineLineCount = 140;

        [TitleGroup("Look")]
        [Tooltip("Share of cells that hold a line at full intensity.")]
        [PropertyRange(0f, 1f)]
        public float density = 0.55f;

        [TitleGroup("Look")]
        [Tooltip("Width of a line at the screen edge, as a fraction of its cell (the fine layer draws at half).")]
        [PropertyRange(0f, 1f)]
        public float lineWidth = 0.45f;

        [TitleGroup("Look")]
        [Tooltip("Distance over which a wedge widens from its tip to full width, in screen heights.")]
        [PropertyRange(0.05f, 1f), SuffixLabel("screens", true)]
        public float taperLength = 0.35f;

        [TitleGroup("Look")]
        [Tooltip("Radius of the clear middle, in screen heights: Y at the start of the band, shrinking to X at full speed — the lines close in on the ship as it nears the reference speed.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 innerRadius = new(0.18f, 0.42f);

        [TitleGroup("Look")]
        [Tooltip("Per-line random spread of the inner tip around the clear radius (0 = every tip on the same circle).")]
        [PropertyRange(0f, 1f)]
        public float innerJitter = 0.3f;

        [TitleGroup("Look")]
        [Tooltip("Pattern re-rolls per second — the lines jump between hand-drawn frames rather than sliding.")]
        [PropertyRange(1f, 60f), SuffixLabel("steps/s", true)]
        public float flickerRate = 12f;

        [TitleGroup("Look")]
        [Tooltip("Anti-aliasing width of a line's edge, in pixels. 0 = razor.")]
        [PropertyRange(0f, 3f), SuffixLabel("px", true)]
        public float edgeSoftness = 1f;

        // -------------------------------------------------------- camera modes
        [TitleGroup("Camera modes")]
        [Tooltip("Intensity multiplier in the Far chase framing (mode 0).")]
        [PropertyRange(0f, 2f)]
        public float farMultiplier = 1f;

        [TitleGroup("Camera modes")]
        [Tooltip("Intensity multiplier in the Close chase framing (mode 1).")]
        [PropertyRange(0f, 2f)]
        public float closeMultiplier = 1f;

        [TitleGroup("Camera modes")]
        [Tooltip("Intensity multiplier in first person (mode 2) — the cockpit view is where the effect sells hardest.")]
        [PropertyRange(0f, 2f)]
        public float firstPersonMultiplier = 1.3f;

        /// <summary>Multiplier for a camera-mode index: 0 Far, 1 Close, 2 First person (the chase rig's CameraMode order).</summary>
        public float ModeMultiplier(int mode) => mode switch
        {
            1 => closeMultiplier,
            2 => firstPersonMultiplier,
            _ => farMultiplier,
        };

        /// <summary>The shipped asset from Resources, or an in-memory default when none exists.</summary>
        public static SpeedLinesSettings Load()
        {
            var asset = Resources.Load<SpeedLinesSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static SpeedLinesSettings CreateDefault()
        {
            var settings = CreateInstance<SpeedLinesSettings>();
            settings.name = "SpeedLinesSettings (default)";
            return settings;
        }
    }
}
