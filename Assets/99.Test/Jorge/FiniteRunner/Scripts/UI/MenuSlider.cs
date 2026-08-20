using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// A volume row: 0..100 in fixed steps, drawn with the Kenney bar sprites
    /// (plain bar as the empty track, gloss bar as the filled part). Changes
    /// report out immediately — the mixer is written while the bar is still
    /// moving, so the player hears what they are setting.
    ///
    /// The filled part is a sliced sprite whose width shrinks rather than a
    /// Filled image, because Unity cannot 9-slice a filled image and the bar's
    /// round caps have to survive.
    /// </summary>
    public class MenuSlider : MenuRow, IPointerDownHandler, IDragHandler
    {
        const float TrackWidth = 250f;
        const float TrackHeight = 30f;
        const float TrackRightMargin = 96f;
        const float GrabMargin = 24f;

        RectTransform track;
        Image fill;
        Text valueText;

        int value;
        int step = 5;
        System.Action<float> changed;

        /// <summary>Current value as 0..1.</summary>
        public float Normalized => value / 100f;

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

        protected override void Build()
        {
            float right = rect.sizeDelta.x * 0.5f;

            var trackImage = MenuScreen.MakeImage("Track", rect,
                new Vector2(right - TrackRightMargin - TrackWidth * 0.5f, 0f),
                new Vector2(TrackWidth, TrackHeight), theme.SliderTrack, new Color(1f, 1f, 1f, 0.3f));
            track = trackImage.rectTransform;

            fill = MenuScreen.MakeImage("Fill", track, Vector2.zero, new Vector2(TrackWidth, TrackHeight),
                                        theme.SliderFill, theme.Accent);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;

            valueText = MenuScreen.MakeText("Value", rect, new Vector2(right - TrackRightMargin * 0.5f, 0f),
                                            new Vector2(TrackRightMargin, rect.sizeDelta.y),
                                            "0", 28, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
        }

        /// <summary>Wires the row up. <paramref name="onChanged"/> receives 0..1.</summary>
        public void Configure(float initialNormalized, int stepSize, System.Action<float> onChanged)
        {
            step = Mathf.Max(1, stepSize);
            changed = onChanged;
            SetValue(Mathf.RoundToInt(Mathf.Clamp01(initialNormalized) * 100f), false);
        }

        public override bool Adjust(int direction)
        {
            int next = Mathf.Clamp(value + direction * step, 0, 100);
            if (next == value) return false;
            SetValue(next, true);
            return true;
        }

        // Confirm on a slider does nothing — Left/Right is how it is changed.
        public override void Activate() { }

        public void OnPointerDown(PointerEventData eventData) => DragTo(eventData);

        public void OnDrag(PointerEventData eventData) => DragTo(eventData);

        void DragTo(PointerEventData eventData)
        {
            screen?.FocusRow(this);
            if (track == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position,
                                                                        eventData.pressEventCamera, out Vector2 local))
                return;

            // Clicking the row's label should focus it, not slam the volume to
            // zero — only presses over the bar itself move the value.
            float half = TrackWidth * 0.5f;
            if (local.x < -half - GrabMargin || local.x > half + GrabMargin) return;

            SetValue(Mathf.RoundToInt(Mathf.InverseLerp(-half, half, local.x) * 100f), true);
        }

        void SetValue(int raw, bool notify)
        {
            int snapped = Mathf.Clamp(Mathf.RoundToInt(raw / (float)step) * step, 0, 100);
            value = snapped;

            float t = value / 100f;
            if (fill != null)
            {
                fill.rectTransform.sizeDelta = new Vector2(TrackWidth * t, TrackHeight);
                fill.enabled = value > 0; // a sliced sprite cannot draw thinner than its caps
            }
            if (valueText != null) valueText.text = value.ToString();

            if (notify) changed?.Invoke(t);
        }
    }
}
