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

        public PadDefinition Definition => definition;

        /// <summary>Runtime assignment used by the track generator.</summary>
        public void SetDefinition(PadDefinition def)
        {
            definition = def;
            ApplyColor();
        }

        void Awake() => ApplyColor();
        void OnValidate() => ApplyColor();

        void OnTriggerEnter(Collider other)
        {
            if (definition == null) return;
            var motor = other.GetComponentInParent<ShipMotor>();
            if (motor == null) return;
            motor.AddSpeedImpulse(definition.speedDelta);
        }

        void ApplyColor()
        {
            if (definition == null) return;
            var rend = GetComponentInChildren<Renderer>();
            if (rend == null) return;
            mpb ??= new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, definition.color);
            rend.SetPropertyBlock(mpb);
        }
    }
}
