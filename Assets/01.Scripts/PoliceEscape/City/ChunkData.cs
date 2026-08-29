using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Pure C# grid model of one city chunk — which cells are road, what kind,
    /// and which edges connect. Deliberately holds no GameObjects: keeping the
    /// model separate from instantiation is what makes recalculate, time-sliced
    /// building and deterministic infinite streaming cheap to support. The
    /// populator reads the Empty cells; the road graph reads connections.
    ///
    /// Two layers: the <b>ground</b> layer (kind + connections) is the classic
    /// one-road-per-cell network; the <b>upper</b> layer (<see cref="GetUpperConnections"/>)
    /// carries overpass decks. A cell with both is a street passing under a
    /// bridge, and the placer guarantees the two run on perpendicular axes.
    /// Ramp cells are ground cells that climb toward the deck; Reserved cells
    /// are swallowed by a feature (roundabout corner, ground under a deck) —
    /// neither road nor buildable.
    /// </summary>
    public class ChunkData
    {
        /// <summary>What occupies a cell. Arterials are world-continuous and may cross chunk borders; connectors never do. Reserved = covered by a road feature, not drivable, not buildable.</summary>
        public enum CellKind : byte { Empty, Arterial, Connector, Reserved }

        /// <summary>
        /// Orthogonal per-cell markers layered over <see cref="CellKind"/>:
        /// <see cref="CellFlags.Curve"/> tags a road cell as part of a
        /// spline-curved avenue chain (its centre offset follows the curve, and
        /// its visual is chord-stamped instead of tile-stamped);
        /// <see cref="CellFlags.ParkLot"/> claims an Empty cell for the nature
        /// pass so the building populator skips it.
        /// </summary>
        [System.Flags]
        public enum CellFlags : byte { None = 0, Curve = 1, ParkLot = 2 }

        public const byte NoRamp = 255;

        public readonly Vector2Int Coord;
        public readonly int SizeInCells;

        /// <summary>Multi-cell road pieces stamped into this chunk (roundabouts, splits…), one entry per piece instance.</summary>
        public readonly List<RoadFeature> Features = new();

        readonly CellKind[] kinds;
        readonly EdgeMask[] connections;
        readonly EdgeMask[] upperConnections;
        readonly byte[] rampDirection;
        readonly byte[] rampStep;
        readonly byte[] rampLength;
        readonly int[] featureIndex;
        readonly Vector2[] centerOffset;
        readonly CellFlags[] cellFlags;

        public ChunkData(Vector2Int coord, int sizeInCells)
        {
            Coord = coord;
            SizeInCells = sizeInCells;
            int count = sizeInCells * sizeInCells;
            kinds = new CellKind[count];
            connections = new EdgeMask[count];
            upperConnections = new EdgeMask[count];
            rampDirection = new byte[count];
            rampStep = new byte[count];
            rampLength = new byte[count];
            featureIndex = new int[count];
            centerOffset = new Vector2[count];
            cellFlags = new CellFlags[count];
            for (int i = 0; i < count; i++)
            {
                rampDirection[i] = NoRamp;
                featureIndex[i] = -1;
            }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < SizeInCells && y < SizeInCells;

        int Index(int x, int y) => y * SizeInCells + x;

        // ------------------------------------------------------------- ground

        public CellKind GetKind(int x, int y) => kinds[Index(x, y)];
        public void SetKind(int x, int y, CellKind kind) => kinds[Index(x, y)] = kind;

        /// <summary>Drivable ground road (arterial or connector). Reserved cells are not roads.</summary>
        public bool IsRoad(int x, int y)
        {
            CellKind kind = kinds[Index(x, y)];
            return kind == CellKind.Arterial || kind == CellKind.Connector;
        }

        /// <summary>Free for the populator — only genuinely empty cells, never Reserved ones.</summary>
        public bool IsBuildable(int x, int y) => kinds[Index(x, y)] == CellKind.Empty;

        public bool IsReserved(int x, int y) => kinds[Index(x, y)] == CellKind.Reserved;

        public EdgeMask GetConnections(int x, int y) => connections[Index(x, y)];
        public void SetConnections(int x, int y, EdgeMask mask) => connections[Index(x, y)] = mask;

        // -------------------------------------------------------------- upper

        /// <summary>Deck road on the upper level, <see cref="EdgeMask.None"/> when the cell has no deck.</summary>
        public EdgeMask GetUpperConnections(int x, int y) => upperConnections[Index(x, y)];
        public void SetUpperConnections(int x, int y, EdgeMask mask) => upperConnections[Index(x, y)] = mask;
        public bool HasDeck(int x, int y) => upperConnections[Index(x, y)] != EdgeMask.None;

        // -------------------------------------------------------------- ramps

        public bool IsRamp(int x, int y) => rampDirection[Index(x, y)] != NoRamp;

        /// <summary>Uphill direction index (0..3 = N,E,S,W) of a ramp cell; <see cref="NoRamp"/> otherwise.</summary>
        public int GetRampDirection(int x, int y) => rampDirection[Index(x, y)];

        /// <summary>Position of this cell in its ramp run, 0 = the foot (ground end).</summary>
        public int GetRampStep(int x, int y) => rampStep[Index(x, y)];

        /// <summary>Total cells of the ramp run this cell belongs to.</summary>
        public int GetRampLength(int x, int y) => rampLength[Index(x, y)];

        public void SetRamp(int x, int y, int uphillDirection, int step, int length)
        {
            int i = Index(x, y);
            rampDirection[i] = (byte)(uphillDirection & 3);
            rampStep[i] = (byte)step;
            rampLength[i] = (byte)length;
        }

        /// <summary>Fraction of the deck height the ramp surface reaches at this cell's centre (0..1).</summary>
        public float RampHeight01(int x, int y)
        {
            int i = Index(x, y);
            if (rampDirection[i] == NoRamp || rampLength[i] == 0) return 0f;
            return (rampStep[i] + 0.5f) / rampLength[i];
        }

        // ----------------------------------------------------------- features

        /// <summary>Index into <see cref="Features"/> of the multi-cell piece covering this cell, -1 when none.</summary>
        public int GetFeatureIndex(int x, int y) => featureIndex[Index(x, y)];
        public void SetFeatureIndex(int x, int y, int index) => featureIndex[Index(x, y)] = index;
        public bool IsCovered(int x, int y) => featureIndex[Index(x, y)] >= 0;

        /// <summary>Any road feature owns this cell (template footprint, ramp, deck, a shifted road or a curve chain) — off limits for further features.</summary>
        public bool IsFeatureCell(int x, int y) => IsCovered(x, y) || IsRamp(x, y) || HasDeck(x, y) || HasCenterOffset(x, y) || HasFlag(x, y, CellFlags.Curve);

        // -------------------------------------------------------------- flags

        public CellFlags GetFlags(int x, int y) => cellFlags[Index(x, y)];
        public void AddFlags(int x, int y, CellFlags flags) => cellFlags[Index(x, y)] |= flags;
        public bool HasFlag(int x, int y, CellFlags flag) => (cellFlags[Index(x, y)] & flag) != 0;

        // ------------------------------------------------------ centre offsets

        /// <summary>
        /// Where the drivable line of a ground road cell sits relative to the
        /// cell centre, in cell units (XZ). Zero for ordinary cells; a fork's
        /// stem runs on the seam between two cells, so both cells' nodes are
        /// pulled half a cell onto it. The graph bakes this into node centres.
        /// </summary>
        public Vector2 GetCenterOffset(int x, int y) => centerOffset[Index(x, y)];
        public void SetCenterOffset(int x, int y, Vector2 offsetInCells) => centerOffset[Index(x, y)] = offsetInCells;
        public bool HasCenterOffset(int x, int y) => centerOffset[Index(x, y)] != Vector2.zero;

        /// <summary>World-grid coordinate of this chunk's cell (0,0) — cell units, not meters.</summary>
        public Vector2Int WorldCellOrigin => new(Coord.x * SizeInCells, Coord.y * SizeInCells);

        // ------------------------------------------------------ serialization

        // Raw array access for BlockLayoutData, the serialized twin this model
        // round-trips through when a baked block is saved into the city
        // prefab. Internal on purpose: gameplay code goes through the typed
        // accessors above, only the (de)serializer copies whole arrays.
        internal CellKind[] RawKinds => kinds;
        internal EdgeMask[] RawConnections => connections;
        internal EdgeMask[] RawUpperConnections => upperConnections;
        internal byte[] RawRampDirection => rampDirection;
        internal byte[] RawRampStep => rampStep;
        internal byte[] RawRampLength => rampLength;
        internal int[] RawFeatureIndex => featureIndex;
        internal Vector2[] RawCenterOffset => centerOffset;
        internal CellFlags[] RawCellFlags => cellFlags;
    }

    /// <summary>What a <see cref="RoadFeature"/> record describes.</summary>
    public enum RoadFeatureKind : byte
    {
        /// <summary>A multi-cell piece stamped once at its footprint centre (roundabout…).</summary>
        Template,
        /// <summary>A Y-split: seam T-junction on a through road, optional stem, split piece, two branches. Stamped from several pieces.</summary>
        Fork,
        /// <summary>A curved avenue: a staircase chain of cells between two arterial junctions, node centres pulled onto the fitted curve, visuals chord-stamped from road-straight pieces. Origin = entry junction, Footprint = exit junction cell.</summary>
        Curve,
    }

    /// <summary>
    /// One stamped road feature. For a <see cref="RoadFeatureKind.Template"/>:
    /// which <c>roadPieces</c> entry, the min-corner cell of its (already
    /// rotated) footprint and the quarter turns applied — instantiated once at
    /// the footprint centre, the cells it covers are skipped by the per-cell
    /// stamping. For a <see cref="RoadFeatureKind.Fork"/>: <see cref="Origin"/>
    /// is the junction cell on the through road, <see cref="QuarterTurns"/>
    /// the direction index the stem points (0..3 = N,E,S,W),
    /// <see cref="Variant"/> ±1 the side of the twin row (clockwise from the
    /// stem = +1) and <see cref="Footprint"/>.x the stem length in cells.
    /// </summary>
    public readonly struct RoadFeature
    {
        public readonly RoadFeatureKind Kind;
        public readonly int PieceIndex;
        public readonly Vector2Int Origin;
        public readonly int QuarterTurns;
        /// <summary>Footprint in cells as placed (after rotation); for forks, x = stem length.</summary>
        public readonly Vector2Int Footprint;
        public readonly int Variant;

        public RoadFeature(int pieceIndex, Vector2Int origin, int quarterTurns, Vector2Int footprint)
            : this(RoadFeatureKind.Template, pieceIndex, origin, quarterTurns, footprint, 0) { }

        public RoadFeature(RoadFeatureKind kind, int pieceIndex, Vector2Int origin, int quarterTurns, Vector2Int footprint, int variant)
        {
            Kind = kind;
            PieceIndex = pieceIndex;
            Origin = origin;
            QuarterTurns = quarterTurns;
            Footprint = footprint;
            Variant = variant;
        }
    }
}
