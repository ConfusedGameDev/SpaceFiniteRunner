using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Which blocks NPCs are allowed to occupy right now: the player's block,
    /// plus each edge-neighbour (pacman-wrapped) whose shared edge the player
    /// is close to. The enter/exit thresholds differ on purpose — a neighbour
    /// activates inside <see cref="CityRoot.npcEdgeEnterDistance"/> but only
    /// deactivates beyond <see cref="CityRoot.npcEdgeExitDistance"/>, so
    /// cruising along a block edge can't thrash spawn/despawn every tick.
    /// Before the first tick (no player yet) everything is allowed, so
    /// nothing downstream needs a special "no player" case.
    /// </summary>
    public sealed class CityBounds
    {
        readonly CityRoot root;
        readonly HashSet<Vector2Int> allowed = new();
        readonly HashSet<Vector2Int> next = new();

        public CityBounds(CityRoot root)
        {
            this.root = root;
        }

        /// <summary>Currently allowed blocks. Empty until the first tick — treated as "everything allowed".</summary>
        public IReadOnlyCollection<Vector2Int> AllowedBlocks => allowed;

        public bool IsAllowed(Vector2Int blockCoord) =>
            allowed.Count == 0 || allowed.Contains(root.WrapBlockCoord(blockCoord));

        public bool IsAllowed(Vector3 worldPosition) =>
            allowed.Count == 0 || allowed.Contains(root.BlockCoordAt(worldPosition));

        /// <summary>Recompute the allowed set around the player's position. Called on the managers' 1 s cadence by <see cref="CityRoot"/>.</summary>
        public void Tick(Vector3 playerPosition)
        {
            Vector2Int current = root.BlockCoordAt(playerPosition);
            float block = root.BlockWorldSize;
            Vector3 origin = root.transform.position;

            // Player position inside the current block, wrapped into the city
            // rectangle first so a frame spent beyond an edge can't produce a
            // bogus local offset.
            float cityX = Wrap01(playerPosition.x - origin.x, root.CitySizeX);
            float cityZ = Wrap01(playerPosition.z - origin.z, root.CitySizeZ);
            float localX = cityX - current.x * block;
            float localZ = cityZ - current.y * block;

            next.Clear();
            next.Add(current);
            for (int dir = 0; dir < 4; dir++)
            {
                Vector2Int neighbour = root.WrapBlockCoord(current + EdgeMaskUtility.Offset(dir));
                if (neighbour == current) continue; // a 1-wide grid wraps onto itself
                float edgeDistance = dir switch
                {
                    0 => block - localZ, // North
                    1 => block - localX, // East
                    2 => localZ,         // South
                    _ => localX,         // West
                };
                float threshold = allowed.Contains(neighbour) ? root.npcEdgeExitDistance : root.npcEdgeEnterDistance;
                if (edgeDistance <= threshold) next.Add(neighbour);
            }

            allowed.Clear();
            allowed.UnionWith(next);
        }

        static float Wrap01(float value, float size) =>
            size <= 0f ? 0f : value - Mathf.Floor(value / size) * size;
    }
}
