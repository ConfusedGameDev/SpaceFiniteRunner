using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// Where a decoration prop may stand. Both spots live on the sidewalk band
    /// of a road tile — never on the driving lane — so AI cars (which follow
    /// cell centres) never plough through parked props.
    /// </summary>
    public enum DecorationPlacement
    {
        /// <summary>The four corner quads of an intersection tile (3+ sockets) — the connecting points where light posts belong.</summary>
        IntersectionCorner,
        /// <summary>Midpoint of a road tile's socket-less edges — the clear sidewalk strip along straights, bends and dead ends.</summary>
        RoadEdge,
        /// <summary>Inside a park lot — trees, bushes, rocks scattered by the nature placer. Ignored by the street decorator.</summary>
        LotInterior,
        /// <summary>Along a park lot's outer edge cells, facing outward — fences, palm rows. Ignored by the street decorator.</summary>
        LotPerimeter,
    }

    /// <summary>
    /// One prop the decorator may place, with its own physics feel: mass and
    /// angular damping are what make a cone fly, a light post topple slowly
    /// and a construction barrier barely budge under the same player impact
    /// (see <see cref="DecorationProp"/>). Swapping the prefab or dragging the
    /// sliders is the whole tuning workflow — no code.
    /// </summary>
    [Serializable]
    public class DecorationDefinition
    {
        [Required, AssetsOnly]
        [Tooltip("Prop model/prefab. FBX assets can be assigned directly — convex mesh colliders and the rigidbody are added at spawn.")]
        public GameObject prefab;

        [Tooltip("Which sidewalk spots this prop competes for.")]
        public DecorationPlacement placement = DecorationPlacement.RoadEdge;

        [Tooltip("Relative pick chance among props sharing the same placement.")]
        [PropertyRange(0.01f, 10f)]
        public float weight = 1f;

        [Tooltip("Extra scale on top of the cell fit (cellSize ÷ the set's nativeCellSize). Nature props need tiny values — their kit is human-scale while a cell is a whole street tile.")]
        [PropertyRange(0.02f, 3f)]
        public float scaleMultiplier = 1f;

        [Tooltip("Extra yaw if the model doesn't face +Z at zero rotation — props are yawed to face the road they stand beside.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float rotationOffset;

        [Tooltip("Random yaw added on top — cones want lots, light posts none.")]
        [PropertyRange(0f, 180f), SuffixLabel("°", true)]
        public float yawJitter;

        [Tooltip("Rigidbody mass. Light = flies away on impact (cone), heavy = shrugs it off (barrier). Player car mass is 1200 for reference.")]
        [PropertyRange(0.5f, 5000f), SuffixLabel("kg", true)]
        public float mass = 100f;

        [Tooltip("Angular damping — high values make a tall prop keel over in slow motion instead of snapping flat.")]
        [PropertyRange(0f, 10f)]
        public float angularDamping = 0.5f;

        [Tooltip("This prop answers a car with a blast instead of a shove (see ExplosiveBarrel). The blast itself — damage, radius, fireball — is shared, and lives on the set.")]
        public bool explosive;
    }
}
