using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// The one blast rule, shared by barrels and dying cars: everything inside
    /// the radius is caught, whoever set it off — every <see cref="IDamageable"/>
    /// on a caught rigidbody takes the blast's damage, and non-kinematic bodies
    /// are thrown. Damaging a barrel detonates it, so blasts CHAIN: the nested
    /// detonation runs inside this loop, which is safe because Destroy is
    /// deferred to end of frame and every damageable ignores damage once spent.
    /// </summary>
    /// <summary>
    /// What a blast does, as one value (refactor Step 9.3): the radius it
    /// catches, the impulse it throws with, how much it lifts, and the
    /// normalized damage it deals. Dying cars (<c>VehicleHealthSettings.blast</c>)
    /// and barrels (<c>DecorationSet.blast</c>) each author one — two
    /// explosions that happen to start equal, not one value copied twice.
    /// </summary>
    [System.Serializable]
    public struct BlastProfile
    {
        [Tooltip("Everything inside this radius is caught: damageables take the damage, loose bodies are thrown.")]
        [Sirenix.OdinInspector.PropertyRange(1f, 30f), Sirenix.OdinInspector.SuffixLabel("m", true)]
        public float radius;

        [Tooltip("Impulse handed to a body at the centre of the blast, falling off to nothing at the radius.")]
        [Sirenix.OdinInspector.PropertyRange(0f, 5000f)]
        public float force;

        [Tooltip("Metres the blast's origin is sunk below itself when throwing bodies — the lower it sits, the more the blast lifts rather than shoves.")]
        [Sirenix.OdinInspector.PropertyRange(0f, 5f), Sirenix.OdinInspector.SuffixLabel("m", true)]
        public float upModifier;

        [Tooltip("Normalized damage dealt to every IDamageable caught — 1 is a full NPC health bar (an outright kill). The player's plating scales it (VehicleHealthSettings.playerBlastDamageScale); another barrel detonates at any amount.")]
        [Sirenix.OdinInspector.PropertyRange(0f, 1f)]
        public float damage;

        public static BlastProfile Default => new() { radius = 7f, force = 900f, upModifier = 1.2f, damage = 1f };
    }

    public static class Blast
    {
        /// <summary>One blast, authored as a <see cref="BlastProfile"/>.</summary>
        public static void Apply(Vector3 origin, BlastProfile profile, Rigidbody ignore) =>
            Apply(origin, profile.radius, profile.force, profile.upModifier, profile.damage, ignore);

        /// <summary>One blast. <paramref name="ignore"/> is the exploder's own rigidbody — it is past caring.</summary>
        public static void Apply(Vector3 origin, float radius, float force, float upModifier,
                                 float damage, Rigidbody ignore)
        {
            // OverlapSphere returns colliders, and a car is several of them —
            // fold to rigidbodies so nobody is thrown (or damaged) twice.
            var caught = new HashSet<Rigidbody>();
            foreach (Collider hit in Physics.OverlapSphere(origin, radius))
            {
                if (hit == null) continue; // a nested chain blast may have consumed it
                Rigidbody body = hit.attachedRigidbody;
                if (body == null || body == ignore || !caught.Add(body)) continue;

                foreach (IDamageable damageable in body.GetComponents<IDamageable>())
                    damageable.ApplyDamage(damage);

                if (!body.isKinematic)
                    body.AddExplosionForce(force, origin, radius, upModifier, ForceMode.Impulse);
            }
        }
    }
}
