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
    /// and type sound. Only one message plays at a time: extra calls queue up
    /// (identical lines are dropped), each types out, holds for its duration,
    /// then hides and fires its onFinished callback — the GameManager uses
    /// that to reset the run only after the game-over line has gone away.
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
        [Tooltip("Seconds a finished message stays on screen when the caller doesn't specify.")]
        [SerializeField, Min(0f)] float defaultHoldSeconds = 2.5f;

        [Header("Sound")]
        [Tooltip("Blip played while typing. Leave empty for a generated placeholder beep.")]
        [SerializeField] AudioClip typeSound;
        [SerializeField, Range(0f, 1f)] float typeSoundVolume = 0.35f;
        [Tooltip("A blip plays every Nth visible character (1 = every character).")]
        [SerializeField, Min(1)] int typeSoundEveryChars = 2;

        [Header("Portrait")]
        [Tooltip("Fallback portrait for messages that don't provide one. With no sprite at all, the frame shows the speaker's initial instead.")]
        [SerializeField] Sprite defaultAvatar;

        [Header("Events")]
        public UnityEvent onMessageStarted = new();
        public UnityEvent onTypingFinished = new();
        public UnityEvent onMessageFinished = new();

        struct Message
        {
            public string speaker;
            public string text;
            public float hold;
            public Color accent;
            public Sprite avatar;
            public bool playSound;
            public System.Action onFinished;
        }

        readonly Queue<Message> queue = new();
        Message current;
        bool showing;
        bool typingDone;
        float visibleCount;
        int shownChars;
        int blipCounter;
        float holdTimer;

        GameObject root;
        Text nameText;
        Text bodyText;
        Text initialText;
        Image portraitImage;
        Image portraitFrame;
        AudioSource audioSource;

        /// <summary>True while a message is on screen or waiting in the queue.</summary>
        public bool IsBusy => showing || queue.Count > 0;

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
            Build();
        }

        const string TestSpeaker = "Author";
        const string TestLine = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

        /// <summary>Editor bake: regenerates the box and leaves it visible with sample text, so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            root.SetActive(true);
            nameText.text = "PILOT";
            bodyText.text = "Preview of the dialogue box — runtime rebuilds this from the style asset.";
            initialText.text = "P";
        }

        /// <summary>
        /// Reverse of Build: reads the box as it currently stands in the scene
        /// — panel rect and colors, portrait rect, font sizes, text inset —
        /// back into the style asset and saves it. The edit-mode tuning loop:
        /// Show Test Message, move/resize the built pieces by hand in the
        /// scene view, fetch, then Rebuild Preview to confirm the asset now
        /// reproduces the tweak.
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

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(style);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(style);
#endif
            Debug.Log($"Fetched the current dialogue-box setup into '{style.name}'.", style);
        }

        /// <summary>
        /// Shows a sample line — "{TestSpeaker}", lorem ipsum, the default
        /// portrait, five seconds. In play mode it goes through the real queue
        /// (typewriter, blips, hold); in edit mode it rebuilds the box and
        /// leaves it fully visible, ready to be hand-tweaked and fetched.
        /// </summary>
        [Button("Show Test Message", ButtonSizes.Large), GUIColor(0.6f, 0.8f, 1f)]
        public void ShowTestMessage()
        {
            if (Application.isPlaying)
            {
                ShowMessage(TestSpeaker, TestLine, 5f, Color.white, defaultAvatar);
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
        }

        // Root components (canvas, scaler, audio) are reused by Build, not
        // torn down: play-mode Destroy is deferred, so removing and re-adding
        // them in the same frame would collide.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            root = null;
            nameText = bodyText = initialText = null;
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


        /// <summary>Shows a message with the default hold time and a white accent.</summary>
        public void ShowMessage(string speaker, string text)
            => ShowMessage(speaker, text, defaultHoldSeconds, Color.white);

        /// <summary>
        /// Queues a message. Only one plays at a time; a line identical to one
        /// already playing or queued is dropped (its callback still runs so a
        /// caller waiting on it can't stall).
        /// </summary>
        public void ShowMessage(string speaker, string text, float holdSeconds, Color accent,
                                Sprite avatar = null, bool playTypeSound = true,
                                System.Action onFinished = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                onFinished?.Invoke();
                return;
            }

            if (showing && current.text == text)
            {
                onFinished?.Invoke();
                return;
            }
            foreach (var queued in queue)
            {
                if (queued.text != text) continue;
                onFinished?.Invoke();
                return;
            }

            queue.Enqueue(new Message
            {
                speaker = speaker,
                text = text,
                hold = holdSeconds,
                accent = accent,
                avatar = avatar,
                playSound = playTypeSound,
                onFinished = onFinished,
            });
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

            if (!typingDone)
            {
                visibleCount += charactersPerSecond * Time.deltaTime;
                int target = Mathf.Min(current.text.Length, Mathf.FloorToInt(visibleCount));
                while (shownChars < target)
                {
                    char c = current.text[shownChars];
                    shownChars++;
                    if (current.playSound && !char.IsWhiteSpace(c) &&
                        ++blipCounter >= typeSoundEveryChars)
                    {
                        blipCounter = 0;
                        PlayBlip();
                    }
                }
                bodyText.text = current.text.Substring(0, shownChars);

                if (shownChars >= current.text.Length)
                {
                    typingDone = true;
                    holdTimer = current.hold;
                    onTypingFinished.Invoke();
                }
                return;
            }

            holdTimer -= Time.deltaTime;
            if (holdTimer <= 0f) FinishCurrent();
        }

        void Begin(Message message)
        {
            current = message;
            showing = true;
            typingDone = false;
            visibleCount = 0f;
            shownChars = 0;
            blipCounter = 0;

            nameText.text = $"-{message.speaker}-";
            nameText.color = message.accent;
            bodyText.text = "";

            var sprite = message.avatar != null ? message.avatar : defaultAvatar;
            portraitImage.enabled = sprite != null;
            portraitImage.sprite = sprite;
            initialText.gameObject.SetActive(sprite == null);
            initialText.text = string.IsNullOrEmpty(message.speaker) ? "?" : message.speaker[..1];
            initialText.color = message.accent;
            portraitFrame.color = Color.Lerp(message.accent, Color.black, 0.78f);

            root.SetActive(true);
            onMessageStarted.Invoke();
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
            if (typeSound == null) typeSound = CreatePlaceholderBlip();
            audioSource.pitch = Random.Range(0.92f, 1.08f);
            audioSource.PlayOneShot(typeSound, typeSoundVolume);
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
