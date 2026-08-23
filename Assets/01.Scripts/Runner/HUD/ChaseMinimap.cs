using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Chase minimap on the right side of the screen: a vertical strip with
    /// the ship pinned at the top and the patrol icon climbing toward it as
    /// the gap closes, plus the gap in meters at the bottom. Lives as a scene
    /// prefab with a baked editor preview (Rebuild Preview); at runtime the
    /// GameManager's Spawn finds it, clears the preview and rebuilds live.
    /// Look tunables live on the ChaseMinimapSettings asset.
    /// </summary>
    public class ChaseMinimap : MonoBehaviour
    {
        [Tooltip("All minimap look tunables live on this asset — add new knobs there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        ChaseMinimapSettings style;

        ShipMotor motor;
        PolicePatrol patrol;
        float rangeMeters; // gap at which the patrol icon sits at the very bottom
        float warnMeters;  // gap below which the readout turns red

        RectTransform bar;
        RectTransform policeIcon;
        Image policeImage;
        Text distanceText;
        float blinkTimer;
        bool blinkState;

        ChaseMinimapSettings Style => style != null ? style : style = ScriptableObject.CreateInstance<ChaseMinimapSettings>();

        public static ChaseMinimap Spawn(ShipMotor motor, PolicePatrol patrol, float rangeMeters, float warnMeters)
        {
            var map = FindFirstObjectByType<ChaseMinimap>();
            if (map == null) map = new GameObject("ChaseMinimap").AddComponent<ChaseMinimap>();
            map.motor = motor;
            map.patrol = patrol;
            map.rangeMeters = Mathf.Max(1f, rangeMeters);
            map.warnMeters = warnMeters;
            map.Build();
            return map;
        }

        void Awake()
        {
            // Scene-placed instance whose Spawn never came (patrol disabled):
            // drop the baked preview so no dead gauge lingers on screen.
            if (motor == null) TearDown();
        }

        /// <summary>Editor bake: regenerates the preview so the prefab is visible before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            distanceText.text = "512 M";
        }

        // Root components are reused by Build — see RpgMessageSystem.TearDown.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            bar = null;
            policeIcon = null;
            policeImage = null;
            distanceText = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }


        void Build()
        {
            TearDown();
            var s = Style;

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Vertical strip hugging the right edge, vertically centered.
            bar = CreateRect("Bar", transform, new Vector2(1f, 0.5f), s.barOffset, s.barSize);
            bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, s.barAlpha);

            // The ship: fixed at the top, a diamond.
            var shipIcon = CreateRect("Ship", bar, new Vector2(0.5f, 1f), Vector2.zero, Vector2.one * s.shipIconSize);
            shipIcon.localRotation = Quaternion.Euler(0f, 0f, 45f);
            shipIcon.gameObject.AddComponent<Image>().color = s.shipColor;

            // The patrol: climbs the strip as it closes in.
            policeIcon = CreateRect("Police", bar, new Vector2(0.5f, 0f), Vector2.zero, Vector2.one * s.policeIconSize);
            policeImage = policeIcon.gameObject.AddComponent<Image>();
            policeImage.color = s.policeRed;

            // Gap readout under the strip.
            var textRect = CreateRect("Distance", bar, new Vector2(0.5f, 0f), new Vector2(0f, -44f), new Vector2(180f, 40f));
            distanceText = textRect.gameObject.AddComponent<Text>();
            distanceText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            distanceText.fontSize = s.fontSize;
            distanceText.fontStyle = FontStyle.Bold;
            distanceText.alignment = TextAnchor.MiddleCenter;
            distanceText.color = Color.white;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        void Update()
        {
            if (motor == null || patrol == null) return;

            float gap = Mathf.Max(0f, patrol.GapToShip);

            // Bottom of the strip = rangeMeters (or more) behind; top = caught.
            float climb = 1f - Mathf.Clamp01(gap / rangeMeters);
            policeIcon.anchoredPosition = new Vector2(0f, climb * bar.rect.height);

            distanceText.text = $"{gap:0} M";
            distanceText.color = gap <= warnMeters ? Style.policeRed : Color.white;

            // Red/blue flicker, same cadence as the patrol's light bar.
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= Style.blinkInterval)
            {
                blinkTimer = 0f;
                blinkState = !blinkState;
                policeImage.color = blinkState ? Style.policeBlue : Style.policeRed;
            }
        }
    }
}
