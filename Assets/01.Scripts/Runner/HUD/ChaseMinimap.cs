using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Track map on the right side of the screen: a vertical strip that IS
    /// the track — the ship diamond starts at the bottom and climbs to the
    /// top as it nears the end, the distance left reads above the strip and
    /// the patrol gap in meters below it. The patrol icon hangs under the
    /// ship on its own zoomed scale (the full minimap range = chaseSpan
    /// pixels; at track scale the gap would be a pixel or two), and is drawn
    /// only when it is inside that range AND there is strip left under the
    /// ship to draw it on. On an endless track the ship stays pinned at the
    /// top. Spawned with or without a patrol. Lives as a scene
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
        PolicePatrol patrol;     // null on a chase-less run: the map still shows the track
        GameManager gameManager;
        float rangeMeters; // gap beyond which the patrol is off the map; at it the icon hangs a full chaseSpan under the ship
        float warnMeters;  // gap below which the readout turns red

        RectTransform bar;
        RectTransform shipIcon;
        RectTransform policeIcon;
        Image policeImage;
        Text endText;      // distance to the end of the track, above the strip
        Text distanceText; // patrol gap, below the strip
        int shownEnd = -1; // what the two labels currently read, so the strings
        int shownGap = -1; // are rebuilt only when the number changes
        float blinkTimer;
        bool blinkState;

        ChaseMinimapSettings Style => style != null ? style : style = ScriptableObject.CreateInstance<ChaseMinimapSettings>();

        public static ChaseMinimap Spawn(ShipMotor motor, PolicePatrol patrol, GameManager gameManager, float rangeMeters, float warnMeters)
        {
            var map = FindFirstObjectByType<ChaseMinimap>();
            if (map == null) map = new GameObject("ChaseMinimap").AddComponent<ChaseMinimap>();
            map.motor = motor;
            map.patrol = patrol;
            map.gameManager = gameManager;
            map.rangeMeters = Mathf.Max(1f, rangeMeters);
            map.warnMeters = warnMeters;
            map.Build();
            return map;
        }

        void Awake()
        {
            // Scene-placed instance whose Spawn has not come (or never will —
            // a scene with no GameManager): drop the baked preview so no dead
            // gauge lingers on screen.
            if (motor == null) TearDown();
        }

        /// <summary>Editor bake: regenerates the preview so the prefab is visible before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            float shipY = Style.barSize.y * 0.4f;
            shipIcon.anchoredPosition = new Vector2(0f, shipY);
            policeIcon.anchoredPosition = new Vector2(0f, shipY - Style.chaseSpan * 0.5f);
            policeIcon.gameObject.SetActive(true);
            endText.text = "12.4 KM";
            distanceText.text = "512 M";
        }

        // Root components are reused by Build — see RpgMessageSystem.TearDown.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            bar = null;
            shipIcon = null;
            policeIcon = null;
            policeImage = null;
            endText = null;
            distanceText = null;
            shownEnd = shownGap = -1;
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

            // The patrol: hangs under the ship, hidden until Update says it
            // is close enough and has strip to stand on. Built first so the
            // ship draws over it.
            policeIcon = CreateRect("Police", bar, new Vector2(0.5f, 0f), Vector2.zero, Vector2.one * s.policeIconSize);
            policeImage = policeIcon.gameObject.AddComponent<Image>();
            policeImage.color = s.policeRed;
            policeIcon.gameObject.SetActive(false);

            // The ship: a diamond, from the bottom (start) to the top (end).
            shipIcon = CreateRect("Ship", bar, new Vector2(0.5f, 0f), Vector2.zero, Vector2.one * s.shipIconSize);
            shipIcon.localRotation = Quaternion.Euler(0f, 0f, 45f);
            shipIcon.gameObject.AddComponent<Image>().color = s.shipColor;

            // Distance to the end above the strip, patrol gap under it.
            endText = CreateLabel("EndDistance", new Vector2(0.5f, 1f), new Vector2(0f, 44f), s.fontSize);
            distanceText = CreateLabel("Distance", new Vector2(0.5f, 0f), new Vector2(0f, -44f), s.fontSize);
        }

        Text CreateLabel(string name, Vector2 anchor, Vector2 position, int fontSize)
        {
            var rect = CreateRect(name, bar, anchor, position, new Vector2(180f, 40f));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
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
            if (motor == null || bar == null) return;

            // The strip is the track: bottom = start, top = end. An endless
            // track has no end to climb to — the ship stays pinned at the top.
            bool finite = gameManager != null && gameManager.HasTrackEnd;
            float remaining = finite ? gameManager.DistanceRemaining : 0f;
            float travelled = Mathf.Max(0f, motor.DistanceTravelled);
            float length = travelled + remaining;
            float shipY = (finite && length > 0f ? Mathf.Clamp01(travelled / length) : 1f) * bar.rect.height;
            shipIcon.anchoredPosition = new Vector2(0f, shipY);

            // Kilometres with one decimal, metres on the last one. Keyed on
            // the shown number (decametres / metres) so the string is rebuilt
            // only when it changes.
            int endKey = !finite ? -2 : remaining >= 1000f ? Mathf.RoundToInt(remaining / 100f) + 1000 : Mathf.RoundToInt(remaining);
            if (endKey != shownEnd)
            {
                shownEnd = endKey;
                endText.text = !finite ? "" : remaining >= 1000f ? $"{remaining / 1000f:0.0} KM" : $"{remaining:0} M";
            }

            // No patrol, one the end of the track took, or one mid-kill: off
            // the map altogether. The kill's teleport must not be visible here
            // either, or the map gives away that it is the same car.
            if (patrol == null || patrol.HiddenFromMap)
            {
                if (policeIcon.gameObject.activeSelf) policeIcon.gameObject.SetActive(false);
                if (shownGap != -2)
                {
                    shownGap = -2;
                    distanceText.text = "";
                }
                return;
            }

            float gap = Mathf.Max(0f, patrol.GapToShip);

            // The patrol hangs under the ship on a zoomed scale (rangeMeters =
            // chaseSpan pixels). Shown only inside that range, and only while
            // that spot is still on the strip — a ship at the very bottom has
            // nothing under it to draw the patrol on.
            float policeY = shipY - gap / rangeMeters * Style.chaseSpan;
            bool visible = gap <= rangeMeters && policeY >= 0f;
            if (policeIcon.gameObject.activeSelf != visible) policeIcon.gameObject.SetActive(visible);
            if (visible) policeIcon.anchoredPosition = new Vector2(0f, policeY);

            int gapKey = Mathf.RoundToInt(gap);
            if (gapKey != shownGap)
            {
                shownGap = gapKey;
                distanceText.text = $"{gapKey} M";
            }
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
