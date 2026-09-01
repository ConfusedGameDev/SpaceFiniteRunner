using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// A section title inside a row list ("GLOBAL", "TOTALED VEHICLES"): an
    /// accent-coloured caption over a thin rule, no plate. It is a row rather
    /// than a free label so it scrolls with the list under a
    /// <see cref="MenuScreen.SetViewport"/> window — but it can never take
    /// focus (<see cref="Focusable"/> is false), so the cursor steps over it
    /// and the mouse cannot land on it.
    /// </summary>
    public class StatHeaderRow : MenuRow
    {
        const int HeaderFontSize = 24;
        const float RuleHeight = 2f;

        Image rule;

        public override bool Focusable => false;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            if (rule != null) rule.rectTransform.sizeDelta = new Vector2(width - LabelInset * 2f, RuleHeight);
        }

        protected override void Build()
        {
            plate.enabled = false;
            plate.raycastTarget = false; // no hover, no click: the row is not a target
            label.fontSize = HeaderFontSize;
            label.alignment = TextAnchor.LowerLeft;
            label.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x, rect.sizeDelta.y - 10f);

            var accent = theme.Accent;
            accent.a = 0.6f;
            rule = MenuScreen.MakeImage("Rule", rect, new Vector2(0f, -rect.sizeDelta.y * 0.5f + 4f),
                                        new Vector2(rect.sizeDelta.x - LabelInset * 2f, RuleHeight), null, accent);
        }

        // Always fully shown in the accent colour: the focus ease is for
        // rows the cursor can reach.
        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            rect.localScale = Vector3.one;
            group.alpha = EntranceAlpha;
            if (label != null) label.color = theme.Accent;
        }

        public override void Activate() { }
    }
}
