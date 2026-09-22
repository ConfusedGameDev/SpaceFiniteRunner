using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.HUD;
namespace ConfusedGameDev.FiniteRunner.Ship.UI
{
    /// <summary>
    /// The ship's speed readout as a thing to DROP INTO ANY LEVEL: the
    /// runner's tachometer wedge (<see cref="SpeedGauge"/>), the km/h number
    /// beside it and a caption under it, built in code under the canvas this
    /// sits on, fed by whichever ship flies the scene. In the Ship System
    /// prefab it is wired to the <see cref="HoverShip"/> beside it; dropped in
    /// on its own it finds the scene's ship through <see cref="ShipRegistry"/>
    /// — kept asking until one appears, so a ship spawned later is found too,
    /// and only in its own scene (the city→runner handoff keeps two alive).
    /// Nothing here is scene-wired: the prefab is one object.
    /// </summary>
    [DefaultExecutionOrder(50)] // after the ships have ticked and registered
    public class ShipSpeedHud : MonoBehaviour
    {
        [Tooltip("The ship to read. Empty = whichever ship flies this scene (ShipRegistry).")]
        [SerializeField] HoverShip ship;

        [Tooltip("The speed that lights the whole wedge, km/h. The runner's Light Speed is ~6500.")]
        [SerializeField, Min(1f)] float fullScaleKmh = 6500f;

        [Tooltip("Font for the number and caption. Empty = Unity's built-in font.")]
        [SerializeField] Font font;

        [Header("Layout (px at 1920×1080)")]
        [Tooltip("The wedge's top-left corner from the canvas's top-left.")]
        [SerializeField] Vector2 topLeft = new(40f, -40f);
        [SerializeField, Range(4, 60)] int segments = 20;
        [SerializeField, Range(4f, 60f)] float segmentWidth = 20f;
        [SerializeField, Range(0f, 20f)] float segmentGap = 4f;
        [SerializeField, Range(4f, 200f)] float minHeight = 18f;
        [SerializeField, Range(4f, 300f)] float maxHeight = 80f;
        [SerializeField, Range(0f, 1f)] float emptyAlpha = 0.2f;
        [SerializeField, Range(20, 200)] int numberFontSize = 84;
        [SerializeField, Range(10, 100)] int captionFontSize = 28;
        [SerializeField] string caption = "KM/H";

        [Header("Colours")]
        [Tooltip("Far below full scale.")]
        [SerializeField] Color slowColor = new(0.31f, 0.76f, 1f);
        [Tooltip("Making good progress.")]
        [SerializeField] Color onTargetColor = new(0.48f, 0.83f, 0.32f);
        [Tooltip("Closing in on full scale.")]
        [SerializeField] Color fastColor = new(1f, 0.35f, 0.25f);

        SpeedGauge gauge;
        Text number, captionText;
        IShip target;
        float searchTimer;

        /// <summary>The ship being read, once found.</summary>
        public IShip Target => target;

        void Start() => Build();

        void Update()
        {
            if (target as Object == null) target = Resolve();
            if (target as Object == null || gauge == null) return;

            float kmh = Mathf.Max(0f, target.CurrentSpeed * 3.6f);
            number.text = Mathf.RoundToInt(kmh).ToString();
            number.color = ColorFor(kmh);
            gauge.SetFill(Mathf.Clamp01(kmh / fullScaleKmh), fraction => ColorFor(fraction * fullScaleKmh));
        }

        // The wired ship first; otherwise the scene's, asked for again every so often until there is one.
        IShip Resolve()
        {
            if (ship != null) return ship;
            searchTimer -= Time.unscaledDeltaTime;
            if (searchTimer > 0f) return null;
            searchTimer = 0.25f;
            return ShipRegistry.Find(gameObject.scene);
        }

        Color ColorFor(float kmh)
        {
            float progress = Mathf.Clamp01(kmh / fullScaleKmh);
            return progress < 0.6f
                ? Color.Lerp(slowColor, onTargetColor, progress / 0.6f)
                : Color.Lerp(onTargetColor, fastColor, (progress - 0.6f) / 0.4f);
        }

        void Build()
        {
            var root = (RectTransform)transform;
            Font face = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            gauge = SpeedGauge.Build(root, topLeft, new SpeedGauge.Layout
            {
                segments = segments,
                segmentWidth = segmentWidth,
                gap = segmentGap,
                minHeight = minHeight,
                maxHeight = maxHeight,
                emptyAlpha = emptyAlpha,
            });

            // The number stands on the wedge's baseline, just past its last segment; the caption under the wedge.
            number = MakeText("Speed", face, numberFontSize, TextAnchor.LowerLeft,
                              new Vector2(topLeft.x + gauge.Width + 16f, topLeft.y - gauge.Height), new Vector2(400f, numberFontSize * 1.2f));
            captionText = MakeText("Caption", face, captionFontSize, TextAnchor.UpperLeft,
                                   new Vector2(topLeft.x, topLeft.y - gauge.Height - 6f), new Vector2(gauge.Width, captionFontSize * 1.3f));
            captionText.text = caption;
            captionText.color = new Color(1f, 1f, 1f, 0.8f);
            number.text = "0";
            gauge.SetFill(0f, fraction => ColorFor(fraction * fullScaleKmh));
        }

        // A top-left anchored text whose rect's BOTTOM-left sits at the given point (so a number's baseline meets the wedge's).
        Text MakeText(string label, Font face, int size, TextAnchor anchor, Vector2 bottomLeft, Vector2 box)
        {
            var go = new GameObject(label, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = bottomLeft;
            rect.sizeDelta = box;
            var text = go.AddComponent<Text>();
            text.font = face;
            text.fontSize = size;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
