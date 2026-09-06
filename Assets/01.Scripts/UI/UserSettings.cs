using UnityEngine;
using UnityEngine.Audio;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Player preferences — the three volumes, the subtitle flag, the language
    /// and the three retro-filter dials of the VIDEO page. These are
    /// deliberately NOT on <see cref="GameSettings"/>: that asset is balance
    /// data shipped with the build and shared by the whole project, while these
    /// belong to whoever is sitting at the machine. They live in PlayerPrefs,
    /// save on every change, and are pushed straight at the audio mixer so a
    /// slider drag is audible while it moves; the filter dials are polled by
    /// the FX drivers every frame for the same reason.
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

        /// <summary>
        /// Dialogue blips (RpgMessageSystem) sit on their own Voice bus under
        /// Gameplay so they duck with the pause snapshot. To the player they
        /// are still "effects", so the SFX slider drives this one too — one
        /// effects slider, three buses.
        /// </summary>
        public const string VoiceVolumeParam = "VoiceVolume";

        /// <summary>Mixer floor. Log10(0) is -Infinity, which would silently poison the mixer, so every conversion clamps here instead.</summary>
        public const float MinDecibels = -80f;

        const string MasterKey = "settings.volume.master";
        const string MusicKey = "settings.volume.music";
        const string SfxKey = "settings.volume.sfx";
        const string SubtitlesKey = "settings.subtitles";
        const string LanguageKey = "settings.language";
        const string PsxFilterKey = "settings.filter.psx";
        const string VhsFilterKey = "settings.filter.vhs";
        const string CrtFilterKey = "settings.filter.crt";

        const float MasterDefault = 0.8f;
        const float MusicDefault = 0.7f;
        const float SfxDefault = 0.8f;
        const bool SubtitlesDefault = true;
        const float FilterDefault = 1f;

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
        static float psxFilter = FilterDefault;
        static float vhsFilter = FilterDefault;
        static float crtFilter = FilterDefault;
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

        /// <summary>SFX bus volume, 0..1 linear. Also drives the UI and Voice buses — one "effects" slider for the player.</summary>
        public static float SfxVolume
        {
            get { EnsureLoaded(); return sfx; }
            set
            {
                Apply(ref sfx, SfxKey, SfxVolumeParam, value);
                Push(UiVolumeParam, sfx);
                Push(VoiceVolumeParam, sfx);
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

        /// <summary>
        /// The player's dial on the PSX look, 0..1 — the VIDEO settings page.
        /// A multiplier on the scene driver's intensity (the asset's dial ×
        /// gameplay's ramp), re-read by the driver every frame, so a slider
        /// drag in the pause menu shows through the menu live — which is why
        /// there is no change event. 0 switches the filter off for this
        /// player without touching the designer's asset.
        /// </summary>
        public static float PsxFilter
        {
            get { EnsureLoaded(); return psxFilter; }
            set => ApplyFilter(ref psxFilter, PsxFilterKey, value);
        }

        /// <summary>The player's dial on the VHS tape, 0..1. See <see cref="PsxFilter"/>.</summary>
        public static float VhsFilter
        {
            get { EnsureLoaded(); return vhsFilter; }
            set => ApplyFilter(ref vhsFilter, VhsFilterKey, value);
        }

        /// <summary>The player's dial on the CRT screen, 0..1. See <see cref="PsxFilter"/>.</summary>
        public static float CrtFilter
        {
            get { EnsureLoaded(); return crtFilter; }
            set => ApplyFilter(ref crtFilter, CrtFilterKey, value);
        }

        /// <summary>The mixer these preferences drive; null until a mixer asset is assigned on the MenuTheme.</summary>
        public static AudioMixer Mixer { get { EnsureLoaded(); return mixer; } }

        // Load before anything can play a sound, so the first frame is already
        // at the player's chosen levels rather than full blast.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            EnsureLoaded();

            // The mixer applies its start snapshot on its first audio update,
            // which lands AFTER this method and after sceneLoaded — a SetFloat
            // made before it is silently overwritten. The bootstrap object
            // re-pushes one frame after every scene load, so the sliders'
            // values survive boot and the menu → chase → runner hand-offs.
            // (Ensure is idempotent: with domain reload disabled, statics
            // outlive a play session.)
            UserSettingsBootstrap.Ensure();
        }

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
            Push(VoiceVolumeParam, sfx);
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
            psxFilter = ReadVolume(PsxFilterKey, FilterDefault);
            vhsFilter = ReadVolume(VhsFilterKey, FilterDefault);
            crtFilter = ReadVolume(CrtFilterKey, FilterDefault);

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

        // A filter dial is a plain stored 0..1: nothing to push, the drivers poll it.
        static void ApplyFilter(ref float field, string key, float value)
        {
            EnsureLoaded();
            value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
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
