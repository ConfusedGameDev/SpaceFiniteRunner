using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Player preferences — the three volumes and the subtitle flag. These are
    /// deliberately NOT on <see cref="GameSettings"/>: that asset is balance
    /// data shipped with the build and shared by the whole project, while these
    /// belong to whoever is sitting at the machine. They live in PlayerPrefs,
    /// save on every change, and are pushed straight at the audio mixer so a
    /// slider drag is audible while it moves.
    /// Named UserSettings rather than PlayerSettings so it can never collide
    /// with UnityEditor.PlayerSettings.
    /// </summary>
    public static class UserSettings
    {
        /// <summary>Exposed mixer parameter names. The mixer asset must expose exactly these strings.</summary>
        public const string MasterVolumeParam = "MasterVolume";
        public const string MusicVolumeParam = "MusicVolume";
        public const string SfxVolumeParam = "SFXVolume";

        /// <summary>
        /// The UI bus sits outside the pause-ducked Gameplay bus (menu blips
        /// must stay audible while paused), so the SFX slider drives it as a
        /// second parameter rather than as a child group.
        /// </summary>
        public const string UiVolumeParam = "UIVolume";

        /// <summary>Mixer floor. Log10(0) is -Infinity, which would silently poison the mixer, so every conversion clamps here instead.</summary>
        public const float MinDecibels = -80f;

        const string MasterKey = "settings.volume.master";
        const string MusicKey = "settings.volume.music";
        const string SfxKey = "settings.volume.sfx";
        const string SubtitlesKey = "settings.subtitles";
        const string LanguageKey = "settings.language";

        const float MasterDefault = 0.8f;
        const float MusicDefault = 0.7f;
        const float SfxDefault = 0.8f;
        const bool SubtitlesDefault = true;

        /// <summary>
        /// Raised whenever the subtitle preference changes. Nothing consumes it
        /// yet — RpgMessageSystem is the intended subscriber — but the event is
        /// wired now so hooking it up later touches only that class.
        /// </summary>
        public static event System.Action<bool> SubtitlesChanged;

        /// <summary>Raised whenever the language changes. Every LocalizedLabel re-fetches its string on this.</summary>
        public static event System.Action<MenuLanguage> LanguageChanged;

        static AudioMixer mixer;
        static float master = MasterDefault;
        static float music = MusicDefault;
        static float sfx = SfxDefault;
        static bool subtitles = SubtitlesDefault;
        static MenuLanguage language = MenuLanguage.English;
        static bool loaded;
        static bool warnedAboutMixer;

        /// <summary>Master volume, 0..1 linear.</summary>
        public static float MasterVolume
        {
            get { EnsureLoaded(); return master; }
            set => Apply(ref master, MasterKey, MasterVolumeParam, value);
        }

        /// <summary>Music bus volume, 0..1 linear.</summary>
        public static float MusicVolume
        {
            get { EnsureLoaded(); return music; }
            set => Apply(ref music, MusicKey, MusicVolumeParam, value);
        }

        /// <summary>SFX bus volume, 0..1 linear. Also drives the UI bus — one "effects" slider for the player.</summary>
        public static float SfxVolume
        {
            get { EnsureLoaded(); return sfx; }
            set
            {
                Apply(ref sfx, SfxKey, SfxVolumeParam, value);
                Push(UiVolumeParam, sfx);
            }
        }

        /// <summary>Whether story messages should be subtitled.</summary>
        public static bool Subtitles
        {
            get { EnsureLoaded(); return subtitles; }
            set
            {
                EnsureLoaded();
                if (subtitles == value) return;
                subtitles = value;
                PlayerPrefs.SetInt(SubtitlesKey, value ? 1 : 0);
                PlayerPrefs.Save();
                SubtitlesChanged?.Invoke(value);
            }
        }

        /// <summary>Menu (and later subtitle) language. English by default.</summary>
        public static MenuLanguage Language
        {
            get { EnsureLoaded(); return language; }
            set
            {
                EnsureLoaded();
                if (language == value) return;
                language = value;
                PlayerPrefs.SetInt(LanguageKey, (int)value);
                PlayerPrefs.Save();
                LanguageChanged?.Invoke(value);
            }
        }

        /// <summary>The mixer these preferences drive; null until a mixer asset is assigned on the MenuTheme.</summary>
        public static AudioMixer Mixer { get { EnsureLoaded(); return mixer; } }

        // Load before anything can play a sound, so the first frame is already
        // at the player's chosen levels rather than full blast.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            EnsureLoaded();

            // The mixer can stomp values applied before its first snapshot
            // settles, and a scene change is the natural moment audio state
            // resets — re-push the saved levels after every load so the
            // sliders' values survive the menu → chase → runner hand-offs.
            // (Unsubscribe first: with domain reload disabled, statics — and
            // this subscription — outlive a play session.)
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PushAll();

        /// <summary>
        /// Linear 0..1 to mixer decibels. 0 (and anything non-finite) maps to
        /// the floor instead of -Infinity — the one conversion bug that would
        /// otherwise be written to disk and never recover.
        /// </summary>
        public static float LinearToDecibels(float linear01)
        {
            if (float.IsNaN(linear01) || linear01 <= 0.0001f) return MinDecibels;
            return Mathf.Clamp(20f * Mathf.Log10(Mathf.Clamp01(linear01)), MinDecibels, 0f);
        }

        /// <summary>Re-pushes every stored value at the mixer. Call after swapping the mixer or reloading a scene.</summary>
        public static void PushAll()
        {
            EnsureLoaded();
            Push(MasterVolumeParam, master);
            Push(MusicVolumeParam, music);
            Push(SfxVolumeParam, sfx);
            Push(UiVolumeParam, sfx);
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true; // set first — PushAll() reads back through the properties

            mixer = MenuTheme.Load().Mixer;

            master = ReadVolume(MasterKey, MasterDefault);
            music = ReadVolume(MusicKey, MusicDefault);
            sfx = ReadVolume(SfxKey, SfxDefault);
            subtitles = PlayerPrefs.GetInt(SubtitlesKey, SubtitlesDefault ? 1 : 0) != 0;
            language = (MenuLanguage)Mathf.Clamp(
                PlayerPrefs.GetInt(LanguageKey, (int)MenuLanguage.English),
                (int)MenuLanguage.English, (int)MenuLanguage.French);

            PushAll();
        }

        static float ReadVolume(string key, float fallback)
        {
            float value = PlayerPrefs.GetFloat(key, fallback);
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Mathf.Clamp01(value);
        }

        static void Apply(ref float field, string key, string param, float value)
        {
            EnsureLoaded();
            value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            Push(param, value);
        }

        static void Push(string param, float linear01)
        {
            if (mixer != null)
            {
                mixer.SetFloat(param, LinearToDecibels(linear01));
                return;
            }

            // No mixer asset wired up yet. Master still has to work, so fall
            // back to the global listener; the two bus sliders simply store
            // their value until a mixer is assigned on the MenuTheme.
            if (param == MasterVolumeParam) AudioListener.volume = linear01;

            if (warnedAboutMixer) return;
            warnedAboutMixer = true;
            Debug.LogWarning($"{nameof(UserSettings)}: no AudioMixer on the MenuTheme asset — " +
                             "master volume falls back to AudioListener.volume and the music/FX sliders only store their values.");
        }
    }
}
