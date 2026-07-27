using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Defines one pad type. Positive delta = boost, negative = brake.
    /// The actual effect on a ship is scaled by its weight and blended in
    /// at its acceleration rate (see ShipMotor.AddSpeedImpulse).
    /// </summary>
    [CreateAssetMenu(fileName = "PadDefinition", menuName = "FiniteRunner/Pad Definition")]
    public class PadDefinition : ScriptableObject
    {
        public string displayName = "Boost";

        [Tooltip("Speed change applied when a ship passes over the pad. Positive = boost, negative = brake.")]
        public float speedDelta = 15f;

        [Tooltip("Tint applied to the pad's renderer so the type is readable at speed.")]
        public Color color = new(0.1f, 1f, 0.3f);
    }
}
