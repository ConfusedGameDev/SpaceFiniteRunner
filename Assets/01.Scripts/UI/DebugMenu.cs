using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Tabbed debug pages for the pause menu. Each tab is a normal
    /// <see cref="MenuScreen"/> (same rows, focus and slide language as the
    /// rest of the menus); the bumpers (or Q/E) cycle between tabs, so new
    /// debug pages are one <see cref="AddTab"/> call away. This class only
    /// tracks which tab is active — the PauseMenu owns input and transitions,
    /// exactly like it does for its other sub-screens.
    /// </summary>
    public class DebugMenu
    {
        readonly List<MenuScreen> tabs = new();
        int active;

        public MenuScreen Active => tabs.Count > 0 ? tabs[active] : null;
        public int Count => tabs.Count;

        public void AddTab(MenuScreen screen) => tabs.Add(screen);

        public bool Contains(MenuScreen screen) => screen != null && tabs.Contains(screen);

        /// <summary>Moves the active tab by <paramref name="step"/> (wraps) and returns it.</summary>
        public MenuScreen Cycle(int step)
        {
            active = ((active + step) % tabs.Count + tabs.Count) % tabs.Count;
            return tabs[active];
        }

        public void HideAllImmediate()
        {
            foreach (var tab in tabs) tab.HideImmediate();
        }

        /// <summary>-1 previous tab / +1 next tab this frame: LB/RB on pad, Q/E on keyboard.</summary>
        public static int TabStepPressed()
        {
            int step = 0;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) step--;
                if (keyboard.eKey.wasPressedThisFrame) step++;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftShoulder.wasPressedThisFrame) step--;
                if (pad.rightShoulder.wasPressedThisFrame) step++;
            }
            return step;
        }

        /// <summary>
        /// Stamps the shared tab header on a tab screen: the tab's localized
        /// title (the "DEBUG — …" line lives whole in the text library, so
        /// translators own the full string) plus the bumper hint, so every
        /// debug page advertises how to switch.
        /// </summary>
        public static void AddTabHeader(MenuScreen screen, MenuTheme theme, MenuTextId titleId, int tabIndex, int tabCount)
        {
            screen.AddLabel("TabTitle", new Vector2(0f, 470f), new Vector2(1200f, 60f),
                            titleId, 44, theme.TextPrimary, theme.TitleFont,
                            TextAnchor.MiddleCenter, 0f);
            screen.AddLabel("TabHint", new Vector2(0f, 415f), new Vector2(900f, 40f),
                            $"LB ◀  TAB {tabIndex + 1}/{tabCount}  ▶ RB   (Q/E)", 24,
                            theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter, 0f);
        }
    }

    /// <summary>
    /// A debug slider row: an arbitrary float range in fixed steps, unlike
    /// <see cref="MenuSlider"/>'s fixed 0..100 volume contract. Left/Right
    /// (keyboard or pad) steps the value; confirm does nothing. Changes report
    /// out immediately so debug values apply live where they can.
    /// </summary>
    public class DebugSliderRow : MenuRow
    {
        const float TrackWidth = 220f;
        const float TrackHeight = 20f;
        const float TrackRightMargin = 104f;

        RectTransform track;
        UnityEngine.UI.Image fill;
        UnityEngine.UI.Text valueText;

        float min, max = 100f, step = 1f, value;
        string format = "0.#";
        System.Action<float> changed;
        Color? labelTint;
        System.Func<Color?> tintProvider;

        /// <summary>Tints the row's label (e.g. a spawn entry's color) instead of the theme's text colors.</summary>
        public void SetLabelTint(Color color)
        {
            labelTint = color;
            ApplyFocus(true);
        }

        /// <summary>
        /// Live tint: polled every frame, applied only when it changes (null
        /// = theme colors). For status rows whose state can move while the
        /// menu is open — SetLabelTint snaps the focus ease, so it must not be
        /// called per frame unchanged.
        /// </summary>
        public void SetLabelTintProvider(System.Func<Color?> provider)
        {
            tintProvider = provider;
            PollTint();
        }

        void PollTint()
        {
            if (tintProvider == null) return;
            Color? next = tintProvider();
            if (next == labelTint) return;
            labelTint = next;
            ApplyFocus(true);
        }

        protected override void Update()
        {
            PollTint();
            base.Update();
        }

        /// <summary>Updates the shown value without firing the change callback — for rebalancing sibling rows.</summary>
        public void SetWithoutNotify(float newValue) => SetValue(newValue, false);

        // Bar + readout, measured from the right edge — the label stops here.
        public override float ReservedRightWidth => TrackRightMargin + TrackWidth;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            float right = width * 0.5f;
            if (track != null)
                track.anchoredPosition = new Vector2(right - TrackRightMargin - TrackWidth * 0.5f, 0f);
            if (valueText != null)
                valueText.rectTransform.anchoredPosition = new Vector2(right - TrackRightMargin * 0.5f, 0f);
        }

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (labelTint.HasValue && label != null)
                label.color = Color.Lerp(Color.Lerp(labelTint.Value, theme.TextDim, 0.45f),
                                         labelTint.Value, Focus);
        }

        protected override void Build()
        {
            float right = rect.sizeDelta.x * 0.5f;

            var trackImage = MenuScreen.MakeImage("Track", rect,
                new Vector2(right - TrackRightMargin - TrackWidth * 0.5f, 0f),
                new Vector2(TrackWidth, TrackHeight), theme.SliderTrack, new Color(1f, 1f, 1f, 0.3f));
            track = trackImage.rectTransform;

            fill = MenuScreen.MakeImage("Fill", track, Vector2.zero, new Vector2(0f, TrackHeight),
                                        theme.SliderFill, theme.Accent);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;

            valueText = MenuScreen.MakeText("Value", rect, new Vector2(right - TrackRightMargin * 0.5f, 0f),
                                            new Vector2(TrackRightMargin, rect.sizeDelta.y),
                                            "0", 24, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
        }

        public void Configure(float min, float max, float step, float initial, string format,
                              System.Action<float> onChanged)
        {
            this.min = min;
            this.max = max;
            this.step = Mathf.Max(0.0001f, step);
            this.format = format;
            changed = onChanged;
            SetValue(initial, false);
        }

        public override bool Adjust(int direction)
        {
            float next = Mathf.Clamp(value + direction * step, min, max);
            if (Mathf.Approximately(next, value)) return false;
            SetValue(next, true);
            return true;
        }

        // Confirm on a slider does nothing — Left/Right is how it is changed.
        public override void Activate() { }

        void SetValue(float raw, bool notify)
        {
            value = Mathf.Clamp(raw, min, max);

            float t = Mathf.InverseLerp(min, max, value);
            if (fill != null)
            {
                fill.rectTransform.sizeDelta = new Vector2(TrackWidth * t, TrackHeight);
                fill.enabled = t > 0.02f; // a sliced sprite cannot draw thinner than its caps
            }
            if (valueText != null) valueText.text = value.ToString(format);

            if (notify) changed?.Invoke(value);
        }
    }

    /// <summary>
    /// A read-only debug row: a label and nothing else. Confirm and Left/Right
    /// do nothing; it exists so a page can list things that are not editable
    /// (a level's non-numeric objectives) in the same plate language as its
    /// sliders, with the same status tinting.
    /// </summary>
    public class DebugLabelRow : MenuRow
    {
        Color? labelTint;
        System.Func<Color?> tintProvider;

        /// <summary>Tints the label instead of the theme's text colors.</summary>
        public void SetLabelTint(Color color)
        {
            labelTint = color;
            ApplyFocus(true);
        }

        /// <summary>Live tint, polled every frame and applied only on change (null = theme colors).</summary>
        public void SetLabelTintProvider(System.Func<Color?> provider)
        {
            tintProvider = provider;
            PollTint();
        }

        void PollTint()
        {
            if (tintProvider == null) return;
            Color? next = tintProvider();
            if (next == labelTint) return;
            labelTint = next;
            ApplyFocus(true);
        }

        protected override void Update()
        {
            PollTint();
            base.Update();
        }

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (labelTint.HasValue && label != null)
                label.color = Color.Lerp(Color.Lerp(labelTint.Value, theme.TextDim, 0.45f),
                                         labelTint.Value, Focus);
        }

        // Nothing to confirm — the base would raise Activated.
        public override void Activate() { }
    }
}
