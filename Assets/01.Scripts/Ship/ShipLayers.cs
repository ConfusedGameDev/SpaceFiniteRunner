using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The physics layers the standalone ship reads the world through. The
    /// ship is a kinematic body that knows its level only by PhysX queries,
    /// so what it may stand on, take and be caught by is decided by layer and
    /// nothing else — a level opts geometry in by putting it on
    /// <see cref="Ground"/>, never by ship-specific markup. The slots are
    /// FIXED (6–9, named by <c>Tools → FiniteRunner → Ship → Install Ship
    /// Layers</c>) rather than looked up by name: prefabs and scenes serialize
    /// the index, so a slot that moved would silently re-layer every asset.
    /// The city uses none of them, which is what keeps a ship query from ever
    /// hitting city colliders while both scenes are alive in the city→runner
    /// handoff.
    /// </summary>
    public static class ShipLayers
    {
        /// <summary>The ship's (and the patrol's) own hull.</summary>
        public const int Ship = 6;
        /// <summary>Every surface a ship may hover over, land on or hit: floors, walls, ramps, loops, tubes.</summary>
        public const int Ground = 7;
        /// <summary>Pickup volumes (orbs, pads, coins) found by the ship's swept query.</summary>
        public const int Pickup = 8;
        /// <summary>Rule volumes the swept query reads: kill volumes, magnet volumes.</summary>
        public const int Volume = 9;
        /// <summary>
        /// A surface the ship RIDES but its hull never hits: the hover probes see it, the wall sweep does not.
        /// For geometry that passes through itself — the runner's loops, whose two halves cross in space when
        /// they barely drift sideways: in track space nothing collided, with colliders the descending ship met
        /// the ascending half as a head-on wall.
        /// </summary>
        public const int Surface = 10;

        public const string ShipName = "Ship";
        public const string GroundName = "ShipGround";
        public const string PickupName = "ShipPickup";
        public const string VolumeName = "ShipVolume";
        public const string SurfaceName = "ShipSurface";

        public const int GroundMask = 1 << Ground;
        public const int PickupMask = 1 << Pickup;
        public const int VolumeMask = 1 << Volume;
        public const int SurfaceMask = 1 << Surface;

        /// <summary>True when the project's layer table carries the four names in their slots — false means the installer was never run.</summary>
        public static bool Installed =>
            LayerMask.LayerToName(Ship) == ShipName &&
            LayerMask.LayerToName(Ground) == GroundName &&
            LayerMask.LayerToName(Pickup) == PickupName &&
            LayerMask.LayerToName(Volume) == VolumeName &&
            LayerMask.LayerToName(Surface) == SurfaceName;
    }
}
