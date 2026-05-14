using UnityEngine;

namespace HoverRacer
{
    [CreateAssetMenu(menuName = "HoverRacer/Vehicle Stats", fileName = "VehicleStats_Default")]
    public class VehicleStatsSO : ScriptableObject
    {
        [Header("Hover")]
        [Tooltip("Target distance to maintain above the ground surface.")]
        [Min(0f)] public float hoverHeight   = 1.5f;
        [Tooltip("Spring stiffness. Higher values = snappier hover correction.")]
        [Min(0f)] public float springStrength = 1200f;
        [Tooltip("Spring damping. Higher values = less oscillation.")]
        [Min(0f)] public float springDamping  = 80f;
        [Tooltip("Max ray length. If no ground is detected within this range the hover force is not applied.")]
        [Min(0f)] public float raycastRange   = 4f;
        [Tooltip("Local-space offsets from which hover rays are cast downward. Typically the four corners of the hull.")]
        public Vector3[] raycastOffsets = new Vector3[]
        {
            new Vector3(-0.5f, 0f,  0.8f),   // front-left
            new Vector3( 0.5f, 0f,  0.8f),   // front-right
            new Vector3(-0.5f, 0f, -0.8f),   // rear-left
            new Vector3( 0.5f, 0f, -0.8f),   // rear-right
        };

        [Header("Forward Movement")]
        [Tooltip("Speed the vehicle starts at, in m/s.")]
        [Min(0f)] public float initialSpeed  = 20f;
        [Tooltip("Natural deceleration rate in m/s² (constant linear drag). The ship loses this much speed per second.")]
        [Min(0f)] public float deceleration  = 3f;
        [Tooltip("Hard cap on speed regardless of boosts applied.")]
        [Min(0f)] public float maxSpeed      = 60f;

        [Header("Steering")]
        [Tooltip("Torque magnitude applied when steering input is at full deflection.")]
        [Min(0f)] public float steerTorque   = 180f;
        [Tooltip("Counter-torque proportional to current angular velocity. Prevents infinite spin.")]
        [Min(0f)] public float steerDamping  = 12f;
        [Tooltip("Maximum visual bank angle (degrees) of the ship mesh when turning.")]
        [Range(0f, 45f)] public float maxBankAngle = 25f;
        [Tooltip("Speed at which the visual bank lerps toward the target angle.")]
        [Min(0f)] public float bankLerpSpeed = 8f;

        [Header("Boosts")]
        [Tooltip("Number of player-triggered boosts available at race start.")]
        [Min(0)] public int boostCount = 3;
        [Tooltip("Speed added instantly per boost activation, in m/s.")]
        [Min(0f)] public float boostSpeedDelta = 10f;
    }
}
