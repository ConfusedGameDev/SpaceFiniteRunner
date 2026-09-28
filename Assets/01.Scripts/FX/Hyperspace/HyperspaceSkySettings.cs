using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every knob of the hyperspace sky in one designer asset, re-applied
    /// every frame by <see cref="HyperspaceSky"/> (no runtime clone, so the
    /// inline inspector tunes the live picture). Generic like the speed-lines
    /// asset: it knows nothing about ships or Light Speed — the owner decides
    /// when the sky is engaged and hands the driver the speed the scroll follows.
    /// Carries the <see cref="material"/> (Skybox/FiniteRunner/Hyperspace) the
    /// driver copies at play time. Lives in Resources; <see cref="Load"/>
    /// falls back to an in-memory default.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_HyperspaceSky", menuName = "FiniteRunner/Hyperspace Sky Settings")]
    public class HyperspaceSkySettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_HyperspaceSky";

        // ------------------------------------------------------------ renderer
        [TitleGroup("Renderer")]
        [Tooltip("Skybox material on the Skybox/FiniteRunner/Hyperspace shader. The driver plays a runtime COPY of it, so the asset is never written by gameplay.")]
        public Material material;

        // ---------------------------------------------------------- transition
        [TitleGroup("Transition")]
        [Tooltip("Seconds the sky takes to crossfade from the scene's skybox into hyperspace once engaged.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float fadeInSeconds = 0.6f;

        [TitleGroup("Transition")]
        [Tooltip("Seconds the sky takes to fade back to the scene's skybox once released (the ship dropped below Light Speed).")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float fadeOutSeconds = 0.8f;

        [TitleGroup("Transition")]
        [Tooltip("Burst of light the jump makes on engaging (0 = none). Fades out over Flash seconds.")]
        [PropertyRange(0f, 4f)]
        public float flashStrength = 1.5f;

        [TitleGroup("Transition")]
        [Tooltip("How long the jump's flash takes to fade.")]
        [PropertyRange(0.05f, 3f), SuffixLabel("s", true)]
        public float flashSeconds = 0.8f;

        [TitleGroup("Transition")]
        [Tooltip("How fast the tunnel's axis follows the ship's heading (exponential response, 1/s). Low = the vanishing point drifts lazily through curves.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("1/s", true)]
        public float axisResponse = 3f;

        // --------------------------------------------------------------- speed
        [TitleGroup("Speed")]
        [Tooltip("Tunnel scroll (cells per second) at the reference speed — the runner hands Light Speed.")]
        [PropertyRange(0f, 40f)]
        public float scrollSpeed = 9f;

        [TitleGroup("Speed")]
        [Tooltip("Scroll speed as a multiple of Scroll Speed, clamped to this band of speed ÷ reference: braking hard never stops the tunnel, a purple orb never makes it strobe.")]
        [MinMaxSlider(0f, 4f, true)]
        public Vector2 speedFactorBand = new(0.5f, 2f);

        // ---------------------------------------------------------------- look
        [TitleGroup("Look")]
        public Color backgroundColor = new(0.015f, 0.07f, 0.08f, 1f);

        [TitleGroup("Look")]
        public Color coreColor = new(0f, 0.01f, 0.015f, 1f);

        [TitleGroup("Look"), ColorUsage(false, true)]
        public Color streakCyan = new(0.2f, 1.6f, 1.5f, 1f);

        [TitleGroup("Look"), ColorUsage(false, true)]
        public Color streakAqua = new(0.35f, 1.1f, 1.8f, 1f);

        [TitleGroup("Look"), ColorUsage(false, true)]
        public Color streakGreen = new(0.4f, 1.8f, 0.5f, 1f);

        [TitleGroup("Look")]
        [Tooltip("Share of streaks painted green; the rest blend cyan → aqua.")]
        [PropertyRange(0f, 1f)]
        public float greenShare = 0.2f;

        [TitleGroup("Look")]
        [Tooltip("Streak brightness (HDR — above ~1 the bloom picks them up).")]
        [PropertyRange(0f, 8f)]
        public float brightness = 1.6f;

        [TitleGroup("Look")]
        [Tooltip("Angular columns round the tunnel. More = thinner, busier streaks.")]
        [PropertyRange(8, 256)]
        public int columns = 72;

        [TitleGroup("Look")]
        [Tooltip("Share of cells that hold a streak.")]
        [PropertyRange(0f, 1f)]
        public float density = 0.35f;

        [TitleGroup("Look")]
        [Tooltip("Length of one cell along the tunnel. Shorter = more, closer streaks.")]
        [PropertyRange(0.1f, 8f)]
        public float cellLength = 1.2f;

        [TitleGroup("Look")]
        [Tooltip("How much of its cell a streak fills along the tunnel.")]
        [PropertyRange(0.05f, 1f)]
        public float streakLength = 0.45f;

        [TitleGroup("Look")]
        [Tooltip("How much of its column a streak fills across.")]
        [PropertyRange(0.05f, 1f)]
        public float streakWidth = 0.55f;

        [TitleGroup("Look")]
        [Tooltip("Size of the dark vanishing point (sine of the angle off the tunnel axis).")]
        [PropertyRange(0f, 0.5f)]
        public float coreRadius = 0.12f;

        /// <summary>The Resources asset, or an in-memory default (no material — the driver then stays off).</summary>
        public static HyperspaceSkySettings Load()
        {
            var asset = Resources.Load<HyperspaceSkySettings>(ResourcePath);
            return asset != null ? asset : CreateInstance<HyperspaceSkySettings>();
        }
    }
}
