using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Turns a <see cref="CityLayout"/> + a block coordinate into a
    /// <see cref="ChunkData"/> — pure computation, no GameObjects. Layout
    /// bias: long arterial straights first, then secondary connectors so
    /// blocks vary and there's always more than one way around.
    ///
    /// Determinism rules (the fixed-city contract): everything that can touch
    /// a block border comes from the layout's periodic, city-seeded arterial
    /// field, so any two blocks — including the pair across the pacman wrap
    /// seam — compute the same border sockets independently. Everything
    /// strictly interior (connectors, features) runs on the BLOCK seed, which
    /// is what lets a single block be rerolled without moving any border road.
    /// Connector-only blocks skip the interior entirely and get a bridge
    /// instead (see <see cref="RoadFeaturePlacer.PlaceBridge"/>); water
    /// blocks skip it and flood every cell (<see cref="ChunkData.CellKind.Water"/>);
    /// a causeway is the bridge with the sea flooded in around it.
    /// </summary>
    public static class RoadNetworkGenerator
    {
        // Hash salt keeping the interior-layout stream uncorrelated with the
        // feature/piece/building/decoration streams derived from the same
        // block seed.
        const int SaltChunk = 303;
        // Own stream for the park-lot rolls, so tuning park chances never
        // reshuffles the road layout (and vice versa).
        const int SaltParkLots = 1110;

        public static ChunkData Generate(CityLayout layout, Vector2Int blockCoord)
        {
            CityLayout.BlockSpec spec = layout.SpecFor(blockCoord);
            var data = new ChunkData(blockCoord, layout.BlockSize);

            CarveArterials(layout, data);

            if (spec.IsWater && !spec.ConnectorOnly)
            {
                // Open sea: the layout carved nothing (every road is
                // suppressed city-wide), so the whole block becomes Water —
                // not road, not buildable — and there is no interior to run.
                FloodEmpty(data);
                ResolveConnections(layout, data);
                return data;
            }

            if (spec.ConnectorOnly)
            {
                // A bridge block has no interior: masks resolve over the one
                // carved line, then the placer turns its middle into
                // ramps → deck → ramps. No connectors, no features, and the
                // baker skips buildings and decorations for it. A causeway is
                // the same bridge with the sea flooded in around it: the
                // under-deck cells stay Reserved (StampRoads puts the pillars
                // there — pillars standing in the water ARE the causeway
                // look), everything else still Empty becomes Water.
                ResolveConnections(layout, data);
                RoadFeaturePlacer.PlaceBridge(layout.Settings, data, spec.ConnectorAxis, spec.BridgeLineLocal);
                if (spec.IsWater) FloodEmpty(data);
                return data;
            }

            BlockKnobs knobs = layout.KnobsFor(blockCoord);
            var rng = new System.Random(DeterministicHash.Combine(spec.Seed, SaltChunk, blockCoord.x, blockCoord.y));
            CarveConnectors(layout, knobs, data, rng);
            if (!knobs.AllowDeadEnds) RepairDeadEnds(layout, data);
            ResolveConnections(layout, data);
            // Curved avenues first — their chain cells become feature cells,
            // so the ordinary feature pass keeps off them. Then features
            // (overpasses, forks, roundabouts…), rewriting interior cells
            // only, after the masks are final — border cells stay untouched so
            // the cross-block arterial contract above still holds.
            RoadFeaturePlacer.PlaceCurves(knobs, data, spec.Seed);
            RoadFeaturePlacer.Place(layout.Settings, knobs, data, spec.Seed);
            // Park lots are part of the MODEL, decided here rather than at
            // stamp time, so the flags ride the serialized layout into the
            // baked prefab (SetData snapshots before any content pass runs).
            MarkParkLots(knobs, data, spec.Seed);
            return data;
        }

        // ------------------------------------------------------------ park lots

        /// <summary>
        /// Claim whole lots for the district's nature pass: every lot in a park
        /// block, or a parkLotChance roll per lot elsewhere. Claimed cells stay
        /// <see cref="ChunkData.CellKind.Empty"/> (the ground slab stays
        /// drivable) but carry <see cref="ChunkData.CellFlags.ParkLot"/>, which
        /// the building populator excludes and the nature placer fills.
        /// </summary>
        static void MarkParkLots(in BlockKnobs knobs, ChunkData data, int blockSeed)
        {
            if (knobs.NatureSet == null || (!knobs.IsPark && knobs.ParkLotChance <= 0f)) return;

            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltParkLots, data.Coord.x, data.Coord.y));
            foreach (List<Vector2Int> lot in ChunkLots.FindLots(data, data.IsBuildable))
            {
                if (lot.Count < 2) continue; // a single grass cell reads as a glitch, not a park
                if (!knobs.IsPark && rng.NextDouble() >= knobs.ParkLotChance) continue;
                foreach (Vector2Int cell in lot)
                    data.AddFlags(cell.x, cell.y, ChunkData.CellFlags.ParkLot);
            }
        }

        // ---------------------------------------------------------------- water

        /// <summary>Turn every still-Empty cell into open water. Roads, ramps and Reserved feature cells keep their kind.</summary>
        static void FloodEmpty(ChunkData data)
        {
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (data.GetKind(x, y) == ChunkData.CellKind.Empty)
                    data.SetKind(x, y, ChunkData.CellKind.Water);
            }
        }

        // ------------------------------------------------------------ arterials

        static void CarveArterials(CityLayout layout, ChunkData data)
        {
            Vector2Int origin = data.WorldCellOrigin;
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (layout.IsRoadCell(origin.x + x, origin.y + y))
                    data.SetKind(x, y, ChunkData.CellKind.Arterial);
            }
        }

        // ----------------------------------------------------------- connectors

        /// <summary>
        /// For every lot bounded by arterials that lie fully inside the block,
        /// roll connectorDensity; a hit carves either a straight span between two
        /// facing arterials or (turnProbability) an L that meets one arterial row
        /// and one arterial column — that's where corner pieces come from.
        /// </summary>
        static void CarveConnectors(CityLayout layout, in BlockKnobs knobs, ChunkData data, System.Random rng)
        {
            Vector2Int origin = data.WorldCellOrigin;
            // Lots are bounded by the block's EFFECTIVE grid — primary
            // arterials plus the district-gated secondary lines — so a dense
            // district's connectors subdivide its smaller lots, not the
            // primary superblocks.
            List<int> rows = new(), cols = new();
            for (int y = 0; y < data.SizeInCells; y++)
                if (layout.RowCrossesBlock(origin.y + y, data.Coord)) rows.Add(y);
            for (int x = 0; x < data.SizeInCells; x++)
                if (layout.ColCrossesBlock(origin.x + x, data.Coord)) cols.Add(x);

            for (int ri = 0; ri < rows.Count - 1; ri++)
            for (int ci = 0; ci < cols.Count - 1; ci++)
            {
                int r1 = rows[ri], r2 = rows[ri + 1];
                int c1 = cols[ci], c2 = cols[ci + 1];
                // Interior must fit at least one cell in both axes.
                if (r2 - r1 < 2 || c2 - c1 < 2) continue;
                if (rng.NextDouble() >= knobs.ConnectorDensity) continue;

                if (rng.NextDouble() < knobs.TurnProbability)
                    CarveLConnector(data, rng, r1, r2, c1, c2);
                else
                    CarveStraightConnector(data, rng, r1, r2, c1, c2);
            }
        }

        static void CarveStraightConnector(ChunkData data, System.Random rng, int r1, int r2, int c1, int c2)
        {
            if (rng.Next(2) == 0)
            {
                int x = rng.Next(c1 + 1, c2);
                for (int y = r1 + 1; y < r2; y++) MarkConnector(data, x, y);
            }
            else
            {
                int y = rng.Next(r1 + 1, r2);
                for (int x = c1 + 1; x < c2; x++) MarkConnector(data, x, y);
            }
        }

        static void CarveLConnector(ChunkData data, System.Random rng, int r1, int r2, int c1, int c2)
        {
            int px = rng.Next(c1 + 1, c2);
            int py = rng.Next(r1 + 1, r2);

            // Vertical leg from a bounding arterial row to the elbow…
            bool fromSouth = rng.Next(2) == 0;
            int yStart = fromSouth ? r1 + 1 : py;
            int yEnd = fromSouth ? py : r2 - 1;
            for (int y = yStart; y <= yEnd; y++) MarkConnector(data, px, y);

            // …then a horizontal leg from the elbow to a bounding arterial column.
            bool toWest = rng.Next(2) == 0;
            int xStart = toWest ? c1 + 1 : px;
            int xEnd = toWest ? px : c2 - 1;
            for (int x = xStart; x <= xEnd; x++) MarkConnector(data, x, py);
        }

        static void MarkConnector(ChunkData data, int x, int y)
        {
            if (data.GetKind(x, y) == ChunkData.CellKind.Empty)
                data.SetKind(x, y, ChunkData.CellKind.Connector);
        }

        // ------------------------------------------------------------- clean-up

        /// <summary>
        /// Iteratively strip connector cells with fewer than two road neighbours.
        /// Arterials are exempt — they always continue past the block border, so
        /// what looks like a stub at the edge is actually a through road.
        /// </summary>
        static void RepairDeadEnds(CityLayout layout, ChunkData data)
        {
            bool removedAny = true;
            while (removedAny)
            {
                removedAny = false;
                for (int y = 0; y < data.SizeInCells; y++)
                for (int x = 0; x < data.SizeInCells; x++)
                {
                    if (data.GetKind(x, y) != ChunkData.CellKind.Connector) continue;
                    if (NeighbourMask(layout, data, x, y).ConnectionCount() < 2)
                    {
                        data.SetKind(x, y, ChunkData.CellKind.Empty);
                        removedAny = true;
                    }
                }
            }
        }

        static void ResolveConnections(CityLayout layout, ChunkData data)
        {
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                data.SetConnections(x, y, data.IsRoad(x, y)
                    ? NeighbourMask(layout, data, x, y)
                    : EdgeMask.None);
            }
        }

        /// <summary>
        /// Which edges of a cell face a road neighbour. Neighbours outside the
        /// block are resolved through the layout's city-wide road predicate —
        /// valid because only city-level roads (arterials, bridge lines) ever
        /// touch a block border, and periodic, so the wrap seam matches too.
        /// </summary>
        static EdgeMask NeighbourMask(CityLayout layout, ChunkData data, int x, int y)
        {
            Vector2Int origin = data.WorldCellOrigin;
            EdgeMask mask = EdgeMask.None;
            for (int dir = 0; dir < 4; dir++)
            {
                Vector2Int offset = EdgeMaskUtility.Offset(dir);
                int nx = x + offset.x, ny = y + offset.y;
                bool neighbourIsRoad = data.InBounds(nx, ny)
                    ? data.IsRoad(nx, ny)
                    : layout.IsRoadCell(origin.x + nx, origin.y + ny);
                if (neighbourIsRoad) mask |= EdgeMaskUtility.DirectionBit(dir);
            }
            return mask;
        }
    }
}
