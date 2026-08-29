using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Effective interior knobs for one block: the block's
    /// <see cref="BlockSettings"/> override when present, else the city
    /// definition's default block settings, else the generation asset's own
    /// values. Resolved once so the generator never chases the fallback chain.
    /// </summary>
    public readonly struct BlockKnobs
    {
        public readonly float ConnectorDensity;
        public readonly float TurnProbability;
        public readonly bool AllowDeadEnds;
        public readonly bool PlaceFeatures;
        public readonly float OverpassChance;
        public readonly float ForkChance;
        public readonly Population.BuildingSet BuildingSet;
        public readonly Decoration.DecorationSet DecorationSet;
        public readonly float BuildingDensityMultiplier;
        public readonly float DecorationDensityMultiplier;

        // District pass-throughs — content flavour only, never border roads
        // (the district's one border-relevant flag is consumed by CityLayout).
        public readonly Decoration.NatureSet NatureSet;
        public readonly bool IsPark;
        public readonly float ParkLotChance;
        public readonly float CurveChance;
        public readonly int MaxCurves;

        public BlockKnobs(CityGenerationSettings city, BlockSettings block, DistrictDefinition district)
        {
            NatureSet = district != null ? district.natureSet : null;
            IsPark = district != null && district.isPark;
            ParkLotChance = district != null ? district.parkLotChance : 0f;
            CurveChance = district != null ? district.curveChance : 0f;
            MaxCurves = district != null ? district.maxCurvedAvenues : 0;
            if (block != null)
            {
                ConnectorDensity = block.connectorDensity;
                TurnProbability = block.turnProbability;
                AllowDeadEnds = block.allowDeadEnds;
                PlaceFeatures = city.placeFeatures && block.placeFeatures;
                OverpassChance = block.overpassChance;
                ForkChance = block.forkChance;
                BuildingSet = block.buildingSet != null ? block.buildingSet : city.buildingSet;
                DecorationSet = block.decorationSet != null ? block.decorationSet : city.decorationSet;
                BuildingDensityMultiplier = block.buildingDensityMultiplier;
                DecorationDensityMultiplier = block.decorationDensityMultiplier;
            }
            else
            {
                ConnectorDensity = city.connectorDensity;
                TurnProbability = city.turnProbability;
                AllowDeadEnds = city.allowDeadEnds;
                PlaceFeatures = city.placeFeatures;
                OverpassChance = city.overpassChance;
                ForkChance = city.forkChance;
                BuildingSet = city.buildingSet;
                DecorationSet = city.decorationSet;
                BuildingDensityMultiplier = 1f;
                DecorationDensityMultiplier = 1f;
            }
        }
    }

    /// <summary>
    /// Pure model of the whole authored city — no GameObjects, no Unity scene
    /// state, just (definition → answers). It owns the two facts every block
    /// must agree on to stay connected:
    ///
    /// 1. <b>The periodic arterial field.</b> Arterial rows/columns are a pure
    ///    function of the wrapped world cell index and the CITY seed, so any
    ///    two blocks — including the pair that meets across the pacman wrap
    ///    seam — compute identical border sockets without talking to each
    ///    other. This is the whole "adjacent blocks always connect" guarantee,
    ///    and why per-block settings may only touch interiors.
    /// 2. <b>Connector-block suppression.</b> Inside a connector-only block
    ///    the ONLY road is its bridge line; every other arterial is answered
    ///    as "no road" city-wide, so neighbouring blocks correctly dead-end
    ///    the streets that would have run into it. Water blocks use the same
    ///    mechanism with NO surviving line — the shoreline is where every
    ///    neighbour's street stops — and water + connector is a causeway.
    ///
    /// <see cref="RoadNetworkGenerator"/> carves single blocks against this
    /// model; the baker, the validation pass and the map all read the same
    /// object, so there is exactly one definition of "is there a road here".
    /// </summary>
    public sealed class CityLayout
    {
        // Same salts the original streaming generator used — kept so a city
        // seed produces familiar layouts and the streams stay uncorrelated.
        public const int SaltRow = 101;
        public const int SaltCol = 202;
        // The secondary (district-gated) arterial field's own streams.
        public const int SaltRow2 = 121;
        public const int SaltCol2 = 242;

        /// <summary>Resolved per-block facts, computed once at construction.</summary>
        public readonly struct BlockSpec
        {
            public readonly int Seed;
            public readonly BlockSettings Settings;
            /// <summary>The block's resolved district (override → seeded map → default); null when the definition has no districts.</summary>
            public readonly DistrictDefinition District;
            public readonly bool ConnectorOnly;
            /// <summary>0 = bridge runs East–West (along a row), 1 = North–South (along a column).</summary>
            public readonly int ConnectorAxis;
            /// <summary>Local index (within the block) of the arterial line the bridge follows; -1 when none qualifies.</summary>
            public readonly int BridgeLineLocal;
            /// <summary>
            /// Open water: the block carries no road at all (every arterial
            /// of either tier is suppressed city-wide, so neighbours dead-end
            /// at the shore), no lots, and a splash for whatever drives in.
            /// With <see cref="ConnectorOnly"/> it is a causeway — the bridge
            /// line is the one road that survives.
            /// </summary>
            public readonly bool IsWater;

            public BlockSpec(int seed, BlockSettings settings, DistrictDefinition district, bool connectorOnly, int connectorAxis, int bridgeLineLocal, bool isWater = false)
            {
                Seed = seed;
                Settings = settings;
                District = district;
                ConnectorOnly = connectorOnly && bridgeLineLocal >= 0;
                ConnectorAxis = connectorAxis;
                BridgeLineLocal = bridgeLineLocal;
                IsWater = isWater;
            }

            /// <summary>A bridge over water — connector-only AND water.</summary>
            public bool IsCauseway => ConnectorOnly && IsWater;
        }

        public readonly CityGenerationSettings Settings;
        public readonly int CitySeed;
        public readonly int GridWidth;
        public readonly int GridHeight;
        public readonly int BlockSize;

        readonly Dictionary<Vector2Int, BlockSpec> specs = new();

        public int PeriodX => GridWidth * BlockSize;
        public int PeriodY => GridHeight * BlockSize;

        public CityLayout(CityDefinition definition)
        {
            Settings = definition.generation;
            CitySeed = definition.citySeed;
            GridWidth = Mathf.Max(1, definition.gridWidth);
            GridHeight = Mathf.Max(1, definition.gridHeight);
            BlockSize = Mathf.Max(1, definition.blockSizeInCells);

            for (int y = 0; y < GridHeight; y++)
            for (int x = 0; x < GridWidth; x++)
            {
                var coord = new Vector2Int(x, y);
                CityDefinition.BlockEntry entry = definition.GetEntry(coord);
                int seed = entry != null ? entry.seed : definition.DerivedSeed(coord);
                DistrictDefinition district = definition.DistrictFor(coord);
                BlockSettings settings = entry != null && entry.settingsOverride != null
                    ? entry.settingsOverride
                    : district != null && district.interiorSettings != null
                        ? district.interiorSettings
                        : definition.defaultBlockSettings;
                bool connector = entry != null && entry.connectorOnly;
                bool water = entry != null && entry.isWater;
                int axis = entry != null ? (int)entry.connectorAxis : 0;
                int line = connector ? FindBridgeLine(coord, axis) : -1;
                specs[coord] = new BlockSpec(seed, settings, district, connector, axis, line, water);
            }
        }

        public BlockSpec SpecFor(Vector2Int coord) =>
            specs.TryGetValue(coord, out BlockSpec spec) ? spec : new BlockSpec(CitySeed, null, null, false, 0, -1);

        public BlockKnobs KnobsFor(Vector2Int coord)
        {
            BlockSpec spec = SpecFor(coord);
            return new BlockKnobs(Settings, spec.Settings, spec.District);
        }

        // -------------------------------------------------------- arterials

        /// <summary>Is this wrapped world row a primary arterial line? Pure function of (citySeed, row).</summary>
        public bool IsArterialRow(int worldY) => IsArterialLine(SaltRow, DeterministicHash.Mod(worldY, PeriodY), Settings.arterialSpacing, Settings.arterialJitter);

        /// <summary>Is this wrapped world column a primary arterial line? Pure function of (citySeed, column).</summary>
        public bool IsArterialColumn(int worldX) => IsArterialLine(SaltCol, DeterministicHash.Mod(worldX, PeriodX), Settings.arterialSpacing, Settings.arterialJitter);

        /// <summary>Is this wrapped world row a SECONDARY arterial line? The line only becomes a road inside districts that enable the tier — see <see cref="IsRoadCell"/>.</summary>
        public bool IsSecondaryRow(int worldY) => IsArterialLine(SaltRow2, DeterministicHash.Mod(worldY, PeriodY), Settings.secondaryArterialSpacing, Settings.secondaryArterialJitter);

        /// <summary>Is this wrapped world column a SECONDARY arterial line?</summary>
        public bool IsSecondaryColumn(int worldX) => IsArterialLine(SaltCol2, DeterministicHash.Mod(worldX, PeriodX), Settings.secondaryArterialSpacing, Settings.secondaryArterialJitter);

        /// <summary>
        /// World rows/columns are split into bands of <paramref name="spacing"/>
        /// cells and each band hosts exactly one arterial line; jitter blends
        /// its offset between band-center (regular grid) and a hashed random
        /// position. Identical math to the original streaming generator,
        /// applied to the wrapped index — which is what makes the field
        /// periodic. Both tiers run through here with their own salt, spacing
        /// and jitter.
        /// </summary>
        bool IsArterialLine(int salt, int wrappedIndex, int spacing, float jitter)
        {
            int band = DeterministicHash.FloorDiv(wrappedIndex, spacing);
            float random01 = DeterministicHash.Value01(CitySeed, salt, band);
            int randomOffset = Mathf.Min((int)(random01 * spacing), spacing - 1);
            int offset = Mathf.RoundToInt(Mathf.Lerp(spacing * 0.5f, randomOffset, jitter));
            return DeterministicHash.Mod(wrappedIndex, spacing) == Mathf.Clamp(offset, 0, spacing - 1);
        }

        // ------------------------------------------------------------ roads

        /// <summary>Block owning a (wrapped) world cell.</summary>
        public Vector2Int BlockOfCell(int worldX, int worldY) =>
            new(DeterministicHash.Mod(worldX, PeriodX) / BlockSize,
                DeterministicHash.Mod(worldY, PeriodY) / BlockSize);

        /// <summary>
        /// The city-wide ground-road predicate: does this world cell carry a
        /// road at the city level (arterials, with connector-block
        /// suppression)? This is what block generation uses to resolve
        /// neighbours beyond its own bounds — including across the wrap seam.
        /// Connectors and features never touch borders, so they don't matter
        /// here. Two tiers: primary arterials exist everywhere (the
        /// connectivity guarantee); the denser secondary field materializes
        /// only inside blocks whose district enables it. Both are pure
        /// functions of (citySeed + authored definition, wrapped index), so a
        /// secondary street crossing a district border dead-ends with both
        /// neighbours computing the identical mask.
        /// </summary>
        public bool IsRoadCell(int worldX, int worldY)
        {
            int wx = DeterministicHash.Mod(worldX, PeriodX);
            int wy = DeterministicHash.Mod(worldY, PeriodY);
            BlockSpec spec = SpecFor(new Vector2Int(wx / BlockSize, wy / BlockSize));
            // Open water suppresses EVERY road — the same city-wide answer
            // the connector branch gives, so a neighbour's street dead-ends
            // at the shore with both sides computing the identical mask. A
            // causeway (water + connector) falls through to the bridge rule.
            if (spec.IsWater && !spec.ConnectorOnly) return false;
            if (spec.ConnectorOnly)
            {
                // Only the bridge line exists; everything else — including the
                // arterials of either tier the field says should cross here —
                // is suppressed. The bridge follows a PRIMARY line only, so a
                // neighbour's district choices can never move it.
                return spec.ConnectorAxis == 0
                    ? wy - (wy / BlockSize) * BlockSize == spec.BridgeLineLocal && IsArterialRow(wy)
                    : wx - (wx / BlockSize) * BlockSize == spec.BridgeLineLocal && IsArterialColumn(wx);
            }
            if (IsArterialColumn(wx) || IsArterialRow(wy)) return true;
            return spec.District != null && spec.District.useSecondaryArterials
                && (IsSecondaryColumn(wx) || IsSecondaryRow(wy));
        }

        /// <summary>Does this world row carry a road inside the given block — primary, or district-gated secondary? Used by the connector carver to bound lots by the block's EFFECTIVE grid.</summary>
        public bool RowCrossesBlock(int worldY, Vector2Int block)
        {
            if (IsArterialRow(worldY)) return true;
            DistrictDefinition district = SpecFor(block).District;
            return district != null && district.useSecondaryArterials && IsSecondaryRow(worldY);
        }

        /// <summary>Does this world column carry a road inside the given block?</summary>
        public bool ColCrossesBlock(int worldX, Vector2Int block)
        {
            if (IsArterialColumn(worldX)) return true;
            DistrictDefinition district = SpecFor(block).District;
            return district != null && district.useSecondaryArterials && IsSecondaryColumn(worldX);
        }

        /// <summary>
        /// The arterial line a connector block's bridge follows: the line on
        /// the chosen axis nearest the block centre, at least one cell inside
        /// the border (a border-line bridge would rewrite cells the neighbour
        /// also computes). -1 when no interior line exists — the baker reports
        /// that as a validation error and treats the block as normal.
        /// </summary>
        int FindBridgeLine(Vector2Int coord, int axis)
        {
            int origin = axis == 0 ? coord.y * BlockSize : coord.x * BlockSize;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 1; i < BlockSize - 1; i++)
            {
                bool arterial = axis == 0 ? IsArterialRow(origin + i) : IsArterialColumn(origin + i);
                if (!arterial) continue;
                float distance = Mathf.Abs(i - (BlockSize - 1) * 0.5f);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        /// <summary>Minimum block size a connector block needs: flat border cell + ramp run on each side + at least one deck cell.</summary>
        public int MinConnectorBlockSize => 2 * (Mathf.Max(1, Settings.rampLengthInCells) + 1) + 1;

        /// <summary>Generate one block's grid model against this layout.</summary>
        public ChunkData GenerateBlock(Vector2Int coord) => RoadNetworkGenerator.Generate(this, coord);
    }
}
