using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// A damaged-hull smoke plume: an opening burst plus a short stream of
    /// dark billboards pouring off whatever it is attached to. The system is
    /// parented to the <c>follow</c> transform and simulates in ITS local
    /// space, so the plume stays on the hull at any speed — a world-space
    /// trail would be left kilometres behind a ship doing Light Speed and
    /// never be seen. The puffs drift backwards and up in that frame, which
    /// reads as smoke streaming off a moving vehicle. Destroys itself when
    /// the last puff dies.
    /// </summary>
    public static class SmokeVfx
    {
        // One material per sprite shared by every plume, like the fireballs.
        static readonly Dictionary<Texture, Material> Materials = new();

        /// <summary>
        /// A plume on <paramref name="follow"/> at <paramref name="localOffset"/>,
        /// streaming for <paramref name="seconds"/> at <paramref name="rate"/>
        /// puffs a second after an opening burst of <paramref name="burst"/>.
        /// </summary>
        public static void SpawnTrail(Transform follow, Vector3 localOffset, IReadOnlyList<Texture2D> textures,
                                      float scale, float seconds, float rate, int burst)
        {
            if (follow == null || textures == null || textures.Count == 0) return;
            Texture2D sprite = textures[Random.Range(0, textures.Count)];
            if (sprite == null) return;

            var go = new GameObject("Smoke");
            go.transform.SetParent(follow, false);
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.identity;

            var particles = go.AddComponent<ParticleSystem>();
            // A fresh system is already playing, and a playing one refuses a
            // duration change — stop it, configure, then start it by hand.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float lifetime = Mathf.Max(0.2f, scale * 0.12f);
            var main = particles.main;
            main.duration = Mathf.Max(0.05f, seconds);
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // glued to the hull, see the summary
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(scale * 0.1f, scale * 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(scale * 0.35f, scale * 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.55f, 0.55f), new Color(0.8f, 0.8f, 0.8f));
            main.maxParticles = Mathf.Max(8, burst + Mathf.CeilToInt(rate * seconds) + 4);
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = particles.emission;
            emission.rateOverTime = rate;
            if (burst > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(burst, 1, 200)) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = scale * 0.15f;

            // Streams backwards off the hull and rises as it goes.
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(scale * 0.1f, scale * 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(-scale * 1.2f, -scale * 0.6f);

            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.8f)));

            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.1f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var smokeRenderer = go.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            smokeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smokeRenderer.receiveShadows = false;
            smokeRenderer.sharedMaterial = Material(sprite); // alpha blend: additive would erase dark smoke

            particles.Play();
        }

        static Material Material(Texture sprite)
        {
            if (Materials.TryGetValue(sprite, out Material cached) && cached != null) return cached;
            Material material = ParticleMaterials.Unlit("Smoke", sprite, additive: false);
            Materials[sprite] = material;
            return material;
        }
    }
}
