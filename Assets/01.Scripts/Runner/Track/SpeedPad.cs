using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Simulation;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A pad or orb on the track that changes the speed of a ship passing
    /// through it. <b>Detection is analytic, not physics</b>: the generator
    /// hands it its track-space spot (<see cref="PlaceOnTrack"/>), it sits in
    /// the <see cref="PickupRegistry"/> while enabled, and the ship's
    /// <see cref="TrackBody"/> finds it by sweeping the distance it covered —
    /// a trigger collider was tunnelled at speed. Whatever colliders the
    /// visual carries are only ever a picture. A boost ORB is used up when
    /// taken (it disappears); a brake pad stays painted on the road but only
    /// bites once.
    /// </summary>
    public class SpeedPad : MonoBehaviour, ITrackPickup, IShipPickup
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] PadDefinition definition;

        MaterialPropertyBlock mpb;

        // Per-instance overrides set by the track generator for tiered orbs.
        // 0 delta / null tint = fall back to the shared definition.
        float speedDeltaOverride;
        Color? tintOverride;

        // Track-space spot, from the generator.
        bool placed;
        bool taken;
        float trackDistance, trackLateral, trackHeight;
        Vector3 trackHalfExtents;
        OrbHover hover; // a swaying orb's lateral moves with it

        /// <summary>Raised whenever any pad or orb is collected by a ship. Static so listeners (GameManager story messages) need no per-pad wiring.</summary>
        public static event System.Action<SpeedPad, IShip> Collected;

        public PadDefinition Definition => definition;

        /// <summary>Orb tier this pad was spawned from (see TrackGenerator.orbTiers); null for untiered pads.</summary>
        public string TierName { get; private set; }

        /// <summary>Effective speed change this pad applies (override or definition).</summary>
        public float SpeedDelta =>
            speedDeltaOverride != 0f ? speedDeltaOverride :
            definition != null ? definition.speedDelta : 0f;

        // ------------------------------------------------------- ITrackPickup
        public float TrackDistance => trackDistance;
        public float TrackLateral => trackLateral + (hover != null ? hover.SwayOffset : 0f);
        public float TrackHeight => trackHeight;
        public Vector3 TrackHalfExtents => trackHalfExtents;
        // A pad is there to be taken whether or not a track placed it: dropped into any level by hand it has its
        // collider for the standalone ship's sweep; only the analytic registry (the track-space patrol) needs the spot.
        public bool Available => !taken && definition != null;

        /// <summary>A floating orb that speeds a ship up — what the patrol goes after.</summary>
        public bool IsBoostOrb => definition != null && definition.floatingOrb && SpeedDelta > 0f;

        /// <summary>Runtime assignment used by the track generator.</summary>
        public void SetDefinition(PadDefinition def)
        {
            definition = def;
            ApplyColor();
        }

        /// <summary>
        /// Runtime assignment for tiered orbs: same definition, but the speed
        /// delta and tint come from the tier instead of the shared asset.
        /// </summary>
        public void SetDefinition(PadDefinition def, float speedDelta, Color tint, string tierName)
        {
            definition = def;
            speedDeltaOverride = speedDelta;
            tintOverride = tint;
            TierName = tierName;
            ApplyColor();
        }

        /// <summary>
        /// Where the pad is in track space and how big its pickup volume is
        /// (half extents: x across the track, y up, z along it). The generator
        /// calls this once, after the visual is built; in play it puts the pad
        /// in the <see cref="PickupRegistry"/>.
        /// </summary>
        public void PlaceOnTrack(float distance, float lateral, float height, Vector3 halfExtents)
        {
            trackDistance = distance;
            trackLateral = lateral;
            trackHeight = height;
            trackHalfExtents = halfExtents;
            hover = GetComponent<OrbHover>();
            placed = true;
            if (Application.isPlaying && isActiveAndEnabled) PickupRegistry.Register(this);
        }

        // The standalone ship's swept query finds the pad by its collider; taking it is the same act.
        void IShipPickup.PickUp(IShip ship) => Collect(ship);

        /// <summary>A ship went through it: apply the speed change, tell the listeners, use an orb up.</summary>
        public void Collect(IShip motor)
        {
            if (!Available || motor == null) return;
            taken = true;
            motor.AddSpeedImpulse(SpeedDelta);
            Collected?.Invoke(this, motor);
            if (definition.floatingOrb) gameObject.SetActive(false); // OnDisable drops it from the registry
        }

        /// <summary>
        /// Taken by something that is not the player's ship (the patrol): the
        /// pad is used up exactly as if collected, but nothing is announced —
        /// <see cref="Collected"/> is the player's event (stats, story lines).
        /// Returns the speed change it carried, 0 if it was already gone.
        /// </summary>
        public float Take()
        {
            if (!Available) return 0f;
            taken = true;
            float delta = SpeedDelta;
            if (definition.floatingOrb) gameObject.SetActive(false);
            return delta;
        }

        void Awake() => ApplyColor();
        void OnValidate() => ApplyColor();

        void OnEnable()
        {
            if (placed && Application.isPlaying) PickupRegistry.Register(this);
        }

        void OnDisable() => PickupRegistry.Unregister(this);

        // Every renderer, not just the first: the boost orbs carry a spinning
        // indicator ring that must wear the tier's colour too.
        void ApplyColor()
        {
            if (definition == null) return;
            mpb ??= new MaterialPropertyBlock();
            foreach (var rend in GetComponentsInChildren<Renderer>())
            {
                rend.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, tintOverride ?? definition.color);
                rend.SetPropertyBlock(mpb);
            }
        }
    }
}
