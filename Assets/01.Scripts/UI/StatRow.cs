using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// A read-only "LABEL ……… VALUE" line for the LOG: the label on the left
    /// like every row, the value right-aligned in a fixed zone measured from
    /// the plate's right edge (the <see cref="MenuChoice"/> shape). Confirm
    /// and Left/Right do nothing — it is focusable only so the list can be
    /// scrolled through and read row by row. The value is plain text the
    /// caller formats (numbers are not localized); <see cref="SetValueProvider"/>
    /// lets a page re-read live values through <see cref="Refresh"/>.
    /// </summary>
    public class StatRow : MenuRow
    {
        const float ValueWidth = 340f;
        const float ValueRightMargin = 24f;
        const int ValueFontSize = 26;

        Text valueText;
        System.Func<string> provider;

        // The value zone, measured from the right edge — the label stops here.
        public override float ReservedRightWidth => ValueRightMargin + ValueWidth;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            if (valueText != null)
                valueText.rectTransform.anchoredPosition =
                    new Vector2(width * 0.5f - ValueRightMargin - ValueWidth * 0.5f, 0f);
        }

        protected override void Build()
        {
            float right = rect.sizeDelta.x * 0.5f;
            valueText = MenuScreen.MakeText("Value", rect,
                new Vector2(right - ValueRightMargin - ValueWidth * 0.5f, 0f),
                new Vector2(ValueWidth, rect.sizeDelta.y),
                string.Empty, ValueFontSize, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleRight);
        }

        /// <summary>Shows a fixed value.</summary>
        public void SetValue(string value)
        {
            provider = null;
            if (valueText != null) valueText.text = value ?? string.Empty;
        }

        /// <summary>Shows a live value: re-read on every <see cref="Refresh"/>.</summary>
        public void SetValueProvider(System.Func<string> valueProvider)
        {
            provider = valueProvider;
            Refresh();
        }

        public void Refresh()
        {
            if (provider != null && valueText != null) valueText.text = provider() ?? string.Empty;
        }

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (valueText != null) valueText.color = Color.Lerp(theme.TextDim, theme.TextPrimary, Focus);
        }

        // Nothing to confirm — the base would raise Activated.
        public override void Activate() { }
    }
}
