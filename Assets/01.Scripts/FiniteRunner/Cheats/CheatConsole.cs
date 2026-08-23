using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.FX;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// The cheats page's console: it echoes the player's last presses as
    /// Kenney key caps or Xbox glyphs, and when a code lands it slams the page
    /// with a glitch, names the cheat and locks input until it has wiped
    /// itself clean.
    ///
    /// Which device it reads follows what is PLUGGED IN, not what was last
    /// touched — the same rule the attract prompt uses. With a pad connected
    /// only the pad is read, so a code can never be half-typed and
    /// half-pressed; unplug it and the keyboard takes over, wiping the buffer
    /// on the way so the strip never mixes two alphabets.
    ///
    /// It owns no matching logic: presses go to <see cref="CheatManager"/>,
    /// which owns the buffer and fires the event. This is only the view and
    /// the input gate. Everything runs on unscaled time — the menu sits at
    /// timeScale 0 — and input is polled from <see cref="CaptureTick"/> rather
    /// than Update, so the owning menu decides when the page is really live.
    /// </summary>
    public class CheatConsole : MonoBehaviour
    {
        enum Phase { Idle, Hold, Wipe }

        const float PopSeconds = 0.18f;
        const float PopScale = 1.45f;
        const float TearWidth = 1920f;
        const float TearBand = 340f; // vertical spread of the tear bars around the console

        static readonly Color Cyan = new(0.35f, 0.92f, 1f, 1f);

        MenuTheme theme;
        MenuScreen screen;
        CheatDefinition definition;
        CheatGlyphSet glyphs;
        CheatManager manager;

        RectTransform root;
        Text unlockedLabel;
        Text idText;
        RectTransform strip;
        readonly List<Slot> slots = new();
        readonly List<Image> tears = new();

        Phase phase = Phase.Idle;
        float phaseTimer;
        float glitch;
        float glitchTotal = 1f;
        float pop;
        bool gamepadMode;

        /// <summary>Raised for every accepted press, so the menu can blip.</summary>
        public event System.Action TokenPushed;

        /// <summary>Raised with the cheat id the moment a code lands.</summary>
        public event System.Action<string> CheatRevealed;

        /// <summary>True while a code is being shown — no further input is taken.</summary>
        public bool Blocked => phase != Phase.Idle;

        class Slot
        {
            public RectTransform rect;
            public Image image;
            public Text fallback;
            public Vector2 basePosition;
        }

        /// <summary>
        /// Builds the console into a cheats page. Joins the page's staggered
        /// entrance like any other item, so it slides in with the title.
        /// </summary>
        public static CheatConsole Create(MenuScreen page, MenuTheme theme)
        {
            var go = new GameObject("CheatConsole", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(page.Root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1920f, 1080f);

            var console = go.AddComponent<CheatConsole>();
            console.theme = theme;
            console.screen = page;
            console.root = rect;
            console.definition = CheatDefinition.Load();
            console.glyphs = CheatGlyphSet.Load();
            console.Build();

            page.AddEntranceItem(rect, go.AddComponent<CanvasGroup>(), theme.TitleLead);
            return console;
        }

        void Build()
        {
            unlockedLabel = MenuScreen.MakeText("Unlocked", root, new Vector2(0f, 118f), new Vector2(900f, 44f),
                                                string.Empty, 28, theme.TextDim, theme.BodyFont,
                                                TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(unlockedLabel, MenuTextId.CheatUnlocked);
            unlockedLabel.gameObject.SetActive(false);

            idText = MenuScreen.MakeText("CheatId", root, new Vector2(0f, 48f), new Vector2(1400f, 90f),
                                         string.Empty, 64, theme.Accent, theme.TitleFont, TextAnchor.MiddleCenter);
            idText.gameObject.SetActive(false);

            var stripGo = new GameObject("Strip", typeof(RectTransform));
            strip = (RectTransform)stripGo.transform;
            strip.SetParent(root, false);
            strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0.5f, 0.5f);
            strip.anchoredPosition = new Vector2(0f, -70f);
            strip.sizeDelta = new Vector2(TearWidth, definition.GlyphSize);

            for (int i = 0; i < definition.BufferLength; i++) slots.Add(MakeSlot(i));

            var hint = MenuScreen.MakeText("Hint", root, new Vector2(0f, -190f), new Vector2(1200f, 50f),
                                          string.Empty, 30, theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(hint, MenuTextId.CheatEnterCode);

            // Tear bars sit above everything and only exist during a burst.
            for (int i = 0; i < definition.TearBars; i++)
            {
                var bar = MenuScreen.MakeImage($"Tear{i}", root, Vector2.zero, new Vector2(TearWidth, 8f),
                                               null, Color.clear);
                bar.gameObject.SetActive(false);
                tears.Add(bar);
            }
        }

        Slot MakeSlot(int index)
        {
            float size = definition.GlyphSize;
            var go = new GameObject($"Glyph{index}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(strip, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;

            // Only used when the glyph set has no art for a token — a named
            // box beats a blank gap when someone forgets to build the set.
            var fallback = MenuScreen.MakeText("Name", rect, Vector2.zero, new Vector2(size, size),
                                               string.Empty, Mathf.RoundToInt(size * 0.3f),
                                               theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
            fallback.gameObject.SetActive(false);

            go.SetActive(false);
            return new Slot { rect = rect, image = image, fallback = fallback };
        }

        void OnEnable()
        {
            manager = CheatManager.Instance;
            manager.BufferChanged += RefreshStrip;
            gamepadMode = Gamepad.current != null;
            ResetToIdle();
            RefreshStrip();
        }

        void OnDisable()
        {
            if (manager != null) manager.BufferChanged -= RefreshStrip;
            // Leaving the page mid-reveal must not strand the shake offset or
            // a lock that nothing is left running to release.
            ResetToIdle();
        }

        /// <summary>
        /// Reads one press. Called by the owning menu only while the cheats
        /// page is actually the live screen and past its input grace, so the
        /// button that opened the page cannot also land in the buffer.
        /// </summary>
        public void CaptureTick()
        {
            bool pad = Gamepad.current != null;
            if (pad != gamepadMode)
            {
                gamepadMode = pad;
                manager.ClearBuffer(); // the strip cannot mix key caps and pad glyphs
            }

            if (Blocked) return;

            var token = pad ? new CheatToken(CheatInputReader.ReadButton())
                            : new CheatToken(CheatInputReader.ReadKey());
            if (token.IsEmpty) return;

            pop = PopSeconds;
            TokenPushed?.Invoke();

            if (!manager.Push(token, out string cheatId)) return;

            Reveal(cheatId);
        }

        void Reveal(string cheatId)
        {
            phase = Phase.Hold;
            phaseTimer = definition.HoldSeconds;
            idText.text = cheatId.ToUpperInvariant();
            idText.gameObject.SetActive(true);
            unlockedLabel.gameObject.SetActive(true);
            Burst();
            CheatRevealed?.Invoke(cheatId);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            pop = Mathf.Max(0f, pop - dt);

            switch (phase)
            {
                case Phase.Hold:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f)
                    {
                        // The second burst is what wipes the page: the glitch
                        // covers the id and the strip disappearing.
                        phase = Phase.Wipe;
                        phaseTimer = definition.GlitchSeconds;
                        Burst();
                        idText.gameObject.SetActive(false);
                        unlockedLabel.gameObject.SetActive(false);
                        manager.ClearBuffer();
                    }
                    break;

                case Phase.Wipe:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f) phase = Phase.Idle;
                    break;
            }

            if (glitch > 0f)
            {
                glitch -= dt;
                if (glitch > 0f) ApplyGlitch(glitch / Mathf.Max(0.01f, glitchTotal));
                else ClearGlitch();
            }
            else
            {
                LayoutSlots();
            }
        }

        void Burst()
        {
            glitch = glitchTotal = Mathf.Max(0.01f, definition.GlitchSeconds);
            GlitchController.Instance?.Pulse(1f);
            HapticsSystem.Instance.Pulse(0.9f, 0.6f, 0.18f);
        }

        // ------------------------------------------------------------ visuals

        void RefreshStrip()
        {
            var buffer = manager.Buffer;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                bool used = i < buffer.Count;
                if (slot.rect.gameObject.activeSelf != used) slot.rect.gameObject.SetActive(used);
                if (!used) continue;

                var sprite = glyphs.Glyph(buffer[i]);
                slot.image.sprite = sprite;
                slot.image.enabled = sprite != null;
                slot.image.color = Color.white;
                slot.fallback.gameObject.SetActive(sprite == null);
                if (sprite == null) slot.fallback.text = buffer[i].ToString();
            }
            LayoutSlots();
        }

        // Slots are placed by hand rather than by a layout group: the glitch
        // throws each glyph around individually, which a layout group would
        // fight back on every frame.
        void LayoutSlots()
        {
            if (manager == null) return;
            int count = manager.Buffer.Count;
            float step = definition.GlyphSize + definition.GlyphSpacing;
            float popEase = pop > 0f ? pop / PopSeconds : 0f;

            for (int i = 0; i < count && i < slots.Count; i++)
            {
                var slot = slots[i];
                slot.basePosition = new Vector2((i - (count - 1) * 0.5f) * step, 0f);
                slot.rect.anchoredPosition = slot.basePosition;
                slot.rect.localScale = Vector3.one *
                    (i == count - 1 ? Mathf.Lerp(1f, PopScale, popEase) : 1f);
                slot.image.color = Color.white;
            }
        }

        void ApplyGlitch(float envelope)
        {
            float amplitude = definition.ShakeAmplitude * envelope;

            // The whole page is thrown around, title included — but only once
            // it has settled, or the shake would fight a slide transition.
            if (screen != null && screen.Interactive)
                screen.Root.anchoredPosition = Random.insideUnitCircle * amplitude;

            LayoutSlots();
            int count = manager.Buffer.Count;
            for (int i = 0; i < count && i < slots.Count; i++)
            {
                var slot = slots[i];
                slot.rect.anchoredPosition = slot.basePosition +
                    new Vector2(Random.Range(-1f, 1f) * amplitude * 0.6f,
                                Random.Range(-1f, 1f) * amplitude * 0.3f);
                slot.image.color = Flicker(envelope);
            }

            if (idText.gameObject.activeSelf)
            {
                idText.rectTransform.anchoredPosition =
                    new Vector2(Random.Range(-1f, 1f) * amplitude, 48f);
                idText.color = Flicker(envelope);
            }

            foreach (var tear in tears)
            {
                bool visible = Random.value < 0.55f + envelope * 0.35f;
                if (tear.gameObject.activeSelf != visible) tear.gameObject.SetActive(visible);
                if (!visible) continue;

                tear.rectTransform.anchoredPosition =
                    new Vector2(Random.Range(-1f, 1f) * 60f, Random.Range(-TearBand, TearBand));
                tear.rectTransform.sizeDelta = new Vector2(TearWidth, Random.Range(3f, 22f));
                var color = Flicker(1f);
                color.a = Random.Range(0.12f, 0.55f) * envelope;
                tear.color = color;
            }
        }

        Color Flicker(float envelope)
        {
            float roll = Random.value;
            if (roll > 0.45f + envelope * 0.4f) return Color.white;
            return roll > 0.25f ? theme.Accent : Cyan;
        }

        void ClearGlitch()
        {
            glitch = 0f;
            if (screen != null) screen.Root.anchoredPosition = Vector2.zero;
            if (idText != null)
            {
                idText.rectTransform.anchoredPosition = new Vector2(0f, 48f);
                idText.color = theme.Accent;
            }
            foreach (var tear in tears) tear.gameObject.SetActive(false);
            LayoutSlots();
        }

        void ResetToIdle()
        {
            phase = Phase.Idle;
            phaseTimer = 0f;
            pop = 0f;
            if (idText != null)
            {
                idText.gameObject.SetActive(false);
                unlockedLabel.gameObject.SetActive(false);
            }
            ClearGlitch();
        }
    }
}
