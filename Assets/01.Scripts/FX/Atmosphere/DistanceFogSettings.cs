using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the distance fog and its far glitch in one designer
    /// asset, re-applied every frame by <see cref="DistanceFog"/> (no runtime
    /// clone, so the inline inspector and the debug FOG page tune the live
    /// picture). Distances are metres from the camera along the view ray.
    /// The one cross-asset invariant: <see cref="fogEnd"/> must stay at or
    /// below the city's <c>CityRoot.streamEnterDistance</c>, or streamed
    /// blocks pop in ahead of the fog (the streamer warns once when it does
    /// not). Lives in Resources like the rain asset, so a scene never has to
    /// wire it; <see cref="Load"/> falls back to an in-memory default.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_DistanceFog", menuName = "FiniteRunner/Distance Fog Settings")]
    public class DistanceFogSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_DistanceFog";

        // ------------------------------------------------------------------ fog
        [TitleGroup("Fog")]
        [Tooltip("Master dial. 0 = off (the render pass is skipped entirely), 1 = the full fog and glitch below. Gameplay scales it further with DistanceFog.SetIntensity.")]
        [PropertyRange(0f, 1f)]
        public float intensity = 1f;

        [TitleGroup("Fog")]
        [Tooltip("Distance the fog starts building at — everything closer is untouched.")]
        [PropertyRange(0f, 2000f), SuffixLabel("m", true)]
        public float fogStart = 120f;

        [TitleGroup("Fog")]
        [Tooltip("Distance the fog is solid at. Keep at or below the city's streamEnterDistance so the block pop-in happens inside the fog.")]
        [PropertyRange(50f, 3000f), SuffixLabel("m", true)]
        public float fogEnd = 480f;

        [TitleGroup("Fog")]
        [Tooltip("Shape of the ramp between start and end (exponential-squared). Low = a slow build that is still see-through near the end; high = clear then a wall.")]
        [PropertyRange(0.5f, 6f)]
        public float fogDensity = 2.5f;

        [TitleGroup("Fog")]
        [Tooltip("Fog colour where it begins. Keep the values below 1 unless the fog should bloom — the pass runs before post-processing.")]
        [ColorUsage(false, true)]
        public Color fogColorNear = new(0.07f, 0.05f, 0.14f);

        [TitleGroup("Fog")]
        [Tooltip("Fog colour at the far end, also the colour dropped-out glitch blocks snap to.")]
        [ColorUsage(false, true)]
        public Color fogColorFar = new(0.30f, 0.09f, 0.42f);

        [TitleGroup("Fog")]
        [Tooltip("How much of the sky the far fog colour covers. 1 buries the skybox; lower keeps a horizon band visible.")]
        [PropertyRange(0f, 1f)]
        public float skyFogAmount = 0.85f;

        // --------------------------------------------------------------- height
        [ToggleGroup("heightFog", "Height falloff")]
        [Tooltip("Thin the fog with altitude, so rooftops and overpass decks stand out of a low-lying haze.")]
        public bool heightFog;

        [ToggleGroup("heightFog")]
        [Tooltip("How fast the fog thins per metre above the base height.")]
        [PropertyRange(0f, 0.2f), SuffixLabel("1/m", true)]
        public float heightFalloff = 0.02f;

        [ToggleGroup("heightFog")]
        [Tooltip("World height the fog is full at; above it the falloff applies.")]
        [PropertyRange(-20f, 200f), SuffixLabel("m", true)]
        public float heightBase;

        // ----------------------------------------------------------- far glitch
        [ToggleGroup("farGlitch", "Far glitch")]
        [Tooltip("Tear, drop out and colour-split the distant picture as it fogs — the far city dissolving into signal noise.")]
        public bool farGlitch = true;

        [ToggleGroup("farGlitch")]
        [Tooltip("Distance the glitch starts at; it reaches full strength at the fog end.")]
        [PropertyRange(0f, 3000f), SuffixLabel("m", true)]
        public float glitchStart = 300f;

        [ToggleGroup("farGlitch")]
        [Tooltip("Overall glitch amount at the fog end.")]
        [PropertyRange(0f, 1f)]
        public float glitchStrength = 0.6f;

        [ToggleGroup("farGlitch")]
        [Tooltip("Corrupt frames per second — the glitch jumps between states at this rate rather than sliding.")]
        [PropertyRange(1f, 60f), SuffixLabel("steps/s", true)]
        public float glitchRate = 12f;

        [ToggleGroup("farGlitch")]
        [Tooltip("How far torn rows shift sideways, as a fraction of the screen.")]
        [PropertyRange(0f, 0.4f)]
        public float sliceStrength = 0.08f;

        [ToggleGroup("farGlitch")]
        [Tooltip("How many distant macroblocks drop to the far fog colour — blocks that have not streamed in yet.")]
        [PropertyRange(0f, 1f)]
        public float blockAmount = 0.5f;

        [ToggleGroup("farGlitch")]
        [Tooltip("Red/blue channel offset on the glitching band, as a fraction of the screen.")]
        [PropertyRange(0f, 0.05f)]
        public float colorSplit = 0.008f;

        [ToggleGroup("farGlitch")]
        [Tooltip("Scanline darkening on the glitching band.")]
        [PropertyRange(0f, 1f)]
        public float scanlineStrength = 0.3f;

        // --------------------------------------------------------------- camera
        [ToggleGroup("clampFarClip", "Clamp camera far clip")]
        [Tooltip("Pull the chase camera's far clip plane in to fog end + margin: nothing past the solid fog can be seen, so nothing past it needs culling or drawing. The biggest free win of the fog.")]
        public bool clampFarClip = true;

        [ToggleGroup("clampFarClip")]
        [Tooltip("Metres past the fog end the camera still draws — a safety band for the fog's last few percent.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float farClipMargin = 150f;

        /// <summary>The shipped asset from Resources, or an in-memory default when none exists.</summary>
        public static DistanceFogSettings Load()
        {
            var asset = Resources.Load<DistanceFogSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static DistanceFogSettings CreateDefault()
        {
            var settings = CreateInstance<DistanceFogSettings>();
            settings.name = "DistanceFogSettings (default)";
            return settings;
        }
    }
}
