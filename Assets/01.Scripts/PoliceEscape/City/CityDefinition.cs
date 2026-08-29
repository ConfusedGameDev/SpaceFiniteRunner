using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// The authored city: a fixed W×H grid of blocks, each with its own seed
    /// and optional <see cref="BlockSettings"/> override, on top of the shared
    /// <see cref="CityGenerationSettings"/> (pieces, cell size, arterial
    /// knobs — everything that must be identical city-wide so block borders
    /// match). This asset is the single source of truth the City Designer
    /// window edits and <c>CityBaker</c> reads; the baked prefab carries
    /// copies of the values it needs at runtime, so play mode never touches
    /// this asset. The city is periodic: arterials wrap at the grid edges, so
    /// the pacman teleport always lands on a continuing road.
    /// </summary>
    [CreateAssetMenu(fileName = "CityDefinition", menuName = "PoliceEscape/City Definition")]
    public class CityDefinition : ScriptableObject
    {
        /// <summary>Which way a connector-only block's bridge runs.</summary>
        public enum BridgeAxis
        {
            /// <summary>Bridge deck runs East–West, connecting the West and East neighbours.</summary>
            EastWest = 0,
            /// <summary>Bridge deck runs North–South, connecting the South and North neighbours.</summary>
            NorthSouth = 1,
        }

        [Required, InlineEditor]
        [Tooltip("City-wide generation settings: road pieces, cell size, arterial spacing/jitter, feature geometry. Shared by every block — per-block knobs live on BlockSettings assets instead.")]
        public CityGenerationSettings generation;

        [TitleGroup("Grid")]
        [Tooltip("How many blocks the city spans West→East.")]
        [PropertyRange(1, 8)]
        public int gridWidth = 3;

        [TitleGroup("Grid")]
        [Tooltip("How many blocks the city spans South→North.")]
        [PropertyRange(1, 8)]
        public int gridHeight = 3;

        [TitleGroup("Grid")]
        [Tooltip("Side length of one block in cells. Must be at least the arterial spacing, so every block edge is crossed by a road.")]
        [PropertyRange(8, 24)]
        public int blockSizeInCells = 14;

        [TitleGroup("Seeds")]
        [Tooltip("Seed of the city-wide arterial plan (and the default for derived block seeds). Rebuilding a single block never changes this — that is what keeps its borders in place.")]
        public int citySeed = 1;

        [TitleGroup("Blocks")]
        [Tooltip("Default interior settings for blocks without an override. Empty = the generation asset's own layout/feature values.")]
        public BlockSettings defaultBlockSettings;

        /// <summary>One weighted entry of the outer-ring district pool.</summary>
        [Serializable]
        public struct WeightedDistrict
        {
            [Tooltip("District this entry stands for.")]
            public DistrictDefinition district;

            [Tooltip("Relative weight of this district in the outer-ring hash pick.")]
            [PropertyRange(0f, 10f)]
            public float weight;
        }

        [TitleGroup("Districts")]
        [Tooltip("District of the seeded map's anchor block — the downtown. Empty disables the seeded district map entirely (every block falls back to the default district).")]
        public DistrictDefinition downtownDistrict;

        [TitleGroup("Districts")]
        [Tooltip("District of the blocks within the inner ring around downtown. Empty = the default district.")]
        public DistrictDefinition innerRingDistrict;

        [TitleGroup("Districts")]
        [Tooltip("Torus Chebyshev distance (in blocks) still counted as the inner ring around the anchor.")]
        [PropertyRange(1, 4)]
        public int innerRingRadius = 1;

        [TitleGroup("Districts")]
        [Tooltip("Weighted pool the outer blocks draw from by city-seed hash — suburbs, parks, beachfront. Empty = the default district.")]
        public List<WeightedDistrict> outerDistricts = new();

        [TitleGroup("Districts")]
        [Tooltip("District used when the seeded map has nothing to say and a block carries no district override. Empty keeps the pre-district behaviour (plain city/default settings).")]
        public DistrictDefinition defaultDistrict;

        [TitleGroup("Districts")]
        [Tooltip("Pin the downtown anchor to a hand-picked block instead of deriving it from the city seed.")]
        public bool useAuthoredAnchor;

        [TitleGroup("Districts"), ShowIf(nameof(useAuthoredAnchor))]
        [Tooltip("The hand-picked downtown anchor block.")]
        public Vector2Int downtownAnchor;

        /// <summary>One authored block of the grid.</summary>
        [Serializable]
        public class BlockEntry
        {
            [HideInInspector]
            public Vector2Int coord;

            [Tooltip("Seed of this block's interior (connectors, features, buildings, decorations). Reroll it to get a different block without moving any border road.")]
            public int seed;

            [Tooltip("Hand-paints this block's district; empty = the seeded district map. Changing it moves border roads when the districts differ in their secondary-arterial use — rebake the block AND its neighbours.")]
            public DistrictDefinition districtOverride;

            [Tooltip("Interior settings for this block. Empty = the block's district settings, then the definition's default block settings.")]
            [InlineEditor]
            public BlockSettings settingsOverride;

            [Tooltip("Connector-only block: no streets, no buildings — just one elevated bridge crossing the block. Streets from perpendicular neighbours dead-end at its edge. Combined with 'is water' it is a CAUSEWAY: the same bridge, over sea instead of over a void block.")]
            public bool connectorOnly;

            [ShowIf(nameof(connectorOnly))]
            [Tooltip("Which way the bridge runs. It follows the arterial line nearest the block centre on that axis.")]
            public BridgeAxis connectorAxis = BridgeAxis.EastWest;

            [Tooltip("Water block: open sea instead of streets — no roads, no lots, a splash for anything that drives in. Every neighbour's street dead-ends at the shore, so toggling this MOVES BORDER ROADS: rebake with 'Rebuild + Neighbours'. Tick 'connector only' as well for a causeway across it. Validate to make sure the land is not split into unreachable islands.")]
            public bool isWater;

            /// <summary>A bridge over water: <see cref="connectorOnly"/> and <see cref="isWater"/> together.</summary>
            public bool IsCauseway => connectorOnly && isWater;

            public string Label => $"Block ({coord.x}, {coord.y})";
        }

        [TitleGroup("Blocks")]
        [ListDrawerSettings(DefaultExpandedState = false, ListElementLabelName = nameof(BlockEntry.Label), HideAddButton = true, HideRemoveButton = true)]
        [Tooltip("One entry per grid block, managed by EnsureEntries/the City Designer window — coordinates are fixed, edit seeds and overrides.")]
        public List<BlockEntry> blocks = new();

        /// <summary>City width in cells — the arterial field's period along X (the wrap seam).</summary>
        public int CellsPerAxisX => gridWidth * blockSizeInCells;

        /// <summary>City height in cells — the arterial field's period along Y.</summary>
        public int CellsPerAxisY => gridHeight * blockSizeInCells;

        public bool InGrid(Vector2Int coord) =>
            coord.x >= 0 && coord.y >= 0 && coord.x < gridWidth && coord.y < gridHeight;

        /// <summary>Deterministic default seed for a block — stable per (citySeed, coord) until explicitly rerolled.</summary>
        public int DerivedSeed(Vector2Int coord) => DeterministicHash.Combine(citySeed, coord.x, coord.y);

        // -------------------------------------------------------------- districts

        /// <summary>Hash stream of the seeded district map — distinct from every generation salt.</summary>
        public const int SaltDistrict = 909;

        /// <summary>The downtown anchor block: authored, or derived from the city seed.</summary>
        public Vector2Int DowntownAnchor => useAuthoredAnchor
            ? new Vector2Int(Mathf.Clamp(downtownAnchor.x, 0, gridWidth - 1), Mathf.Clamp(downtownAnchor.y, 0, gridHeight - 1))
            : new Vector2Int(
                Mathf.Min((int)(DeterministicHash.Value01(citySeed, SaltDistrict, 0) * gridWidth), gridWidth - 1),
                Mathf.Min((int)(DeterministicHash.Value01(citySeed, SaltDistrict, 1) * gridHeight), gridHeight - 1));

        /// <summary>
        /// A block's district: authored override → seeded radial map (rings of
        /// torus Chebyshev distance around the downtown anchor; the outer ring
        /// hash-picks from the weighted pool) → the default district. A pure
        /// function of the authored asset and the city seed — never a block
        /// seed — which is what lets <see cref="CityLayout"/> feed it into the
        /// border road field: both sides of any border resolve the owning
        /// block's district identically.
        /// </summary>
        public DistrictDefinition DistrictFor(Vector2Int coord)
        {
            BlockEntry entry = GetEntry(coord);
            if (entry != null && entry.districtOverride != null) return entry.districtOverride;
            if (downtownDistrict == null) return defaultDistrict;

            Vector2Int anchor = DowntownAnchor;
            int dx = Mathf.Abs(coord.x - anchor.x);
            int dy = Mathf.Abs(coord.y - anchor.y);
            dx = Mathf.Min(dx, gridWidth - dx);
            dy = Mathf.Min(dy, gridHeight - dy);
            int ring = Mathf.Max(dx, dy);

            if (ring == 0) return downtownDistrict;
            if (ring <= innerRingRadius) return innerRingDistrict != null ? innerRingDistrict : defaultDistrict;
            return PickOuterDistrict(coord);
        }

        DistrictDefinition PickOuterDistrict(Vector2Int coord)
        {
            float total = 0f;
            foreach (WeightedDistrict entry in outerDistricts)
                if (entry.district != null && entry.weight > 0f)
                    total += entry.weight;
            if (total <= 0f) return defaultDistrict;

            float roll = DeterministicHash.Value01(citySeed, SaltDistrict, DeterministicHash.Combine(coord.x, coord.y)) * total;
            foreach (WeightedDistrict entry in outerDistricts)
            {
                if (entry.district == null || entry.weight <= 0f) continue;
                roll -= entry.weight;
                if (roll <= 0f) return entry.district;
            }
            return defaultDistrict;
        }

        public BlockEntry GetEntry(Vector2Int coord)
        {
            foreach (var entry in blocks)
                if (entry != null && entry.coord == coord) return entry;
            return null;
        }

        public BlockEntry GetOrCreateEntry(Vector2Int coord)
        {
            var entry = GetEntry(coord);
            if (entry != null) return entry;
            entry = new BlockEntry { coord = coord, seed = DerivedSeed(coord) };
            blocks.Add(entry);
            return entry;
        }

        /// <summary>
        /// Reconcile the entry list with the grid: add missing blocks (derived
        /// seeds), drop entries that fell outside a shrunk grid. Existing
        /// entries are never reseeded — the project's never-reseed rule.
        /// </summary>
        public void EnsureEntries()
        {
            blocks.RemoveAll(entry => entry == null || !InGrid(entry.coord));
            for (int y = 0; y < gridHeight; y++)
            for (int x = 0; x < gridWidth; x++)
                GetOrCreateEntry(new Vector2Int(x, y));
            blocks.Sort((a, b) => a.coord.y != b.coord.y ? a.coord.y - b.coord.y : a.coord.x - b.coord.x);
        }

        /// <summary>Roll a new city: fresh city seed and fresh derived seeds for every block.</summary>
        public void RerollAllSeeds()
        {
            citySeed = UnityEngine.Random.Range(int.MinValue / 2, int.MaxValue / 2);
            EnsureEntries();
            foreach (var entry in blocks) entry.seed = DerivedSeed(entry.coord);
        }
    }
}
