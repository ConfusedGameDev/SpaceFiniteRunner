using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// A 4-vertex quad whose UVs show one cell of a sprite sheet and whose
    /// vertex colour tints/fades it. URP particle shaders ignore
    /// <c>_BaseMap_ST</c> but honour vertex colour, so frames and fades are
    /// written into the mesh. Horizontal quads lie on the XZ plane; vertical
    /// ones on the XY plane (billboards). Ported from RevolverBallad's
    /// FlipbookQuad for <see cref="MeshSurfaceSparks"/>.
    /// </summary>
    public class FlipbookQuad
    {
        readonly Mesh mesh;
        readonly int columns, rows;
        readonly Vector2[] uvs = new Vector2[4];
        readonly Color[] colors = new Color[4];
        int frame = -1;
        bool mirrored;

        public Mesh Mesh => mesh;
        public int FrameCapacity => columns * rows;

        public FlipbookQuad(int columns, int rows, bool horizontal, string name)
        {
            this.columns = Mathf.Max(1, columns);
            this.rows = Mathf.Max(1, rows);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = horizontal
                ? new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) }
                : new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            var n = horizontal ? Vector3.up : Vector3.back;
            mesh.normals = new[] { n, n, n, n };
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>Width / height of one cell of the given texture.</summary>
        public float CellAspect(Texture tex) => tex ? (tex.width / (float)columns) / (tex.height / (float)rows) : 1f;

        /// <summary>Shows a cell, counted left-to-right, top-to-bottom. Mirror flips it horizontally.</summary>
        public void SetFrame(int f, bool mirror = false)
        {
            if (f == frame && mirror == mirrored) return;
            frame = f;
            mirrored = mirror;
            int col = f % columns;
            int row = f / columns;
            float w = 1f / columns, h = 1f / rows;
            float u0 = col * w, u1 = u0 + w;
            float v1 = 1f - row * h, v0 = v1 - h;
            if (mirror) (u0, u1) = (u1, u0);
            uvs[0] = new Vector2(u0, v0);
            uvs[1] = new Vector2(u1, v0);
            uvs[2] = new Vector2(u0, v1);
            uvs[3] = new Vector2(u1, v1);
            mesh.uv = uvs;
        }

        public void SetColor(Color c)
        {
            for (int i = 0; i < 4; i++) colors[i] = c;
            mesh.colors = colors;
        }

        public void Destroy()
        {
            if (mesh) Object.Destroy(mesh);
        }
    }
}
