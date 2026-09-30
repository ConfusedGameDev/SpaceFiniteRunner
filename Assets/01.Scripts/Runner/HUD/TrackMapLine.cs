using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The chase minimap's track line (TrackAuthoringPRD M6): a UI graphic that
    /// draws a polyline — the whole track seen from above — inside its own
    /// rect, as one quad per segment (each pushed half a width past its ends
    /// so the joints close). The first <see cref="SetDriven"/> points are
    /// drawn in the driven colour: the stretch already flown. Points are in
    /// the rect's local space, offsets from its centre; the minimap fits
    /// them. A picture only: no raycasts.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class TrackMapLine : MaskableGraphic
    {
        readonly List<Vector2> points = new();
        float width = 3f;
        Color drivenColor = Color.green;
        int drivenCount;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>The line, as offsets from the rect's centre.</summary>
        public void SetPoints(List<Vector2> newPoints)
        {
            points.Clear();
            if (newPoints != null) points.AddRange(newPoints);
            drivenCount = 0;
            SetVerticesDirty();
        }

        public void SetStyle(float lineWidth, Color lineColor, Color driven)
        {
            width = Mathf.Max(0.5f, lineWidth);
            color = lineColor;
            drivenColor = driven;
            SetVerticesDirty();
        }

        /// <summary>How many points from the start are drawn as flown (rebuilds only when it changes).</summary>
        public void SetDriven(int count)
        {
            count = Mathf.Clamp(count, 0, points.Count);
            if (count == drivenCount) return;
            drivenCount = count;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2) return;
            Vector2 centre = rectTransform.rect.center;
            float half = width * 0.5f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = centre + points[i], b = centre + points[i + 1];
                Vector2 along = b - a;
                if (along.sqrMagnitude < 1e-6f) continue;
                along.Normalize();
                Vector2 across = new Vector2(-along.y, along.x) * half;
                a -= along * half; // overlap the neighbours: no gaps at the bends
                b += along * half;

                Color32 c = i < drivenCount ? drivenColor : color;
                int start = vh.currentVertCount;
                vh.AddVert(a - across, c, Vector2.zero);
                vh.AddVert(a + across, c, Vector2.zero);
                vh.AddVert(b + across, c, Vector2.zero);
                vh.AddVert(b - across, c, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
