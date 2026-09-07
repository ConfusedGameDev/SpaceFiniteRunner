using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Audio
{
    /// <summary>
    /// Every knob of the runner's soundtrack in one designer asset: the loop
    /// itself, its level, whether each play starts at a random point, and the
    /// fade-in / fade-out times. <see cref="RunnerMusic"/> reads it live (no
    /// runtime clone — the inline inspector and the GameSettings foldout tune
    /// the running scene). The only place these tunables live: the system
    /// component owns no knobs of its own. Lives in Resources like the fog and
    /// speed-lines assets; <see cref="Load"/> falls back to an in-memory
    /// default, so a missing asset means silence, never an exception.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_Music", menuName = "FiniteRunner/Music Settings")]
    public class MusicSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_Music";

        // --------------------------------------------------------------- track
        [TitleGroup("Track")]
        [Tooltip("The soundtrack. Authored as a seamless loop: the source loops it and every play may start anywhere in it.")]
        public AudioClip clip;

        [TitleGroup("Track")]
        [Tooltip("Source volume at full level. The player's MUSIC slider (the mixer's MusicVolume) sits on top of this and is never touched by the system.")]
        [PropertyRange(0f, 1f)]
        public float volume = 0.8f;

        [TitleGroup("Track")]
        [Tooltip("Start every play (scene start, every RETRY) at a random point in the loop instead of its first beat.")]
        public bool randomStart = true;

        // --------------------------------------------------------------- fades
        [TitleGroup("Fades")]
        [Tooltip("Seconds the music takes to rise from silence when a play starts. Real time — unaffected by the loop slow-mo or a pause.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float fadeInSeconds = 2f;

        [TitleGroup("Fades")]
        [Tooltip("Seconds the music takes to fall to silence when the run is LOST (caught, timed out, stalled). A WIN fades over the glitch ramp + hold on GameSettings instead, so it lands silent as the Mission Complete panel opens.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float fadeOutSeconds = 1.5f;

        // ------------------------------------------------------------- loading
        /// <summary>The shipped asset from Resources, or an in-memory default (no clip — silent) when it is missing.</summary>
        public static MusicSettings Load()
        {
            var asset = Resources.Load<MusicSettings>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the C# defaults — never written to disk.</summary>
        public static MusicSettings CreateDefault()
        {
            var settings = CreateInstance<MusicSettings>();
            settings.name = "MusicSettings (default)";
            return settings;
        }
    }
}
