using UnityEngine;

namespace HoverRacer
{
    [CreateAssetMenu(menuName = "HoverRacer/Track Object Config", fileName = "TrackObjectConfig_Default")]
    public class TrackObjectConfigSO : ScriptableObject
    {
        [Tooltip("Speed change applied to the vehicle in m/s. " +
                 "Positive values increase speed (booster). Negative values decrease speed (obstacle).")]
        public float speedDelta = 10f;

        [Tooltip("When true, this object deactivates after one use and respawns after RespawnDelay seconds. " +
                 "When false, it can be triggered repeatedly.")]
        public bool singleUse = false;

        [Tooltip("Seconds before a single-use object becomes active again. Only relevant when SingleUse is true.")]
        [Min(0f)] public float respawnDelay = 5f;
    }
}
