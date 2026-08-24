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
    public static class Blast
    {
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
