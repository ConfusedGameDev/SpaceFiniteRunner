using System.Collections;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Audio
{
    /// <summary>
    /// The car radio: a hand-placed scene-lifetime system under
    /// <c>===SYSTEMS===</c> (placed by <c>SceneSystemsPlacer</c>) that plays the
    /// <see cref="RadioSettings"/> playlist through the Music bus — so the
    /// pause menu's snapshot duck mutes it for free — and announces every song
    /// on the RPG box ("Now Playing: …"). Playlist rules: bundled clips first,
    /// then whatever the streaming folder held when the level started
    /// (loaded in the background and appended as each file decodes, so a slow
    /// folder never delays the first song); a finished song hands to the next,
    /// and the end of the list wraps to the first. Controls, read only over
    /// live gameplay (a running clock, no main menu, no cinema, no loading
    /// curtain — the pause menu spends the d-pad on its sliders): pad RIGHT /
    /// key 6 next song, pad LEFT / key 5 previous song, and a LONG press
    /// (<see cref="RadioSettings.longPressSeconds"/>) of left / 5 switches the
    /// radio OFF while a long press of right / 6 switches it back ON, resuming
    /// where it stopped. A long press fires while still held and swallows the
    /// release, so a power switch never doubles as a skip. Nothing cuts hard:
    /// every transition rides one volume fade (<see cref="RadioSettings.fadeSeconds"/>,
    /// unscaled time) — a song change fades the old song out, THEN swaps and
    /// fades the new one in; power off fades out and pauses at silence; power
    /// on resumes and fades in — and a request landing mid-fade simply replaces
    /// what happens at silence, so mashing skip lands on the last song asked
    /// for. Streamed clips are owned here and destroyed with the system;
    /// bundled clips are assets and are never touched.
    /// </summary>
    public class RadioSystem : MonoBehaviour
    {
        public static RadioSystem Instance { get; private set; }

        [InlineEditor]
        [Tooltip("Playlist and feel. Empty = the PoliceEscape_Radio asset from Resources.")]
        public RadioSettings settings;

        readonly List<AudioClip> playlist = new();
        readonly List<AudioClip> streamed = new(); // clips this system created — destroyed with it
        AudioSource source;
        int index = -1;
        bool on;
        bool waitingForSongs; // switched on before any song was available — starts as soon as one lands
        float level;        // 0..1 fade level; the source plays at settings.volume × level
        float levelTarget;  // where the fade is heading
        System.Action atSilence; // what a fade-out is for — the clip swap or the pause — run when the level reaches 0
        readonly HoldButton left = new();
        readonly HoldButton right = new();

        /// <summary>True while the radio is switched on (a song may still be loading).</summary>
        public bool IsOn => on;

        /// <summary>The song playing (or paused by the power switch), null when none.</summary>
        public AudioClip Current => index >= 0 && index < playlist.Count ? playlist[index] : null;

        /// <summary>Bundled songs followed by the streamed ones loaded so far.</summary>
        public IReadOnlyList<AudioClip> Playlist => playlist;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("RadioSystem: a second instance was found — the hand-placed one wins, destroying this one.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (settings == null) settings = RadioSettings.Load();

            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = GameAudio.Music;
        }

        void Start()
        {
            foreach (AudioClip clip in settings.songs)
                if (clip != null && !HasSongNamed(clip.name)) playlist.Add(clip);
            if (settings.useStreamingAssets) StartCoroutine(LoadStreamingSongs());

            if (settings.startOn) SetPower(true, announce: false);
        }

        void OnDestroy()
        {
            if (source != null) source.Stop();
            foreach (AudioClip clip in streamed)
                if (clip != null) Destroy(clip);
            streamed.Clear();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ control

        /// <summary>Skips to the next song; the end of the list wraps to the first.</summary>
        [Button("Next Song"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void Next() => Play(index + 1);

        /// <summary>Skips back one song; the start of the list wraps to the last.</summary>
        [Button("Previous Song"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void Previous() => Play(index - 1);

        /// <summary>Flips the radio off / on (the inspector's test button; the pad uses one direction per state).</summary>
        [Button("Toggle Power"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void TogglePower() => SetPower(!on);

        /// <summary>
        /// Switches the radio on (resuming the paused song under a fade-in, or
        /// starting the first when nothing has played yet) or off (fading out,
        /// then pausing in place so the next switch-on picks up where it
        /// stopped). Switching on mid fade-out just turns the fade around.
        /// </summary>
        public void SetPower(bool value, bool announce = true)
        {
            if (on == value) return;
            on = value;

            if (!on)
            {
                waitingForSongs = false;
                FadeOutThen(() => { if (source.isPlaying) source.Pause(); });
                if (announce) Say(settings.radioOffText);
                return;
            }

            if (Current != null)
            {
                atSilence = null; // an unfinished power-off fade turns around instead of pausing
                source.UnPause();
                if (!source.isPlaying) source.Play();
                levelTarget = 1f;
                if (announce) Say(NowPlaying(Current));
            }
            else if (playlist.Count > 0) Play(0);
            else waitingForSongs = true; // a streamed song may still be on its way
        }

        /// <summary>
        /// Moves to song <paramref name="i"/> (wrapping both ways). Audible
        /// audio fades out first and the swap happens at silence; a silent or
        /// paused source swaps at once. Either way the new song fades in.
        /// </summary>
        void Play(int i)
        {
            if (playlist.Count == 0)
            {
                waitingForSongs = on;
                return;
            }
            int target = ((i % playlist.Count) + playlist.Count) % playlist.Count;
            waitingForSongs = false;

            if (source.isPlaying && level > 0f) FadeOutThen(() => StartClip(target));
            else StartClip(target);
        }

        void StartClip(int i)
        {
            index = i;
            atSilence = null;
            source.Stop();
            source.clip = playlist[index];
            level = 0f;
            levelTarget = 1f;
            ApplyVolume();
            source.Play();
            Say(NowPlaying(playlist[index]));
        }

        /// <summary>Heads the fade to silence and books what happens there; a later request simply replaces the booking.</summary>
        void FadeOutThen(System.Action then)
        {
            atSilence = then;
            levelTarget = 0f;
            if (settings.fadeSeconds <= 0f || level <= 0f) RunAtSilence(); // hard cuts (or already silent) don't wait a frame
        }

        void RunAtSilence()
        {
            level = 0f;
            ApplyVolume();
            var action = atSilence;
            atSilence = null;
            action?.Invoke();
        }

        void ApplyVolume() => source.volume = settings.volume * level;

        void StepFade()
        {
            if (settings.fadeSeconds <= 0f) level = levelTarget;
            else level = Mathf.MoveTowards(level, levelTarget, Time.unscaledDeltaTime / settings.fadeSeconds);
            ApplyVolume();
            if (levelTarget <= 0f && level <= 0f && atSilence != null) RunAtSilence();
        }

        string NowPlaying(AudioClip clip) => string.Format(settings.nowPlayingFormat, clip != null ? clip.name : "?");

        void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            RpgMessageSystem.Instance.ShowMessage(settings.speakerName, text, settings.messageHoldSeconds, settings.accent);
        }

        // ------------------------------------------------------------- update

        void Update()
        {
            if (source == null) return;
            StepFade(); // unscaled: a fade started before a pause still settles under it

            bool live = Time.timeScale > 0f && !MainMenuController.IsOpen && !CinemaSystem.IsFrozen && !LoadingScreen.IsLoading; // a cinema the game runs under keeps the radio's d-pad
            if (live) ReadInput();
            else { left.Reset(); right.Reset(); } // the press that opened a menu must not skip a song on its release

            if (!on) return;

            if (waitingForSongs)
            {
                if (playlist.Count > 0) Play(0);
                return;
            }

            // A song that ran out hands to the next — but not while the clock
            // is stopped (the pause duck would hide the announcement anyway),
            // while a fade-out is already carrying a booked swap, or with the
            // app in the background, where every source reads idle.
            if (live && atSilence == null && Current != null && !source.isPlaying && Application.isFocused)
                Next();
        }

        void ReadInput()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            float hold = settings.longPressSeconds;

            // Left / 5: long press switches OFF, short press = previous song.
            bool leftDown = (keyboard != null && keyboard.digit5Key.isPressed) || (pad != null && pad.dpad.left.isPressed);
            switch (left.Step(leftDown, hold))
            {
                case HoldButton.Result.Long: if (on) SetPower(false); break;
                case HoldButton.Result.Short: if (on) Previous(); break;
            }

            // Right / 6: long press switches ON, short press = next song.
            bool rightDown = (keyboard != null && keyboard.digit6Key.isPressed) || (pad != null && pad.dpad.right.isPressed);
            switch (right.Step(rightDown, hold))
            {
                case HoldButton.Result.Long: if (!on) SetPower(true); break;
                case HoldButton.Result.Short: if (on) Next(); break;
            }
        }

        /// <summary>
        /// Short-vs-long press reader for one button: Long fires once while the
        /// button is still held past the threshold, Short fires on a release
        /// that never got there — so one press yields exactly one of the two.
        /// </summary>
        class HoldButton
        {
            public enum Result { None, Short, Long }
            float heldSince = -1f;
            bool longFired;

            public Result Step(bool down, float holdSeconds)
            {
                if (down)
                {
                    if (heldSince < 0f)
                    {
                        heldSince = Time.unscaledTime;
                        longFired = false;
                    }
                    else if (!longFired && Time.unscaledTime - heldSince >= holdSeconds)
                    {
                        longFired = true;
                        return Result.Long;
                    }
                    return Result.None;
                }
                if (heldSince < 0f) return Result.None;
                heldSince = -1f;
                return longFired ? Result.None : Result.Short;
            }

            public void Reset() => heldSince = -1f;
        }

        // ---------------------------------------------------------- streaming

        bool HasSongNamed(string name)
        {
            foreach (AudioClip clip in playlist)
                if (clip != null && string.Equals(clip.name, name, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Loads every audio file in the streaming folder, one at a time, and
        /// appends each as it lands. Files sharing a bundled song's name are
        /// skipped (the Copy button seeds the folder with the bundled songs).
        /// Data stays compressed in memory and decodes on play, so a folder of
        /// long mp3s costs megabytes, not hundreds.
        /// </summary>
        IEnumerator LoadStreamingSongs()
        {
            string dir = settings.StreamingPath;
            if (!Directory.Exists(dir))
            {
                Debug.Log($"RadioSystem: no streaming folder at {dir} — playing the bundled songs only.", this);
                yield break;
            }

            string[] files = Directory.GetFiles(dir);
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (!RadioSettings.IsAudioFile(file)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (HasSongNamed(name)) continue;

                using var request = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(file).AbsoluteUri, TypeFor(file));
                ((DownloadHandlerAudioClip)request.downloadHandler).compressed = true;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"RadioSystem: could not load '{file}' — {request.error}", this);
                    continue;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null) continue;
                clip.name = name;
                streamed.Add(clip);
                playlist.Add(clip);
            }

            if (on && playlist.Count == 0) Say(settings.noSongsText);
        }

        static AudioType TypeFor(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mp3" => AudioType.MPEG,
                ".ogg" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                _ => AudioType.UNKNOWN
            };
        }
    }
}
