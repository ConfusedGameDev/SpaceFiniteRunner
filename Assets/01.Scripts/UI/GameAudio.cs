using UnityEngine;
using UnityEngine.Audio;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Static facade over the game's AudioMixer buses, so no caller has to
    /// know the mixer's layout: Master → Gameplay → (Music / FX / Voice),
    /// plus UI, PauseMusic, LoadingMusic and Cinema directly under Master.
    /// Route sources through the properties here (<see cref="Fx"/> for world
    /// sound effects, <see cref="Voice"/> for dialogue, <see cref="Ui"/> for
    /// menu blips, <see cref="Cinema"/> for a cinema clip's own sound…) and
    /// the ducks come for free.
    ///
    /// <see cref="SetPaused"/>, <see cref="SetLoading"/> and
    /// <see cref="SetCinema"/> are those ducks: they crossfade between the
    /// mixer's four snapshots — "Gameplay" (PauseMusic and LoadingMusic
    /// muted), "Paused" (the whole Gameplay bus muted, PauseMusic up),
    /// "Loading" (Gameplay AND PauseMusic muted, LoadingMusic up; UI stays
    /// audible in every mix, so the blip that started a trip is never cut)
    /// and "Cinema" (Gameplay and both musics muted, the Cinema bus up — a
    /// world-freezing cinema silences the game the way the pause menu does,
    /// while its clip's sound plays on through a bus the duck never
    /// touches; the bus is also up in Gameplay, so a cinema the world runs
    /// under is heard, and muted under pause and loading). The requests are
    /// remembered as flags and resolved together, loading winning over
    /// paused over cinema: a scene load destroys the
    /// PauseMenu of the scene it leaves, and that menu's OnDestroy un-pauses
    /// the mix — under the loading curtain that must not lift the loading
    /// duck, only clear the pause so the next scene lands on Gameplay once
    /// the curtain hands back. Snapshots are the one mixer control that
    /// still works on the buses the user-volume sliders don't touch: exposed
    /// parameters (MasterVolume, MusicVolume, SFXVolume, UIVolume,
    /// VoiceVolume) leave snapshot control the moment
    /// <see cref="UserSettings"/> sets them, which is exactly why the duck
    /// handle is the un-exposed Gameplay parent group and not the Music/FX
    /// volumes themselves. The mixer runs in UnscaledTime update mode
    /// (transitions follow Time.timeScale in the default mode), so a fade
    /// completes even though it is started on the same frame timeScale hits 0.
    /// </summary>
    public static class GameAudio
    {
        /// <summary>Snapshot names on the mixer asset — it must contain exactly these four.</summary>
        public const string GameplaySnapshot = "Gameplay";
        public const string PausedSnapshot = "Paused";
        public const string LoadingSnapshot = "Loading";
        public const string CinemaSnapshot = "Cinema";

        static AudioMixerGroup music, fx, voice, ui, pauseMusic, loadingMusic, cinema;
        static bool paused, loading, cinemaDuck;

        /// <summary>The game mixer, off the MenuTheme via UserSettings; null until one is wired there.</summary>
        public static AudioMixer Mixer => UserSettings.Mixer;

        /// <summary>Soundtrack bus — ducked while paused or loading.</summary>
        public static AudioMixerGroup Music => Find(ref music, "Music");

        /// <summary>World sound effects (car audio, pads, explosions) — ducked while paused or loading.</summary>
        public static AudioMixerGroup Fx => Find(ref fx, "FX");

        /// <summary>Dialogue / typewriter blips — ducked while paused or loading (messages freeze with the menu).</summary>
        public static AudioMixerGroup Voice => Find(ref voice, "Voice");

        /// <summary>Menu blips — NOT ducked, the pause menu itself has to stay audible.</summary>
        public static AudioMixerGroup Ui => Find(ref ui, "UI");

        /// <summary>Pause-menu music — muted during gameplay and loading, faded in by the Paused snapshot.</summary>
        public static AudioMixerGroup PauseMusic => Find(ref pauseMusic, "PauseMusic");

        /// <summary>Loading-curtain music — muted everywhere but the Loading snapshot.</summary>
        public static AudioMixerGroup LoadingMusic => Find(ref loadingMusic, "LoadingMusic");

        /// <summary>A cinema clip's own sound — outside the Gameplay bus, so the cinema duck leaves it audible; muted under pause and loading.</summary>
        public static AudioMixerGroup Cinema => Find(ref cinema, "Cinema");

        /// <summary>True while the loading duck holds the mix (a curtain is up or still fading out).</summary>
        public static bool IsLoadingMix => loading;

        /// <summary>
        /// Crossfade the in-game buses out (or back in) for a cinema that
        /// freezes the world, over <paramref name="fadeSeconds"/>. Owned by
        /// the CinemaSystem: raised as the clip starts, released once it is
        /// done or skipped. Pause and loading both outrank it.
        /// </summary>
        public static void SetCinema(bool value, float fadeSeconds = 0.6f)
        {
            cinemaDuck = value;
            Apply(fadeSeconds);
        }

        /// <summary>
        /// Crossfade the mixer into (or out of) the paused mix over
        /// <paramref name="fadeSeconds"/>. Safe to call with no mixer wired —
        /// it just does nothing, same contract as UserSettings. While the
        /// loading duck holds, only the flag is recorded — the mix stays on
        /// Loading and lands on the right snapshot when the curtain lifts.
        /// </summary>
        public static void SetPaused(bool value, float fadeSeconds = 0.4f)
        {
            paused = value;
            Apply(fadeSeconds);
        }

        /// <summary>
        /// Crossfade the mixer into (or out of) the loading mix over
        /// <paramref name="fadeSeconds"/> — everything but UI and the
        /// LoadingMusic bus goes silent. Owned by <see cref="LoadingScreen"/>;
        /// releasing it lands on Paused or Gameplay, whichever is current.
        /// </summary>
        public static void SetLoading(bool value, float fadeSeconds = 0.4f)
        {
            loading = value;
            Apply(fadeSeconds);
        }

        static void Apply(float fadeSeconds)
        {
            AudioMixer mixer = Mixer;
            if (mixer == null) return;
            // Snapshot transitions follow Time.timeScale in the default
            // update mode — and this is called right after timeScale hits 0,
            // which would freeze the fade half-done. The asset ships with
            // UnscaledTime; re-assert it here so the duck can never regress
            // if the mixer is rebuilt or swapped.
            mixer.updateMode = AudioMixerUpdateMode.UnscaledTime;
            string name = loading ? LoadingSnapshot : paused ? PausedSnapshot : cinemaDuck ? CinemaSnapshot : GameplaySnapshot;
            AudioMixerSnapshot snapshot = mixer.FindSnapshot(name);
            if (snapshot == null)
            {
                Debug.LogWarning($"[GameAudio] the mixer has no '{name}' snapshot — see GameAudio for the required layout.", mixer);
                return;
            }
            // 0 would snap with a click; a frame-ish minimum keeps "instant" smooth.
            snapshot.TransitionTo(Mathf.Max(0.02f, fadeSeconds));
        }

        // The flags describe the running game; with domain reload disabled a
        // new play session would otherwise inherit the last one's state.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            paused = loading = cinemaDuck = false;
            music = fx = voice = ui = pauseMusic = loadingMusic = cinema = null;
        }

        // FindMatchingGroups matches by substring, so "Music" also returns
        // "PauseMusic" and "LoadingMusic" — filter down to the exact name.
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
