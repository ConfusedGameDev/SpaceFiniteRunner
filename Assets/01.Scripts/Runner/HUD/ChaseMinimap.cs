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
    /// top. Spawned with or without a patrol.
    /// <para><b>The layout is the designer's.</b> The strip, icons and labels
    /// are prefab children wired below; at runtime the GameManager's Spawn
    /// only binds them. Code moves the icons along the strip's height and sets
    /// their size and colours from the settings asset — nothing else: the
    /// strip's rect, the labels' place and fonts and the icons' sideways
    /// offset are left as authored. <b>Rebuild UI</b> creates any unwired
    /// part with a default look and re-applies the style; editing the style
    /// asset does the same live.</para>
    /// </summary>
    public class ChaseMinimap : MonoBehaviour
    {
        [Tooltip("All minimap look tunables live on this asset — add new knobs there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [OnValueChanged(nameof(RebuildUI))]
        ChaseMinimapSettings style;

        [Title("Parts")]
        [Tooltip("The strip: bottom = the track's start, top = its end.")]
        [SerializeField] RectTransform bar;
        [Tooltip("Child of the strip, bottom-anchored: moved up it by the run.")]
        [SerializeField] RectTransform shipIcon;
        [Tooltip("Child of the strip, bottom-anchored, with an Image: hangs under the ship.")]
        [SerializeField] RectTransform policeIcon;
        [Tooltip("Distance to the end of the track.")]
        [SerializeField] Text endText;
        [Tooltip("Patrol gap.")]
        [SerializeField] Text distanceText;

        ShipMotor motor;
        PolicePatrol patrol;     // null on a chase-less run: the map still shows the track
        GameManager gameManager;
        float rangeMeters; // gap beyond which the patrol is off the map; at it the icon hangs a full chaseSpan under the ship
        float warnMeters;  // gap below which the readout turns red

        Image policeImage;
        int shownEnd = -1; // what the two labels currently read, so the strings
        int shownGap = -1; // are rebuilt only when the number changes
        float blinkTimer;
        bool blinkState;

        /// <summary>The ship icon part (editor sync reads its rotation back into the style).</summary>
        public RectTransform ShipIcon => shipIcon;
        /// <summary>The police icon part (editor sync reads its rotation back into the style).</summary>
        public RectTransform PoliceIcon => policeIcon;
        /// <summary>The assigned style asset, or null.</summary>
        public ChaseMinimapSettings StyleAsset => style;

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
            map.Bind();
            return map;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Rebuilds every live minimap on <paramref name="changed"/> — scene
        /// instances and the open prefab stage, never the prefab asset itself.
        /// Called by the settings asset whenever it is edited.
        /// </summary>
        public static void RebuildAllUsing(ChaseMinimapSettings changed)
        {
            foreach (var map in Resources.FindObjectsOfTypeAll<ChaseMinimap>())
                if (map.style == changed && !UnityEditor.EditorUtility.IsPersistent(map))
                    map.RebuildUI();
        }
#endif

        void Awake()
        {
            // Scene-placed instance whose Spawn has not come (or never will —
            // a scene with no GameManager): hide it so no dead gauge lingers.
            if (motor == null) SetShown(false);
        }

        void SetShown(bool shown)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = shown;
        }

        // Runtime: adopt the authored parts (building only what a bare,
        // prefab-less map lacks) and apply the settings' sizes and colours.
        void Bind()
        {
            RebuildUI();
            SetShown(true);
            policeIcon.gameObject.SetActive(false);
            shownEnd = shownGap = -1;
        }

        /// <summary>
        /// Creates any unwired part with the default look, then applies the
        /// style asset to the icons: sizes, colours and sprites. Runs on Spawn,
        /// from this button, and live whenever the style asset is edited or
        /// swapped. The strip's rect, the labels and the icons' sideways offset
        /// are never touched.
        /// </summary>
        [Button("Rebuild UI", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildUI()
        {
            BuildMissingParts();
            ApplyStyle();
        }

        void ApplyStyle()
        {
            var s = Style;
            shipIcon.sizeDelta = Vector2.one * s.ShipIconEdge;
            policeIcon.sizeDelta = Vector2.one * s.PoliceIconEdge;
            var shipImage = shipIcon.GetComponent<Image>();
            if (shipImage != null)
            {
                shipImage.color = s.shipColor;
                ApplySprite(shipImage, s.shipSprite, 45f, s.shipSpriteRotation);
            }
            policeImage = policeIcon.GetComponent<Image>();
            if (policeImage != null)
            {
                policeImage.color = s.policeRed;
                ApplySprite(policeImage, s.policeSprite, 0f, s.policeSpriteRotation);
            }
            blinkTimer = 0f;
            blinkState = false;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(shipIcon);
                UnityEditor.EditorUtility.SetDirty(policeIcon);
                if (shipImage != null) UnityEditor.EditorUtility.SetDirty(shipImage);
                if (policeImage != null) UnityEditor.EditorUtility.SetDirty(policeImage);
            }
#endif
        }

        // An assigned sprite keeps its aspect and takes the style's rotation;
        // none gives back the default shape — a plain square turned by
        // defaultAngle (the ship's diamond is a square at 45°) — so clearing a
        // sprite is live too.
        static void ApplySprite(Image image, Sprite sprite, float defaultAngle, float spriteAngle)
        {
            image.sprite = sprite;
            image.preserveAspect = sprite != null;
            image.transform.localRotation = Quaternion.Euler(0f, 0f, sprite != null ? spriteAngle : defaultAngle);
        }

        // Creates every unwired part with the default look (strip on the right
        // edge, icons on it, labels above and below) and wires it. Parts that
        // are already wired are left exactly as they are.
        void BuildMissingParts()
        {
            var s = Style;
            if (GetComponent<Canvas>() == null)
            {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            if (bar == null)
            {
                // Vertical strip hugging the right edge, vertically centered.
                bar = CreateRect("Bar", transform, new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(10f, 480f));
                bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
            }
            // The patrol first so the ship draws over it.
            if (policeIcon == null)
            {
                policeIcon = CreateRect("Police", bar, new Vector2(0.5f, 0f), Vector2.zero, Vector2.one * s.policeIconSize);
                policeIcon.gameObject.AddComponent<Image>().color = s.policeRed;
                policeIcon.SetAsFirstSibling();
            }
            if (shipIcon == null)
            {
                // A diamond, from the bottom (start) to the top (end).
                shipIcon = CreateRect("Ship", bar, new Vector2(0.5f, 0f), new Vector2(0f, 190f), Vector2.one * s.shipIconSize);
                shipIcon.localRotation = Quaternion.Euler(0f, 0f, 45f);
                shipIcon.gameObject.AddComponent<Image>().color = s.shipColor;
            }
            if (endText == null) endText = CreateLabel("EndDistance", new Vector2(0.5f, 1f), new Vector2(0f, 44f), "12.4 KM");
            if (distanceText == null) distanceText = CreateLabel("Distance", new Vector2(0.5f, 0f), new Vector2(0f, -44f), "512 M");
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        Text CreateLabel(string name, Vector2 anchor, Vector2 position, string preview)
        {
            var rect = CreateRect(name, bar, anchor, position, new Vector2(180f, 40f));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = preview;
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
            shipIcon.anchoredPosition = new Vector2(shipIcon.anchoredPosition.x, shipY);

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
            if (visible) policeIcon.anchoredPosition = new Vector2(policeIcon.anchoredPosition.x, policeY);

            int gapKey = Mathf.RoundToInt(gap);
            if (gapKey != shownGap)
            {
                shownGap = gapKey;
                distanceText.text = $"{gapKey} M";
            }
            distanceText.color = gap <= warnMeters ? Style.policeRed : Color.white;

            // Red/blue flicker, same cadence as the patrol's light bar — or
            // one steady colour when the blink is off.
            if (policeImage == null) return;
            if (!Style.policeBlink)
            {
                policeImage.color = Style.policeRed;
                return;
            }
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
