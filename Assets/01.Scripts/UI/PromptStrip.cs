using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The footer row of prompts. Rebuilt whenever the current screen changes,
    /// because each screen offers a different set of actions.
    /// </summary>
    public class PromptStrip : MonoBehaviour
    {
        MenuTheme theme;
        RectTransform rect;

        public static PromptStrip Create(RectTransform parent, MenuTheme theme, float bottomMargin)
        {
            var go = new GameObject("PromptStrip", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottomMargin);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 46f;
            // The hints size themselves; this group just spaces them out.
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var strip = go.AddComponent<PromptStrip>();
            strip.theme = theme;
            strip.rect = rect;
            return strip;
        }

        public void SetHints(params (PromptAction action, MenuTextId caption)[] hints)
        {
            var stale = new List<GameObject>();
            foreach (Transform child in rect) stale.Add(child.gameObject);
            foreach (var go in stale)
            {
                // Unparent first: Destroy is deferred a frame and the old hints
                // would otherwise still be laid out beside the new ones.
                go.transform.SetParent(null, false);
                Destroy(go);
            }

            foreach (var hint in hints)
                PromptHint.Create(rect, theme, hint.action, hint.caption);
        }
    }
}
