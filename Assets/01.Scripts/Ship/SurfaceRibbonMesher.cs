using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>One cross-section of a ribbon: where its centre line is, which way it faces, how wide and how curled it is there.</summary>
    public struct RibbonFrame
    {
        /// <summary>Centre of the surface at this cross-section.</summary>
        public Vector3 position;
        /// <summary>Across the ribbon, unit.</summary>
        public Vector3 right;
        /// <summary>Out of the surface at its centre, unit — the side a ship rides.</summary>
        public Vector3 up;
        /// <summary>Half the surface's width measured ALONG it (arc length when curled), metres.</summary>
        public float halfWidth;
        /// <summary>0 = flat. Above 0 the cross-section wraps round a pipe of this radius lying under the centre line (a tube ridden on its outside).</summary>
        public float pipeRadius;
    }

    /// <summary>
    /// Builds the collision surface a ship rides from a run of cross-sections:
    /// a floor ribbon facing <see cref="RibbonFrame.up"/>, optionally curled
    /// round a pipe, with optional inward-facing walls on either edge. It is
    /// the one geometry source for everything a ship flies over that is not
    /// hand-modelled — the sandbox course today, the runner's streamed track
    /// colliders later — so "what a ribbon is" (winding, which way a wall
    /// faces, how a tube curls) is decided once. Faces are single-sided on
    /// purpose: PhysX queries ignore back-faces, which is what lets a loop
    /// cross over its own entry road without the ship below ever seeing the
    /// underside of the road above.
    /// </summary>
    public static class SurfaceRibbonMesher
    {
        /// <summary>
        /// <paramref name="crossSegments"/> quads across (1 is enough for a
        /// flat ribbon, a curled one wants ~4 per 30° of wrap);
        /// <paramref name="wallHeight"/> 0 = no walls.
        /// </summary>
        public static Mesh Build(IReadOnlyList<RibbonFrame> frames, int crossSegments, float wallHeight,
                                 bool leftWall, bool rightWall, string name)
        {
            var mesh = new Mesh { name = name };
            if (frames == null || frames.Count < 2) return mesh;
            crossSegments = Mathf.Max(1, crossSegments);

            int columns = crossSegments + 1;
            var vertices = new List<Vector3>(frames.Count * (columns + 4));
            var triangles = new List<int>(frames.Count * crossSegments * 6);
            var uvs = new List<Vector2>(vertices.Capacity);

            // Floor: one row of vertices per frame, left edge to right edge.
            // UVs are in metres (u across, v along) so a tiling texture reads as a ground grid.
            var along = new float[frames.Count];
            for (int i = 1; i < frames.Count; i++)
                along[i] = along[i - 1] + Vector3.Distance(frames[i - 1].position, frames[i].position);
            for (int i = 0; i < frames.Count; i++)
                for (int j = 0; j < columns; j++)
                {
                    float across = Mathf.Lerp(-1f, 1f, j / (float)crossSegments);
                    vertices.Add(SurfacePoint(frames[i], across, out _));
                    uvs.Add(new Vector2(across * frames[i].halfWidth, along[i]));
                }

            for (int i = 0; i < frames.Count - 1; i++)
                for (int j = 0; j < crossSegments; j++)
                {
                    int l0 = i * columns + j, r0 = l0 + 1;
                    int l1 = l0 + columns, r1 = l1 + 1;
                    // Seen from above with forward up the screen: clockwise = facing up.
                    triangles.Add(l0); triangles.Add(l1); triangles.Add(r1);
                    triangles.Add(l0); triangles.Add(r1); triangles.Add(r0);
                }

            if (wallHeight > 0f)
            {
                if (leftWall) AddWall(frames, along, -1f, wallHeight, vertices, uvs, triangles);
                if (rightWall) AddWall(frames, along, 1f, wallHeight, vertices, uvs, triangles);
            }

            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A point across a frame, <paramref name="across"/> −1 (left edge) .. +1 (right edge), and the surface normal there.</summary>
        public static Vector3 SurfacePoint(in RibbonFrame frame, float across, out Vector3 normal)
        {
            float lateral = across * frame.halfWidth;
            if (frame.pipeRadius <= 0f)
            {
                normal = frame.up;
                return frame.position + frame.right * lateral;
            }
            float angle = lateral / frame.pipeRadius;
            normal = frame.right * Mathf.Sin(angle) + frame.up * Mathf.Cos(angle);
            Vector3 axis = frame.position - frame.up * frame.pipeRadius;
            return axis + normal * frame.pipeRadius;
        }

        // A wall stands on an edge along that edge's own normal and faces the ribbon's centre.
        static void AddWall(IReadOnlyList<RibbonFrame> frames, float[] along, float side, float height,
                            List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            int start = vertices.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                Vector3 foot = SurfacePoint(frames[i], side, out Vector3 normal);
                vertices.Add(foot);
                vertices.Add(foot + normal * height);
                uvs.Add(new Vector2(0f, along[i]));
                uvs.Add(new Vector2(height, along[i]));
            }
            for (int i = 0; i < frames.Count - 1; i++)
            {
                int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                if (side < 0f)
                {
                    // Left wall, seen from the centre: forward runs to the right of the screen.
                    triangles.Add(b0); triangles.Add(t0); triangles.Add(t1);
                    triangles.Add(b0); triangles.Add(t1); triangles.Add(b1);
                }
                else
                {
                    triangles.Add(b1); triangles.Add(t1); triangles.Add(t0);
                    triangles.Add(b1); triangles.Add(t0); triangles.Add(b0);
                }
            }
        }
    }
}
