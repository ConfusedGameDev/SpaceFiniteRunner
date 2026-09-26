using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>Which lane a pad spawns on: the flight line, or the air lane above it that only a jump reaches.</summary>
    public enum PadLane { Ground, Air }

    /// <summary>
    /// One <see cref="SpeedPad"/> kind — a boost orb tier or the brake pad.
    /// Probability is its share of a <see cref="SpeedOrbSpawner"/> roll (the
    /// sliders auto-rebalance so the tiers always sum to 100%). A prefab
    /// replaces the code-built primitive; boosts apply
    /// GameSettings.powerUpSpeedBoost × multiplier, and sway makes the
    /// juicier orbs drift across the track so they must be earned.
    /// </summary>
    [System.Serializable]
    public class PadSpawnEntry : IWeightedEntry
    {
        public string name = "Green";

        [Tooltip("Optional model spawned instead of the code-built primitive. Colliders are forced to triggers; one is added if the prefab has none.")]
        public GameObject prefab;

        [Tooltip("What the pad does on pickup (boost/brake, orb or flat pad, size).")]
        [Required] public PadDefinition definition;

        [Tooltip("Share of every spawn roll this entry wins. The tiers always sum to 100%. Unused by a single-entry spawner.")]
        [PropertyRange(0f, 100f), SuffixLabel("%", true)]
        public float probability = 100f;

        [Tooltip("Boosts only: multiplies GameSettings.powerUpSpeedBoost. Brakes use their definition's own delta.")]
        [Min(0f)] public float multiplier = 1f;

        [Tooltip("Tint of the code-built primitive (prefabs keep their own materials) and of the pickup's story color.")]
        public Color color = new(0.1f, 1f, 0.3f);

        [Tooltip("How far the orb sways side to side across the track, in meters. 0 = holds the flight line.")]
        [Min(0f)] public float swayAmplitude;

        [Tooltip("Sway cycles per second.")]
        [Min(0f)] public float swayFrequency = 0.5f;

        [Tooltip("Ground = on the flight line. Air = GameSettings.airLaneHeight above it, where only a jump reaches — the future air lane, keep such entries at 0% until jumps ship them.")]
        public PadLane lane = PadLane.Ground;

        public float Probability { get => probability; set => probability = value; }
    }
}
