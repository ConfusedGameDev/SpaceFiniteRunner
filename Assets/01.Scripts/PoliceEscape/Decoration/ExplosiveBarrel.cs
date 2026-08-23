using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
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
    /// anything inside the set's blast radius is caught, whoever set it off.
    /// That is what makes a barrel a weapon — leading a cruiser past one is
    /// worth doing, and standing next to the one you just clipped is not. The
    /// player takes <see cref="DecorationSet.explosionDamage"/> on the same
    /// corruption meter police shunts fill; a caught police car is wrecked
    /// outright, and the <see cref="AI.PatrolManager"/>'s next maintenance tick
    /// cuts a replacement in at its spawn band — which is, by definition, away
    /// from the player.
    ///
    /// A barrel fires once. It is destroyed by its own blast, and the fireball
    /// is spawned unparented so it outlives the prop (and the chunk, if the
    /// street is culled behind the player mid-explosion).
    /// </summary>
    public class ExplosiveBarrel : MonoBehaviour
    {
        // Nine fireball sprites shared by every barrel in the run: built once,
        // kept alive by HideAndDontSave, so a busy chase is not making a
        // material per blast.
        static readonly Dictionary<Texture, Material> Fireballs = new();

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

        /// <summary>Blows the barrel now. Public so a scripted beat can set one off without a car.</summary>
        public void Detonate()
        {
            if (spent || set == null) return;
            spent = true;

            Vector3 origin = transform.position;
            SpawnFireball(origin);

            // OverlapSphere returns colliders, and a car is several of them —
            // fold to rigidbodies so nobody is thrown (or wrecked) twice.
            var caught = new HashSet<Rigidbody>();
            foreach (Collider hit in Physics.OverlapSphere(origin, set.blastRadius))
            {
                Rigidbody body = hit.attachedRigidbody;
                if (body == null || !caught.Add(body)) continue;

                // Wrecked cruisers skip the shove — they are about to stop
                // existing, and the fleet is refilled away from the player.
                if (body.GetComponent<AI.PoliceCarInput>() != null)
                {
                    Destroy(body.gameObject);
                    continue;
                }

                if (!body.isKinematic)
                    body.AddExplosionForce(set.blastForce, origin, set.blastRadius,
                                           set.blastUpModifier, ForceMode.Impulse);

                if (body.GetComponent<Vehicles.CarInput>() != null) HurtPlayer();
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// The player caught the blast: corruption on the run's damage meter,
        /// and a hard rumble. A scene with no level flow (the road-kit test
        /// scenes) simply has nothing to damage — the blast stays cosmetic
        /// rather than throwing.
        /// </summary>
        void HurtPlayer()
        {
            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(1f, 0.7f, 0.45f);

            var level = FindAnyObjectByType<LevelManager>();
            if (level != null) level.ApplyDamage(set.explosionDamage, "barrel blast");
            else if (GlitchController.Instance != null) GlitchController.Instance.Pulse(1f);
        }

        /// <summary>
        /// One burst of billboards off a randomly-picked fireball sprite, lifted
        /// to the barrel's middle and drifting upward. It is unparented and set
        /// to destroy itself when the last particle dies, so nothing has to
        /// remember to clean it up.
        /// </summary>
        void SpawnFireball(Vector3 origin)
        {
            if (set.explosionTextures == null || set.explosionTextures.Count == 0) return;
            Texture2D sprite = set.explosionTextures[Random.Range(0, set.explosionTextures.Count)];
            if (sprite == null) return;

            var go = new GameObject("Explosion");
            go.transform.position = origin + Vector3.up * (set.explosionScale * 0.25f);

            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(set.explosionLifetime * 0.6f, set.explosionLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(set.explosionScale * 0.2f, set.explosionScale * 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(set.explosionScale * 0.5f, set.explosionScale * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // radians, and the whole circle
            main.gravityModifier = -0.12f;                                          // fire rises
            main.maxParticles = Mathf.Max(1, set.explosionParticles);
            main.stopAction = ParticleSystemStopAction.Destroy;                      // cleans itself up

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(set.explosionParticles, 1, 200)) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = set.explosionScale * 0.25f;

            // Punch out fast, then keep swelling as it dies — a fireball that
            // only fades reads as a decal, one that grows reads as pressure.
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.3f)));

            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var fireRenderer = go.GetComponent<ParticleSystemRenderer>();
            fireRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            fireRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fireRenderer.receiveShadows = false;
            // Alpha, never additive: these sprites carry their own smoke, and
            // additive would erase the grey and leave a bare fire blob.
            fireRenderer.sharedMaterial = Fireball(sprite);
        }

        static Material Fireball(Texture sprite)
        {
            if (Fireballs.TryGetValue(sprite, out Material cached) && cached != null) return cached;
            Material material = ParticleMaterials.Unlit("Explosion", sprite, additive: false);
            Fireballs[sprite] = material;
            return material;
        }
    }
}
