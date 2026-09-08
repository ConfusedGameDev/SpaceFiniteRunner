using UnityEngine;

using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Audio
{
    /// <summary>
    /// A cruiser's siren: a looping 3D source riding the car, audible only
    /// while its driver is in Chase — Patrol and Search are silent, so the
    /// wail IS the "you've been spotted" cue, and a dead cruiser's fuse burns
    /// quiet. Attached by the PatrolManager at spawn like CarHealth (the
    /// prefab stays untouched); every knob lives in the "Siren" block of the
    /// PursuitSettings the driver already carries, read live.
    ///
    /// Routed through <see cref="GameAudio.Fx"/>, so the Paused / Loading /
    /// Cinema snapshots duck it and the SFX slider scales it with no
    /// detection here. The end of the run is gated explicitly —
    /// <see cref="LevelManager.IsOver"/> (the completion handoff, the death
    /// hold before GAME OVER, the time-up line) and the
    /// <see cref="GameOverScreen"/> itself — because the world keeps running
    /// under a result panel and no snapshot catches that. Every change rides
    /// one gain fade on unscaled time (the radio's idiom); at silence the
    /// source stops so nothing plays inaudibly, and each rise restarts the
    /// loop at a random point so a fleet never wails in phase. Linear
    /// rolloff over the settings' distance band: full inside the near
    /// distance, gone at the far one — the band reads as "how far away you
    /// hear the police coming".
    /// </summary>
    [RequireComponent(typeof(PoliceCarInput))]
    public class PoliceSiren : MonoBehaviour
    {
        PoliceCarInput driver;
        CarHealth health;
        LevelManager level;
        bool levelLookedUp;
        AudioSource source;
        float gain; // 0..1 fade; the source plays at settings.sirenVolume × gain

        /// <summary>True while the wail is audible or fading.</summary>
        public bool IsWailing => source != null && source.isPlaying;

        void Awake()
        {
            driver = GetComponent<PoliceCarInput>();

            var go = new GameObject("Siren");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 1f; // a cruiser passing by should bend — the one sound in the city that moves fast
            source.priority = 64;     // above the default 128: a siren is never the voice the engine steals
            source.outputAudioMixerGroup = GameAudio.Fx;
        }

        void OnDisable()
        {
            gain = 0f;
            if (source != null) source.Stop();
        }

        void Update()
        {
            if (source == null) return;
            PursuitSettings settings = driver != null ? driver.settings : null;

            bool wanted = settings != null
                && settings.sirenEnabled
                && settings.sirenClip != null
                && driver.State == PoliceCarInput.AiState.Chase
                && !IsDead
                && !IsRunOver;

            if (wanted && !source.isPlaying) StartLoop(settings.sirenClip);

            float fade = settings != null ? settings.sirenFadeSeconds : 0f;
            float target = wanted ? 1f : 0f;
            // Unscaled: a fade caught by a pause or the death hold's frozen
            // clock still settles, under the duck or to silence.
            gain = fade <= 0f ? target : Mathf.MoveTowards(gain, target, Time.unscaledDeltaTime / fade);

            if (!source.isPlaying) return;
            if (settings != null)
            {
                // Read live so the asset's sliders tune a running chase.
                source.volume = settings.sirenVolume * gain;
                source.minDistance = settings.SirenNearDistance;
                source.maxDistance = settings.SirenFarDistance;
            }
            if (!wanted && gain <= 0f) source.Stop();
        }

        void StartLoop(AudioClip clip)
        {
            source.clip = clip;
            // Seek BEFORE Play, the last beat left out so a start never lands
            // on the loop seam; a random point keeps a fleet out of phase.
            source.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.25f));
            source.volume = 0f;
            source.Play();
        }

        // Attached by the manager before the driver's Initialize, so it is
        // there by the first Update — fetched lazily all the same.
        bool IsDead
        {
            get
            {
                if (health == null) health = GetComponent<CarHealth>();
                return health != null && health.IsDead;
            }
        }

        // One lookup: the level is a scene-lifetime object, and a scene
        // without one (a bare car test) simply never ends.
        bool IsRunOver
        {
            get
            {
                if (!levelLookedUp)
                {
                    level = FindAnyObjectByType<LevelManager>();
                    levelLookedUp = true;
                }
                return (level != null && level.IsOver) || GameOverScreen.IsOpen;
            }
        }
    }
}
