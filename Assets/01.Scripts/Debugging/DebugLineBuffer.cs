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
    /// watched while playing. Almost everything is a line — a circle is a fan
    /// of them — because one GL.LINES batch per visualizer is cheap enough to
    /// leave running for a whole session; translucent filled quads exist for
    /// the few marks that need a volume (trigger boxes), and they draw before
    /// the lines so an outline always reads on top of its fill.
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

        public readonly struct Quad
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;
            public readonly Vector3 D;
            public readonly Color Color;

            public Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                A = a;
                B = b;
                C = c;
                D = d;
                Color = color;
            }
        }

        readonly List<Segment> segments = new();
        readonly List<Quad> quads = new();

        static Material xrayMaterial;
        static Material depthMaterial;

        public int Count => segments.Count + quads.Count;

        public IReadOnlyList<Segment> Segments => segments;

        public void Clear()
        {
            segments.Clear();
            quads.Clear();
        }

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

        /// <summary>Single translucent face. Winding is irrelevant — the debug material culls nothing.</summary>
        public void SolidQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color) =>
            quads.Add(new Quad(a, b, c, d, color));

        /// <summary>
        /// Filled box in a local frame (pass a transform's localToWorldMatrix
        /// for an oriented collider volume) — six translucent faces.
        /// </summary>
        public void SolidBox(Matrix4x4 localToWorld, Vector3 center, Vector3 size, Color color)
        {
            Corners(localToWorld, center, size, out Vector3 c0, out Vector3 c1, out Vector3 c2, out Vector3 c3,
                out Vector3 c4, out Vector3 c5, out Vector3 c6, out Vector3 c7);
            SolidQuad(c0, c1, c2, c3, color); // bottom
            SolidQuad(c4, c5, c6, c7, color); // top
            SolidQuad(c0, c1, c5, c4, color);
            SolidQuad(c1, c2, c6, c5, color);
            SolidQuad(c2, c3, c7, c6, color);
            SolidQuad(c3, c0, c4, c7, color);
        }

        /// <summary>The matching 12-edge outline for <see cref="SolidBox"/>.</summary>
        public void WireBox(Matrix4x4 localToWorld, Vector3 center, Vector3 size, Color color)
        {
            Corners(localToWorld, center, size, out Vector3 c0, out Vector3 c1, out Vector3 c2, out Vector3 c3,
                out Vector3 c4, out Vector3 c5, out Vector3 c6, out Vector3 c7);
            Line(c0, c1, color); Line(c1, c2, color); Line(c2, c3, color); Line(c3, c0, color);
            Line(c4, c5, color); Line(c5, c6, color); Line(c6, c7, color); Line(c7, c4, color);
            Line(c0, c4, color); Line(c1, c5, color); Line(c2, c6, color); Line(c3, c7, color);
        }

        // c0-c3 bottom ring, c4-c7 the top ring directly above, both wound the same way.
        static void Corners(Matrix4x4 m, Vector3 center, Vector3 size,
            out Vector3 c0, out Vector3 c1, out Vector3 c2, out Vector3 c3,
            out Vector3 c4, out Vector3 c5, out Vector3 c6, out Vector3 c7)
        {
            Vector3 h = size * 0.5f;
            c0 = m.MultiplyPoint3x4(center + new Vector3(-h.x, -h.y, -h.z));
            c1 = m.MultiplyPoint3x4(center + new Vector3(h.x, -h.y, -h.z));
            c2 = m.MultiplyPoint3x4(center + new Vector3(h.x, -h.y, h.z));
            c3 = m.MultiplyPoint3x4(center + new Vector3(-h.x, -h.y, h.z));
            c4 = m.MultiplyPoint3x4(center + new Vector3(-h.x, h.y, -h.z));
            c5 = m.MultiplyPoint3x4(center + new Vector3(h.x, h.y, -h.z));
            c6 = m.MultiplyPoint3x4(center + new Vector3(h.x, h.y, h.z));
            c7 = m.MultiplyPoint3x4(center + new Vector3(-h.x, h.y, h.z));
        }

        /// <summary>
        /// Draw the buffer for the camera currently rendering. Call from
        /// OnRenderObject so it runs once per camera — Game view and Scene
        /// view both get it, with no Gizmos toggle involved.
        /// </summary>
        /// <param name="xray">Ignore depth, so the overlay stays visible through buildings.</param>
        public void Render(bool xray)
        {
            if (segments.Count == 0 && quads.Count == 0) return;
            Material material = xray ? XrayMaterial : DepthMaterial;
            if (material == null) return;

            material.SetPass(0);
            GL.PushMatrix(); // identity model matrix: vertices below are world space

            if (quads.Count > 0)
            {
                GL.Begin(GL.QUADS);
                for (int i = 0; i < quads.Count; i++)
                {
                    Quad quad = quads[i];
                    GL.Color(quad.Color);
                    GL.Vertex(quad.A);
                    GL.Vertex(quad.B);
                    GL.Vertex(quad.C);
                    GL.Vertex(quad.D);
                }
                GL.End();
            }

            if (segments.Count > 0)
            {
                GL.Begin(GL.LINES);
                for (int i = 0; i < segments.Count; i++)
                {
                    Segment segment = segments[i];
                    GL.Color(segment.Color);
                    GL.Vertex(segment.A);
                    GL.Vertex(segment.B);
                }
                GL.End();
            }

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
