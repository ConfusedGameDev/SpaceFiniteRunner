using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
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

}
