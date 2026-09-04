using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// A selectable "LABEL ……… VALUE" row: the <see cref="StatRow"/> shape
    /// (label left, a plain-text value right-aligned in a fixed zone) that
    /// DOES confirm. The Store's START MISSION row and every row of the
    /// MISSIONS map use it — the value is a best rank and total on a
    /// completed mission (<c>S  $12,400</c>) or the requirement a locked
    /// one prints (<c>REQUIRES: $5,000</c>). <see cref="MenuRow.Enabled"/>
    /// greys both texts but keeps the row focusable so the player can read
    /// why it is locked; the base row still raises Activated, so the screen
    /// that owns it refuses the press itself.
    /// </summary>
    public class MissionRow : MenuRow
    {
        const float ValueWidth = 340f;
        const float ValueRightMargin = 24f;
        const int ValueFontSize = 26;

        /// <summary>Right-edge space the value claims — screens that pre-measure a column add it to the label width.</summary>
        public const float RightReserve = ValueRightMargin + ValueWidth;

        Text valueText;

        public override float ReservedRightWidth => RightReserve;

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

        /// <summary>The right-side text; empty hides it.</summary>
        public void SetValue(string value)
        {
            if (valueText != null) valueText.text = value ?? string.Empty;
        }

        /// <summary>Greys or restores the row (label and value); it stays focusable either way.</summary>
        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            ApplyFocus(true);
        }

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (valueText != null)
                valueText.color = Enabled ? Color.Lerp(theme.TextDim, theme.TextPrimary, Focus) : DisabledTint;
        }

        // A mission is one deliberate press, never a slide.
        public override bool Adjust(int direction) => false;
    }
}
