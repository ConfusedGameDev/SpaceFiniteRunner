using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Debugging
{
    /// <summary>
    /// A frame's worth of debug lines plus the immediate-mode renderer that
    /// puts them on screen. Visualizers rebuild the buffer from live gameplay
    /// data and hand it to <see cref="Render"/> once per camera.
    ///
    /// Drawing through GL rather than Gizmos is the whole point: a Gizmo-only
    /// overlay is invisible in the Game view unless the Gizmos toggle is on,
    /// and the thing being debugged (an AI car deciding where to turn) is
    /// watched while playing. Everything is a line — a circle is a fan of them
    /// — because one GL.LINES batch per visualizer is cheap enough to leave
    /// running for a whole session.
    ///
    /// The materials are shared statics with HideAndDontSave: an editor-only
    /// overlay must not leak assets into the scene or the build.
    /// </summary>
    public class DebugLineBuffer
    {
        public readonly struct Segment
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Color Color;

            public Segment(Vector3 a, Vector3 b, Color color)
            {
                A = a;
                B = b;
                Color = color;
            }
        }

        readonly List<Segment> segments = new();

        static Material xrayMaterial;
        static Material depthMaterial;

        public int Count => segments.Count;

        public IReadOnlyList<Segment> Segments => segments;

        public void Clear() => segments.Clear();

        public void Line(Vector3 a, Vector3 b, Color color) => segments.Add(new Segment(a, b, color));

        public void Ray(Vector3 origin, Vector3 direction, float length, Color color) =>
            Line(origin, origin + direction.normalized * length, color);

        /// <summary>Line with a two-whisker arrow head at the far end — direction of travel, readable at a glance.</summary>
        public void Arrow(Vector3 from, Vector3 to, Color color, float headSize = 0.6f)
        {
            Line(from, to, color);
            Vector3 back = from - to;
            if (back.sqrMagnitude < 0.0001f) return;
            back = back.normalized * headSize;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(Vector3.forward, back);
            side = side.normalized * (headSize * 0.5f);
            Line(to, to + back + side, color);
            Line(to, to + back - side, color);
        }

        /// <summary>Chain of points, each leg drawn end to end. Fewer than two points draws nothing.</summary>
        public void Polyline(IReadOnlyList<Vector3> points, Color color, Vector3 lift = default)
        {
            for (int i = 1; i < points.Count; i++)
                Line(points[i - 1] + lift, points[i] + lift, color);
        }

        /// <summary>Flat ring on the XZ plane — reads as a spot on the road, unlike a wire sphere.</summary>
        public void Circle(Vector3 center, float radius, Color color, int steps = 16)
        {
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float angle = i / (float)steps * Mathf.PI * 2f;
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Line(previous, point, color);
                previous = point;
            }
        }

        /// <summary>Three-axis cross — a point in space, visible from any angle.</summary>
        public void Cross(Vector3 center, float size, Color color)
        {
            Line(center - Vector3.right * size, center + Vector3.right * size, color);
            Line(center - Vector3.up * size, center + Vector3.up * size, color);
            Line(center - Vector3.forward * size, center + Vector3.forward * size, color);
        }

        /// <summary>Flat diamond marker on the XZ plane, used for graph nodes.</summary>
        public void Diamond(Vector3 center, float size, Color color)
        {
            Vector3 north = center + Vector3.forward * size;
            Vector3 east = center + Vector3.right * size;
            Vector3 south = center - Vector3.forward * size;
            Vector3 west = center - Vector3.right * size;
            Line(north, east, color);
            Line(east, south, color);
            Line(south, west, color);
            Line(west, north, color);
        }

        /// <summary>
        /// Draw the buffer for the camera currently rendering. Call from
        /// OnRenderObject so it runs once per camera — Game view and Scene
        /// view both get it, with no Gizmos toggle involved.
        /// </summary>
        /// <param name="xray">Ignore depth, so the overlay stays visible through buildings.</param>
        public void Render(bool xray)
        {
            if (segments.Count == 0) return;
            Material material = xray ? XrayMaterial : DepthMaterial;
            if (material == null) return;

            material.SetPass(0);
            GL.PushMatrix(); // identity model matrix: vertices below are world space
            GL.Begin(GL.LINES);
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                GL.Color(segment.Color);
                GL.Vertex(segment.A);
                GL.Vertex(segment.B);
            }
            GL.End();
            GL.PopMatrix();
        }

        static Material XrayMaterial => xrayMaterial != null ? xrayMaterial : xrayMaterial = CreateMaterial(true);
        static Material DepthMaterial => depthMaterial != null ? depthMaterial : depthMaterial = CreateMaterial(false);

        static Material CreateMaterial(bool xray)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)(xray
                ? UnityEngine.Rendering.CompareFunction.Always
                : UnityEngine.Rendering.CompareFunction.LessEqual));
            return material;
        }
    }
}
