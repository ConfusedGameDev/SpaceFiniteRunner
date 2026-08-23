using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// A pad on the track that changes the speed of any ship passing over it.
    /// Needs a trigger collider; the ship carries a kinematic rigidbody.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SpeedPad : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] PadDefinition definition;

        MaterialPropertyBlock mpb;

        // Per-instance overrides set by the track generator for tiered orbs.
        // 0 delta / null tint = fall back to the shared definition.
        float speedDeltaOverride;
        Color? tintOverride;

        /// <summary>Raised whenever any pad or orb is collected by a ship. Static so listeners (GameManager story messages) need no per-pad wiring.</summary>
        public static event System.Action<SpeedPad, ShipMotor> Collected;

        public PadDefinition Definition => definition;

        /// <summary>Orb tier this pad was spawned from (see TrackGenerator.orbTiers); null for untiered pads.</summary>
        public string TierName { get; private set; }

        /// <summary>Effective speed change this pad applies (override or definition).</summary>
        public float SpeedDelta =>
            speedDeltaOverride != 0f ? speedDeltaOverride :
            definition != null ? definition.speedDelta : 0f;

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

        void Awake() => ApplyColor();
        void OnValidate() => ApplyColor();

        void OnTriggerEnter(Collider other)
        {
            if (definition == null) return;
            var motor = other.GetComponentInParent<ShipMotor>();
            if (motor == null) return;
            motor.AddSpeedImpulse(SpeedDelta);
            Collected?.Invoke(this, motor);
        }

        void ApplyColor()
        {
            if (definition == null) return;
            var rend = GetComponentInChildren<Renderer>();
            if (rend == null) return;
            mpb ??= new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, tintOverride ?? definition.color);
            rend.SetPropertyBlock(mpb);
        }
    }
}
