using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Turns settings + a chunk coordinate into a <see cref="ChunkData"/> —
    /// pure computation, no GameObjects. Layout bias: long arterial straights
    /// first, then secondary connectors so blocks vary and there's always more
    /// than one way around.
    ///
    /// Determinism rules (the infinite-streaming contract):
    /// arterial lines are a function of *world* cell coordinates hashed with
    /// the global seed, so neighbouring chunks compute the same crossings
    /// independently and arterials continue across borders for free. Only
    /// arterials touch chunk edges — connectors stay strictly interior — so a
    /// chunk never needs to know a neighbour's connector layout.
    /// </summary>
    public static class RoadNetworkGenerator
    {
        // Hash salts keep independent random streams from correlating.
        const int SaltRow = 101;
        const int SaltCol = 202;
        const int SaltChunk = 303;

        public static ChunkData Generate(CityGenerationSettings settings, Vector2Int chunkCoord)
        {
            var data = new ChunkData(chunkCoord, settings.chunkSizeInCells);
            var rng = new System.Random(DeterministicHash.Combine(settings.globalSeed, SaltChunk, chunkCoord.x, chunkCoord.y));

            CarveArterials(settings, data);
            CarveConnectors(settings, data, rng);
            if (!settings.allowDeadEnds) RepairDeadEnds(settings, data);
            ResolveConnections(settings, data);
            // Features (overpasses, roundabouts…) rewrite interior cells only,
            // after the masks are final — border cells stay untouched so the
            // cross-chunk arterial contract above still holds.
            RoadFeaturePlacer.Place(settings, data);
            return data;
        }

        /// <summary>Chunk seed exposed for gizmo labels.</summary>
        public static int ChunkSeed(CityGenerationSettings settings, Vector2Int chunkCoord)
            => DeterministicHash.Combine(settings.globalSeed, SaltChunk, chunkCoord.x, chunkCoord.y);

        // ------------------------------------------------------------ arterials

        /// <summary>Is this world cell on an arterial line? Pure function of world coords — chunk-independent.</summary>
        public static bool IsArterialWorldCell(CityGenerationSettings settings, int worldX, int worldY)
            => IsArterialLine(settings, SaltCol, worldX) || IsArterialLine(settings, SaltRow, worldY);

        /// <summary>
        /// World rows/columns are split into bands of arterialSpacing cells and
        /// each band hosts exactly one arterial line; jitter blends its offset
        /// between band-center (regular grid) and a hashed random position.
        /// </summary>
        static bool IsArterialLine(CityGenerationSettings settings, int salt, int worldIndex)
        {
            int spacing = settings.arterialSpacing;
            int band = DeterministicHash.FloorDiv(worldIndex, spacing);
            float random01 = DeterministicHash.Value01(settings.globalSeed, salt, band);
            int randomOffset = Mathf.Min((int)(random01 * spacing), spacing - 1);
            int offset = Mathf.RoundToInt(Mathf.Lerp(spacing * 0.5f, randomOffset, settings.arterialJitter));
            return DeterministicHash.Mod(worldIndex, spacing) == Mathf.Clamp(offset, 0, spacing - 1);
        }

        static void CarveArterials(CityGenerationSettings settings, ChunkData data)
        {
            Vector2Int origin = data.WorldCellOrigin;
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (IsArterialWorldCell(settings, origin.x + x, origin.y + y))
                    data.SetKind(x, y, ChunkData.CellKind.Arterial);
            }
        }

        // ----------------------------------------------------------- connectors

        /// <summary>
        /// For every block bounded by arterials that lie fully inside the chunk,
        /// roll connectorDensity; a hit carves either a straight span between two
        /// facing arterials or (turnProbability) an L that meets one arterial row
        /// and one arterial column — that's where corner pieces come from.
        /// </summary>
        static void CarveConnectors(CityGenerationSettings settings, ChunkData data, System.Random rng)
        {
            Vector2Int origin = data.WorldCellOrigin;
            List<int> rows = new(), cols = new();
            for (int y = 0; y < data.SizeInCells; y++)
                if (IsArterialLine(settings, SaltRow, origin.y + y)) rows.Add(y);
            for (int x = 0; x < data.SizeInCells; x++)
                if (IsArterialLine(settings, SaltCol, origin.x + x)) cols.Add(x);

            for (int ri = 0; ri < rows.Count - 1; ri++)
            for (int ci = 0; ci < cols.Count - 1; ci++)
            {
                int r1 = rows[ri], r2 = rows[ri + 1];
                int c1 = cols[ci], c2 = cols[ci + 1];
                // Interior must fit at least one cell in both axes.
                if (r2 - r1 < 2 || c2 - c1 < 2) continue;
                if (rng.NextDouble() >= settings.connectorDensity) continue;

                if (rng.NextDouble() < settings.turnProbability)
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
        /// Arterials are exempt — they always continue past the chunk border, so
        /// what looks like a stub at the edge is actually a through road.
        /// </summary>
        static void RepairDeadEnds(CityGenerationSettings settings, ChunkData data)
        {
            bool removedAny = true;
            while (removedAny)
            {
                removedAny = false;
                for (int y = 0; y < data.SizeInCells; y++)
                for (int x = 0; x < data.SizeInCells; x++)
                {
                    if (data.GetKind(x, y) != ChunkData.CellKind.Connector) continue;
                    if (NeighbourMask(settings, data, x, y).ConnectionCount() < 2)
                    {
                        data.SetKind(x, y, ChunkData.CellKind.Empty);
                        removedAny = true;
                    }
                }
            }
        }

        static void ResolveConnections(CityGenerationSettings settings, ChunkData data)
        {
            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                data.SetConnections(x, y, data.IsRoad(x, y)
                    ? NeighbourMask(settings, data, x, y)
                    : EdgeMask.None);
            }
        }

        /// <summary>
        /// Which edges of a cell face a road neighbour. Neighbours outside the
        /// chunk are resolved through the world-space arterial function — valid
        /// because only arterials ever touch a chunk border.
        /// </summary>
        static EdgeMask NeighbourMask(CityGenerationSettings settings, ChunkData data, int x, int y)
        {
            Vector2Int origin = data.WorldCellOrigin;
            EdgeMask mask = EdgeMask.None;
            for (int dir = 0; dir < 4; dir++)
            {
                Vector2Int offset = EdgeMaskUtility.Offset(dir);
                int nx = x + offset.x, ny = y + offset.y;
                bool neighbourIsRoad = data.InBounds(nx, ny)
                    ? data.IsRoad(nx, ny)
                    : IsArterialWorldCell(settings, origin.x + nx, origin.y + ny);
                if (neighbourIsRoad) mask |= EdgeMaskUtility.DirectionBit(dir);
            }
            return mask;
        }
    }
}
