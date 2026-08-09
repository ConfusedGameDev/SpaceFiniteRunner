using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// One road prefab the generator may stamp into a cell, described by its
    /// socket shape rather than its name: <see cref="connectionMask"/> says
    /// which edges carry road at the prefab's default rotation, and the picker
    /// tries all four rotations to match a cell. If an asset's pivot or facing
    /// is off, fix it with <see cref="rotationOffset"/> here — never by editing
    /// the imported model.
    /// </summary>
    [Serializable]
    public class RoadPieceDefinition
    {
        [Required, AssetsOnly]
        [Tooltip("Road model/prefab. FBX assets can be assigned directly.")]
        public GameObject prefab;

        [Tooltip("Edges that carry a road connection at the prefab's default rotation (North = +Z, East = +X).")]
        public EdgeMask connectionMask = EdgeMask.North | EdgeMask.South;

        [Tooltip("Relative pick chance when several pieces (or rotations) fit the same cell.")]
        [PropertyRange(0.01f, 10f)]
        public float weight = 1f;

        [Tooltip("Extra yaw applied on top of the socket rotation — use to fix models whose visual doesn't match their mask.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float rotationOffset;
    }
}
