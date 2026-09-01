using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
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
        float rowHeightOverride = -1f;
        float rowSpacingOverride = -1f;
        float rowWidth; // per-screen: grows to fit the widest label in any language, never below theme.RowWidth

        int visibleRows = int.MaxValue; // SetViewport: rows shown at once; the rest sit under the window and scroll
        int scrollIndex;                // index of the row in the window's top slot
        Text overflowUp;                // "▲ n" / "▼ n" cues, created on the first windowed layout
        Text overflowDown;

        float RowHeight => rowHeightOverride > 0f ? rowHeightOverride : theme.RowHeight;
        float RowSpacing => rowSpacingOverride >= 0f ? rowSpacingOverride : theme.RowSpacing;

        public RectTransform Root => root;
        public bool Visible => phase != Phase.Hidden;

        /// <summary>
        /// True once the page has finished arriving and nothing is animating
        /// its root. Effects that write <see cref="Root"/> directly (the
        /// cheats console's screen shake) check this so they never fight a
        /// slide transition for the same property.
        /// </summary>
        public bool Interactive => phase == Phase.Shown;
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
            screen.rowWidth = theme.RowWidth;
            screen.group = go.AddComponent<CanvasGroup>();
            go.SetActive(false);
            return screen;
        }

        /// <summary>
        /// Adds the screen's title plate. Titles settle slightly ahead of the
        /// rows. The plate grows to the title's widest translation (never
        /// below the designed 560), so no language ever clips.
        /// </summary>
        public void SetTitle(MenuTextId titleId)
        {
            const float TitleFontSize = 46;
            const float TitlePadding = 30f; // plate border on each side of the text

            var library = MenuTextLibrary.Load();
            float plateWidth = Mathf.Max(
                560f, Mathf.Ceil(library.MaxWidth(titleId, theme.TitleFont, (int)TitleFontSize) + TitlePadding * 2f));

            var plate = MakeImage("TitlePlate", root, new Vector2(columnX, contentTop + 150f),
                                  new Vector2(plateWidth, 110f), theme.TitlePlate, theme.PlateFocused);
            var text = MakeText("TitleText", plate.rectTransform, Vector2.zero,
                                new Vector2(plateWidth - TitlePadding * 2f, 100f),
                                library.Get(titleId), (int)TitleFontSize, theme.TextPrimary, theme.TitleFont,
                                TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(text, titleId);

            AddEntranceItem(plate.rectTransform, plate.gameObject.AddComponent<CanvasGroup>(), null, 0f);
            rowDelayBase = theme.TitleLead;
        }

        /// <summary>
        /// Compact-row mode for dense screens (the debug tabs): every row added
        /// afterwards uses this height and spacing instead of the theme's.
        /// </summary>
        public void SetRowMetrics(float height, float spacing)
        {
            rowHeightOverride = height;
            rowSpacingOverride = spacing;
        }

        /// <summary>
        /// Shows at most <paramref name="count"/> rows at once; the rest sit
        /// under the window and scroll into view as the focus moves (the LOG's
        /// stat list). Nothing is masked — rows outside the window are simply
        /// inactive — so the marker and pointer hover need no changes.
        /// </summary>
        public void SetViewport(int count)
        {
            visibleRows = Mathf.Max(1, count);
            scrollIndex = 0;
            if (rows.Count > 0) LayoutRows();
        }

        /// <summary>
        /// Removes every row (the title and free labels stay) so a page can be
        /// rebuilt with fresh content — the LOG re-reads the profile on open.
        /// </summary>
        public void ClearRows()
        {
            // Drop the entrance entries first: after DestroyImmediate (edit
            // mode) a row compares equal to null and the filter would miss it.
            entrance.RemoveAll(item => item.row != null);
            foreach (var row in rows)
            {
                if (row == null) continue;
                if (Application.isPlaying) Destroy(row.gameObject);
                else DestroyImmediate(row.gameObject);
            }
            rows.Clear();
            focus = -1;
            scrollIndex = 0;
            rowWidth = theme.RowWidth;
            UpdateOverflowCues();
        }

        /// <summary>
        /// Adds a focusable row of type <typeparamref name="T"/> at the bottom
        /// of the column. Screens with no rows (Cheats, Credits) simply never
        /// call this — adding rows later needs no navigation changes.
        /// The label is measured in every language so the row fits its widest
        /// translation, and the screen's plates stay one uniform width.
        /// </summary>
        public T AddRow<T>(MenuTextId labelId) where T : MenuRow
        {
            var library = MenuTextLibrary.Load();
            return AddRowInternal<T>(library.Get(labelId), labelId,
                                     library.MaxWidth(labelId, theme.BodyFont, MenuRow.LabelFontSize));
        }

        /// <summary>Raw-string variant for rows that must not localize (debug tabs, generated labels).</summary>
        public T AddRow<T>(string label) where T : MenuRow
            => AddRowInternal<T>(label, null,
                                 MenuTextLibrary.MeasureWidth(label, theme.BodyFont, MenuRow.LabelFontSize));

        T AddRowInternal<T>(string label, MenuTextId? localizeId, float labelWidth) where T : MenuRow
        {
            var go = new GameObject($"Row_{label}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            // Uniform fit: the widest label on the page (in any language,
            // plus the row type's widget reserve) sets the width of EVERY
            // plate on it — a longer row added later re-widens the earlier
            // ones, so the column always lines up and nothing clips.
            var row = go.AddComponent<T>();
            float required = Mathf.Ceil(row.RequiredWidth(labelWidth));
            if (required > rowWidth) SetRowWidth(required);

            rect.sizeDelta = new Vector2(rowWidth, RowHeight);
            rect.anchoredPosition = new Vector2(columnX, contentTop - rows.Count * (RowHeight + RowSpacing));

            row.Bind(this, theme, label, localizeId);
            rows.Add(row);

            AddEntranceItem(rect, null, row, rowDelayBase + (rows.Count - 1) * theme.EntranceStagger);
            EnsureMarker();
            if (focus < 0) SetFocus(0);
            if (rows.Count > visibleRows) LayoutRows(); // a row added past the window starts hidden
            return row;
        }

        void SetRowWidth(float width)
        {
            rowWidth = width;
            foreach (var row in rows) row.SetWidth(width);
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

        /// <summary>
        /// Enrolls an arbitrary widget in the page's staggered entrance, for
        /// content built outside <see cref="AddRow{T}"/> / <see cref="AddLabel(string, Vector2, Vector2, string, int, Color, Font, TextAnchor, float)"/>
        /// — the cheats console, which builds its own sub-tree.
        /// </summary>
        public void AddEntranceItem(RectTransform rect, CanvasGroup itemGroup, float delay)
            => AddEntranceItem(rect, itemGroup, null, delay);

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

        public void SetFocus(int index) => SetFocus(index, +1);

        /// <summary>
        /// Focuses <paramref name="index"/> (wrapping both ways). A row that
        /// cannot take focus (a section header) is stepped over in
        /// <paramref name="direction"/>, so headers never trap the cursor;
        /// then the window scrolls to keep the focused row in view.
        /// </summary>
        void SetFocus(int index, int direction)
        {
            int count = rows.Count;
            if (count == 0) return;
            index = ((index % count) + count) % count; // wrap-around, both ways
            for (int tries = 0; tries < count && !rows[index].Focusable; tries++)
                index = ((index + direction) % count + count) % count;
            if (!rows[index].Focusable) return; // nothing on this page can take focus

            if (index != focus)
            {
                focus = index;
                for (int i = 0; i < count; i++) rows[i].SetFocused(i == focus);
            }
            ScrollToFocus();
        }

        public void MoveFocus(int step) => SetFocus((focus < 0 ? 0 : focus) + step, step < 0 ? -1 : +1);

        /// <summary>Mouse hover and click route here, so pointer and pad share one focus model.</summary>
        public void FocusRow(MenuRow row)
        {
            int index = rows.IndexOf(row);
            if (index >= 0 && rows[index].Focusable) SetFocus(index);
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
                new Vector2(columnX - rowWidth * 0.5f - MarkerGap, markerY);
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

        // Keeps the focused row inside the window, and pulls the header right
        // above it into view when it lands on the top slot.
        void ScrollToFocus()
        {
            if (visibleRows >= rows.Count || focus < 0) return;
            if (focus < scrollIndex) scrollIndex = focus;
            else if (focus >= scrollIndex + visibleRows) scrollIndex = focus - visibleRows + 1;
            if (scrollIndex == focus && focus > 0 && !rows[focus - 1].Focusable) scrollIndex = focus - 1;
            scrollIndex = Mathf.Clamp(scrollIndex, 0, rows.Count - visibleRows);
            LayoutRows();
        }

        // Places every row by its slot relative to the window and hides the
        // ones outside it. The entrance item's base position is rewritten
        // too: it is a struct in the list, and ApplyEntrance would otherwise
        // snap a scrolled row back to where it was built on the next Show.
        void LayoutRows()
        {
            float pitch = RowHeight + RowSpacing;
            bool windowed = visibleRows < rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int slot = windowed ? i - scrollIndex : i;
                bool shown = !windowed || (slot >= 0 && slot < visibleRows);
                var pos = new Vector2(columnX, contentTop - slot * pitch);
                row.Rect.anchoredPosition = pos;
                if (row.gameObject.activeSelf != shown) row.gameObject.SetActive(shown);
                for (int k = 0; k < entrance.Count; k++)
                {
                    if (entrance[k].row != row) continue;
                    var item = entrance[k];
                    item.basePosition = pos;
                    entrance[k] = item;
                    break;
                }
            }
            UpdateOverflowCues();
        }

        // "▲ n" above the column and "▼ n" under the last slot: how many rows
        // wait outside the window on each side. Plain labels, never rows.
        void UpdateOverflowCues()
        {
            bool windowed = visibleRows < rows.Count;
            int above = windowed ? scrollIndex : 0;
            int below = windowed ? rows.Count - scrollIndex - visibleRows : 0;
            if (above == 0 && below == 0 && overflowUp == null) return;

            if (overflowUp == null)
            {
                const int CueSize = 22;
                float pitch = RowHeight + RowSpacing;
                overflowUp = MakeText("OverflowUp", root, new Vector2(columnX, contentTop + RowHeight * 0.5f + 20f),
                                      new Vector2(600f, 30f), string.Empty, CueSize, theme.TextDim, theme.BodyFont,
                                      TextAnchor.MiddleCenter);
                overflowDown = MakeText("OverflowDown", root,
                                        new Vector2(columnX, contentTop - (visibleRows - 1) * pitch - RowHeight * 0.5f - 20f),
                                        new Vector2(600f, 30f), string.Empty, CueSize, theme.TextDim, theme.BodyFont,
                                        TextAnchor.MiddleCenter);
            }
            overflowUp.text = above > 0 ? $"▲ {above}" : string.Empty;
            overflowDown.text = below > 0 ? $"▼ {below}" : string.Empty;
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
