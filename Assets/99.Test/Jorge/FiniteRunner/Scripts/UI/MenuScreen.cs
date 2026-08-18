using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// One page of the main menu — the main list, Settings, Cheats, Credits or
    /// the exit confirmation. Owns a focus list with wrap-around, the eased
    /// entrance (items slide in from the left and fade, staggered so they
    /// arrive one after another), the slide in/out used between pages, and the
    /// highlight marker that eases toward the focused row instead of snapping
    /// to it.
    ///
    /// Everything animates on unscaled time: the menu runs at timeScale 0.
    /// Screens hold no input code — <see cref="MainMenuController"/> polls the
    /// devices once and drives whichever screen is current, so pad, keyboard
    /// and mouse all move the same single focus index.
    /// </summary>
    public class MenuScreen : MonoBehaviour
    {
        enum Phase { Hidden, Entering, SlidingIn, Shown, SlidingOut }

        struct EntranceItem
        {
            public RectTransform rect;
            public CanvasGroup group; // set for plain art
            public MenuRow row;       // set for rows (they combine entrance and focus alpha themselves)
            public Vector2 basePosition;
            public float delay;
        }

        const float MarkerSize = 40f;
        const float MarkerGap = 42f;

        MenuTheme theme;
        RectTransform root;
        CanvasGroup group;
        Image marker;
        CanvasGroup markerGroup;

        readonly List<MenuRow> rows = new();
        readonly List<EntranceItem> entrance = new();

        Phase phase = Phase.Hidden;
        float timer;
        float slideX;
        float markerY;
        float markerVelocity;
        int focus = -1;

        float columnX;
        float contentTop;
        float rowDelayBase;

        public RectTransform Root => root;
        public bool Visible => phase != Phase.Hidden;
        public IReadOnlyList<MenuRow> Rows => rows;
        public MenuRow Focused => focus >= 0 && focus < rows.Count ? rows[focus] : null;

        /// <summary>Creates an empty full-screen page, parented to the menu canvas and starting hidden.</summary>
        public static MenuScreen Create(string name, RectTransform parent, MenuTheme theme, float columnX, float contentTop)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.anchoredPosition = Vector2.zero;

            var screen = go.AddComponent<MenuScreen>();
            screen.root = rect;
            screen.theme = theme;
            screen.columnX = columnX;
            screen.contentTop = contentTop;
            screen.group = go.AddComponent<CanvasGroup>();
            go.SetActive(false);
            return screen;
        }

        /// <summary>Adds the screen's title plate. Titles settle slightly ahead of the rows.</summary>
        public void SetTitle(MenuTextId titleId)
        {
            var plate = MakeImage("TitlePlate", root, new Vector2(columnX, contentTop + 150f),
                                  new Vector2(560f, 110f), theme.TitlePlate, theme.PlateFocused);
            var text = MakeText("TitleText", plate.rectTransform, Vector2.zero, new Vector2(500f, 100f),
                                MenuTextLibrary.Load().Get(titleId), 46, theme.TextPrimary, theme.TitleFont,
                                TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(text, titleId);

            AddEntranceItem(plate.rectTransform, plate.gameObject.AddComponent<CanvasGroup>(), null, 0f);
            rowDelayBase = theme.TitleLead;
        }

        /// <summary>
        /// Adds a focusable row of type <typeparamref name="T"/> at the bottom
        /// of the column. Screens with no rows (Cheats, Credits) simply never
        /// call this — adding rows later needs no navigation changes.
        /// </summary>
        public T AddRow<T>(MenuTextId labelId) where T : MenuRow
        {
            var go = new GameObject($"Row_{labelId}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(theme.RowWidth, theme.RowHeight);
            rect.anchoredPosition = new Vector2(columnX, contentTop - rows.Count * (theme.RowHeight + theme.RowSpacing));

            var row = go.AddComponent<T>();
            row.Bind(this, theme, MenuTextLibrary.Load().Get(labelId), labelId);
            rows.Add(row);

            AddEntranceItem(rect, null, row, rowDelayBase + (rows.Count - 1) * theme.EntranceStagger);
            EnsureMarker();
            if (focus < 0) SetFocus(0);
            return row;
        }

        /// <summary>Adds a non-interactive line of text that joins the entrance animation.</summary>
        public Text AddLabel(string name, Vector2 position, Vector2 size, string content, int fontSize,
                             Color color, Font font, TextAnchor anchor, float delay)
        {
            var text = MakeText(name, root, position, size, content, fontSize, color, font, anchor);
            AddEntranceItem(text.rectTransform, text.gameObject.AddComponent<CanvasGroup>(), null, delay);
            return text;
        }

        /// <summary>Localized variant: the line re-fetches its string when the language changes.</summary>
        public Text AddLabel(string name, Vector2 position, Vector2 size, MenuTextId id, int fontSize,
                             Color color, Font font, TextAnchor anchor, float delay)
        {
            var text = AddLabel(name, position, size, MenuTextLibrary.Load().Get(id), fontSize, color, font, anchor, delay);
            LocalizedLabel.Bind(text, id);
            return text;
        }

        /// <summary>Shows the screen. <paramref name="staggered"/> plays the slow item-by-item entrance.</summary>
        public void Show(bool staggered)
        {
            gameObject.SetActive(true);
            group.alpha = 1f;
            root.anchoredPosition = Vector2.zero;
            timer = 0f;

            if (staggered)
            {
                phase = Phase.Entering;
                ApplyEntrance(0f);
            }
            else
            {
                phase = Phase.Shown;
                ApplyEntrance(float.MaxValue);
            }

            SnapMarker();
        }

        /// <summary>Slides the page in from <paramref name="fromX"/> (positive = from the right).</summary>
        public void SlideIn(float fromX)
        {
            gameObject.SetActive(true);
            ApplyEntrance(float.MaxValue);
            slideX = fromX;
            timer = 0f;
            phase = Phase.SlidingIn;
            root.anchoredPosition = new Vector2(fromX, 0f);
            group.alpha = 0f;
            SnapMarker();
        }

        /// <summary>Slides the page out toward <paramref name="toX"/> and hides it.</summary>
        public void SlideOut(float toX)
        {
            if (!gameObject.activeSelf) return;
            slideX = toX;
            timer = 0f;
            phase = Phase.SlidingOut;
        }

        public void HideImmediate()
        {
            phase = Phase.Hidden;
            gameObject.SetActive(false);
        }

        public void SetFocus(int index)
        {
            if (rows.Count == 0) return;
            index = ((index % rows.Count) + rows.Count) % rows.Count; // wrap-around, both ways
            if (index == focus) return;

            focus = index;
            for (int i = 0; i < rows.Count; i++) rows[i].SetFocused(i == focus);
        }

        public void MoveFocus(int step) => SetFocus((focus < 0 ? 0 : focus) + step);

        /// <summary>Mouse hover and click route here, so pointer and pad share one focus model.</summary>
        public void FocusRow(MenuRow row)
        {
            int index = rows.IndexOf(row);
            if (index >= 0) SetFocus(index);
        }

        void Update()
        {
            if (theme == null) return;
            float dt = Time.unscaledDeltaTime;

            switch (phase)
            {
                case Phase.Entering:
                    timer += dt;
                    ApplyEntrance(timer);
                    if (timer >= TotalEntranceTime)
                    {
                        ApplyEntrance(float.MaxValue);
                        phase = Phase.Shown;
                    }
                    break;

                case Phase.SlidingIn:
                {
                    timer += dt;
                    float e = theme.Ease(Mathf.Clamp01(timer / Mathf.Max(0.01f, theme.ScreenTransition)));
                    root.anchoredPosition = new Vector2(Mathf.Lerp(slideX, 0f, e), 0f);
                    group.alpha = e;
                    if (e >= 1f) phase = Phase.Shown;
                    break;
                }

                case Phase.SlidingOut:
                {
                    timer += dt;
                    float p = Mathf.Clamp01(timer / Mathf.Max(0.01f, theme.ScreenTransition));
                    float e = theme.Ease(p);
                    root.anchoredPosition = new Vector2(Mathf.Lerp(0f, slideX, e), 0f);
                    group.alpha = 1f - e;
                    if (p >= 1f) HideImmediate();
                    break;
                }
            }

            UpdateMarker(dt);
        }

        float TotalEntranceTime
        {
            get
            {
                float longest = 0f;
                foreach (var item in entrance) longest = Mathf.Max(longest, item.delay);
                return theme.EntranceDuration + longest;
            }
        }

        void AddEntranceItem(RectTransform rect, CanvasGroup itemGroup, MenuRow row, float delay)
        {
            entrance.Add(new EntranceItem
            {
                rect = rect,
                group = itemGroup,
                row = row,
                basePosition = rect.anchoredPosition,
                delay = delay
            });
        }

        void ApplyEntrance(float t)
        {
            float duration = Mathf.Max(0.01f, theme.EntranceDuration);
            foreach (var item in entrance)
            {
                float e = theme.Ease(Mathf.Clamp01((t - item.delay) / duration));
                item.rect.anchoredPosition = item.basePosition + new Vector2(-theme.EntranceSlide * (1f - e), 0f);
                if (item.row != null) item.row.EntranceAlpha = e;
                else if (item.group != null) item.group.alpha = e;
            }
        }

        void EnsureMarker()
        {
            if (marker != null) return;

            marker = MakeImage("FocusMarker", root, Vector2.zero, new Vector2(MarkerSize, MarkerSize),
                               theme.SelectionMarker, theme.Accent);
            markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
            markerGroup.alpha = 0f;
        }

        // The marker is not an entrance item: it chases the focused row every
        // frame, so it borrows that row's entrance alpha instead of animating
        // its own position from the left.
        void UpdateMarker(float dt)
        {
            if (marker == null) return;

            var target = Focused;
            if (target == null)
            {
                markerGroup.alpha = 0f;
                return;
            }

            markerY = Mathf.SmoothDamp(markerY, target.Rect.anchoredPosition.y, ref markerVelocity,
                                       Mathf.Max(0.01f, theme.FocusEaseSeconds), Mathf.Infinity, dt);
            marker.rectTransform.anchoredPosition =
                new Vector2(columnX - theme.RowWidth * 0.5f - MarkerGap, markerY);
            markerGroup.alpha = target.EntranceAlpha;
        }

        // Opening a screen should not make the highlight fly across it — only
        // moving between rows eases.
        void SnapMarker()
        {
            var target = Focused;
            markerY = target != null ? target.Rect.anchoredPosition.y : 0f;
            markerVelocity = 0f;
        }

        // ------------------------------------------------------- build helpers
        // Shared by every menu widget so the whole menu is built the same way.

        public static Text MakeText(string name, Transform parent, Vector2 position, Vector2 size, string content,
                                    int fontSize, Color color, Font font, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Image MakeImage(string name, Transform parent, Vector2 position, Vector2 size,
                                      Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            // 9-slice whenever the sprite was imported with a border; a missing
            // sprite still draws as a flat plate rather than nothing at all.
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }
    }
}
