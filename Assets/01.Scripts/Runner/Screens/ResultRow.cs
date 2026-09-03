using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// A read-only "OBJECTIVE ……… $1,000" line of the Mission Complete panel:
    /// the label on the left (typed in by the reveal), the value right-aligned
    /// in a fixed zone (counted up by the reveal). Never focusable — the
    /// panel is a readout, its only cursor lives on the buttons. Unlike a
    /// focus row it owns its own scale and colours: <see cref="MenuRow.ApplyFocus"/>
    /// rewrites the row's scale, alpha and label tint every frame, so the
    /// override pins those and puts the typing punch on the label and value
    /// rects instead (the <see cref="StatHeaderRow"/> rule).
    /// </summary>
    public class ResultRow : MenuRow
    {
        const float ValueWidth = 340f;
        const float ValueRightMargin = 24f;
        const int ValueFontSize = 30;
        const int RowLabelFontSize = 28;
        const float PunchDecayPerSecond = 7f;

        Text valueText;
        Color labelColor;
        Color valueColor;
        bool dim;
        float labelPunch = 1f;
        float valuePunch = 1f;

        public override bool Focusable => false;

        // The value zone, measured from the right edge — the label stops here.
        public override float ReservedRightWidth => ValueRightMargin + ValueWidth;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            PlaceTexts();
        }

        protected override void Build()
        {
            plate.raycastTarget = false; // no hover, no click: the row is not a target
            labelColor = theme.TextPrimary;
            valueColor = theme.Accent;
            label.fontSize = RowLabelFontSize;
            valueText = MenuScreen.MakeText("Value", rect, Vector2.zero, new Vector2(ValueWidth, rect.sizeDelta.y),
                                            string.Empty, ValueFontSize, valueColor, theme.TitleFont, TextAnchor.MiddleRight);
            PlaceTexts();
        }

        // The label scales from its left edge and the value from its right
        // one, so a punch grows the text into the row rather than off it.
        void PlaceTexts()
        {
            float half = rect.sizeDelta.x * 0.5f;
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(-half + LabelInset, 0f);
            if (valueText != null)
            {
                valueText.rectTransform.pivot = new Vector2(1f, 0.5f);
                valueText.rectTransform.anchoredPosition = new Vector2(half - ValueRightMargin, 0f);
            }
        }

        public void SetLabelText(string text) => label.text = text ?? string.Empty;

        public void SetValueText(string text)
        {
            if (valueText != null) valueText.text = text ?? string.Empty;
        }

        public void SetLabelFontSize(int size) => label.fontSize = size;
        public void SetValueFontSize(int size)
        {
            if (valueText != null) valueText.fontSize = size;
        }

        /// <summary>Colours the two texts; dim rows (a failed challenge) ignore these until <see cref="SetDim"/> is lifted.</summary>
        public void SetTint(Color labelTint, Color valueTint)
        {
            labelColor = labelTint;
            valueColor = valueTint;
        }

        /// <summary>Greys the row out — a challenge that did not land.</summary>
        public void SetDim(bool on) => dim = on;

        /// <summary>A scale kick on the label (a typed character) that decays on its own.</summary>
        public void PunchLabel(float scale) => labelPunch = Mathf.Max(labelPunch, scale);

        /// <summary>A scale kick on the value (a counted step).</summary>
        public void PunchValue(float scale) => valuePunch = Mathf.Max(valuePunch, scale);

        protected override void Update()
        {
            base.Update();
            float dt = Time.unscaledDeltaTime;
            labelPunch = Mathf.MoveTowards(labelPunch, 1f, PunchDecayPerSecond * dt);
            valuePunch = Mathf.MoveTowards(valuePunch, 1f, PunchDecayPerSecond * dt);
        }

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            rect.localScale = Vector3.one;
            group.alpha = EntranceAlpha;
            if (plate != null) plate.color = theme.PlateIdle;
            if (label != null)
            {
                label.color = dim ? theme.TextDim : labelColor;
                label.rectTransform.localScale = Vector3.one * labelPunch;
            }
            if (valueText != null)
            {
                valueText.color = dim ? theme.TextDim : valueColor;
                valueText.rectTransform.localScale = Vector3.one * valuePunch;
            }
        }

        public override void Activate() { }
    }
}
