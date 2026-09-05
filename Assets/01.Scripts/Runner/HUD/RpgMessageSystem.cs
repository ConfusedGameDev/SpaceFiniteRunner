using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// RPG-style dialogue box: a portrait frame on the left overlapping a
    /// bottom panel with the speaker's name and a typewriter-revealed line.
    /// Singleton — auto-created on first use like FloatingTextSystem; pre-place
    /// one in the scene to wire the UnityEvents or assign a portrait sprite
    /// and type sound. A message is one speaker with one or more PAGES: each
    /// page types out, holds for the message's duration, then the next page
    /// starts; after the last one the box hides and fires its onFinished
    /// callback — the GameManager uses that to reset the run only after the
    /// game-over line has gone away. Only one message plays at a time: extra
    /// calls queue up (identical page lists are dropped).
    /// Enter / numpad Enter / gamepad A is the advance chord (never Space — it
    /// is the car's handbrake): a press while a page is typing FAST-FORWARDS
    /// it (the typewriter keeps running, just <see cref="skipSpeedMultiplier"/>
    /// times faster, so the effect survives), a press once it has typed ends
    /// the hold at once. A short lockout after each page starts and again
    /// after it finishes typing eats a double-tap, so a page can never be
    /// skipped unread. The chord is ignored while the world is frozen (the
    /// pause menu owns A there) and while <see cref="SkipInputSuppressed"/>
    /// is raised (the cinema's long-press skip shares the button). A blinking
    /// marker in the panel's corner says "press" once a page has typed.
    /// Runs on scaled time so an in-flight message freezes with the pause menu.
    /// The portrait falls back to the speaker's initial when no sprite is set,
    /// and the type blip falls back to a generated placeholder beep.
    /// </summary>
    public class RpgMessageSystem : MonoBehaviour
    {
        /// <summary>How the dialogue box shares the screen with the HUD gauges while a message is up.</summary>
        public enum HudMode
        {
            /// <summary>The box simply renders over the gauges (its canvas sorts above the HUD tier).</summary>
            DrawAboveHud,
            /// <summary>Gauges that poll <see cref="HudSuppressed"/> hide while a message is on screen and return when the queue empties.</summary>
            HideHudWhileTalking,
        }

        static RpgMessageSystem instance;

        public static RpgMessageSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<RpgMessageSystem>();
                    if (instance == null)
                        instance = new GameObject("RpgMessageSystem").AddComponent<RpgMessageSystem>();
                }
                return instance;
            }
        }

        [Tooltip("All dialogue-box look tunables live on this asset — add new knobs there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        RpgMessageStyle style;

        [Header("HUD")]
        [Tooltip("While a message is up: draw over the speedometer/map, or hide them until the message queue empties.")]
        [SerializeField] HudMode hudMode = HudMode.DrawAboveHud;

        [Header("Typewriter")]
        [SerializeField, Min(1f)] float charactersPerSecond = 45f;
        [Tooltip("Seconds a finished page stays on screen when the caller doesn't specify.")]
        [SerializeField, Min(0f)] float defaultHoldSeconds = 2.5f;

        [Header("Skip")]
        [Tooltip("Enter / A while a page is typing multiplies the typing speed by this — the page lands almost at once but still types.")]
        [SerializeField, PropertyRange(2f, 20f)] float skipSpeedMultiplier = 8f;
        [Tooltip("Seconds after a page starts, and again after it finishes typing, during which Enter / A is ignored — eats a double-tap so a page can't be skipped unread.")]
        [SerializeField, PropertyRange(0f, 0.5f), SuffixLabel("s", true)] float skipLockoutSeconds = 0.15f;

        [Header("Sound")]
        [Tooltip("Blip played while typing. Leave empty for a generated placeholder beep.")]
        [SerializeField] AudioClip typeSound;
        [SerializeField, Range(0f, 1f)] float typeSoundVolume = 0.35f;
        [Tooltip("A blip plays every Nth visible character (1 = every character). While fast-forwarding the blips keep this same rate in time, not per character.")]
        [SerializeField, Min(1)] int typeSoundEveryChars = 2;

        [Header("Portrait")]
        [Tooltip("Fallback portrait for messages that don't provide one. With no sprite at all, the frame shows the speaker's initial instead.")]
        [SerializeField] Sprite defaultAvatar;

        [Header("Events")]
        public UnityEvent onMessageStarted = new();
        [Tooltip("Fires once per PAGE, the moment it has fully typed.")]
        public UnityEvent onTypingFinished = new();
        public UnityEvent onMessageFinished = new();

        struct Message
        {
            public string speaker;
            public string[] pages;
            public float hold;
            public Color accent;
            public Sprite avatar;
            public bool playSound;
            public System.Action onFinished;
        }

        readonly Queue<Message> queue = new();
        Message current;
        bool showing;
        int pageIndex;
        bool typingDone;
        bool fastForward;
        float visibleCount;
        int shownChars;
        int blipCounter;
        float blipTimer;
        float holdTimer;
        float lockoutTimer;

        GameObject root;
        Text nameText;
        Text bodyText;
        Text initialText;
        Text continueMarker;
        Image portraitImage;
        Image portraitFrame;
        AudioSource audioSource;

        /// <summary>True while a message is on screen or waiting in the queue.</summary>
        public bool IsBusy => showing || queue.Count > 0;

        /// <summary>
        /// Raised by whoever owns the advance chord for the moment — the
        /// cinema, whose skip is a long press of the same Enter / A — so a
        /// message under it keeps playing on its own clock but cannot be
        /// fast-forwarded or dismissed. Cleared again when that owner is done
        /// (and by every fresh instance's Awake, so a scene change can never
        /// leave it stuck).
        /// </summary>
        public static bool SkipInputSuppressed { get; set; }

        /// <summary>The page currently on screen (0-based) and how many the message has — for HUD prompts and debug overlays.</summary>
        public int CurrentPage => showing ? pageIndex : 0;
        public int PageCount => showing ? current.pages.Length : 0;

        /// <summary>
        /// True while HUD gauges should stay off the screen: the system is set
        /// to <see cref="HudMode.HideHudWhileTalking"/> and a message is up or
        /// queued (IsBusy, not just showing — the gap between two queued lines
        /// must not blink the gauges back for a frame). Computed, never stored
        /// — gauges poll it in their own Update (they already re-decide
        /// visibility every frame), so there is no stale hidden state to
        /// unwind if a message is cleared mid-line.
        /// </summary>
        public static bool HudSuppressed =>
            instance != null && instance.hudMode == HudMode.HideHudWhileTalking && instance.IsBusy;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            SkipInputSuppressed = false;
            Build();
        }

        const string TestSpeaker = "Author";
        const string TestLine = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
        const string TestLine2 = "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";

        /// <summary>Editor bake: regenerates the box and leaves it visible with sample text, so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            root.SetActive(true);
            nameText.text = "PILOT";
            bodyText.text = "Preview of the dialogue box — runtime rebuilds this from the style asset.";
            initialText.text = "P";
            continueMarker.enabled = true;
        }

        /// <summary>
        /// Reverse of Build: reads the box as it currently stands in the scene
        /// — panel rect and colors, portrait rect, font sizes, text inset,
        /// continue-marker corner inset — back into the style asset and saves
        /// it. The edit-mode tuning loop: Show Test Message, move/resize the
        /// built pieces by hand in the scene view, fetch, then Rebuild Preview
        /// to confirm the asset now reproduces the tweak.
        /// </summary>
        [Button("Fetch Current Setup", ButtonSizes.Large), GUIColor(1f, 0.85f, 0.5f)]
        public void FetchCurrentSetup()
        {
            if (style == null)
            {
                Debug.LogWarning("Assign a style asset first — there is nothing to save the setup into.", this);
                return;
            }
            if (root == null || nameText == null || bodyText == null || portraitFrame == null)
            {
                Debug.LogWarning("Nothing built to fetch — press Show Test Message or Rebuild Preview, tweak the box in the scene, then fetch.", this);
                return;
            }

            var inner = (RectTransform)bodyText.rectTransform.parent; // "Background"
            var border = (RectTransform)inner.parent;                 // "Panel"
            style.borderColor = border.GetComponent<Image>().color;
            style.backgroundColor = inner.GetComponent<Image>().color;
            style.panelSideMargin = border.offsetMin.x;
            style.panelBottomMargin = border.offsetMin.y;
            style.panelHeight = border.offsetMax.y;

            var frameRect = portraitFrame.rectTransform;
            style.portraitPosition = frameRect.anchoredPosition;
            style.portraitSize = frameRect.sizeDelta;

            style.speakerFontSize = nameText.fontSize;
            style.bodyFontSize = bodyText.fontSize;
            style.textLeftInset = bodyText.rectTransform.offsetMin.x;

            if (continueMarker != null)
            {
                style.continueMarkerInset = -continueMarker.rectTransform.anchoredPosition;
                style.continueMarkerFontSize = continueMarker.fontSize;
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(style);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(style);
#endif
            Debug.Log($"Fetched the current dialogue-box setup into '{style.name}'.", style);
        }

        /// <summary>
        /// Shows a sample two-page message — "{TestSpeaker}", lorem ipsum, the
        /// default portrait, five seconds a page. In play mode it goes through
        /// the real queue (typewriter, blips, hold, the advance chord); in edit
        /// mode it rebuilds the box and leaves it fully visible, marker
        /// included, ready to be hand-tweaked and fetched.
        /// </summary>
        [Button("Show Test Message", ButtonSizes.Large), GUIColor(0.6f, 0.8f, 1f)]
        public void ShowTestMessage()
        {
            if (Application.isPlaying)
            {
                ShowMessage(TestSpeaker, new[] { TestLine, TestLine2 }, 5f, Color.white, defaultAvatar);
                return;
            }

            Build();
            root.SetActive(true);
            nameText.text = $"-{TestSpeaker}-";
            nameText.color = Color.white;
            bodyText.text = TestLine;
            portraitImage.enabled = defaultAvatar != null;
            portraitImage.sprite = defaultAvatar;
            initialText.gameObject.SetActive(defaultAvatar == null);
            initialText.text = TestSpeaker[..1];
            initialText.color = Color.white;
            portraitFrame.color = Color.Lerp(Color.white, Color.black, 0.78f);
            continueMarker.enabled = true;
            continueMarker.color = Color.white;
        }

        // Root components (canvas, scaler, audio) are reused by Build, not
        // torn down: play-mode Destroy is deferred, so removing and re-adding
        // them in the same frame would collide.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            root = null;
            nameText = bodyText = initialText = continueMarker = null;
            portraitImage = portraitFrame = null;
            audioSource = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }


        /// <summary>Shows a one-page message with the default hold time and a white accent.</summary>
        public void ShowMessage(string speaker, string text)
            => ShowMessage(speaker, text, defaultHoldSeconds, Color.white);

        /// <summary>One-page form of <see cref="ShowMessage(string, IReadOnlyList{string}, float, Color, Sprite, bool, System.Action)"/>.</summary>
        public void ShowMessage(string speaker, string text, float holdSeconds, Color accent,
                                Sprite avatar = null, bool playTypeSound = true,
                                System.Action onFinished = null)
            => ShowMessage(speaker, new[] { text }, holdSeconds, accent, avatar, playTypeSound, onFinished);

        /// <summary>
        /// Queues a message of one or more pages, all spoken by one speaker
        /// with one portrait and accent; every page holds for holdSeconds.
        /// Blank pages are dropped, and a message left with none finishes at
        /// once. Only one message plays at a time; one whose pages match a
        /// message already playing or queued is dropped (its callback still
        /// runs so a caller waiting on it can't stall).
        /// </summary>
        public void ShowMessage(string speaker, IReadOnlyList<string> pages, float holdSeconds, Color accent,
                                Sprite avatar = null, bool playTypeSound = true,
                                System.Action onFinished = null)
        {
            string[] clean = CleanPages(pages);
            if (clean.Length == 0)
            {
                onFinished?.Invoke();
                return;
            }

            if (showing && SamePages(current.pages, clean))
            {
                onFinished?.Invoke();
                return;
            }
            foreach (var queued in queue)
            {
                if (!SamePages(queued.pages, clean)) continue;
                onFinished?.Invoke();
                return;
            }

            queue.Enqueue(new Message
            {
                speaker = speaker,
                pages = clean,
                hold = holdSeconds,
                accent = accent,
                avatar = avatar,
                playSound = playTypeSound,
                onFinished = onFinished,
            });
        }

        static string[] CleanPages(IReadOnlyList<string> pages)
        {
            if (pages == null) return System.Array.Empty<string>();
            var list = new List<string>(pages.Count);
            for (int i = 0; i < pages.Count; i++)
                if (!string.IsNullOrWhiteSpace(pages[i])) list.Add(pages[i]);
            return list.ToArray();
        }

        static bool SamePages(string[] a, string[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Hides the current message and drops the queue without firing callbacks or events (used when a run restarts mid-message).</summary>
        public void ClearMessages()
        {
            queue.Clear();
            if (!showing) return;
            showing = false;
            current = default;
            root.SetActive(false);
        }

        void Update()
        {
            if (!showing)
            {
                if (queue.Count > 0) Begin(queue.Dequeue());
                return;
            }

            float dt = Time.deltaTime;
            if (lockoutTimer > 0f) lockoutTimer -= dt;
            bool advance = AdvancePressed();

            if (!typingDone)
            {
                if (advance) fastForward = true;
                TypeStep(dt);
                return;
            }

            BlinkMarker();
            holdTimer -= dt;
            if (advance || holdTimer <= 0f) AdvancePage();
        }

        // The advance chord, gated: never while the world is frozen (the
        // pause menu is reading A), never under a cinema, never inside the
        // double-tap lockout. A press the lockout swallows is lost, not
        // deferred — deferring would make the mash it guards against work.
        bool AdvancePressed()
        {
            if (SkipInputSuppressed || Time.timeScale <= 0f || lockoutTimer > 0f) return false;
            return UI.MenuNavigator.DialogueAdvancePressed();
        }

        // Reveals the characters due this frame. Blips are per-character at
        // normal speed; fast-forward switches them to a clock at the same
        // rate, so an 8x page sounds like typing, not a buzz.
        void TypeStep(float dt)
        {
            string page = current.pages[pageIndex];
            float rate = fastForward ? charactersPerSecond * skipSpeedMultiplier : charactersPerSecond;
            visibleCount += rate * dt;
            int target = Mathf.Min(page.Length, Mathf.FloorToInt(visibleCount));
            bool revealedVisible = false;
            while (shownChars < target)
            {
                char c = page[shownChars];
                shownChars++;
                if (char.IsWhiteSpace(c)) continue;
                revealedVisible = true;
                if (current.playSound && !fastForward && ++blipCounter >= typeSoundEveryChars)
                {
                    blipCounter = 0;
                    PlayBlip();
                }
            }
            bodyText.text = page.Substring(0, shownChars);

            if (fastForward && current.playSound)
            {
                blipTimer += dt;
                float interval = typeSoundEveryChars / charactersPerSecond;
                if (revealedVisible && blipTimer >= interval)
                {
                    blipTimer = 0f;
                    PlayBlip();
                }
            }

            if (shownChars >= page.Length)
            {
                typingDone = true;
                holdTimer = current.hold;
                lockoutTimer = skipLockoutSeconds;
                continueMarker.enabled = true;
                onTypingFinished.Invoke();
            }
        }

        void BlinkMarker()
        {
            float period = style != null ? style.continueMarkerBlinkSeconds : 0.8f;
            if (period <= 0f) { continueMarker.enabled = true; return; }
            continueMarker.enabled = Mathf.Repeat(Time.time, period) < period * 0.6f;
        }

        void Begin(Message message)
        {
            current = message;
            showing = true;
            pageIndex = 0;

            nameText.text = $"-{message.speaker}-";
            nameText.color = message.accent;
            continueMarker.color = message.accent;

            var sprite = message.avatar != null ? message.avatar : defaultAvatar;
            portraitImage.enabled = sprite != null;
            portraitImage.sprite = sprite;
            initialText.gameObject.SetActive(sprite == null);
            initialText.text = string.IsNullOrEmpty(message.speaker) ? "?" : message.speaker[..1];
            initialText.color = message.accent;
            portraitFrame.color = Color.Lerp(message.accent, Color.black, 0.78f);

            BeginPage();
            root.SetActive(true);
            onMessageStarted.Invoke();
        }

        // Fresh typewriter state for the page at pageIndex. The lockout starts
        // here too, so the press that ended the previous page's hold cannot
        // carry into this one.
        void BeginPage()
        {
            typingDone = false;
            fastForward = false;
            visibleCount = 0f;
            shownChars = 0;
            blipCounter = 0;
            blipTimer = 0f;
            lockoutTimer = skipLockoutSeconds;
            bodyText.text = "";
            continueMarker.enabled = false;
        }

        void AdvancePage()
        {
            if (pageIndex + 1 < current.pages.Length)
            {
                pageIndex++;
                BeginPage();
                return;
            }
            FinishCurrent();
        }

        void FinishCurrent()
        {
            root.SetActive(false);
            showing = false;
            var callback = current.onFinished;
            current = default;
            onMessageFinished.Invoke();
            callback?.Invoke();
        }

        void PlayBlip()
        {
            if (typeSound == null) typeSound = PlaceholderBlip();
            audioSource.pitch = Random.Range(0.92f, 1.08f);
            audioSource.PlayOneShot(typeSound, typeSoundVolume);
        }

        static AudioClip placeholderBlip;

        /// <summary>The retro square-wave beep, built once and shared with any other screen that types (the Mission Complete panel).</summary>
        internal static AudioClip PlaceholderBlip()
        {
            if (placeholderBlip == null) placeholderBlip = CreatePlaceholderBlip();
            return placeholderBlip;
        }

        // Retro square-wave beep so the typewriter is audible without any
        // audio asset; replace by assigning typeSound in the inspector.
        static AudioClip CreatePlaceholderBlip()
        {
            const int rate = 44100;
            const float duration = 0.045f;
            const float frequency = 880f;
            int samples = (int)(rate * duration);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float envelope = 1f - i / (float)samples;
                data[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * i / rate)) * 0.25f * envelope;
            }
            var clip = AudioClip.Create("TypewriterBlip", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Build()
        {
            TearDown();
            var s = style != null ? style : ScriptableObject.CreateInstance<RpgMessageStyle>();

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15; // above the HUD, below the pause menu's dim

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            audioSource = GetOrAdd<AudioSource>(gameObject);
            audioSource.playOnAwake = false;
            // Dialogue rides the Voice bus: it ducks with the pause snapshot,
            // matching this system's scaled-time freeze while the menu is up.
            audioSource.outputAudioMixerGroup = UI.GameAudio.Voice;

            root = new GameObject("MessageRoot", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

            // Bottom panel: light border with a deep translucent blue inside.
            // The style asset is the source of truth for this layout: tweak
            // the built rects by hand in edit mode, then Fetch Current Setup
            // to persist them — Build always reapplies from the asset.
            var border = MakeRect("Panel", rootRect);
            border.anchorMin = new Vector2(0f, 0f);
            border.anchorMax = new Vector2(1f, 0f);
            border.pivot = new Vector2(0.5f, 0f);
            border.offsetMin = new Vector2(s.panelSideMargin, s.panelBottomMargin);
            border.offsetMax = new Vector2(-s.panelSideMargin, s.panelHeight);
            border.gameObject.AddComponent<Image>().color = s.borderColor;

            var inner = MakeRect("Background", border);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);
            inner.gameObject.AddComponent<Image>().color = s.backgroundColor;

            nameText = MakeText("Speaker", inner, TextAnchor.UpperLeft, s.speakerFontSize);
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(s.textLeftInset, -16f);
            nameRect.sizeDelta = new Vector2(900f, 44f);

            bodyText = MakeText("Body", inner, TextAnchor.UpperLeft, s.bodyFontSize);
            var bodyRect = bodyText.rectTransform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(s.textLeftInset, 18f);
            bodyRect.offsetMax = new Vector2(-28f, -66f);
            bodyText.fontStyle = FontStyle.Normal;

            // Continue marker: the classic blinking triangle in the panel's
            // bottom-right corner, shown only once a page has typed. Hidden
            // until then so it never contradicts the typewriter.
            continueMarker = MakeText("Continue", inner, TextAnchor.LowerRight, s.continueMarkerFontSize);
            continueMarker.text = "▼";
            var markerRect = continueMarker.rectTransform;
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(1f, 0f);
            markerRect.pivot = new Vector2(1f, 0f);
            markerRect.anchoredPosition = -s.continueMarkerInset;
            markerRect.sizeDelta = new Vector2(s.continueMarkerFontSize * 1.5f, s.continueMarkerFontSize * 1.5f);
            continueMarker.enabled = false;

            // Portrait frame sits on the root so it can poke out above the
            // panel, like a classic RPG talking head.
            portraitFrame = MakeRect("Portrait", rootRect).gameObject.AddComponent<Image>();
            var frameRect = portraitFrame.rectTransform;
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 0f);
            frameRect.pivot = new Vector2(0f, 0f);
            frameRect.anchoredPosition = s.portraitPosition;
            frameRect.sizeDelta = s.portraitSize;

            portraitImage = MakeRect("Sprite", frameRect).gameObject.AddComponent<Image>();
            var portraitRect = portraitImage.rectTransform;
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(6f, 6f);
            portraitRect.offsetMax = new Vector2(-6f, -6f);
            portraitImage.preserveAspect = true;

            initialText = MakeText("Initial", frameRect, TextAnchor.MiddleCenter, 150);
            var initialRect = initialText.rectTransform;
            initialRect.anchorMin = Vector2.zero;
            initialRect.anchorMax = Vector2.one;
            initialRect.offsetMin = initialRect.offsetMax = Vector2.zero;

            root.SetActive(false);
        }

        static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static Text MakeText(string name, Transform parent, TextAnchor alignment, int fontSize)
        {
            var text = MakeRect(name, parent).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }
    }
}
