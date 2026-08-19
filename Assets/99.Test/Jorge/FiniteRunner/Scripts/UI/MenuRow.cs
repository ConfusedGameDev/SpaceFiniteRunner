using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FiniteRunner
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
                                        text, 34, theme.TextDim, theme.BodyFont, TextAnchor.MiddleLeft);
            if (localizeId.HasValue) LocalizedLabel.Bind(label, localizeId.Value);

            Build();
            ApplyFocus(true);
        }

        /// <summary>Extra widgets a subclass hangs off the row (bar, toggle box, value readout).</summary>
        protected virtual void Build() { }

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
            if (plate != null) plate.color = Color.Lerp(theme.PlateIdle, theme.PlateFocused, focus);
            if (label != null) label.color = Color.Lerp(theme.TextDim, theme.TextPrimary, focus);
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
