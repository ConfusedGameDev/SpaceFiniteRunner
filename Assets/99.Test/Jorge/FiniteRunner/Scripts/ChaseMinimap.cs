using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// Chase minimap on the right side of the screen: a vertical strip with
    /// the ship pinned at the top and the patrol icon climbing toward it as
    /// the gap closes, plus the gap in meters at the bottom. Built entirely
    /// from code on its own overlay canvas — no scene wiring needed.
    /// Spawned by the GameManager alongside the patrol.
    /// </summary>
    public class ChaseMinimap : MonoBehaviour
    {
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

        static readonly Color ShipColor = new(0.48f, 1f, 0.4f);
        static readonly Color PoliceRed = new(1f, 0.25f, 0.2f);
        static readonly Color PoliceBlue = new(0.3f, 0.5f, 1f);

        public static ChaseMinimap Spawn(ShipMotor motor, PolicePatrol patrol, float rangeMeters, float warnMeters)
        {
            var go = new GameObject("ChaseMinimap");
            var map = go.AddComponent<ChaseMinimap>();
            map.motor = motor;
            map.patrol = patrol;
            map.rangeMeters = Mathf.Max(1f, rangeMeters);
            map.warnMeters = warnMeters;
            map.Build();
            return map;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Vertical strip hugging the right edge, vertically centered.
            bar = CreateRect("Bar", transform, new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(10f, 480f));
            bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

            // The ship: fixed at the top, a diamond.
            var shipIcon = CreateRect("Ship", bar, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(24f, 24f));
            shipIcon.localRotation = Quaternion.Euler(0f, 0f, 45f);
            shipIcon.gameObject.AddComponent<Image>().color = ShipColor;

            // The patrol: climbs the strip as it closes in.
            policeIcon = CreateRect("Police", bar, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(20f, 20f));
            policeImage = policeIcon.gameObject.AddComponent<Image>();
            policeImage.color = PoliceRed;

            // Gap readout under the strip.
            var textRect = CreateRect("Distance", bar, new Vector2(0.5f, 0f), new Vector2(0f, -44f), new Vector2(180f, 40f));
            distanceText = textRect.gameObject.AddComponent<Text>();
            distanceText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            distanceText.fontSize = 28;
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
            distanceText.color = gap <= warnMeters ? PoliceRed : Color.white;

            // Red/blue flicker, same cadence as the patrol's light bar.
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= 0.25f)
            {
                blinkTimer = 0f;
                blinkState = !blinkState;
                policeImage.color = blinkState ? PoliceBlue : PoliceRed;
            }
        }
    }
}
