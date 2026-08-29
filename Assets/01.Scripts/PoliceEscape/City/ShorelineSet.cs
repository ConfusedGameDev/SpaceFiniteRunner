using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// The cliff kit a water block's shores are built from: straight edge
    /// pieces, the two corner kinds and an optional waterfall garnish, each
    /// with its measured native bounds so <see cref="ShorelinePlacer"/> can
    /// stand it exactly <see cref="targetHeight"/> tall with its top flush
    /// on the road plane (y = 0) — the nature kit is human-scale, so the
    /// bounds, not the cell size, drive the scale (the same trap
    /// <c>KenneyNatureSetBuilder</c> solves for trees).
    ///
    /// Orientation contract (before a piece's own <see cref="Piece.rotationOffset"/>):
    /// an EDGE piece at yaw 0 is a shore along a water block's NORTH edge —
    /// land to +Z, its cliff face looking -Z at the water. An INNER corner
    /// at yaw 0 sits in the block's north-east corner with land to the north
    /// and the east (two cliff faces meeting concavely, seen from the sea).
    /// An OUTER corner at yaw 0 sits in the block's north-east corner with
    /// land only diagonally — the convex cap where two shore strips of the
    /// neighbouring blocks turn the land's corner. Every other edge/corner is
    /// that piece yawed by a multiple of 90°. The Kenney FBXs mirror X on
    /// import, so the offsets are tuned by eye in the baked scene.
    /// Built by <c>Tools → Police Escape → Create Kenney Shoreline Set</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "ShorelineSet", menuName = "PoliceEscape/Shoreline Set")]
    public class ShorelineSet : ScriptableObject
    {
        [Serializable]
        public class Piece
        {
            [Tooltip("The cliff model. Its renderer bounds (measured by the builder tool) decide scale and sink.")]
            public GameObject prefab;

            [Tooltip("Relative pick weight among the pieces of its slot.")]
            [PropertyRange(0f, 10f)]
            public float weight = 1f;

            [Tooltip("Extra yaw so the model matches the slot's orientation contract (see the set's summary). Tune by eye after a bake.")]
            [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
            public float rotationOffset;

            [Tooltip("Renderer bounds of the prefab in its own space, measured by the builder — the placer scales and sinks by these, never by the cell size.")]
            public Bounds nativeBounds = new(new Vector3(0f, 0.5f, 0f), Vector3.one);

            public string Label => prefab != null ? prefab.name : "(empty)";
        }

        [TitleGroup("Pieces")]
        [Tooltip("Straight cliff along a shore edge — the bulk of every coastline.")]
        [ListDrawerSettings(ListElementLabelName = nameof(Piece.Label))]
        public List<Piece> edges = new();

        [TitleGroup("Pieces")]
        [Tooltip("Concave corner: a water block's corner with land on BOTH adjacent sides.")]
        [ListDrawerSettings(ListElementLabelName = nameof(Piece.Label))]
        public List<Piece> innerCorners = new();

        [TitleGroup("Pieces")]
        [Tooltip("Convex corner: a water block's corner touching land only diagonally — caps the land's corner between two neighbours' shore strips.")]
        [ListDrawerSettings(ListElementLabelName = nameof(Piece.Label))]
        public List<Piece> outerCorners = new();

        [TitleGroup("Pieces")]
        [Tooltip("Waterfall variants that replace an edge piece now and then — a little motion on a long straight shore.")]
        [ListDrawerSettings(ListElementLabelName = nameof(Piece.Label))]
        public List<Piece> waterfalls = new();

        [TitleGroup("Placement")]
        [Tooltip("World height every cliff piece stands, top flush at the road plane (y = 0). Deeper than the water level so the cliff keeps going under the surface.")]
        [PropertyRange(1f, 20f), SuffixLabel("m", true)]
        public float targetHeight = 5f;

        [TitleGroup("Placement")]
        [Tooltip("Chance that an edge slot takes a waterfall piece instead of a plain cliff (0 = never).")]
        [PropertyRange(0f, 0.5f)]
        public float waterfallChance = 0.05f;

        [TitleGroup("Placement")]
        [Tooltip("Fit a box collider to each stamped piece so the car drives over the cliff top and drops off its lip — the shore is a real ledge, not a ghost. Off = visual only.")]
        public bool addColliders = true;

        [TitleGroup("Placement")]
        [Tooltip("Extra stretch along the shore so neighbouring pieces overlap a hair and never show a seam.")]
        [PropertyRange(1f, 1.2f)]
        public float overlap = 1.03f;

        /// <summary>Weighted pick from a slot's list; null when the list has no usable piece.</summary>
        public static Piece Pick(List<Piece> pieces, System.Random rng)
        {
            if (pieces == null) return null;
            Piece picked = null;
            float total = 0f;
            foreach (Piece piece in pieces)
            {
                if (piece?.prefab == null || piece.weight <= 0f) continue;
                total += piece.weight;
                if ((float)rng.NextDouble() * total <= piece.weight) picked = piece;
            }
            return picked;
        }
    }
}
