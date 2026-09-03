using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// One focusable line of a <see cref="MenuScreen"/>: a plate, a label and
    /// an activate callback. Focus is a smoothed 0..1 value, never a boolean
    /// swap — the row eases up to its focused scale, brightness and alpha so
    /// moving the selection reads as movement rather than a flicker.
    ///
    /// Pointer events route back through the screen so hovering with the mouse
    /// moves the same focus index the pad uses; there is deliberately no
    /// separate "hovered" state to get out of sync.
    /// </summary>
    public class MenuRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        protected const float LabelInset = 34f;

        /// <summary>Both label insets — what a plate spends on padding before its label and widgets (for pre-measuring a column).</summary>
        public const float LabelInsetWidth = LabelInset * 2f;

        /// <summary>Point size of every row label — screens measure label widths with it.</summary>
        public const int LabelFontSize = 34;

        protected MenuTheme theme;
        protected MenuScreen screen;
        protected RectTransform rect;
        protected CanvasGroup group;
        protected Image plate;
        protected Text label;

        float focus;
        float focusTarget;
        float focusVelocity;

        /// <summary>Smoothed focus 0..1, for subclasses recoloring their own widgets in ApplyFocus.</summary>
        protected float Focus => focus;

        /// <summary>Fade written by the screen's entrance animation; multiplied with the focus alpha.</summary>
        public float EntranceAlpha { get; set; } = 1f;

        /// <summary>
        /// False greys the row out — label dimmed, plate never lit — while it
        /// stays focusable, so the player can still read WHY it is off (a
        /// price the wallet can't cover). The base row keeps activating; a
        /// screen that must refuse the press checks this itself.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Label colour of a disabled row.</summary>
        protected Color DisabledTint => new(theme.TextDim.r, theme.TextDim.g, theme.TextDim.b, theme.TextDim.a * 0.55f);

        public RectTransform Rect => rect;

        /// <summary>Raised on confirm (A / Enter / left click).</summary>
        public event System.Action Activated;

        /// <summary>
        /// Builds the row. Called by <see cref="MenuScreen.AddRow{T}"/> right
        /// after AddComponent — screens are built while inactive, so Awake has
        /// not run and every piece of setup has to happen here.
        /// </summary>
        internal void Bind(MenuScreen owner, MenuTheme menuTheme, string text, MenuTextId? localizeId = null)
        {
            screen = owner;
            theme = menuTheme;
            rect = (RectTransform)transform;
            group = gameObject.AddComponent<CanvasGroup>();

            plate = MenuScreen.MakeImage("Plate", rect, Vector2.zero, rect.sizeDelta, theme.RowPlate, theme.PlateIdle);
            plate.raycastTarget = true; // the plate is the row's whole hit area

            label = MenuScreen.MakeText("Label", rect, Vector2.zero,
                                        new Vector2(rect.sizeDelta.x - LabelInset * 2f, rect.sizeDelta.y),
                                        text, LabelFontSize, theme.TextDim, theme.BodyFont, TextAnchor.MiddleLeft);
            if (localizeId.HasValue) LocalizedLabel.Bind(label, localizeId.Value);

            Build();
            ApplyFocus(true);
        }

        /// <summary>Extra widgets a subclass hangs off the row (bar, toggle box, value readout).</summary>
        protected virtual void Build() { }

        /// <summary>
        /// Horizontal space this row type's widgets claim from the right edge
        /// (bar + readout, toggle box, choice value). The label must end
        /// before it, so screens add it when computing the fitted row width.
        /// </summary>
        public virtual float ReservedRightWidth => 0f;

        /// <summary>
        /// False for rows the cursor must step over — section headers inside
        /// a list. They still lay out and scroll like rows; they just never
        /// take focus, from the pad or the mouse.
        /// </summary>
        public virtual bool Focusable => true;

        /// <summary>Plate width this row needs for a label of the given rendered width.</summary>
        public float RequiredWidth(float labelWidth) => LabelInset * 2f + labelWidth + ReservedRightWidth;

        /// <summary>
        /// Re-widens the row after a later sibling turned out to need more
        /// room — screens keep every plate on a page the same width.
        /// Subclasses re-anchor their right-edge widgets on top of this.
        /// </summary>
        public virtual void SetWidth(float width)
        {
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            if (plate != null) plate.rectTransform.sizeDelta = rect.sizeDelta;
            if (label != null)
                label.rectTransform.sizeDelta = new Vector2(width - LabelInset * 2f, rect.sizeDelta.y);
        }

        public void SetFocused(bool focused) => focusTarget = focused ? 1f : 0f;

        /// <summary>Confirm. Sliders override this to do nothing.</summary>
        public virtual void Activate() => Activated?.Invoke();

        /// <summary>Left/Right on the focused row. Returns true if anything actually changed.</summary>
        public virtual bool Adjust(int direction) => false;

        protected virtual void Update()
        {
            if (theme == null) return;

            focus = Mathf.SmoothDamp(focus, focusTarget, ref focusVelocity,
                                     Mathf.Max(0.01f, theme.FocusEaseSeconds), Mathf.Infinity, Time.unscaledDeltaTime);
            ApplyFocus(false);
        }

        protected virtual void ApplyFocus(bool immediate)
        {
            if (immediate)
            {
                focus = focusTarget;
                focusVelocity = 0f;
            }

            rect.localScale = Vector3.one * Mathf.Lerp(1f, theme.FocusScale, focus);
            group.alpha = EntranceAlpha * Mathf.Lerp(theme.UnfocusedAlpha, 1f, focus);
            if (plate != null) plate.color = Enabled ? Color.Lerp(theme.PlateIdle, theme.PlateFocused, focus) : theme.PlateIdle;
            if (label != null) label.color = Enabled ? Color.Lerp(theme.TextDim, theme.TextPrimary, focus) : DisabledTint;
        }

        public void OnPointerEnter(PointerEventData eventData) => screen?.FocusRow(this);

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return; // right click is Back, handled globally
            screen?.FocusRow(this);
            Activate();
        }
    }
}
