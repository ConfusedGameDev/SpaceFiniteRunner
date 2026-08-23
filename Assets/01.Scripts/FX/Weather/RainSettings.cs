using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the weather in one designer-facing asset: how hard it
    /// pours, how big the volume around the camera is, which way the wind
    /// blows, what a drop looks like, and the three optional extras (ground
    /// splashes, thunder, overcast atmosphere). <see cref="RainSystem"/> re-applies the
    /// whole asset every frame, so a slider moved in play mode lands on the
    /// next frame — same live-tuning workflow as the orbit camera and the car
    /// configs; there is no runtime clone to catch.
    ///
    /// The shipped asset lives in a Resources folder (<see cref="ResourcePath"/>)
    /// like the menu theme and the debug settings, so a scene that spawns rain
    /// never has to wire a reference. <see cref="Load"/> falls back to an
    /// in-memory default, which keeps a fresh project raining rather than
    /// erroring — the same rule <c>LevelDefinition.CreateDefault</c> follows.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_Rain", menuName = "FiniteRunner/Rain Settings")]
    public class RainSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_Rain";

        // ------------------------------------------------------------ downpour
        [TitleGroup("Downpour")]
        [Tooltip("Master dial. 0 = dry, 1 = the full drop count below. Gameplay scales this further with RainSystem.SetIntensity.")]
        [PropertyRange(0f, 1f)]
        public float intensity = 0.55f;

        [TitleGroup("Downpour")]
        [Tooltip("Drops spawned per second at intensity 1 — the cost dial as much as the look one.")]
        [PropertyRange(200f, 20000f), SuffixLabel("drops/s", true)]
        public float dropsPerSecond = 5000f;

        [TitleGroup("Downpour")]
        [Tooltip("How fast a drop falls. The spread is what keeps the curtain from reading as one solid sheet.")]
        [MinMaxSlider(2f, 80f, true), SuffixLabel("m/s", true)]
        public Vector2 fallSpeed = new(18f, 28f);

        [TitleGroup("Downpour")]
        [Tooltip("Width of a drop. Length comes from the streak below, not from here.")]
        [MinMaxSlider(0.005f, 0.25f, true), SuffixLabel("m", true)]
        public Vector2 dropSize = new(0.02f, 0.05f);

        [TitleGroup("Downpour")]
        [Tooltip("Metres of streak per m/s of fall speed — drops are stretched billboards, so this is the whole 'heavy rain' feel dial.")]
        [PropertyRange(0f, 0.3f), SuffixLabel("m per m/s", true)]
        public float streakLength = 0.045f;

        // -------------------------------------------------------------- volume
        [TitleGroup("Volume")]
        [Tooltip("Half-width of the spawn box that rides with the camera — rain only ever exists inside this radius.")]
        [PropertyRange(5f, 150f), SuffixLabel("m", true)]
        public float areaRadius = 28f;

        [TitleGroup("Volume")]
        [Tooltip("How high above the camera drops are born. Higher = longer fall on screen, more particles alive at once.")]
        [PropertyRange(3f, 80f), SuffixLabel("m", true)]
        public float spawnHeight = 18f;

        [TitleGroup("Volume")]
        [Tooltip("Pushes the spawn box along the view direction, so at speed the rain is where you are about to be instead of behind you.")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        public float leadDistance = 8f;

        [TitleGroup("Volume")]
        [Tooltip("How much of the camera's own motion the drops carry. 0 = they hang in the world and you tear through them (right at walking pace); 1 = they travel with you (the only thing that reads at ship speeds, where world-static rain is gone between two frames).")]
        [PropertyRange(0f, 1f)]
        public float followSpeed = 0.5f;

        // ---------------------------------------------------------------- wind
        [TitleGroup("Wind")]
        [Tooltip("Compass direction the wind blows TOWARDS, in world degrees (0 = +Z, 90 = +X).")]
        [PropertyRange(0f, 360f), SuffixLabel("°", true)]
        public float windDirection = 210f;

        [TitleGroup("Wind")]
        [Tooltip("Steady sideways push on every drop — this is what slants the curtain.")]
        [PropertyRange(0f, 40f), SuffixLabel("m/s", true)]
        public float windSpeed = 5f;

        [TitleGroup("Wind")]
        [Tooltip("Turbulence on top of the steady wind. 0 = a dead-straight sheet.")]
        [PropertyRange(0f, 15f), SuffixLabel("m/s", true)]
        public float gustStrength = 2f;

        [TitleGroup("Wind")]
        [Tooltip("How quickly the gusts churn — low is a slow roll, high is spatter.")]
        [PropertyRange(0.01f, 2f), SuffixLabel("Hz", true)]
        public float gustFrequency = 0.25f;

        [TitleGroup("Downpour")]
        [Tooltip("Ceiling on how long a streak may actually be drawn, in metres. Streak length is per m/s, so without this a ship at light speed draws kilometre-long smears. 0 = uncapped.")]
        [PropertyRange(0f, 30f), SuffixLabel("m", true)]
        public float maxStreakLength = 3f;

        // ---------------------------------------------------------------- look
        [TitleGroup("Look")]
        [Tooltip("Tint and opacity of a drop. Rain reads best barely there — alpha does most of the work.")]
        public Color dropColor = new(0.76f, 0.85f, 0.98f, 0.45f);

        [TitleGroup("Look")]
        [Tooltip("Drop sprite. Drops are stretched along their velocity, so this wants a HORIZONTAL streak (the Kenney 'Rotated' traces). Empty = a soft streak generated in code.")]
        public Texture2D dropTexture;

        [TitleGroup("Look")]
        [Tooltip("Add the drops to the frame instead of blending over it — reads as neon-lit rain, and never darkens a bright sky.")]
        public bool additive;

        // ------------------------------------------------------------ splashes
        [ToggleGroup("splashes", "Ground splashes")]
        [Tooltip("Ripples where drops land. Rides on particle collision, which is the expensive half of the system — turn it off for the cheap mode (drops simply fade out).")]
        public bool splashes = true;

        [ToggleGroup("splashes")]
        [Tooltip("Share of landing drops that leave a ripple. Well under 1 — one splash per drop is both slower and busier than real rain.")]
        [PropertyRange(0.01f, 1f)]
        public float splashChance = 0.25f;

        [ToggleGroup("splashes")]
        [Tooltip("Diameter a ripple grows to.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("m", true)]
        public float splashSize = 0.35f;

        [ToggleGroup("splashes")]
        [Tooltip("How long a ripple takes to spread and fade.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("s", true)]
        public float splashLifetime = 0.35f;

        [ToggleGroup("splashes")]
        public Color splashColor = new(0.85f, 0.92f, 1f, 0.4f);

        [ToggleGroup("splashes")]
        [Tooltip("Ripple sprite — a soft ring. Empty = one generated in code.")]
        public Texture2D splashTexture;

        // ------------------------------------------------------------- thunder
        [ToggleGroup("thunder", "Thunder")]
        [Tooltip("Lightning: the screen is washed out every so often, and RainSystem.onThunderStrike fires with it — that event is where the thunderclap sound hangs.")]
        public bool thunder = true;

        [ToggleGroup("thunder")]
        [Tooltip("Seconds between strikes. A fresh gap is rolled out of this band as each strike begins, so the storm never falls into a rhythm.")]
        [MinMaxSlider(1f, 90f, true), SuffixLabel("s", true)]
        public Vector2 strikeInterval = new(8f, 26f);

        [ToggleGroup("thunder")]
        [Tooltip("How often lightning strikes, as a multiple of the band above: 1 = exactly that spacing, 2 = twice as often, 0.5 = half as often. The band stays the storm's CHARACTER — its spread is what keeps strikes out of a rhythm — and this is the single dial for the rate, so one slider can thicken or thin the lightning without re-authoring the spacing.")]
        [PropertyRange(0.1f, 5f), SuffixLabel("x", true)]
        public float thunderFrequency = 1f;

        [ToggleGroup("thunder")]
        [Tooltip("Storms lighter than this never strike at all — drizzle with lightning in it reads as a bug, not as weather.")]
        [PropertyRange(0f, 1f)]
        public float thunderMinIntensity = 0.15f;

        [ToggleGroup("thunder")]
        [Tooltip("Colour the screen is washed with. Its alpha is ignored — the peak below is what drives the flash.")]
        public Color flashColor = Color.white;

        [ToggleGroup("thunder")]
        [Tooltip("How white the screen goes at the brightest pop. 1 = a total white-out.")]
        [PropertyRange(0f, 1f)]
        public float flashPeak = 0.85f;

        [ToggleGroup("thunder")]
        [Tooltip("Length of a whole strike, every flicker included.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("s", true)]
        public float flashDuration = 0.55f;

        [ToggleGroup("thunder")]
        [Tooltip("Bright pops inside one strike. Lightning rarely fires once — 2-3 reads as a strike, 1 reads as a camera flash.")]
        [PropertyRange(1, 5)]
        public int flashFlickers = 3;

        // ---------------------------------------------------------- atmosphere
        [ToggleGroup("atmosphere", "Atmosphere")]
        [Tooltip("Let the rain drive the scene's fog and ambient light. These are GLOBAL render settings: the system captures them on enable and puts them back on disable, so a scene without rain can never inherit the overcast.")]
        public bool atmosphere;

        [ToggleGroup("atmosphere")]
        [ColorUsage(false, true)]
        public Color fogColor = new(0.42f, 0.46f, 0.53f);

        [ToggleGroup("atmosphere")]
        [Tooltip("Exponential fog density at intensity 1 — scaled down with the downpour.")]
        [PropertyRange(0f, 0.05f)]
        public float fogDensity = 0.008f;

        [ToggleGroup("atmosphere")]
        [Tooltip("How far the ambient light is pulled down at intensity 1. 0 = leave the lighting alone.")]
        [PropertyRange(0f, 1f)]
        public float ambientDim = 0.35f;

        /// <summary>Steady wind as a world-space velocity — what the drops are actually pushed by.</summary>
        public Vector3 WindVelocity
        {
            get
            {
                float radians = windDirection * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * windSpeed;
            }
        }

        /// <summary>The shipped asset from Resources, or an in-memory default so a project with no asset still rains.</summary>
        public static RainSettings Load()
        {
            var asset = Resources.Load<RainSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static RainSettings CreateDefault()
        {
            var settings = CreateInstance<RainSettings>();
            settings.name = "RainSettings (default)";
            return settings;
        }
    }
}
