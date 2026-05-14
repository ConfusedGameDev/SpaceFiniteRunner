using UnityEngine;

namespace HoverRacer
{
    [CreateAssetMenu(menuName = "HoverRacer/Checkpoint Config", fileName = "CheckpointConfig_Default")]
    public class CheckpointConfigSO : ScriptableObject
    {
        [Header("Rating Thresholds (absolute speed delta from target, m/s)")]
        [Tooltip("Speed delta at or below this = Perfect")]
        [Min(0f)] public float perfectRange = 1f;
        [Tooltip("Speed delta at or below this = Awesome")]
        [Min(0f)] public float awesomeRange = 3f;
        [Tooltip("Speed delta at or below this = Great")]
        [Min(0f)] public float greatRange   = 7f;
        // Beyond greatRange = Meh

        [Header("Boost Effects")]
        [Tooltip("Meh: boost charges lost (use a negative value, e.g. -1).")]
        public float mehEffect     = -1f;
        [Tooltip("Great: boost charges gained (half a boost).")]
        public float greatEffect   =  0.5f;
        [Tooltip("Awesome: boost charges gained (one full boost).")]
        public float awesomeEffect =  1f;
        // Perfect: replenishes all boosts to maximum.
    }
}
