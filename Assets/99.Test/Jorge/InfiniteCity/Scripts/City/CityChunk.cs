using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Marker on every generated chunk root: identifies generated content (so
    /// Clear only ever removes what the generator spawned) and carries the
    /// chunk's <see cref="ChunkData"/> for gizmos and, later, the road graph.
    /// The data is deliberately not serialized — chunks are always reproducible
    /// from seed + settings, never saved with the scene.
    /// </summary>
    public class CityChunk : MonoBehaviour
    {
        public Vector2Int Coord { get; private set; }

        /// <summary>Grid model this chunk was built from. Null after a domain reload — press Recalculate.</summary>
        public ChunkData Data { get; private set; }

        public void Initialize(Vector2Int coord, ChunkData data)
        {
            Coord = coord;
            Data = data;
        }
    }
}
