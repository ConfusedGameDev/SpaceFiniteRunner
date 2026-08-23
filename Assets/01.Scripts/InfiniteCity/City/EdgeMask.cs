using System;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Which of a cell's four edges carry a road connection. Road prefabs declare
    /// their sockets with this mask so the generator picks and rotates pieces by
    /// socket matching instead of by prefab name. North is +Z, East is +X; a
    /// piece yawed by 90° × k has its North socket facing direction index k.
    /// </summary>
    [Flags]
    public enum EdgeMask
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8,
        All = North | East | South | West,
    }

    /// <summary>
    /// Mask math shared by the generator and the piece picker: clockwise
    /// rotation, socket counting, and the grid offset behind each direction.
    /// Direction indices are 0..3 = N, E, S, W, matching quarter-turns of yaw.
    /// </summary>
    public static class EdgeMaskUtility
    {
        static readonly Vector2Int[] Offsets =
        {
            new(0, 1),   // North (+Z)
            new(1, 0),   // East  (+X)
            new(0, -1),  // South (-Z)
            new(-1, 0),  // West  (-X)
        };

        /// <summary>Grid offset of the neighbour across the given direction index (0..3 = N,E,S,W).</summary>
        public static Vector2Int Offset(int direction) => Offsets[direction & 3];

        /// <summary>The single-edge mask for a direction index (0..3 = N,E,S,W).</summary>
        public static EdgeMask DirectionBit(int direction) => (EdgeMask)(1 << (direction & 3));

        /// <summary>Rotate a socket mask clockwise (viewed from above) by the given quarter turns.</summary>
        public static EdgeMask RotateCw(this EdgeMask mask, int quarterTurns)
        {
            int m = (int)mask & 0xF;
            int t = quarterTurns & 3;
            return (EdgeMask)(((m << t) | (m >> (4 - t))) & 0xF);
        }

        /// <summary>How many edges of the mask carry a connection (0..4).</summary>
        public static int ConnectionCount(this EdgeMask mask)
        {
            int m = (int)mask & 0xF;
            m = (m & 0x5) + ((m >> 1) & 0x5);
            return (m & 0x3) + ((m >> 2) & 0x3);
        }
    }
}
