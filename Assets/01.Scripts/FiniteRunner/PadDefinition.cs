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

        [Tooltip("Scale applied to the generator's base pad size. Boosts are small (0.3) and must be aimed for; brakes are big (1.2) and must be dodged.")]
        [Min(0.05f)] public float sizeMultiplier = 1f;

        [Tooltip("Spawn as a hovering orb on the flight line instead of a flat pad on the road.")]
        public bool floatingOrb;
    }
}
