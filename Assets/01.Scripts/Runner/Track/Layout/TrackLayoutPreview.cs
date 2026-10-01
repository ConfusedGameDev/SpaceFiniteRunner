using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A saved track's shape from above, with no track built: the SELECT
    /// COURSE screen's preview. <see cref="SampleXZ"/> rebuilds the layout's
    /// spline in memory — the same <c>Spline.Add(knot, mode)</c> call
    /// <c>TrackManager</c> makes, so AutoSmooth knots curve as they do in
    /// play — and samples its centre line on the XZ plane; <see cref="Fit"/>
    /// is the chase minimap's framing (start → finish up the map, bounds
    /// fitted with the aspect kept), giving offsets from a rect's centre
    /// that <c>TrackMapLine.SetPoints</c> takes as they are. Loops and tubes
    /// are inserted sections, so only a loop's bridge shows — the HUD map
    /// draws the same. Pure functions: nothing here touches a scene.
    /// </summary>
    public static class TrackLayoutPreview
    {
        /// <summary>
        /// <paramref name="samples"/> points along the centre line, as
        /// (x, z). The raw knot positions when the layout has under two
        /// knots or the spline cannot be built.
        /// </summary>
        public static List<Vector2> SampleXZ(TrackLayout layout, int samples = 200)
        {
            var points = new List<Vector2>();
            if (layout == null || layout.knots == null || layout.knots.Count < 2) return points;
            samples = Mathf.Max(2, samples);

            try
            {
                var spline = new Spline(layout.knots.Count);
                foreach (TrackLayout.Knot knot in layout.knots) spline.Add(knot.ToBezier(), knot.mode);
                for (int i = 0; i < samples; i++)
                {
                    float3 p = spline.EvaluatePosition(i / (samples - 1f));
                    points.Add(new Vector2(p.x, p.z));
                }
                return points;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{nameof(TrackLayoutPreview)}: spline rebuild failed, drawing the knots instead ({e.Message}).");
                points.Clear();
                foreach (TrackLayout.Knot knot in layout.knots) points.Add(new Vector2(knot.position.x, knot.position.z));
                return points;
            }
        }

        /// <summary>
        /// Frames <paramref name="raw"/> inside a rect of <paramref name="area"/>:
        /// turned so start → finish runs along <paramref name="runDirection"/>
        /// (up the map by default, the chase minimap's convention; right for a
        /// landscape plane; zero = world north up), scaled to fit with
        /// <paramref name="padding"/> (a fraction of each side) kept clear and
        /// the aspect kept, centred. Offsets from the rect's centre.
        /// </summary>
        public static List<Vector2> Fit(IReadOnlyList<Vector2> raw, Vector2 area, float padding = 0.08f, Vector2? runDirection = null)
        {
            var fitted = new List<Vector2>();
            if (raw == null || raw.Count == 0) return fitted;

            Vector2 direction = runDirection ?? Vector2.up;
            float turn = 0f;
            Vector2 run = raw[raw.Count - 1] - raw[0];
            if (direction.sqrMagnitude > 0f && run.sqrMagnitude > 1f) turn = Vector2.SignedAngle(run, direction);
            Quaternion rotate = Quaternion.Euler(0f, 0f, turn);

            var turned = new List<Vector2>(raw.Count);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < raw.Count; i++)
            {
                Vector2 r = rotate * raw[i];
                turned.Add(r);
                min = Vector2.Min(min, r);
                max = Vector2.Max(max, r);
            }

            float usable = 1f - 2f * Mathf.Clamp(padding, 0f, 0.45f);
            Vector2 size = Vector2.Max(max - min, Vector2.one);
            float scale = Mathf.Min(area.x * usable / size.x, area.y * usable / size.y);
            Vector2 middle = (min + max) * 0.5f;
            foreach (Vector2 r in turned) fitted.Add((r - middle) * scale);
            return fitted;
        }
    }
}
