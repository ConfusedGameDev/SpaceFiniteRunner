using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// Analog speedometer in the bottom-left corner, mirroring the radar in
    /// the bottom-right: circular face with tick marks (red past the
    /// redline), a smoothed needle sweeping the dial and a digital km/h
    /// readout (built-in LegacyRuntime font — no TMP setup needed). Built
    /// entirely from code on its own overlay canvas; CityManager spawns it
    /// when its speedometer settings field is assigned; hides itself while
    /// no player car exists. All look/feel knobs live on SpeedometerSettings.
    /// </summary>
    public class Speedometer : MonoBehaviour
    {
        const int UiLayer = 5;

        [Required, InlineEditor]
        [Tooltip("All gauge tunables live on this asset — add new knobs there, not here.")]
        public SpeedometerSettings settings;

        Canvas canvas;
        RectTransform needle;
        Image needleImage;
        Text digital;
        Text unitLabel;
        CarController player;
        Sprite circleSprite;
        float needleAngle;
        float refreshTimer;
        bool built;

        void Update()
        {
            if (settings == null) return;
            if (!built) Build();

            RefreshTarget();
            // HudSuppressed: the dialogue box can ask the gauges to step aside while it talks.
            bool visible = player != null && !RpgMessageSystem.HudSuppressed;
            if (canvas.enabled != visible) canvas.enabled = visible;
            if (!visible) return;

            float speed = player.SpeedKmh;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(1f, settings.maxSpeedKmh));

            // Needle: 0 = left end of the sweep, 1 = right end, eased toward the target.
            float target = Mathf.Lerp(settings.sweepDegrees * 0.5f, -settings.sweepDegrees * 0.5f, speed01);
            needleAngle = Mathf.Lerp(needleAngle, target, 1f - Mathf.Exp(-settings.needleSharpness * Time.deltaTime));
            needle.localEulerAngles = new Vector3(0f, 0f, needleAngle);
            needleImage.color = settings.needleColor;

            digital.enabled = unitLabel.enabled = settings.showDigital;
            if (settings.showDigital)
            {
                digital.text = Mathf.RoundToInt(speed).ToString();
                digital.color = speed01 >= settings.redlineFraction ? settings.redlineColor : settings.textColor;
                unitLabel.color = settings.textColor;
            }
        }

        void RefreshTarget()
        {
            refreshTimer -= Time.deltaTime;
            if (player != null && refreshTimer > 0f) return;
            refreshTimer = 1f;
            player = PatrolManager.FindPlayerCar();
        }

        // --------------------------------------------------------------- build

        /// <summary>Editor bake: regenerates the gauge preview so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            canvas.enabled = true;
            if (digital != null) digital.text = "88";
        }

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            canvas = null;
            needle = null;
            needleImage = null;
            digital = unitLabel = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void Build()
        {
            TearDown();
            built = true;
            circleSprite = CreateCircleSprite(128);
            float size = settings.sizePixels;
            float radius = size * 0.5f;

            canvas = new GameObject("SpeedometerCanvas").AddComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // HUD tier — below the RPG messages (15) and the pause menu (20)
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Anchored to the bottom-left corner.
            RectTransform root = CreateRect("Root", canvas.transform, new Vector2(size, size));
            root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = new Vector2(settings.marginPixels, settings.marginPixels);

            CreateImage("Border", root, circleSprite, settings.borderColor,
                new Vector2(size + settings.borderWidth * 2f, size + settings.borderWidth * 2f));
            CreateImage("Face", root, circleSprite, settings.backgroundColor, new Vector2(size, size));

            BuildTicks(root, radius);

            // Needle: bottom-pivoted bar rotating around the hub.
            Image needleImg = CreateImage("Needle", root, null, settings.needleColor,
                new Vector2(settings.needleWidth, radius * settings.needleLength));
            needleImg.rectTransform.pivot = new Vector2(0.5f, 0f);
            needleImg.rectTransform.anchoredPosition = Vector2.zero;
            needle = needleImg.rectTransform;
            needleImage = needleImg;
            CreateImage("Hub", root, circleSprite, settings.borderColor, new Vector2(size * 0.1f, size * 0.1f));

            digital = CreateText("Digital", root, Mathf.RoundToInt(size * 0.18f), settings.textColor, FontStyle.Bold);
            digital.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.22f);
            digital.text = "0";
            unitLabel = CreateText("Unit", root, Mathf.RoundToInt(size * 0.075f), settings.textColor, FontStyle.Normal);
            unitLabel.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.34f);
            unitLabel.text = "km/h";
        }

        void BuildTicks(RectTransform root, float radius)
        {
            int tickCount = Mathf.FloorToInt(settings.maxSpeedKmh / Mathf.Max(1f, settings.tickIntervalKmh));
            for (int i = 0; i <= tickCount; i++)
            {
                float fraction = i * settings.tickIntervalKmh / settings.maxSpeedKmh;
                float angle = Mathf.Lerp(settings.sweepDegrees * 0.5f, -settings.sweepDegrees * 0.5f, fraction);
                Color color = fraction >= settings.redlineFraction ? settings.redlineColor : settings.tickColor;

                Image tick = CreateImage($"Tick_{i}", root, null, color, new Vector2(3f, radius * 0.1f));
                tick.rectTransform.pivot = new Vector2(0.5f, 1f);       // hang inward from the rim
                tick.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
                float rad = (angle + 90f) * Mathf.Deg2Rad;              // z-rotation 0 = up
                tick.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (radius * 0.93f);
            }
        }

        // ---------------------------------------------------------- UI helpers

        static RectTransform CreateRect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = UiLayer;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.gameObject.layer = UiLayer;
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = Vector2.zero;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        static Text CreateText(string name, Transform parent, int fontSize, Color color, FontStyle style)
        {
            var text = new GameObject(name).AddComponent<Text>();
            text.gameObject.layer = UiLayer;
            text.transform.SetParent(parent, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(10f, 10f); // overflow renders beyond this
            return text;
        }

        static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            float radius = size * 0.5f - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                byte alpha = (byte)(255f * Mathf.Clamp01(radius - distance + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
