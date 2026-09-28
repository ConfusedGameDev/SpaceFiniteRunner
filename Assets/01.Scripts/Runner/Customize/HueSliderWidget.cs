using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Customize
{
    /// <summary>
    /// One part's colour slider, the arcade car-select look: the part's name
    /// over a full rainbow bar with an I-beam caret on the chosen hue and a
    /// swatch of the colour the part really shows. Not a menu plate row —
    /// the bar has a fixed width, so the plate auto-fit rule does not apply.
    /// The caret draws hollow while the part still shows its authored colour.
    /// Plain C#, built from the menu framework's helpers and theme colours.
    /// </summary>
    public class HueSliderWidget
    {
        public const float Width = 700f;
        const float BarWidth = 600f;
        const float BarHeight = 34f;
        const float SwatchSize = 44f;
        const float LabelHeight = 36f;
        const float FocusScale = 1.04f;
        const float UnfocusedAlpha = 0.55f;

        static Texture2D hueTexture;

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Image frame;
        readonly RectTransform caret;
        readonly Image caretFill;
        readonly Image swatch;
        readonly Text label;
        readonly MenuTheme theme;

        public RectTransform Rect => root;

        public HueSliderWidget(RectTransform parent, MenuTheme menuTheme, Vector2 position, string title)
        {
            theme = menuTheme;
            var go = new GameObject($"Hue_{title}", typeof(RectTransform));
            root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = position;
            root.sizeDelta = new Vector2(Width, LabelHeight + BarHeight + 16f);
            group = go.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;

            float barLeft = -Width * 0.5f;
            float barY = -LabelHeight * 0.5f;
            float barCentreX = barLeft + BarWidth * 0.5f;

            label = MenuScreen.MakeText("Label", root, new Vector2(barCentreX, barY + BarHeight * 0.5f + LabelHeight * 0.5f + 2f),
                                        new Vector2(BarWidth, LabelHeight), title, 28, Color.white, theme.BodyFont,
                                        TextAnchor.MiddleLeft);

            frame = MenuScreen.MakeImage("Frame", root, new Vector2(barCentreX, barY),
                                         new Vector2(BarWidth + 8f, BarHeight + 8f), null, theme.TextDim);

            var barGo = new GameObject("Bar", typeof(RectTransform));
            var bar = (RectTransform)barGo.transform;
            bar.SetParent(root, false);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2(barCentreX, barY);
            bar.sizeDelta = new Vector2(BarWidth, BarHeight);
            var raw = barGo.AddComponent<RawImage>();
            raw.texture = HueTexture();
            raw.raycastTarget = false;

            // The I-beam: a dark outline behind a white core, taller than the bar.
            Image outline = MenuScreen.MakeImage("Caret", bar, Vector2.zero, new Vector2(14f, BarHeight + 22f), null, Color.black);
            caret = outline.rectTransform;
            caretFill = MenuScreen.MakeImage("Core", caret, Vector2.zero, new Vector2(8f, BarHeight + 16f), null, Color.white);

            swatch = MenuScreen.MakeImage("Swatch", root, new Vector2(barLeft + BarWidth + 20f + SwatchSize * 0.5f, barY),
                                          new Vector2(SwatchSize, SwatchSize), null, Color.white);
            SetFocused(false);
        }

        /// <summary>Moves the caret to <paramref name="hue"/> and paints the swatch.</summary>
        public void SetValue(float hue, bool tinted, Color shown)
        {
            caret.anchoredPosition = new Vector2((Mathf.Repeat(hue, 1f) - 0.5f) * BarWidth, 0f);
            caretFill.color = tinted ? Color.white : new Color(1f, 1f, 1f, 0.15f);
            Color display = shown;
            display.a = 1f;
            // HDR colours (emissive-looking RealToon tints) clamp for the UI swatch.
            float peak = Mathf.Max(display.r, Mathf.Max(display.g, display.b));
            if (peak > 1f) display = new Color(display.r / peak, display.g / peak, display.b / peak, 1f);
            swatch.color = display;
        }

        public void SetFocused(bool focused)
        {
            group.alpha = focused ? 1f : UnfocusedAlpha;
            root.localScale = Vector3.one * (focused ? FocusScale : 1f);
            frame.color = focused ? theme.Accent : theme.TextDim;
            label.color = focused ? theme.Accent : Color.white;
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        static Texture2D HueTexture()
        {
            if (hueTexture != null) return hueTexture;
            const int Size = 256;
            hueTexture = new Texture2D(Size, 1, TextureFormat.RGBA32, false)
            {
                name = "HueBar",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            for (int x = 0; x < Size; x++) hueTexture.SetPixel(x, 0, Color.HSVToRGB(x / (Size - 1f), 1f, 1f));
            hueTexture.Apply(false, true);
            return hueTexture;
        }
    }
}
