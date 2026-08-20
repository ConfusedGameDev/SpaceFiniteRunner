using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// A row that cycles through a fixed set of options — currently only the
    /// language selector. Left/Right steps through the list (wrapping, unlike
    /// the sliders: a ring of options has no natural ends), Confirm steps
    /// forward, and a mouse click does the same. The value is drawn as
    /// "&lt; OPTION &gt;" in plain text because the Kenney kit ships no arrow
    /// sprites — do not invent filenames.
    /// </summary>
    public class MenuChoice : MenuRow
    {
        const float ValueWidth = 300f;
        const float ValueRightMargin = 24f;

        Text valueText;
        string[] options;
        int index;
        System.Action<int> changed;

        public int Index => index;

        // "< OPTION >" value zone, measured from the right edge — the label stops here.
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
                string.Empty, 28, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleRight);
        }

        /// <summary>Wires the row up. <paramref name="onChanged"/> receives the new option index.</summary>
        public void Configure(string[] choiceLabels, int initialIndex, System.Action<int> onChanged)
        {
            options = choiceLabels;
            changed = onChanged;
            index = Mathf.Clamp(initialIndex, 0, options.Length - 1);
            RefreshValue();
        }

        public override bool Adjust(int direction)
        {
            if (options == null || options.Length < 2) return false;
            index = ((index + direction) % options.Length + options.Length) % options.Length;
            RefreshValue();
            changed?.Invoke(index);
            return true;
        }

        // Confirm steps forward, so a player who never finds Left/Right can
        // still reach every option.
        public override void Activate()
        {
            Adjust(+1);
            base.Activate();
        }

        void RefreshValue()
        {
            if (valueText != null && options != null && options.Length > 0)
                valueText.text = $"< {options[index]} >";
        }
    }
}
