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
    /// <see cref="VehicleHealthSettings.playerBlastDamageScale"/> before it
    /// lands on the <see cref="PlayerHealthMeter"/>
    /// — the same meter police shunts fill — so a barrel costs about a third
    /// of a run, not the whole of it. A scene with no meter (the road-kit
    /// test scenes) has nothing to corrupt; the hit stays a glitch pulse.
    /// </summary>
    public class PlayerDamageReceiver : MonoBehaviour, IDamageable
    {
        public void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;
            var meter = FindAnyObjectByType<PlayerHealthMeter>();
            if (meter != null) { meter.ApplyBlast(amount); return; }

            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(VehicleHealthSettings.Load().playerBlastRumble);
            if (GlitchController.Instance != null) GlitchController.Instance.Pulse(1f);
        }
    }
}
