using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Audio
{
    /// <summary>
    /// Every clip and feel knob of the ship's own sounds in one designer
    /// asset: the power-up pickup (pitched up by orb tier), the speed-driven
    /// engine loop, the jump takeoff, the lateral dash and the barrel roll.
    /// <see cref="ShipAudio"/> reads it live
    /// (no runtime clone — the inline inspector and the GameSettings foldout
    /// tune the running scene). Paired values are single min-max bands
    /// unpacked by accessors so gameplay never touches <c>.x</c>/<c>.y</c>.
    /// Lives in Resources like the music asset; <see cref="Load"/> falls back
    /// to an in-memory default — every clip empty, so a missing asset means
    /// silence, never an exception.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_Sfx", menuName = "FiniteRunner/Sound Effects Settings")]
    public class RunnerSfxSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_Sfx";

        // ------------------------------------------------------------ power-up
        [TitleGroup("Power-up")]
        [Tooltip("One-shot on a boost orb pickup (FX bus). Empty = silent.")]
        public AudioClip powerUpClip;

        [TitleGroup("Power-up")]
        [PropertyRange(0f, 1f)]
        public float powerUpVolume = 0.9f;

        [TitleGroup("Power-up")]
        [Tooltip("Pitch of the pickup at a green (1×) orb .. at a purple (10×) orb, on a log scale so blue (2.5×) lands at 0.4 of the band.")]
        [MinMaxSlider(0.5f, 2f, true)]
        public Vector2 powerUpPitchBand = new(1f, 1.5f);

        [TitleGroup("Power-up")]
        [Tooltip("One-shot on a brake pad hit (FX bus). Empty = silent — the default: brakes are not power-ups.")]
        public AudioClip brakeClip;

        [TitleGroup("Power-up")]
        [PropertyRange(0f, 1f)]
        public float brakeVolume = 0.9f;

        // -------------------------------------------------------------- engine
        [TitleGroup("Engine")]
        [Tooltip("The engine loop (FX bus). Empty = silent.")]
        public AudioClip engineClip;

        [TitleGroup("Engine")]
        [Tooltip("Loop volume at a standstill .. at Light Speed — the engine gets stronger the faster the ship goes.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 engineVolumeBand = new(0.25f, 0.9f);

        [TitleGroup("Engine")]
        [Tooltip("Loop pitch at a standstill .. at Light Speed.")]
        [MinMaxSlider(0.5f, 3f, true)]
        public Vector2 enginePitchBand = new(0.8f, 2f);

        [TitleGroup("Engine")]
        [Tooltip("How sharply the loop follows the speed: the smoothing of the speed term (higher = snappier). Real time.")]
        [PropertyRange(1f, 20f)]
        public float engineResponse = 6f;

        [TitleGroup("Engine")]
        [Tooltip("How long the loop takes to drop out when the sim freezes (run over, tuning screen) and to come back on a relaunch. Real time.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("s", true)]
        public float engineFadeSeconds = 0.4f;

        [TitleGroup("Engine")]
        [Tooltip("Multiply the loop's pitch by the world clock, so the loop's slow motion drags the engine down with the picture. Unity never ties pitch to timeScale on its own.")]
        public bool engineFollowsClock = true;

        // ---------------------------------------------------------------- jump
        [TitleGroup("Jump")]
        [Tooltip("One-shot the frame the ship leaves a ramp's lip (FX bus). Empty = silent.")]
        public AudioClip jumpClip;

        [TitleGroup("Jump")]
        [PropertyRange(0f, 1f)]
        public float jumpVolume = 0.8f;

        // ---------------------------------------------------------------- dash
        [TitleGroup("Dash")]
        [Tooltip("One-shot on a lateral dash on the track (FX bus). An airborne dash is a barrel roll and plays the roll clip instead. Empty = silent.")]
        public AudioClip dashClip;

        [TitleGroup("Dash")]
        [PropertyRange(0f, 1f)]
        public float dashVolume = 0.8f;

        [TitleGroup("Dash")]
        [Tooltip("One-shot the frame an airborne dash starts its barrel roll (FX bus). Empty = silent.")]
        public AudioClip barrelRollClip;

        [TitleGroup("Dash")]
        [PropertyRange(0f, 1f)]
        public float barrelRollVolume = 0.8f;

        // ----------------------------------------------------------- accessors
        /// <summary>Pickup pitch at a 1× orb (band X).</summary>
        public float PowerUpPitchMin => powerUpPitchBand.x;

        /// <summary>Pickup pitch at a 10× orb (band Y).</summary>
        public float PowerUpPitchMax => powerUpPitchBand.y;

        /// <summary>Engine volume at a standstill (band X).</summary>
        public float EngineVolumeIdle => engineVolumeBand.x;

        /// <summary>Engine volume at Light Speed (band Y).</summary>
        public float EngineVolumeFull => engineVolumeBand.y;

        /// <summary>Engine pitch at a standstill (band X).</summary>
        public float EnginePitchIdle => enginePitchBand.x;

        /// <summary>Engine pitch at Light Speed (band Y).</summary>
        public float EnginePitchFull => enginePitchBand.y;

        // ------------------------------------------------------------- loading
        /// <summary>The shipped asset from Resources, or an in-memory default (no clips — silent) when it is missing.</summary>
        public static RunnerSfxSettings Load()
        {
            var asset = Resources.Load<RunnerSfxSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static RunnerSfxSettings CreateDefault()
        {
            var settings = CreateInstance<RunnerSfxSettings>();
            settings.name = "RunnerSfxSettings (default)";
            return settings;
        }
    }
}
