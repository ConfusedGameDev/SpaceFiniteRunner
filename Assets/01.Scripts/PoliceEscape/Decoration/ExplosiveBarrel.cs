using ConfusedGameDev.FiniteRunner.FX;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// A street prop that answers a car with a blast instead of a shove. It is
    /// an ordinary <see cref="DecorationProp"/> underneath — same wake trigger,
    /// same mass rules — with this bolted on by
    /// <see cref="DecorationProp.Configure"/> when the definition is flagged
    /// explosive.
    ///
    /// Detonation is deliberately POSITIONAL rather than about who touched it:
    /// the shared <see cref="Blast"/> catches everything inside the set's
    /// radius, whoever set it off. That is what makes a barrel a weapon —
    /// leading a cruiser past one is worth doing, and standing next to the
    /// one you just clipped is not. Every <see cref="IDamageable"/> caught
    /// takes <see cref="DecorationSet.blastDamage"/>: an NPC car dies through
    /// its <see cref="Vehicles.CarHealth"/> — it stops, burns its fuse and
    /// explodes in turn instead of vanishing — the player takes it scaled on
    /// the corruption meter, and another barrel detonates too (the barrel IS
    /// an IDamageable whose answer to any damage is to go off), so barrels
    /// chain. The <see cref="AI.PatrolManager"/>'s next maintenance tick cuts
    /// a wrecked cruiser's replacement in at its spawn band, which is by
    /// definition away from the player.
    ///
    /// A barrel fires once. It is destroyed by its own blast, and the fireball
    /// (shared with dying cars via <see cref="ExplosionVfx"/>) is spawned
    /// unparented so it outlives the prop (and the chunk, if the street is
    /// culled behind the player mid-explosion).
    /// </summary>
    public class ExplosiveBarrel : MonoBehaviour, IDamageable
    {
        DecorationSet set;
        bool spent;

        /// <summary>Arms a freshly configured prop. Called by <see cref="DecorationProp.Configure"/>, never by hand.</summary>
        public static void Configure(GameObject instance, DecorationSet set)
        {
            instance.AddComponent<ExplosiveBarrel>().set = set;
        }

        /// <summary>
        /// Any car, at speed, sets it off — the player's, a cruiser's, a
        /// civilian's. The speed floor is what keeps traffic brushing a barrel
        /// in the gutter from levelling the street.
        /// </summary>
        void OnCollisionEnter(Collision collision)
        {
            if (spent || set == null) return;

            Rigidbody other = collision.rigidbody;
            if (other == null || other.GetComponent<Vehicles.CarController>() == null) return;
            if (collision.relativeVelocity.magnitude < set.detonationSpeed) return;
            Detonate();
        }

        /// <summary>
        /// A barrel's answer to ANY damage is to go off — it has no health
        /// bar. This is what lets one blast set off the next: barrels chain.
        /// </summary>
        public void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;
            Detonate();
        }

        /// <summary>Blows the barrel now. Public so a scripted beat can set one off without a car.</summary>
        public void Detonate()
        {
            if (spent || set == null) return;
            spent = true;

            Vector3 origin = transform.position;
            ExplosionVfx.SpawnFireball(origin, set.explosionTextures,
                set.explosionScale, set.explosionLifetime, set.explosionParticles);

            Blast.Apply(origin, set.blastRadius, set.blastForce,
                        set.blastUpModifier, set.blastDamage, GetComponent<Rigidbody>());

            Destroy(gameObject);
        }
    }
}
