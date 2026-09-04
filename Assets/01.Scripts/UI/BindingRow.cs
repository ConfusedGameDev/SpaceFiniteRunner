using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// One action of the CONTROLS screen: the label, then two slots measured
    /// from the plate's right edge — the keyboard key cap and the gamepad
    /// glyph (the <see cref="MenuChoice"/> value-zone shape, twice), each a
    /// sprite from the <see cref="ControlGlyphSet"/> or a bracketed name when
    /// the set has no art. An accent underline marks the column the screen is
    /// on; Left/Right moves it (the column belongs to the whole screen, so
    /// stepping down keeps the device) and Confirm asks the screen to listen
    /// for that device, during which the slots give way to a PRESS A KEY…
    /// line. The row never touches the bindings itself: it draws what
    /// <see cref="ControlBindings"/> says and delegates every change to its
    /// <see cref="ControlsScreen"/>.
    /// </summary>
    public class BindingRow : MenuRow
    {
        const float SlotWidth = 150f;
        const float SlotGap = 16f;
        const float RightMargin = 24f;
        const float GlyphSize = 44f;
        const float UnderlineHeight = 2f;
        const int SlotFontSize = 20;
        const int ListeningFontSize = 22;

        /// <summary>Right-edge space the two slots claim — the LOG row's reserve, so the page reads the same.</summary>
        public const float RightReserve = RightMargin + SlotWidth * 2f + SlotGap;

        struct Slot
        {
            public RectTransform rect;
            public Image image;
            public Text fallback;
        }

        GameAction action;
        ControlsScreen owner;
        Slot keySlot;
        Slot padSlot;
        Image underline;
        Text listeningText;
        int column;
        bool listening;

        public GameAction Action => action;

        public override float ReservedRightWidth => RightReserve;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            Reanchor(width);
        }

        protected override void Build()
        {
            keySlot = MakeSlot("Key");
            padSlot = MakeSlot("Pad");

            var accent = theme.Accent;
            underline = MenuScreen.MakeImage("Underline", rect, Vector2.zero,
                                             new Vector2(SlotWidth - 20f, UnderlineHeight), null, accent);
            underline.raycastTarget = false;

            listeningText = MenuScreen.MakeText("Listening", rect, Vector2.zero,
                                                new Vector2(SlotWidth * 2f + SlotGap, rect.sizeDelta.y),
                                                string.Empty, ListeningFontSize, theme.Accent, theme.BodyFont,
                                                TextAnchor.MiddleRight);
            listeningText.gameObject.SetActive(false);

            Reanchor(rect.sizeDelta.x);
        }

        Slot MakeSlot(string name)
        {
            var go = new GameObject($"Slot_{name}", typeof(RectTransform));
            var slotRect = (RectTransform)go.transform;
            slotRect.SetParent(rect, false);
            slotRect.anchorMin = slotRect.anchorMax = slotRect.pivot = new Vector2(0.5f, 0.5f);
            slotRect.sizeDelta = new Vector2(SlotWidth, rect.sizeDelta.y);

            var image = MenuScreen.MakeImage("Glyph", slotRect, Vector2.zero, new Vector2(GlyphSize, GlyphSize),
                                             null, Color.white);
            image.raycastTarget = false;
            image.preserveAspect = true;

            // Only used when the glyph set has no art for a control — a named
            // box beats a blank gap when someone forgets to build the set.
            var fallback = MenuScreen.MakeText("Name", slotRect, Vector2.zero, new Vector2(SlotWidth, rect.sizeDelta.y),
                                               string.Empty, SlotFontSize, theme.TextPrimary, theme.BodyFont,
                                               TextAnchor.MiddleCenter);
            fallback.gameObject.SetActive(false);

            return new Slot { rect = slotRect, image = image, fallback = fallback };
        }

        void Reanchor(float width)
        {
            if (keySlot.rect == null) return;
            float right = width * 0.5f;
            float padX = right - RightMargin - SlotWidth * 0.5f;
            float keyX = padX - SlotWidth - SlotGap;
            padSlot.rect.anchoredPosition = new Vector2(padX, 0f);
            keySlot.rect.anchoredPosition = new Vector2(keyX, 0f);
            listeningText.rectTransform.anchoredPosition = new Vector2(right - RightMargin - (SlotWidth * 2f + SlotGap) * 0.5f, 0f);
            PlaceUnderline();
        }

        void PlaceUnderline()
        {
            float x = (column == 0 ? keySlot : padSlot).rect.anchoredPosition.x;
            underline.rectTransform.anchoredPosition = new Vector2(x, -rect.sizeDelta.y * 0.5f + 6f);
        }

        /// <summary>Wires the row to its action and the screen that owns the column and the capture.</summary>
        public void Configure(GameAction gameAction, ControlsScreen screenOwner)
        {
            action = gameAction;
            owner = screenOwner;
            Refresh();
        }

        /// <summary>Re-reads both bindings off <see cref="ControlBindings"/>.</summary>
        public void Refresh()
        {
            if (keySlot.rect == null) return;
            var glyphs = ControlGlyphSet.Load();
            var key = ControlBindings.KeyFor(action);
            var pad = ControlBindings.PadFor(action);
            Fill(keySlot, glyphs.For(key), ControlGlyphSet.Label(key));
            Fill(padSlot, glyphs.For(pad), ControlGlyphSet.Label(pad));
        }

        static void Fill(Slot slot, Sprite sprite, string label)
        {
            bool art = sprite != null;
            slot.image.sprite = sprite;
            slot.image.enabled = art;
            slot.fallback.gameObject.SetActive(!art);
            if (!art) slot.fallback.text = label == "—" ? label : $"[{label}]";
        }

        /// <summary>0 = keyboard column, 1 = gamepad column.</summary>
        public void SetColumn(int index)
        {
            column = Mathf.Clamp(index, 0, 1);
            if (underline != null) PlaceUnderline();
        }

        /// <summary>Swaps the slots for the PRESS A KEY… line (and back).</summary>
        public void SetListening(bool on, string text)
        {
            listening = on;
            keySlot.rect.gameObject.SetActive(!on);
            padSlot.rect.gameObject.SetActive(!on);
            underline.gameObject.SetActive(!on);
            listeningText.gameObject.SetActive(on);
            if (on) listeningText.text = text ?? string.Empty;
        }

        /// <summary>Left/Right pick the device column — for the whole screen.</summary>
        public override bool Adjust(int direction) => owner != null && owner.StepColumn(direction);

        /// <summary>Confirm (or a click) starts listening for the selected device.</summary>
        public override void Activate() => owner?.BeginCapture(this);

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (keySlot.rect == null) return;

            var text = Enabled ? Color.Lerp(theme.TextDim, theme.TextPrimary, Focus) : DisabledTint;
            keySlot.fallback.color = text;
            padSlot.fallback.color = text;
            keySlot.image.color = Color.Lerp(new Color(1f, 1f, 1f, 0.75f), Color.white, Focus);
            padSlot.image.color = keySlot.image.color;

            var accent = theme.Accent;
            accent.a = Mathf.Lerp(0.3f, 1f, Focus);
            underline.color = accent;
            listeningText.color = theme.Accent;
        }
    }
}
