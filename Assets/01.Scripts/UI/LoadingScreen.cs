using System.Collections;
using ConfusedGameDev.FiniteRunner.SaveData;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The PS1-era loading curtain: a full-screen backdrop with LOADING... and
    /// a filling bar, plus a slot in the bottom-right corner for the spinning
    /// disc. It owns the scene trip it covers — <see cref="Load(int)"/> and
    /// friends put the curtain up, run the load asynchronously, and tear the
    /// curtain down by themselves once the new scene has drawn its first frame,
    /// so no caller has to know when loading finished. Both games route their
    /// game-over answers through it (the runner's NO, the city's YES and NO).
    /// Rules it enforces: the object is <c>DontDestroyOnLoad</c> on its own
    /// overlay canvas above every menu (the main menu it lands on must not show
    /// through while it is still building), it animates on unscaled time (the
    /// clock it inherits is whatever the death screen left), and the bar is a
    /// TIME-driven fill, not a progress readout: Unity loads a scene in one long
    /// hitch, so a bar chasing <c>AsyncOperation.progress</c> sits at 0, freezes,
    /// and slams to 1. The hitch is the scene's ACTIVATION (its Awakes, the
    /// whole city prefab instantiating) — nothing can draw during it, so the
    /// bar is staged around it: it climbs to the half mark while the scene
    /// streams in (activation held), the activation hitch happens with the
    /// bar parked at half, and once the new scene is in the bar finishes its
    /// run under the curtain. A bar that reached the end BEFORE the hitch read
    /// as a hang; one that stops halfway and then completes reads as a load.
    /// Each frame's step is capped (the hitch counts as one ordinary step) and
    /// the whole run takes <see cref="MenuTheme.LoadingFillSeconds"/>. One
    /// trip runs at a time — a second request while one is in flight is
    /// dropped. The spinner is empty until <see cref="MenuTheme"/> carries a
    /// disc sprite; when it does, it spins at the theme's rate for free.
    /// Every scene trip goes through it except the city → runner completion
    /// handoff, which already dissolves through the maxed glitch. It also owns
    /// the trip's sound: raising the curtain crossfades the mixer into its
    /// Loading snapshot (<see cref="GameAudio.SetLoading"/> — every gameplay
    /// bus and any pause music out, the LoadingMusic bus in) and starts the
    /// theme's <see cref="MenuTheme.LoadingMusicClip"/> on that bus; lifting
    /// it fades the mix back over the same <see cref="MenuTheme.LoadingAudioFade"/>,
    /// and the object lingers invisibly for that fade before destroying
    /// itself, so the music never cuts on the frame the new scene appears.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        const int SortingOrder = 40; // above the main menu (30) and the game-over screen (25)
        const float BarWidth = 720f;
        const float BarHeight = 28f;
        const float BarFrame = 4f;
        const float TextOffsetY = 40f;
        const float BarOffsetY = -24f;
        const float SpinnerSize = 128f;
        const float SpinnerMargin = 48f;
        const float MinFillWidth = 6f; // a 9-sliced fill collapses into its caps below this
        const float MaxFrameStep = 0.05f; // the load hitch is one long frame — the bar takes it as a single step, never a jump
        const float HitchFill = 0.5f; // where the bar parks for the activation hitch — the half the player watches complete afterwards

        static LoadingScreen active;

        /// <summary>True while a curtain is up. Gameplay that polls input should stay quiet under it.</summary>
        public static bool IsLoading => active != null;

        MenuTheme theme;
        Canvas canvas;
        Image fill;
        Image spinner;
        AudioSource music;   // loops on the mixer's LoadingMusic bus — audible only in the Loading snapshot
        bool audioReleased;  // the loading duck has been handed back (Finish, or an early destroy)
        float shown; // the bar's displayed fill, 0..1 — eased toward the real progress

        /// <summary>Back to the attract screen (build index 0) under the curtain.</summary>
        public static void LoadMainMenu() => Load(0);

        /// <summary>Loads a scene by build index under the curtain.</summary>
        public static void Load(int buildIndex) => Begin(() => SceneManager.LoadSceneAsync(buildIndex));

        /// <summary>Loads a scene by name under the curtain.</summary>
        public static void Load(string sceneName) => Begin(() => SceneManager.LoadSceneAsync(sceneName));

        /// <summary>Reloads the given scene (by index when it is in the build list, by name otherwise) under the curtain.</summary>
        public static void Reload(Scene scene)
        {
            if (scene.buildIndex >= 0) Load(scene.buildIndex);
            else Load(scene.name);
        }

        static void Begin(System.Func<AsyncOperation> startLoad)
        {
            if (active != null) return; // one trip at a time

            // The screen this covers may have frozen the clock; the next scene
            // must start on a running one, and our own animation reads unscaled.
            Time.timeScale = 1f;
            // A running clock, but not play: the profile's play-time tick skips the trip.
            PlayerStats.SuspendPlayTime = true;

            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            active = go.AddComponent<LoadingScreen>();
            active.theme = MenuTheme.Load();
            active.Build();
            active.StartCoroutine(active.Run(startLoad));
        }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var backdrop = panel.AddComponent<Image>();
            var backdropColor = theme.Backdrop;
            backdropColor.a = 1f; // a curtain, not a dim: the scene being torn down must not show through
            backdrop.color = backdropColor;
            backdrop.raycastTarget = true; // swallow clicks so nothing beneath can be poked mid-load

            var label = MenuScreen.MakeText("Label", panelRect, new Vector2(0f, TextOffsetY),
                                            new Vector2(BarWidth, 72f), string.Empty, 52,
                                            theme.TextPrimary, theme.TitleFont, TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(label, MenuTextId.Loading);

            // Bar: a thin frame in the dim text colour, the track inside it, the
            // accent fill growing from the left — the slider's look, widened.
            var frame = MenuScreen.MakeImage("BarFrame", panelRect, new Vector2(0f, BarOffsetY),
                                             new Vector2(BarWidth + BarFrame * 2f, BarHeight + BarFrame * 2f),
                                             null, theme.TextDim);
            var track = MenuScreen.MakeImage("BarTrack", frame.rectTransform, Vector2.zero,
                                             new Vector2(BarWidth, BarHeight), theme.SliderTrack, backdropColor);
            fill = MenuScreen.MakeImage("BarFill", track.rectTransform, Vector2.zero,
                                        new Vector2(MinFillWidth, BarHeight), theme.SliderFill, theme.Accent);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;

            // Bottom-right spinner slot. Empty until the theme carries the disc
            // sprite; the object is always there so the layout is settled now.
            spinner = MenuScreen.MakeImage("Spinner", panelRect, Vector2.zero,
                                           new Vector2(SpinnerSize, SpinnerSize), theme.LoadingSpinner, Color.white);
            var spinnerRect = spinner.rectTransform;
            spinnerRect.anchorMin = spinnerRect.anchorMax = new Vector2(1f, 0f);
            spinnerRect.pivot = new Vector2(1f, 0f);
            spinnerRect.anchoredPosition = new Vector2(-SpinnerMargin, SpinnerMargin);
            spinner.preserveAspect = true;
            spinner.enabled = theme.LoadingSpinner != null;

            SetFill(0f);
            BuildAudio();
        }

        // Its bus is muted outside the Loading snapshot, so this source is
        // only ever heard through the loading crossfade — the same shape as
        // the pause menu's PauseMusic source. Own child object, like there.
        void BuildAudio()
        {
            var go = new GameObject("LoadingMusic");
            go.transform.SetParent(transform, false);
            music = go.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.spatialBlend = 0f;
            music.clip = theme.LoadingMusicClip;
            music.outputAudioMixerGroup = GameAudio.LoadingMusic;

            // Duck the world under the curtain: the Loading snapshot fades the
            // Gameplay bus and any pause music out and LoadingMusic in. UI
            // blips stay up, so the confirm that started the trip finishes.
            GameAudio.SetLoading(true, theme.LoadingAudioFade);
            if (music.clip != null) music.Play();
        }

        IEnumerator Run(System.Func<AsyncOperation> startLoad)
        {
            // Let the curtain draw once before the load stalls the main thread,
            // or the player sees the old scene hitch and then the curtain pop.
            yield return null;

            AsyncOperation load = startLoad();
            if (load == null)
            {
                Debug.LogError("[LoadingScreen] the scene load could not start — is the scene in the build list?", this);
                Finish();
                yield break;
            }

            // Stage 1 — streaming, activation held: the bar climbs to the half
            // mark (following the real progress when a load is genuinely long,
            // at its steady pace otherwise) and waits there for the scene.
            load.allowSceneActivation = false;
            float fillSeconds = Mathf.Max(0.05f, theme.LoadingFillSeconds);
            while (true)
            {
                bool streamed = load.progress >= 0.9f; // Unity's "loaded, waiting for activation"
                float ceiling = streamed ? HitchFill : Mathf.Min(HitchFill, load.progress / 0.9f * HitchFill);
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep);
                shown = Mathf.MoveTowards(shown, ceiling, dt / fillSeconds);
                SetFill(shown);

                if (streamed && shown >= HitchFill - 0.001f) break;
                yield return null;
            }

            // Stage 2 — the hitch: activation stalls the main thread for as
            // long as the scene's Awakes take, with the bar parked at half.
            SetFill(HitchFill);
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            // Stage 3 — the scene is in (its Awake/Start ran under the curtain):
            // the bar finishes its run at the same pace, so the load reads as
            // having continued through the stall rather than ended before it.
            while (shown < 0.999f)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep);
                shown = Mathf.MoveTowards(shown, 1f, dt / fillSeconds);
                SetFill(shown);
                yield return null;
            }
            SetFill(1f);

            // One more frame under the curtain so the new scene has drawn —
            // a menu building itself must not be seen mid-assembly.
            yield return null;
            Finish();
        }

        void Update()
        {
            if (spinner != null && spinner.enabled && theme.LoadingSpinnerSpin != 0f)
                spinner.rectTransform.Rotate(0f, 0f, -theme.LoadingSpinnerSpin * Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep));
        }

        void SetFill(float t)
        {
            if (fill == null) return;
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(MinFillWidth, BarWidth * Mathf.Clamp01(t)), BarHeight);
        }

        void Finish()
        {
            if (active == this) active = null;
            // The curtain lifts now, the sound over the theme's fade: hide
            // the canvas, hand the mix back, and let the music play out under
            // the fading bus before the object goes.
            if (canvas != null) canvas.enabled = false;
            PlayerStats.SuspendPlayTime = false;
            ReleaseAudio(theme.LoadingAudioFade);
            StartCoroutine(DestroyAfter(theme.LoadingAudioFade));
        }

        IEnumerator DestroyAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds + 0.05f); // a hair past the fade so the bus is silent first
            Destroy(gameObject);
        }

        void ReleaseAudio(float fadeSeconds)
        {
            if (audioReleased) return;
            audioReleased = true;
            GameAudio.SetLoading(false, fadeSeconds);
        }

        void OnDestroy()
        {
            if (active == this) active = null;
            PlayerStats.SuspendPlayTime = false;
            // The mixer outlives every scene: a curtain destroyed early (play
            // mode stopped mid-load) must not leave the game on the loading mix.
            ReleaseAudio(0f);
        }
    }
}
