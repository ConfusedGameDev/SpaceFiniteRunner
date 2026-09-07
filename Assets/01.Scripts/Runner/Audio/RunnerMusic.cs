using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.Audio
{
    /// <summary>
    /// The runner's soundtrack: one looping track, started at a random point
    /// on every play (the scene's first frames and every RETRY), faded in on
    /// start and faded out when the run is won or lost.
    ///
    /// A hand-placed scene-lifetime system under <c>===SYSTEMS===</c> (placed
    /// as <c>Music</c> by Tools → FiniteRunner → Place Scene Systems), found by
    /// <see cref="Apply"/> and NEVER spawned — systems live in the scene so
    /// they can be tuned before play. Its knobs all live on the
    /// <see cref="MusicSettings"/> asset it reads live.
    ///
    /// The source is routed to <see cref="GameAudio.Music"/>, so the Paused /
    /// Loading / Cinema snapshots duck it for free — a pause needs no
    /// detection here, the mixer fades the whole Gameplay bus and brings the
    /// pause loop in. The win/lose fades are this source's own volume, stepped
    /// on unscaled time (the win wind-down runs under a stopped clock), and
    /// never touch an exposed mixer parameter: <c>MusicVolume</c> is the
    /// player's slider and must stay under snapshot control.
    /// </summary>
    public class RunnerMusic : MonoBehaviour
    {
        public static RunnerMusic Instance { get; private set; }

        [InlineEditor]
        [Tooltip("Clip, level and fade times. Empty = the FiniteRunner_Music asset from Resources.")]
        public MusicSettings settings;

        AudioSource source;
        float level;             // 0..1 fade level; the source plays at settings.volume × level
        float levelTarget;       // where the fade is heading
        float fadeSeconds;       // how long the current fade takes end to end
        System.Action atSilence; // what a fade-out is for — run once when the level reaches 0
        bool warnedNoClip;

        /// <summary>True while the track is audible or fading (false once a fade-out has parked it).</summary>
        public bool IsPlaying => source != null && source.isPlaying;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("RunnerMusic: a second instance was found — the hand-placed one wins, destroying this one.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (settings == null) settings = MusicSettings.Load();

            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true; // the track is one big loop: the end hands straight back to the start
            source.spatialBlend = 0f;
            source.priority = 0; // music is never the voice the engine steals
            source.outputAudioMixerGroup = GameAudio.Music;
        }

        void Start()
        {
            // The scene's first frames get the fade-in on their own — the
            // manager only has to find this object, not start it.
            Play();
        }

        void OnDisable()
        {
            // Parked (music off on GameSettings) = silent, and a fade in
            // flight is dropped with its booking.
            atSilence = null;
            if (source != null) source.Stop();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// The owner's boot call: finds the scene's hand-placed system, pushes
        /// an override asset onto it when one is given, and parks it when the
        /// owner says off. NEVER creates one — a missing system is a
        /// scene-setup error (run Tools → FiniteRunner → Place Scene Systems).
        /// Returns null when off or missing.
        /// </summary>
        public static RunnerMusic Apply(bool enabled, MusicSettings settings = null)
        {
            RunnerMusic system = Instance != null
                ? Instance
                : FindAnyObjectByType<RunnerMusic>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null)
            {
                Debug.LogError($"{nameof(RunnerMusic)}: the scene has no Music object — place one under ===SYSTEMS=== (Tools → FiniteRunner → Place Scene Systems adds it to the open scene). Systems are never spawned at play time.");
                return null;
            }
            if (settings != null) system.settings = settings;
            if (system.settings == null) system.settings = MusicSettings.Load();
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        // ------------------------------------------------------------ control

        /// <summary>
        /// A fresh play: the loop restarts from a random point (or its start)
        /// and fades in over the asset's fade-in time. Cancels a fade-out
        /// still in flight — a RETRY pressed fast simply turns the music back
        /// around onto a new point.
        /// </summary>
        [Button("Play From Random Point"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void Play()
        {
            if (source == null) return;
            atSilence = null;
            source.Stop();

            AudioClip clip = settings != null ? settings.clip : null;
            if (clip == null)
            {
                if (!warnedNoClip)
                {
                    warnedNoClip = true;
                    Debug.LogWarning("RunnerMusic: no clip on the MusicSettings asset — the run plays without music.", this);
                }
                return;
            }

            source.clip = clip;
            // Seek BEFORE Play: the documented way to start a compressed clip
            // mid-way. The last second is left out so a start never lands on
            // the loop seam.
            source.time = settings.randomStart ? Random.Range(0f, Mathf.Max(0f, clip.length - 1f)) : 0f;
            level = 0f;
            levelTarget = 1f;
            fadeSeconds = settings.fadeInSeconds;
            ApplyVolume();
            source.Play();
        }

        /// <summary>
        /// Heads the music to silence over <paramref name="seconds"/> (the
        /// asset's fade-out time when negative) and pauses the source once it
        /// gets there, so nothing keeps playing inaudibly under a result
        /// panel. A later <see cref="Play"/> restarts it regardless.
        /// </summary>
        [Button("Fade Out"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void FadeOut(float seconds = -1f)
        {
            if (source == null) return;
            fadeSeconds = seconds < 0f ? (settings != null ? settings.fadeOutSeconds : 0f) : seconds;
            atSilence = () => { if (source.isPlaying) source.Pause(); };
            levelTarget = 0f;
            if (fadeSeconds <= 0f || level <= 0f) RunAtSilence(); // hard cuts (or already silent) don't wait a frame
        }

        // -------------------------------------------------------------- fade

        void Update()
        {
            if (source == null) return;
            // Unscaled: the win fade runs under FinishWin's stopped clock, and
            // a fade caught by a pause still settles under the duck.
            if (fadeSeconds <= 0f) level = levelTarget;
            else level = Mathf.MoveTowards(level, levelTarget, Time.unscaledDeltaTime / fadeSeconds);
            ApplyVolume();
            if (levelTarget <= 0f && level <= 0f && atSilence != null) RunAtSilence();
        }

        void RunAtSilence()
        {
            level = 0f;
            ApplyVolume();
            var action = atSilence;
            atSilence = null;
            action?.Invoke();
        }

        void ApplyVolume()
        {
            if (source != null) source.volume = (settings != null ? settings.volume : 1f) * level;
        }
    }
}
