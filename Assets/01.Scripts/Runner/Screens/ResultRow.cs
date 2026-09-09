using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// A read-only "OBJECTIVE ……… $1,000" line of the Mission Complete panel:
    /// the label on the left (typed in by the reveal), the value right-aligned
    /// in a fixed zone (counted up by the reveal). It is a text line, not a
    /// plate: no row background, a white label and an accent value straight
    /// on the panel's dark backdrop, the way the section headers and the
    /// mission brief's objective lines read. A label the fitted column cannot
    /// hold shrinks its own font rather than running into the value.
    /// Never focusable — the
    /// panel is a readout, its only cursor lives on the buttons. Unlike a
    /// focus row it owns its own scale and colours: <see cref="MenuRow.ApplyFocus"/>
    /// rewrites the row's scale, alpha and label tint every frame, so the
    /// override pins those and puts the typing punch on the label and value
    /// rects instead (the <see cref="StatHeaderRow"/> rule).
    /// </summary>
    public class ResultRow : MenuRow
    {
        const float ValueWidth = 220f;      // "$7,000" at the TOTAL's 40 pt is ~190; the value is right-pivoted, so a longer one runs left
        const float ValueRightMargin = 24f;
        const int ValueFontSize = 30;
        const int RowLabelFontSize = 28;
        const int MinLabelFontSize = 18;
        const float PunchDecayPerSecond = 7f;

        Text valueText;
        int labelFontSize = RowLabelFontSize; // the size the label wants; FitLabel may render it smaller
        Color labelColor;
        Color valueColor;
        bool dim;
        float labelPunch = 1f;
        float valuePunch = 1f;

        public override bool Focusable => false;

        // The value zone, measured from the right edge — the label stops here.
        public override float ReservedRightWidth => ValueRightMargin + ValueWidth;

        // The screen measures labels at MenuRow.LabelFontSize (34); this row
        // renders them at 28, so the plate it asks for is scaled to match.
        public override float RequiredWidth(float labelWidth)
            => LabelInsetWidth + labelWidth * (RowLabelFontSize / (float)LabelFontSize) + ReservedRightWidth;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            PlaceTexts();
            FitLabel();
        }

        protected override void Build()
        {
            plate.enabled = false;       // a text line, not a plate (the StatHeaderRow rule)
            plate.raycastTarget = false; // no hover, no click: the row is not a target
            labelColor = Color.white;    // the theme's primary is tuned for plates; on the bare backdrop only white reads
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

        // Shrinks the label's font so its text fits between the inset and the
        // value zone — the guard for a label longer than the capped column.
        // Runs while the row still shows its full label: the screen fits the
        // column before the reveal clears the labels to type them back.
        void FitLabel()
        {
            if (label == null) return;
            float available = rect.sizeDelta.x - LabelInsetWidth - ReservedRightWidth;
            float needed = MenuTextLibrary.MeasureWidth(label.text, label.font, labelFontSize);
            label.fontSize = needed > available && needed > 0f
                ? Mathf.Max(MinLabelFontSize, Mathf.FloorToInt(labelFontSize * available / needed))
                : labelFontSize;
        }

        public void SetValueText(string text)
        {
            if (valueText != null) valueText.text = text ?? string.Empty;
        }

        public void SetLabelFontSize(int size)
        {
            labelFontSize = size;
            FitLabel();
        }
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
