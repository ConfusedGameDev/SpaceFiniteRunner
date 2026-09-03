using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema
{
    /// <summary>
    /// The mission cinema player: a scene-lifetime system hand-placed under
    /// ===SYSTEMS=== (SceneSystemsPlacer puts it there; <see cref="Ensure"/>
    /// is the find-or-create fallback for an older scene) that plays a
    /// <see cref="LevelObjective"/>'s clip in one of the
    /// <see cref="CinemaFormatLibrary"/>'s formats with the world frozen
    /// under it. On Awake it builds its own overlay canvas and ONE HOLDER PER
    /// FORMAT — a RectTransform anchored to the format's viewport, framing a
    /// RawImage — so the holders are real objects in the scene while the
    /// asset stays the single source of truth for where they sit.
    ///
    /// A cinema FREEZES THE WORLD BY DEFAULT: playing sets
    /// <c>Time.timeScale = 0</c>, which is what stops the cars (rigidbodies
    /// driven from FixedUpdate) and, through the existing
    /// <c>timeScale &gt; 0</c> gates, keeps the pause menu, city map and
    /// camera cycle shut; the VideoPlayer runs on the DSP clock, so it plays
    /// through the freeze, and every animation here runs on unscaled time.
    /// A caller can ask for the opposite (<c>pauseGame</c> false on the step
    /// or the trigger): the game keeps running under the picture, so the
    /// clip, the countdown and the slide ride the GAME clock instead — a
    /// pause menu opened over such a cinema freezes it too, and the canvas
    /// hides while someone else holds the clock at 0 so the picture never
    /// sits over the menu. <see cref="IsPlaying"/> says a cinema is up,
    /// <see cref="IsFrozen"/> that it is the one holding the world still.
    /// ONE CINEMA AT A TIME: a request while one is up ends the old one at
    /// once (its caller IS called back — its cinema is over, and a level
    /// gate or a trigger cooldown waiting on it must not stall) and the new
    /// one starts in the same call.
    /// The clip is PREPARED before the holder is revealed (a fresh
    /// RenderTexture shows black for a few frames otherwise), a bad clip or a
    /// stalled prepare ends the cinema instead of leaving the game frozen,
    /// and the clip is never <c>Stop()</c>ped while visible — the duration is
    /// authoritative, so a clip that ends early just holds its last frame in
    /// the texture until the countdown runs out.
    ///
    /// Skip is a LONG PRESS of Enter / A (Space is the car's handbrake), shown
    /// as a radial ring that fills over the library's hold time and drains
    /// on release; it only arms after the theme's input grace AND one seen
    /// release, so a button held through the mission brief's collapse cannot
    /// pre-charge it. Finishing unfreezes time BEFORE the callback runs: the
    /// RPG dialogue box types on scaled time, so a briefing line queued from
    /// the callback must land on a running clock. Dying mid-cinema (scene
    /// unload, play-mode exit) restores time and drops the callback, the
    /// MissionBriefScreen rule.
    /// </summary>
    public class CinemaSystem : MonoBehaviour
    {
        const int SortingOrder = 22;            // above the RPG box (15), thunder (18) and pause menu (20); below the mission brief (24) and game over (25)
        const float PrepareTimeoutSeconds = 5f; // unscaled; a clip that never prepares must not freeze the run
        const float RingSize = 96f;
        const float RingMargin = 44f;
        const float HoldDrainSeconds = 0.15f;   // a released hold empties the ring this fast
        const int MaxTextureWidth = 1920;

        enum Phase { Idle, Preparing, SlidingIn, Playing, SlidingOut }

        /// <summary>One built holder: the format it draws and the rects the slide and the clip aspect drive.</summary>
        class Holder
        {
            public CinemaFormat format;
            public RectTransform root;
            public RawImage video;
            public AspectRatioFitter clipFitter;
            public Vector2 slideStart;
        }

        public static CinemaSystem Instance { get; private set; }

        /// <summary>True from the moment a cinema starts until it hands back — frozen world or not.</summary>
        public static bool IsPlaying { get; private set; }

        /// <summary>True while the cinema that is up is the one holding <c>Time.timeScale</c> at 0.</summary>
        public static bool IsFrozen { get; private set; }

        [Tooltip("The display formats this system builds holders for. Empty = the Resources asset (or the built-in defaults).")]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public CinemaFormatLibrary library;

        MenuTheme theme;
        Canvas canvas;
        CanvasGroup canvasGroup; // hides a non-freezing cinema while another owner holds the clock at 0 (the pause menu)
        RectTransform canvasRect;
        Image backdrop;
        readonly List<Holder> holders = new();
        Holder active;
        VideoPlayer video;
        AudioSource voice;
        AudioSource ui;
        RenderTexture texture;

        RectTransform skipRoot;
        CanvasGroup skipGroup;
        Image ring;
        Image glyph;
        Text keyLabel;

        Phase phase;
        bool frozen;          // this cinema set timeScale to 0 and owes a 1 on the way out
        float slide;          // 0 = off screen, 1 = home
        float prepareTimer;
        float remaining;
        float hold;
        bool armed;
        float openedTime;
        System.Action callback;

        /// <summary>
        /// The scene's cinema system: the hand-placed one (a DISABLED one
        /// means cinemas are switched off — null, so the caller briefs
        /// without one), else one created under ===SYSTEMS===.
        /// </summary>
        public static CinemaSystem Ensure(Scene scene)
        {
            if (Instance != null) return Instance.isActiveAndEnabled ? Instance : null;
            var existing = FindAnyObjectByType<CinemaSystem>(FindObjectsInactive.Include);
            if (existing != null) return existing.isActiveAndEnabled ? existing : null;

            var go = new GameObject("CinemaSystem");
            SceneHierarchy.Adopt(go, SceneHierarchy.Systems(scene), worldPositionStays: false);
            return go.AddComponent<CinemaSystem>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("CinemaSystem: a second instance was found — the hand-placed one wins, destroying this one.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (library == null) library = CinemaFormatLibrary.Load();
            theme = MenuTheme.Load();
            Build();
        }

        void OnEnable() => InputPromptBinder.DeviceChanged += RefreshGlyph;

        // Losing the component mid-cinema (scene unload, play-mode exit,
        // someone disabling it) must never leave the game frozen — and never
        // fires the callback: the level is going away with us.
        void OnDisable()
        {
            InputPromptBinder.DeviceChanged -= RefreshGlyph;
            if (phase != Phase.Idle) Cancel();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ build

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvasRect = (RectTransform)transform;
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // No GraphicRaycaster: nothing here is clickable.

            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            var dimGo = new GameObject("Backdrop", typeof(RectTransform));
            Stretch((RectTransform)dimGo.transform, canvasRect);
            backdrop = dimGo.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0f);
            backdrop.raycastTarget = false;

            if (library.formats != null)
                foreach (CinemaFormat format in library.formats)
                    if (format != null && !string.IsNullOrEmpty(format.id))
                        holders.Add(BuildHolder(format));

            BuildSkipPrompt();

            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.source = VideoSource.VideoClip;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.isLooping = false;
            video.waitForFirstFrame = true;
            video.skipOnDrop = true;
            video.timeUpdateMode = VideoTimeUpdateMode.DSPTime; // per play: the audio clock (immune to timeScale) under a freeze, GameTime when the world runs on
            video.prepareCompleted += OnPrepared;
            video.errorReceived += OnVideoError;
            video.loopPointReached += OnClipEnded;

            // The clip's sound rides the Cinema bus, outside the Gameplay bus
            // the freeze ducks, so the video stays audible while the game
            // goes quiet (an old mixer without the bus falls back to Voice).
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.outputAudioMixerGroup = GameAudio.Cinema != null ? GameAudio.Cinema : GameAudio.Voice;

            canvas.enabled = false;
        }

        /// <summary>
        /// One holder: the root anchored to the format's viewport, a Panel
        /// (fitted to the fixed aspect when one is set) drawing the frame,
        /// an inset for the frame padding, and the RawImage the clip lands
        /// in — letterboxed by its own fitter when the format keeps the clip
        /// aspect. Inactive until it is the one playing.
        /// </summary>
        Holder BuildHolder(CinemaFormat format)
        {
            var rootGo = new GameObject($"Holder_{format.id}", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(canvasRect, false);
            root.anchorMin = new Vector2(format.viewport.xMin, format.viewport.yMin);
            root.anchorMax = new Vector2(format.viewport.xMax, format.viewport.yMax);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = root.offsetMax = Vector2.zero;

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            var panel = Stretch((RectTransform)panelGo.transform, root);
            var frame = panelGo.AddComponent<Image>();
            frame.color = format.frameColor;
            frame.raycastTarget = false;
            if (format.fixedAspect > 0f)
            {
                var fitter = panelGo.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = format.fixedAspect;
            }

            var insetGo = new GameObject("Inset", typeof(RectTransform));
            var inset = Stretch((RectTransform)insetGo.transform, panel);
            inset.offsetMin = new Vector2(format.framePadding, format.framePadding);
            inset.offsetMax = new Vector2(-format.framePadding, -format.framePadding);

            var videoGo = new GameObject("Video", typeof(RectTransform));
            Stretch((RectTransform)videoGo.transform, inset);
            var raw = videoGo.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            AspectRatioFitter clipFitter = null;
            if (format.keepClipAspect)
            {
                clipFitter = videoGo.AddComponent<AspectRatioFitter>();
                clipFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                clipFitter.aspectRatio = 16f / 9f;
            }

            rootGo.SetActive(false);
            return new Holder { format = format, root = root, video = raw, clipFitter = clipFitter };
        }

        /// <summary>
        /// The skip widget, bottom-right: a dim ring under the filling ring
        /// (Radial360 from the top, set AFTER MakeImage, which forces Simple),
        /// the confirm glyph or key name inside it — whichever matches the
        /// device, refreshed like PromptHint — and the localized HOLD TO SKIP
        /// caption to its left.
        /// </summary>
        void BuildSkipPrompt()
        {
            var go = new GameObject("SkipPrompt", typeof(RectTransform));
            skipRoot = (RectTransform)go.transform;
            skipRoot.SetParent(canvasRect, false);
            skipRoot.anchorMin = skipRoot.anchorMax = new Vector2(1f, 0f);
            skipRoot.pivot = new Vector2(1f, 0f);
            skipRoot.anchoredPosition = new Vector2(-RingMargin, RingMargin);
            skipRoot.sizeDelta = new Vector2(RingSize, RingSize);
            skipGroup = go.AddComponent<CanvasGroup>();
            skipGroup.blocksRaycasts = false;

            Sprite ringSprite = UiSprites.Ring(128, 14);
            var ringSize = new Vector2(RingSize, RingSize);
            var track = MenuScreen.MakeImage("Track", skipRoot, Vector2.zero, ringSize, ringSprite, theme.TextDim);
            var trackColor = track.color;
            trackColor.a = 0.3f;
            track.color = trackColor;

            ring = MenuScreen.MakeImage("Fill", skipRoot, Vector2.zero, ringSize, ringSprite, theme.Accent);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;

            glyph = MenuScreen.MakeImage("Glyph", skipRoot, Vector2.zero, new Vector2(44f, 44f),
                                         InputPromptBinder.Glyph(theme, PromptAction.Confirm), theme.TextPrimary);
            keyLabel = MenuScreen.MakeText("Key", skipRoot, Vector2.zero, new Vector2(RingSize, 30f),
                                           InputPromptBinder.KeyLabel(PromptAction.Confirm), 18,
                                           theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            var caption = MenuScreen.MakeText("Caption", skipRoot, new Vector2(-RingSize * 0.5f - 18f, 0f),
                                              new Vector2(600f, 40f), MenuTextLibrary.Load().Get(MenuTextId.HoldToSkip),
                                              26, theme.TextDim, theme.BodyFont, TextAnchor.MiddleRight);
            caption.rectTransform.pivot = new Vector2(1f, 0.5f);
            LocalizedLabel.Bind(caption, MenuTextId.HoldToSkip);

            RefreshGlyph(InputPromptBinder.Device);
            go.SetActive(false);
        }

        void RefreshGlyph(PromptDevice device)
        {
            if (glyph == null || keyLabel == null) return;
            bool usePad = device == PromptDevice.Gamepad && InputPromptBinder.Glyph(theme, PromptAction.Confirm) != null;
            glyph.gameObject.SetActive(usePad);
            keyLabel.gameObject.SetActive(!usePad);
        }

        static RectTransform Stretch(RectTransform rect, RectTransform parent)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        // ------------------------------------------------------------- play

        /// <summary>
        /// Play the step's cinema — freezing the world when the step asks
        /// for it (<see cref="LevelObjective.cinemaPausesGame"/>, the
        /// default); <paramref name="onFinished"/> runs once it is gone and
        /// time is running again. A step without a playable cinema finishes
        /// at once (the callback still runs, so the caller never stalls); an
        /// unknown format id falls back to the library's first row with a
        /// warning.
        /// </summary>
        public void Play(LevelObjective step, System.Action onFinished)
        {
            if (step == null || !step.HasCinema)
            {
                onFinished?.Invoke();
                return;
            }
            Play(step.cinemaClip, step.cinemaFormat, step.cinemaSeconds, step.cinemaPausesGame, onFinished);
        }

        /// <summary>The freezing form of <see cref="Play(VideoClip, string, float, bool, System.Action)"/> — the default.</summary>
        public void Play(VideoClip clip, string formatId, float seconds, System.Action onFinished)
            => Play(clip, formatId, seconds, pauseGame: true, onFinished);

        /// <summary>
        /// The raw form every caller (a level step, a <see cref="CinemaTrigger"/>)
        /// lands on: play <paramref name="clip"/> in the format under
        /// <paramref name="formatId"/> for <paramref name="seconds"/>, with
        /// the world frozen under it when <paramref name="pauseGame"/> is on
        /// and running on when it is off. No clip or no formats finishes at
        /// once — the callback still runs. A cinema already up is ENDED
        /// FIRST and its caller called back (its cinema is over; a gate
        /// waiting on it must not stall), then the new one starts — one
        /// cinema at a time, the newest wins.
        /// </summary>
        public void Play(VideoClip clip, string formatId, float seconds, bool pauseGame, System.Action onFinished)
        {
            if (clip == null || holders.Count == 0)
            {
                if (holders.Count == 0) Debug.LogWarning("CinemaSystem: the format library has no formats — skipping the cinema.", this);
                onFinished?.Invoke();
                return;
            }
            if (phase != Phase.Idle) End(invokeCallback: true);

            callback = onFinished;
            active = ResolveHolder(formatId);
            remaining = Mathf.Max(0.1f, seconds);

            // The world stops the moment the step activates, not when the
            // first frame is ready — nothing may happen under a loading clip.
            // A non-freezing cinema leaves the clock alone and rides it: the
            // clip follows game time so a pause menu halts it too.
            frozen = pauseGame;
            if (frozen)
            {
                Time.timeScale = 0f;
                Gamepad.current?.ResetHaptics();
                GameAudio.SetCinema(true, theme.CinemaAudioFade); // the in-game buses fade out under the picture, like the pause menu's duck
            }
            IsPlaying = true;
            IsFrozen = frozen;
            video.timeUpdateMode = frozen ? VideoTimeUpdateMode.DSPTime : VideoTimeUpdateMode.GameTime;
            openedTime = Time.unscaledTime;
            armed = false;
            hold = 0f;
            slide = 0f;
            prepareTimer = 0f;

            texture = CreateTexture(clip);
            video.clip = clip;
            video.targetTexture = texture;
            if (clip.audioTrackCount > 0)
            {
                video.audioOutputMode = VideoAudioOutputMode.AudioSource;
                video.EnableAudioTrack(0, true);
                video.SetTargetAudioSource(0, voice);
            }
            else video.audioOutputMode = VideoAudioOutputMode.None;

            active.video.texture = texture;
            if (active.clipFitter != null && clip.height > 0)
                active.clipFitter.aspectRatio = (float)clip.width / clip.height;
            active.slideStart = SlideStart(active.format);
            active.root.anchoredPosition = active.slideStart;

            canvas.enabled = true;
            SetBackdrop(0f);
            phase = Phase.Preparing;
            video.Prepare();
        }

        /// <summary>Tear the cinema down without calling back — the level is resetting or leaving.</summary>
        public void Cancel() => End(invokeCallback: false);

        Holder ResolveHolder(string id)
        {
            foreach (Holder holder in holders)
                if (holder.format.id == id) return holder;
            Debug.LogWarning($"CinemaSystem: no cinema format '{id}' in '{library.name}' — using '{holders[0].format.id}'.", this);
            return holders[0];
        }

        /// <summary>A texture at the clip's size (capped, keeping its aspect), cleared to black so nothing stale shows before the first frame.</summary>
        static RenderTexture CreateTexture(VideoClip clip)
        {
            int width = Mathf.Max(16, (int)clip.width);
            int height = Mathf.Max(16, (int)clip.height);
            if (width > MaxTextureWidth)
            {
                height = Mathf.Max(16, Mathf.RoundToInt(height * (MaxTextureWidth / (float)width)));
                width = MaxTextureWidth;
            }
            var rt = new RenderTexture(width, height, 0) { name = "CinemaRT" };
            rt.Create();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
            return rt;
        }

        /// <summary>Where the holder starts its slide, in canvas units: just past the edge it enters from, relative to its home.</summary>
        Vector2 SlideStart(CinemaFormat format)
        {
            Vector2 size = canvasRect.rect.size;
            if (size.x <= 0f || size.y <= 0f) size = new Vector2(1920f, 1080f); // the scaler has not run yet
            return format.slideFrom switch
            {
                SlideDirection.Left => new Vector2(-format.viewport.xMax * size.x, 0f),
                SlideDirection.Right => new Vector2((1f - format.viewport.xMin) * size.x, 0f),
                SlideDirection.Top => new Vector2(0f, (1f - format.viewport.yMin) * size.y),
                SlideDirection.Bottom => new Vector2(0f, -format.viewport.yMax * size.y),
                _ => Vector2.zero
            };
        }

        void OnPrepared(VideoPlayer source)
        {
            if (phase != Phase.Preparing) return;
            video.Play();
            active.root.gameObject.SetActive(true);
            skipRoot.gameObject.SetActive(true);
            ring.fillAmount = 0f;
            phase = Phase.SlidingIn;
            ApplySlide();
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            if (phase == Phase.Idle) return;
            Debug.LogWarning($"CinemaSystem: the clip failed to play ({message}) — ending the cinema.", this);
            End(invokeCallback: true);
        }

        // The duration is authoritative: a clip that ends first just holds its
        // last frame in the texture (Pause keeps it; Stop would not).
        void OnClipEnded(VideoPlayer source)
        {
            if (phase == Phase.Idle) return;
            video.Pause();
        }

        // ----------------------------------------------------------- update

        void Update()
        {
            if (phase == Phase.Idle) return;
            // A freezing cinema animates on unscaled time (the clock is 0 by
            // its own hand); a non-freezing one on game time, so whoever
            // else stops the clock — the pause menu — stops it too, and the
            // picture steps out of that owner's way meanwhile.
            float dt = frozen ? Time.unscaledDeltaTime : Time.deltaTime;
            canvasGroup.alpha = !frozen && Time.timeScale <= 0f ? 0f : 1f;
            InputPromptBinder.Poll(); // only the main menu polls this — the glyph must follow the device here too

            switch (phase)
            {
                case Phase.Preparing:
                    prepareTimer += Time.unscaledDeltaTime; // the watchdog runs whatever the clock does
                    if (prepareTimer >= PrepareTimeoutSeconds)
                    {
                        Debug.LogWarning("CinemaSystem: the clip did not prepare in time — ending the cinema.", this);
                        End(invokeCallback: true);
                    }
                    return;

                case Phase.SlidingIn:
                    slide = Mathf.Min(1f, slide + dt / Mathf.Max(0.001f, active.format.slideSeconds));
                    ApplySlide();
                    if (slide >= 1f) phase = Phase.Playing;
                    TickPlayback(dt);
                    return;

                case Phase.Playing:
                    TickPlayback(dt);
                    return;

                case Phase.SlidingOut:
                    slide = Mathf.Max(0f, slide - dt / Mathf.Max(0.001f, active.format.slideSeconds));
                    ApplySlide();
                    if (slide <= 0f) End(invokeCallback: true);
                    return;
            }
        }

        /// <summary>The countdown and the hold-to-skip, both running from the first visible frame.</summary>
        void TickPlayback(float dt)
        {
            remaining -= dt;
            if (remaining <= 0f)
            {
                BeginExit();
                return;
            }

            bool held = MenuNavigator.ConfirmHeld();
            if (!armed)
            {
                // Arm only on a release seen after the grace: the press that
                // accepted the brief must not start charging the ring.
                if (!held && Time.unscaledTime - openedTime >= theme.InputGrace) armed = true;
            }
            else if (held) hold += dt;
            else hold = Mathf.MoveTowards(hold, 0f, dt * library.skipHoldSeconds / HoldDrainSeconds);

            float holdSeconds = Mathf.Max(0.1f, library.skipHoldSeconds);
            ring.fillAmount = Mathf.Clamp01(hold / holdSeconds);
            if (armed && hold >= holdSeconds)
            {
                if (theme.ConfirmClip != null) ui.PlayOneShot(theme.ConfirmClip, theme.UiVolume);
                BeginExit();
            }
        }

        void BeginExit()
        {
            if (active.format.slideFrom == SlideDirection.None || active.format.slideSeconds <= 0f)
            {
                End(invokeCallback: true);
                return;
            }
            phase = Phase.SlidingOut;
        }

        void ApplySlide()
        {
            float e = theme.Ease(Mathf.Clamp01(slide));
            active.root.anchoredPosition = active.slideStart * (1f - e);
            SetBackdrop(active.format.backdropAlpha * e);
            skipGroup.alpha = e;
        }

        void SetBackdrop(float alpha)
        {
            Color color = backdrop.color;
            color.a = alpha;
            backdrop.color = color;
        }

        /// <summary>
        /// Hide, release the clip and the texture, unfreeze, THEN call back —
        /// in that order, so whatever the callback queues (the briefing line,
        /// on scaled time) starts on a running clock.
        /// </summary>
        void End(bool invokeCallback)
        {
            phase = Phase.Idle;
            if (active != null)
            {
                active.root.gameObject.SetActive(false);
                active.video.texture = null;
                active = null;
            }
            if (skipRoot != null) skipRoot.gameObject.SetActive(false);
            if (canvas != null) canvas.enabled = false;

            if (video != null)
            {
                video.Stop();
                video.targetTexture = null;
                video.clip = null;
            }
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
                texture = null;
            }

            if (frozen)
            {
                Time.timeScale = 1f; // only the clock we stopped — a non-freezing cinema never owned it
                GameAudio.SetCinema(false, theme.CinemaAudioFade); // and the game's sound fades back in
            }
            frozen = false;
            IsPlaying = false;
            IsFrozen = false;
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            System.Action finished = callback;
            callback = null;
            if (invokeCallback) finished?.Invoke();
        }
    }
}
