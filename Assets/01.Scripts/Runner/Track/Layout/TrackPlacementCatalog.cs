using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// What the track editor's palette offers to place (TrackAuthoringPRD
    /// D13): every power-up, as data. An entry is either one of the track's
    /// own kinds — a speed orb tier or the repair orb, built by the spawn
    /// set's spawners exactly as a generated track builds them — or a
    /// <b>new power-up</b>: a prefab plus a <see cref="PadDefinition"/>
    /// (<see cref="PadSpawnEntry"/>), built as a <see cref="SpeedPad"/> from
    /// a <see cref="TrackPlacementKind.Pickup"/> record that points at the
    /// entry by index. Adding a power-up is adding an entry: no code. Brake
    /// pads are retired and not offered. Loaded from
    /// <c>Resources/FiniteRunner_PlacementCatalog</c> unless the generator
    /// names one; keep the order — Pickup records index it.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_PlacementCatalog", menuName = "FiniteRunner/Placement Catalog")]
    public class TrackPlacementCatalog : ScriptableObject
    {
        public const string ResourcePath = "FiniteRunner_PlacementCatalog";

        [System.Serializable]
        public class Entry
        {
            [Tooltip("The palette's label.")]
            public string displayName = "Power-up";

            [Tooltip("SpeedOrb = a tier of the spawn set's speed orbs (below). RepairOrb = the repair orb. Pickup = a new power-up built from the pad below.")]
            public TrackPlacementKind builtAs = TrackPlacementKind.Pickup;

            [Tooltip("SpeedOrb only: the tier (0 Green, 1 Blue, 2 Purple in the shipped set).")]
            [ShowIf(nameof(builtAs), TrackPlacementKind.SpeedOrb)]
            [Min(0)] public int speedOrbTier;

            [Tooltip("Pickup only: the power-up — prefab, definition (what it does), boost multiplier, colour, sway, lane.")]
            [ShowIf(nameof(builtAs), TrackPlacementKind.Pickup)]
            public PadSpawnEntry pad = new() { name = "PowerUp" };

            [Tooltip("The palette's and the gizmo's colour.")]
            public Color color = Color.white;
        }

        [ListDrawerSettings(ShowIndexLabels = true)]
        public List<Entry> entries = new();

        /// <summary>The catalog in Resources, or null.</summary>
        public static TrackPlacementCatalog LoadDefault() => Resources.Load<TrackPlacementCatalog>(ResourcePath);

        /// <summary>The record an entry places at a spot (its kind and variant); the caller sets distance and lateral.</summary>
        public TrackPlacement RecordFor(int index, float distance, float lateral)
        {
            Entry e = entries[index];
            return e.builtAs switch
            {
                TrackPlacementKind.SpeedOrb => new TrackPlacement(TrackPlacementKind.SpeedOrb, distance, lateral, e.speedOrbTier),
                TrackPlacementKind.RepairOrb => new TrackPlacement(TrackPlacementKind.RepairOrb, distance, lateral),
                _ => new TrackPlacement(TrackPlacementKind.Pickup, distance, lateral, index),
            };
        }
    }
}
