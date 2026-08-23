using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>What a road piece is for. Only <see cref="Standard"/> pieces are socket-matched per cell; the others are stamped by the overpass logic.</summary>
    public enum RoadPieceRole
    {
        /// <summary>Socket-matched ground piece — single cell, or a multi-cell template (roundabout, split).</summary>
        Standard,
        /// <summary>One link of the ramp chain that climbs from the ground to the deck height.</summary>
        Ramp,
        /// <summary>Elevated straight deck (road-bridge) placed on the upper level.</summary>
        Deck,
        /// <summary>Support stamped on the ground under deck cells that carry no street.</summary>
        Pillar,
        /// <summary>
        /// Y-split (one entrance, two exits). At rotationOffset 0 the stem
        /// enters on the West (-X) edge at the centre and the two branches
        /// leave through the East (+X) edge half a cell either side of the
        /// centre — i.e. the piece is 1 × 2 cells with its stem on the seam
        /// between the two cells. Stamped by the fork feature, never matched.
        /// </summary>
        Fork,
        /// <summary>Half-length straight (road along X at rotationOffset 0, like road-straight). Fills the half cells beside a fork's seam junction.</summary>
        HalfStraight,
    }

    /// <summary>
    /// One road prefab the generator may stamp, described by its socket shape
    /// rather than its name: <see cref="connectionMask"/> says which edges carry
    /// road at the prefab's default rotation, and the picker tries all four
    /// rotations to match a cell. Multi-cell pieces describe every footprint
    /// cell (<see cref="cellMasks"/>, row-major from the south-west corner) and
    /// are placed as templates wherever the generated grid matches. If an
    /// asset's pivot or facing is off, fix it with <see cref="rotationOffset"/>
    /// here — never by editing the imported model. Pivots are assumed at the
    /// footprint centre (true for the Kenney kit).
    /// </summary>
    [Serializable]
    public class RoadPieceDefinition
    {
        [Required, AssetsOnly]
        [Tooltip("Road model/prefab. FBX assets can be assigned directly.")]
        public GameObject prefab;

        [EnumToggleButtons]
        [Tooltip("Standard = socket-matched ground piece. Ramp/Deck/Pillar are the overpass parts, Fork/HalfStraight the Y-split parts — those are stamped by their feature, never mask-matched.")]
        public RoadPieceRole role = RoadPieceRole.Standard;

        [ShowIf(nameof(IsSingleCell))]
        [Tooltip("Edges that carry a road connection at the prefab's default rotation (North = +Z, East = +X).")]
        public EdgeMask connectionMask = EdgeMask.North | EdgeMask.South;

        [ShowIf(nameof(IsSingleCell))]
        [Tooltip("Relative pick chance when several pieces (or rotations) fit the same cell.")]
        [PropertyRange(0.01f, 10f)]
        public float weight = 1f;

        [Tooltip("Extra yaw applied on top of the socket rotation — use to fix models whose visual doesn't match their mask (ramps: uphill must be North at offset 0).")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float rotationOffset;

        // --------------------------------------------------------- multi-cell
        [ShowIf(nameof(IsStandard))]
        [Tooltip("Footprint in cells (X × Z) at the default rotation. Anything above 1×1 turns the piece into a template matched against the generated grid. Values below 1 are treated as 1.")]
        public Vector2Int footprintInCells = Vector2Int.one;

        [ShowIf(nameof(IsMultiCell))]
        [Tooltip("Required socket mask of every footprint cell, row by row from the south-west corner (index = z * width + x). None = the cell must be empty and becomes Reserved (no road, no building).")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        public List<EdgeMask> cellMasks = new();

        [ShowIf(nameof(IsMultiCell))]
        [Tooltip("Chance to stamp the template on each spot of the grid that matches it.")]
        [PropertyRange(0f, 1f)]
        public float placeChance = 0.35f;

        // ----------------------------------------------------- drivable surface
        /// <summary>
        /// Height of the piece's **driving lane** above its pivot, in native
        /// units — the surface a wheel rests on, NOT the raised curb at the
        /// tile's edge. The two differ (Kenney: lane 0.01, curb 0.02) and
        /// taking the piece's bounds max would silently pick the curb, which
        /// is how the chunk ground slab ended up half a curb below the road
        /// and left a step at the foot of every ramp.
        /// </summary>
        [ShowIf(nameof(IsStandard))]
        [Tooltip("Height of the DRIVING LANE above the pivot, in native units — not the curb at the tile edge (Kenney: lane 0.01, curb 0.02). The chunk's ground slab is lifted to this, so cars drive on the asphalt instead of inside it.")]
        [PropertyRange(0f, 0.5f)]
        public float laneHeight = 0.01f;

        // -------------------------------------------------------------- ramps
        [ShowIf(nameof(IsRamp))]
        [Tooltip("Surface height (native units) at the foot of this ramp link — 0 for the first link of the chain.")]
        [PropertyRange(0f, 2f)]
        public float rampStartHeight;

        [ShowIf(nameof(IsRamp))]
        [Tooltip("Surface height (native units) at the top of this ramp link — the deck height for the last link.")]
        [PropertyRange(0f, 2f)]
        public float rampEndHeight = 0.5f;

        [ShowIf(nameof(IsDeck))]
        [Tooltip("Height of the drivable deck surface above the piece's pivot, in native units (Kenney road-bridge: 0.5).")]
        [PropertyRange(0.05f, 2f)]
        public float deckHeight = 0.5f;

        [ShowIf(nameof(IsDeck))]
        [Tooltip("The model is complete below its deck — supports plus the perpendicular street at ground level (Kenney road-bridge is) — so nothing else is stamped under it: no ground piece at crossings, no pillar.")]
        public bool includesUnderpass = true;

        public bool IsStandard => role == RoadPieceRole.Standard;
        public bool IsRamp => role == RoadPieceRole.Ramp;
        public bool IsDeck => role == RoadPieceRole.Deck;
        public bool IsFork => role == RoadPieceRole.Fork;
        public bool IsMultiCell => IsStandard && (footprintInCells.x > 1 || footprintInCells.y > 1);
        public bool IsSingleCell => !IsMultiCell;

        /// <summary>Template mask of footprint cell (u, v); None when the list is short.</summary>
        public EdgeMask CellMask(int u, int v)
        {
            int index = v * footprintInCells.x + u;
            return cellMasks != null && index >= 0 && index < cellMasks.Count ? cellMasks[index] : EdgeMask.None;
        }

        /// <summary>Inspector label: prefab name + role so the list reads at a glance.</summary>
        public string Label => prefab != null
            ? (role == RoadPieceRole.Standard ? prefab.name : $"{prefab.name} [{role}]")
            : "(empty)";
    }
}
