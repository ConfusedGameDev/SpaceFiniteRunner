using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Defines a ship's identity, tuning stats and movement behaviour.
    /// The ship never accelerates on its own: it launches with an initial
    /// impulse and constantly bleeds speed until a pad (or card effect)
    /// feeds it more. When speed reaches zero, the run is over.
    /// </summary>
    [CreateAssetMenu(fileName = "ShipDefinition", menuName = "FiniteRunner/Ship Definition")]
    public class ShipDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Fighter";
        [TextArea]
        public string description;

        [Header("Speed")]
        [Tooltip("Hard cap. Boost pads can never push the ship past this.")]
        [Min(0f)] public float maxSpeed = 60f;

        [Tooltip("Speed the ship launches with at the start of a run.")]
        [Min(0f)] public float initialImpulse = 25f;

        [Tooltip("Speed lost per second when not touching any pad. This is the core pressure of the game.")]
        [Min(0f)] public float passiveDeceleration = 3f;

        [Tooltip("How quickly external speed changes (pads, impulses) blend into the current speed, in speed units per second.")]
        [Min(0.01f)] public float acceleration = 40f;

        [Header("Handling")]
        [Tooltip("Lateral movement speed across the track, in units per second, at full steer input.")]
        [Min(0f)] public float lateralSpeed = 8f;

        [Tooltip("Responsiveness of the steering. Higher values reach full lateral speed faster; low values feel heavy and drifty.")]
        [Min(0.01f)] public float handlingResponse = 8f;

        [Header("Weight")]
        [Tooltip("Scales how much pads affect this ship. 1 = full effect, 2 = pads (boost AND brake) only apply half their effect.")]
        [Min(0.1f)] public float weight = 1f;

        [Header("Hover")]
        [Tooltip("How high the ship model floats above the flight line (visual only — pad detection is unaffected).")]
        [Min(0f)] public float hoverHeight = 2f;

        [Tooltip("How far the ship bobs up and down around the hover height.")]
        [Min(0f)] public float bobAmplitude = 0.35f;

        [Tooltip("How fast the hover bobbing moves.")]
        [Min(0f)] public float bobFrequency = 1.5f;

        [Tooltip("Maximum nose pitch wobble from the hover, in degrees.")]
        [Range(0f, 10f)] public float hoverPitchDegrees = 2.5f;

        [Header("Feel")]
        [Tooltip("Maximum roll angle in degrees when steering at full input.")]
        [Range(0f, 90f)] public float maxBankAngle = 35f;

        [Tooltip("How fast the ship rolls into / out of a bank.")]
        [Min(0.01f)] public float bankResponse = 6f;

        /// <summary>Speed delta a pad of the given raw magnitude applies to this ship.</summary>
        public float ScalePadEffect(float rawMagnitude) => rawMagnitude / weight;
    }
}
