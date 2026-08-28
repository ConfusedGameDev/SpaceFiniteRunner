using UnityEngine;
using UnityEngine.Audio;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Static facade over the game's AudioMixer buses, so no caller has to
    /// know the mixer's layout: Master → Gameplay → (Music / FX / Voice),
    /// plus UI and PauseMusic directly under Master. Route sources through
    /// the properties here (<see cref="Fx"/> for world sound effects,
    /// <see cref="Voice"/> for dialogue, <see cref="Ui"/> for menu blips…)
    /// and pause ducking comes for free.
    ///
    /// <see cref="SetPaused"/> is that ducking: it crossfades between the
    /// mixer's two snapshots — "Gameplay" (PauseMusic muted) and "Paused"
    /// (the whole Gameplay bus muted, PauseMusic up). Snapshots are the one
    /// mixer control that still works on the buses the user-volume sliders
    /// don't touch: exposed parameters (MasterVolume, MusicVolume, SFXVolume,
    /// UIVolume, VoiceVolume) leave
    /// snapshot control the moment <see cref="UserSettings"/> sets them,
    /// which is exactly why the duck handle is the un-exposed Gameplay
    /// parent group and not the Music/FX volumes themselves. The mixer runs
    /// in UnscaledTime update mode (transitions follow Time.timeScale in the
    /// default mode), so the fade completes even though it is started on the
    /// same frame timeScale hits 0.
    /// </summary>
    public static class GameAudio
    {
        /// <summary>Snapshot names on the mixer asset — it must contain exactly these two.</summary>
        public const string GameplaySnapshot = "Gameplay";
        public const string PausedSnapshot = "Paused";

        static AudioMixerGroup music, fx, voice, ui, pauseMusic;

        /// <summary>The game mixer, off the MenuTheme via UserSettings; null until one is wired there.</summary>
        public static AudioMixer Mixer => UserSettings.Mixer;

        /// <summary>Soundtrack bus — ducked while paused.</summary>
        public static AudioMixerGroup Music => Find(ref music, "Music");

        /// <summary>World sound effects (car audio, pads, explosions) — ducked while paused.</summary>
        public static AudioMixerGroup Fx => Find(ref fx, "FX");

        /// <summary>Dialogue / typewriter blips — ducked while paused (messages freeze with the menu).</summary>
        public static AudioMixerGroup Voice => Find(ref voice, "Voice");

        /// <summary>Menu blips — NOT ducked, the pause menu itself has to stay audible.</summary>
        public static AudioMixerGroup Ui => Find(ref ui, "UI");

        /// <summary>Pause-menu music — muted during gameplay, faded in by the Paused snapshot.</summary>
        public static AudioMixerGroup PauseMusic => Find(ref pauseMusic, "PauseMusic");

        /// <summary>
        /// Crossfade the mixer into (or out of) the paused mix over
        /// <paramref name="fadeSeconds"/>. Safe to call with no mixer wired —
        /// it just does nothing, same contract as UserSettings.
        /// </summary>
        public static void SetPaused(bool paused, float fadeSeconds = 0.4f)
        {
            AudioMixer mixer = Mixer;
            if (mixer == null) return;
            // Snapshot transitions follow Time.timeScale in the default
            // update mode — and this is called right after timeScale hits 0,
            // which would freeze the fade half-done. The asset ships with
            // UnscaledTime; re-assert it here so the duck can never regress
            // if the mixer is rebuilt or swapped.
            mixer.updateMode = AudioMixerUpdateMode.UnscaledTime;
            AudioMixerSnapshot snapshot = mixer.FindSnapshot(paused ? PausedSnapshot : GameplaySnapshot);
            // 0 would snap with a click; a frame-ish minimum keeps "instant" smooth.
            snapshot?.TransitionTo(Mathf.Max(0.02f, fadeSeconds));
        }

        // FindMatchingGroups matches by substring, so "Music" also returns
        // "PauseMusic" — filter down to the exact name.
        static AudioMixerGroup Find(ref AudioMixerGroup cache, string name)
        {
            if (cache != null) return cache;
            AudioMixer mixer = Mixer;
            if (mixer == null) return null;
            foreach (AudioMixerGroup group in mixer.FindMatchingGroups(name))
            {
                if (group.name != name) continue;
                cache = group;
                break;
            }
            return cache;
        }
    }
}
