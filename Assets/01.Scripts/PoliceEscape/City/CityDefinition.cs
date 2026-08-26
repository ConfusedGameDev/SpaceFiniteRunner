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

        /// <summary>One authored block of the grid.</summary>
        [Serializable]
        public class BlockEntry
        {
            [HideInInspector]
            public Vector2Int coord;

            [Tooltip("Seed of this block's interior (connectors, features, buildings, decorations). Reroll it to get a different block without moving any border road.")]
            public int seed;

            [Tooltip("Interior settings for this block. Empty = the definition's default block settings.")]
            [InlineEditor]
            public BlockSettings settingsOverride;

            [Tooltip("Connector-only block: no streets, no buildings — just one elevated bridge crossing the block. Streets from perpendicular neighbours dead-end at its edge.")]
            public bool connectorOnly;

            [ShowIf(nameof(connectorOnly))]
            [Tooltip("Which way the bridge runs. It follows the arterial line nearest the block centre on that axis.")]
            public BridgeAxis connectorAxis = BridgeAxis.EastWest;

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
