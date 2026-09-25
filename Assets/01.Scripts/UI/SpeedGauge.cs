using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The runner's speed bar: a row of segments (the Store's pips, grown
    /// up) whose heights ramp from short at the left to tall at the right —
    /// a tachometer wedge — lit from the left up to the ship's fraction of
    /// Light Speed. Each segment carries its OWN colour for the fraction it
    /// stands for (the HUD's blue → green → hot ramp), so the wedge reads as
    /// a scale even before anything is lit. Plain quads, no sprites. Two ways
    /// in: <see cref="Build"/> makes its own top-left anchored rect (the
    /// standalone ship's HUD), <see cref="BuildInto"/> fills a rect a designer
    /// placed and never moves or resizes it (the runner's HUD) — the segments
    /// are anchored as fractions of that rect, so they follow it.
    /// </summary>
    public class SpeedGauge : MonoBehaviour
    {
        /// <summary>The geometry, handed in by the HUD from its inspector knobs.</summary>
        public struct Layout
        {
            public int segments;
            public float segmentWidth;
            public float gap;
            public float minHeight;
            public float maxHeight;
            public float emptyAlpha;
        }

        Image[] segments;
        Layout layout;
        int lit = -1;
        Color[] palette;

        /// <summary>Total width of the wedge — where the number sits after it.</summary>
        public float Width => layout.segments * layout.segmentWidth + Mathf.Max(0, layout.segments - 1) * layout.gap;

        /// <summary>Total height — the tallest segment; every segment stands on the same baseline.</summary>
        public float Height => layout.maxHeight;

        /// <summary>
        /// Builds the wedge under <paramref name="parent"/> with its top-left
        /// corner at <paramref name="topLeft"/> (top-left anchored, so it sits
        /// beside the HUD's own texts).
        /// </summary>
        public static SpeedGauge Build(RectTransform parent, Vector2 topLeft, Layout layout)
        {
            layout.segments = Mathf.Max(1, layout.segments);
            var go = new GameObject("SpeedGauge", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;

            var gauge = go.AddComponent<SpeedGauge>();
            gauge.layout = layout;
            rect.sizeDelta = new Vector2(gauge.Width, gauge.Height);

            gauge.segments = new Image[layout.segments];
            gauge.palette = new Color[layout.segments];
            for (int i = 0; i < layout.segments; i++)
            {
                float t = layout.segments > 1 ? i / (float)(layout.segments - 1) : 1f;
                float height = Mathf.Lerp(layout.minHeight, layout.maxHeight, t);

                var segGo = new GameObject($"Segment{i}", typeof(RectTransform));
                var seg = (RectTransform)segGo.transform;
                seg.SetParent(rect, false);
                // Bottom-left anchored and pivoted: every segment stands on the wedge's baseline.
                seg.anchorMin = seg.anchorMax = new Vector2(0f, 0f);
                seg.pivot = new Vector2(0f, 0f);
                seg.anchoredPosition = new Vector2(i * (layout.segmentWidth + layout.gap), 0f);
                seg.sizeDelta = new Vector2(layout.segmentWidth, height);

                var image = segGo.AddComponent<Image>();
                image.raycastTarget = false;
                gauge.segments[i] = image;
                gauge.palette[i] = Color.white;
            }
            gauge.Recolour();
            return gauge;
        }

        /// <summary>
        /// Fills <paramref name="container"/> with the wedge: the segments share
        /// its width (minus the gaps), the tallest is its full height and the
        /// shortest <paramref name="minHeightFraction"/> of it. The container's
        /// own transform is never touched; segments a previous build (or an
        /// editor preview) left in it are replaced.
        /// </summary>
        public static SpeedGauge BuildInto(RectTransform container, int segmentCount, float gap, float minHeightFraction, float emptyAlpha)
        {
            segmentCount = Mathf.Max(1, segmentCount);
            ClearSegments(container);

            var gauge = container.GetComponent<SpeedGauge>();
            if (gauge == null) gauge = container.gameObject.AddComponent<SpeedGauge>();

            Rect rect = container.rect;
            float width = Mathf.Max(1f, rect.width);
            float cell = Mathf.Max(0f, (width - (segmentCount - 1) * gap) / segmentCount);
            gauge.layout = new Layout
            {
                segments = segmentCount,
                segmentWidth = cell,
                gap = gap,
                minHeight = rect.height * minHeightFraction,
                maxHeight = rect.height,
                emptyAlpha = emptyAlpha,
            };

            gauge.segments = new Image[segmentCount];
            gauge.palette = new Color[segmentCount];
            gauge.lit = -1;
            for (int i = 0; i < segmentCount; i++)
            {
                float t = segmentCount > 1 ? i / (float)(segmentCount - 1) : 1f;
                float left = i * (cell + gap) / width;

                var segGo = new GameObject($"{SegmentPrefix}{i}", typeof(RectTransform));
                var seg = (RectTransform)segGo.transform;
                seg.SetParent(container, false);
                // Fractions of the container, standing on its bottom edge.
                seg.anchorMin = new Vector2(left, 0f);
                seg.anchorMax = new Vector2(left + cell / width, Mathf.Lerp(minHeightFraction, 1f, t));
                seg.pivot = Vector2.zero;
                seg.offsetMin = seg.offsetMax = Vector2.zero;

                var image = segGo.AddComponent<Image>();
                image.raycastTarget = false;
                gauge.segments[i] = image;
                gauge.palette[i] = Color.white;
            }
            gauge.Recolour();
            return gauge;
        }

        const string SegmentPrefix = "Segment";

        static void ClearSegments(RectTransform container)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                GameObject child = container.GetChild(i).gameObject;
                if (!child.name.StartsWith(SegmentPrefix)) continue;
                if (Application.isPlaying)
                {
                    child.SetActive(false); // Destroy lands at the end of the frame
                    Destroy(child);
                }
                else DestroyImmediate(child);
            }
        }

        /// <summary>
        /// Lights the wedge up to <paramref name="fraction01"/> of its length.
        /// <paramref name="colorAt"/> gives the colour for a fraction of the
        /// range — each segment asks for the fraction its right edge stands
        /// for, so the palette is the scale's, not the current speed's.
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

        void Recolour()
        {
            for (int i = 0; i < segments.Length; i++)
            {
                Color c = palette[i];
                c.a = i < lit ? 1f : layout.emptyAlpha;
                segments[i].color = c;
            }
        }
    }
}
