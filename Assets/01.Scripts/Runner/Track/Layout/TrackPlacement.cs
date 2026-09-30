using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>What a <see cref="TrackPlacement"/> builds. Serialized (track assets, M2): append only.</summary>
    public enum TrackPlacementKind
    {
        SpeedOrb = 0,
        RepairOrb = 1,
        LaserGate = 2,
        Ramp = 3,
        Collectible = 4,
        /// <summary>A catalog power-up (TrackAuthoringPRD D13) — reserved for M5.</summary>
        Pickup = 5,
        /// <summary>Any prefab dropped on the track in the editor — reserved for M5.</summary>
        CustomPrefab = 6,
        EndRamp = 7,
        Loop = 8,
        /// <summary>Retired: only runtime generation places one, when the brake spawner is back in the set.</summary>
        BrakePad = 9,
    }

    /// <summary>
    /// One thing on the track, DECIDED but not built: what it is, where it is
    /// (distance from the track start — the authoritative coordinate — lateral
    /// and height) and whatever was rolled for it. The generator decides the
    /// whole track's placements up front and builds each one only when the
    /// ship comes within the stream window (<see cref="TrackGenerator"/>), so
    /// every roll happens at decision time and building is pure: the same
    /// record always builds the same object.
    /// <list type="bullet">
    /// <item><see cref="variant"/>: the speed orb's tier index, the laser
    /// gate's <c>LaserGateVariant</c>, the feature table index of a ramp or a
    /// loop, the end ramp's slot (0 left, 1 middle, 2 right).</item>
    /// <item><see cref="data"/>: the laser gate's (beam length, rotor speed,
    /// rotor phase, wavy 0/1); the coin's (value).</item>
    /// </list>
    /// </summary>
    [System.Serializable]
    public struct TrackPlacement
    {
        public TrackPlacementKind kind;
        public float distance;
        public float lateral;
        public float height;
        public int variant;
        public Vector4 data;

        public TrackPlacement(TrackPlacementKind kind, float distance, float lateral, int variant = 0, Vector4 data = default, float height = 0f)
        {
            this.kind = kind;
            this.distance = distance;
            this.lateral = lateral;
            this.height = height;
            this.variant = variant;
            this.data = data;
        }
    }
}
