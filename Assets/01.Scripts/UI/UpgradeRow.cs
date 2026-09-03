using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.SaveData;
namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// A purchase row for the Store: the category label on the left, and on
    /// the right a ten-pip meter of the levels already bought followed by
    /// the price of the NEXT one (<c>SPEED  ▮▮▮▯▯▯▯▯▯▯  $1,125</c>). Confirm
    /// buys one level — the screen owning the row does the spending and
    /// calls <see cref="SetState"/> back; the row itself only draws. A price
    /// the wallet can't cover greys the row (<see cref="MenuRow.Enabled"/>)
    /// but leaves it focusable so the player can read what it would cost;
    /// at the last level the price reads MAX. Left/Right do nothing: a
    /// purchase is one deliberate press, never a drag.
    /// </summary>
    public class UpgradeRow : MenuRow
    {
        public const int PipCount = 10;

        const float PipWidth = 14f;
        const float PipHeight = 24f;
        const float PipGap = 4f;
        const float PipsWidth = PipCount * PipWidth + (PipCount - 1) * PipGap;
        const float PriceWidth = 130f;
        const float PriceRightMargin = 20f;
        const float PriceGap = 12f;
        const int PriceFontSize = 26;
        const float EmptyPipAlpha = 0.25f;
        const float PunchScale = 1.35f;
        const float PunchDecayPerSecond = 4f;

        readonly Image[] pips = new Image[PipCount];
        RectTransform pipsRoot;
        Text priceText;
        int level;
        float punch = 1f;

        /// <summary>Levels currently drawn as bought.</summary>
        public int Level => level;

        /// <summary>Right-edge space the meter and the price claim — screens that place the column pre-measure rows with it.</summary>
        public const float RightReserve = PriceRightMargin + PriceWidth + PriceGap + PipsWidth;

        // Price zone + pips, measured from the right edge — the label stops here.
        public override float ReservedRightWidth => RightReserve;

        public override void SetWidth(float width)
        {
            base.SetWidth(width);
            Anchor(width);
        }

        protected override void Build()
        {
            var pipsGo = new GameObject("Pips", typeof(RectTransform));
            pipsRoot = (RectTransform)pipsGo.transform;
            pipsRoot.SetParent(rect, false);
            pipsRoot.anchorMin = pipsRoot.anchorMax = new Vector2(0.5f, 0.5f);
            pipsRoot.pivot = new Vector2(0.5f, 0.5f);
            pipsRoot.sizeDelta = new Vector2(PipsWidth, PipHeight);

            float first = -PipsWidth * 0.5f + PipWidth * 0.5f;
            for (int i = 0; i < PipCount; i++)
            {
                pips[i] = MenuScreen.MakeImage($"Pip{i}", pipsRoot, new Vector2(first + i * (PipWidth + PipGap), 0f),
                                               new Vector2(PipWidth, PipHeight), null, EmptyColor());
            }

            priceText = MenuScreen.MakeText("Price", rect, Vector2.zero, new Vector2(PriceWidth, rect.sizeDelta.y),
                                            string.Empty, PriceFontSize, theme.TextPrimary, theme.TitleFont,
                                            TextAnchor.MiddleRight);
            Anchor(rect.sizeDelta.x);
        }

        void Anchor(float width)
        {
            float right = width * 0.5f;
            if (priceText != null)
                priceText.rectTransform.anchoredPosition = new Vector2(right - PriceRightMargin - PriceWidth * 0.5f, 0f);
            if (pipsRoot != null)
                pipsRoot.anchoredPosition = new Vector2(right - PriceRightMargin - PriceWidth - PriceGap - PipsWidth * 0.5f, 0f);
        }

        /// <summary>
        /// Redraws the meter: <paramref name="level"/> pips lit, the price of
        /// level + 1 (or <paramref name="maxLabel"/> once every level is
        /// bought), greyed when <paramref name="affordable"/> is false.
        /// </summary>
        public void SetState(int level, long nextCost, bool affordable, string maxLabel)
        {
            this.level = Mathf.Clamp(level, 0, PipCount);
            bool maxed = this.level >= PipCount;
            for (int i = 0; i < PipCount; i++)
                pips[i].color = i < this.level ? theme.Accent : EmptyColor();
            if (priceText != null) priceText.text = maxed ? maxLabel : StatFormat.Money(nextCost);
            Enabled = !maxed && affordable;
        }

        /// <summary>Kicks the meter's scale — the buy feedback, decaying on unscaled time.</summary>
        public void PunchPips() => punch = PunchScale;

        Color EmptyColor() => new(theme.TextDim.r, theme.TextDim.g, theme.TextDim.b, EmptyPipAlpha);

        protected override void Update()
        {
            base.Update();
            if (pipsRoot == null) return;
            punch = Mathf.MoveTowards(punch, 1f, PunchDecayPerSecond * Time.unscaledDeltaTime);
            pipsRoot.localScale = Vector3.one * punch;
        }

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (priceText != null)
                priceText.color = Enabled ? Color.Lerp(theme.TextDim, theme.TextPrimary, Focus) : DisabledTint;
        }

        // A purchase is never a slide.
        public override bool Adjust(int direction) => false;
    }
}
