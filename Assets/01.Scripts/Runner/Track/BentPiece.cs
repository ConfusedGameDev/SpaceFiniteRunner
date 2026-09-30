using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Owns the per-piece mesh copies <see cref="TrackDecorator"/> bent along
    /// the track for one stamped piece, and frees them with it. Added at
    /// runtime only, never baked: a bent mesh is unique to the distance it was
    /// stamped at, so nothing may share it, and without this every culled
    /// piece would leak its meshes.
    /// </summary>
    public sealed class BentPiece : MonoBehaviour
    {
        readonly List<Mesh> meshes = new();

        public void Own(Mesh mesh) => meshes.Add(mesh);

        void OnDestroy()
        {
            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            meshes.Clear();
        }
    }
}
