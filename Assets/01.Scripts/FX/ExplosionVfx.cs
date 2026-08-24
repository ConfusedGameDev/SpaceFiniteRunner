using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The one fireball recipe: a burst of billboards off a randomly-picked
    /// sprite, lifted off the ground and drifting upward. Barrels and dying
    /// cars both blow up through here, so the two explosions stay visually
    /// identical by construction. The spawned system is unparented and set to
    /// destroy itself when the last particle dies — it outlives whatever blew
    /// up (and the chunk, if the street is culled mid-explosion), and nothing
    /// has to remember to clean it up.
    /// </summary>
    public static class ExplosionVfx
    {
        // One material per sprite shared by every blast in the run: built once,
        // kept alive by HideAndDontSave, so a busy chase is not making a
        // material per explosion.
        static readonly Dictionary<Texture, Material> Fireballs = new();

        /// <summary>One blast at <paramref name="origin"/>, sized and paced by the caller's knobs.</summary>
        public static void SpawnFireball(Vector3 origin, IReadOnlyList<Texture2D> textures,
                                         float scale, float lifetime, int particleCount)
        {
            if (textures == null || textures.Count == 0) return;
            Texture2D sprite = textures[Random.Range(0, textures.Count)];
            if (sprite == null) return;

            var go = new GameObject("Explosion");
            go.transform.position = origin + Vector3.up * (scale * 0.25f);

            var particles = go.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem starts playing the moment it is added, and
            // Unity refuses duration changes on a playing system — halt it
            // completely before configuring, then start it by hand at the end.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(scale * 0.2f, scale * 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(scale * 0.5f, scale * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // radians, and the whole circle
            main.gravityModifier = -0.12f;                                          // fire rises
            main.maxParticles = Mathf.Max(1, particleCount);
            main.stopAction = ParticleSystemStopAction.Destroy;                      // cleans itself up

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(particleCount, 1, 200)) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = scale * 0.25f;

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

            particles.Play();
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
