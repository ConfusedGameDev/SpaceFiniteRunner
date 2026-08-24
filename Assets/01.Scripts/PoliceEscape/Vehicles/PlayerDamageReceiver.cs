using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The player car's side of <see cref="IDamageable"/>, attached by
    /// <see cref="CarFactory.Spawn"/> so blasts can hurt the player through
    /// the same interface as everything else. The hero car is PLATED: incoming
    /// normalized damage (1 = what kills an NPC car outright) is scaled by
    /// <see cref="damageScale"/> before it lands on the run's corruption meter
    /// — the same meter police shunts fill — so a barrel costs about a third
    /// of a run, not the whole of it. A scene with no level flow (the road-kit
    /// test scenes) has nothing to corrupt; the hit stays a glitch pulse.
    /// </summary>
    public class PlayerDamageReceiver : MonoBehaviour, IDamageable
    {
        [Tooltip("Fraction of incoming normalized damage that reaches the corruption meter — the hero car's plating.")]
        public float damageScale = 0.35f;

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;
            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(1f, 0.7f, 0.45f);

            var level = FindAnyObjectByType<LevelManager>();
            if (level != null) level.ApplyDamage(amount * damageScale, "blast");
            else if (GlitchController.Instance != null) GlitchController.Instance.Pulse(1f);
        }
    }
}
