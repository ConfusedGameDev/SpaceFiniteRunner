using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// A segmented ring gauge for the code-built overlays: N arc segments
    /// laid around a circle, lit from the start angle up to a fraction of
    /// the sweep, each segment its own colour — the radial twin of the
    /// runner's SpeedGauge wedge, with the same SetFill contract (fraction +
    /// a colour-for-fraction callback, alpha as the "lit" channel, no Image
    /// writes unless something changed). Each segment is one Radial360-filled
    /// annulus Image rotated to its start angle, so the ring is a handful of
    /// Images and no custom mesh. An optional track (a dim full ring) sits
    /// under the segments so "empty" is still a shape on screen.
    /// Geometry is fixed at Create; colours and fill are live.
    /// </summary>
    public sealed class RingGauge : MonoBehaviour
    {
        public struct Layout
        {
            public int segments;        // arc count around the sweep
            public float diameter;      // outer diameter, reference pixels
            public float thickness;     // ring thickness, reference pixels
            public float gapDegrees;    // dark gap between neighbouring segments
            public float startDegrees;  // where segment 0 begins: 0 = top, positive = clockwise
            public float sweepDegrees;  // total arc the segments cover (360 = a full ring)
            public bool clockwise;      // fill direction from the start angle
            public float emptyAlpha;    // alpha of an unlit segment
            public Color trackColor;    // full ring under the segments; alpha 0 = none
        }

        const int SpriteSize = 256;

        Layout layout;
        Image[] segments;
        Color[] palette;
        int lit = -1;
        RectTransform rect;

        /// <summary>The ring's own rect — centre-anchored on the parent; scale it for a punch.</summary>
        public RectTransform Rect => rect;

        public static RingGauge Create(string name, Transform parent, Layout layout)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(layout.diameter, layout.diameter);

            var gauge = go.AddComponent<RingGauge>();
            gauge.rect = rect;
            gauge.layout = layout;
            gauge.layout.segments = Mathf.Max(1, layout.segments);
            gauge.Build();
            return gauge;
        }

        void Build()
        {
            // Sprite thickness is in texture pixels; the Image is stretched to the diameter.
            int texThickness = Mathf.Max(1, Mathf.RoundToInt(layout.thickness * SpriteSize / Mathf.Max(1f, layout.diameter)));
            Sprite ring = UiSprites.Ring(SpriteSize, texThickness);
            var size = new Vector2(layout.diameter, layout.diameter);

            if (layout.trackColor.a > 0f)
            {
                Image track = MakeImage("Track", ring, size);
                track.color = layout.trackColor;
            }

            int count = layout.segments;
            float step = layout.sweepDegrees / count;
            float arc = Mathf.Max(0f, step - layout.gapDegrees);
            float dir = layout.clockwise ? 1f : -1f;
            segments = new Image[count];
            palette = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Image seg = MakeImage($"Segment{i}", ring, size);
                seg.type = Image.Type.Filled;           // after the sprite: a Simple image ignores fill
                seg.fillMethod = Image.FillMethod.Radial360;
                seg.fillOrigin = (int)Image.Origin360.Top;
                seg.fillClockwise = layout.clockwise;
                seg.fillAmount = arc / 360f;
                // Unity UI rotates counter-clockwise for positive z, so a
                // clockwise start angle is a negative rotation.
                float start = layout.startDegrees + dir * i * step;
                seg.rectTransform.localEulerAngles = new Vector3(0f, 0f, -start);
                segments[i] = seg;
                palette[i] = Color.white;
            }
            Recolour();
        }

        Image MakeImage(string name, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            var r = (RectTransform)go.transform;
            r.SetParent(rect, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Lights the ring up to <paramref name="fraction01"/> of its sweep.
        /// <paramref name="colorAt"/> gives the colour for a fraction of the
        /// range (each segment asks for the fraction its far edge stands
        /// for); its alpha is the lit segment's alpha, so a caller can blink
        /// the whole ring through it. Null keeps the current palette.
        /// </summary>
        public void SetFill(float fraction01, System.Func<float, Color> colorAt)
        {
            if (segments == null) return;
            int count = segments.Length;
            int next = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(fraction01) * count + 1e-4f), 0, count);

            bool paletteChanged = false;
            if (colorAt != null)
            {
                for (int i = 0; i < count; i++)
                {
                    Color c = colorAt((i + 1) / (float)count);
                    if (c != palette[i]) { palette[i] = c; paletteChanged = true; }
                }
            }
            if (next == lit && !paletteChanged) return;
            lit = next;
            Recolour();
        }

        /// <summary>How many segments are lit right now.</summary>
        public int Lit => Mathf.Max(0, lit);

        /// <summary>Segment count.</summary>
        public int Segments => segments != null ? segments.Length : 0;

        void Recolour()
        {
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                Color c = palette[i];
                if (i >= lit) c.a = layout.emptyAlpha;
                segments[i].color = c;
            }
        }
    }
}
