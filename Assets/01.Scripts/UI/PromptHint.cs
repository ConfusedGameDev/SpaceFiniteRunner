using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// One "[glyph] CAPTION" prompt. Holds both representations and shows
    /// whichever matches the last-used device, so the footer re-labels itself
    /// the moment the player picks up a pad or touches the mouse.
    /// </summary>
    public class PromptHint : MonoBehaviour
    {
        const float GlyphSize = 40f;
        const int FontSize = 24;

        MenuTheme theme;
        PromptAction action;
        Image glyph;
        Text keyText;

        public CanvasGroup Group { get; private set; }

        public static PromptHint Create(RectTransform parent, MenuTheme theme, PromptAction action,
                                        MenuTextId captionId)
        {
            var go = new GameObject($"Hint_{action}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hint = go.AddComponent<PromptHint>();
            hint.theme = theme;
            hint.action = action;
            hint.Group = go.AddComponent<CanvasGroup>();

            hint.glyph = MenuScreen.MakeImage("Glyph", rect, Vector2.zero, new Vector2(GlyphSize, GlyphSize),
                                              InputPromptBinder.Glyph(theme, action), theme.TextPrimary);
            var element = hint.glyph.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = GlyphSize;

            hint.keyText = MenuScreen.MakeText("Key", rect, Vector2.zero, new Vector2(0f, GlyphSize),
                                               InputPromptBinder.KeyLabel(action), FontSize,
                                               theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            var caption = MenuScreen.MakeText("Caption", rect, Vector2.zero, new Vector2(0f, GlyphSize),
                                              MenuTextLibrary.Load().Get(captionId), FontSize,
                                              theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(caption, captionId);

            hint.Refresh(InputPromptBinder.Device);
            return hint;
        }

        void OnEnable()
        {
            InputPromptBinder.DeviceChanged += Refresh;
            Refresh(InputPromptBinder.Device);
        }

        void OnDisable() => InputPromptBinder.DeviceChanged -= Refresh;

        void Refresh(PromptDevice device)
        {
            if (glyph == null || keyText == null) return;

            // A pad glyph the theme is missing would draw as a white box, so
            // fall back to the key label rather than lie about the art.
            bool usePad = device == PromptDevice.Gamepad &&
                          InputPromptBinder.Glyph(theme, action) != null;

            glyph.gameObject.SetActive(usePad);
            keyText.gameObject.SetActive(!usePad);
        }
    }

}
