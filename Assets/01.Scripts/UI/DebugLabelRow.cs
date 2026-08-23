using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
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
