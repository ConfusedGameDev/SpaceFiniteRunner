using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The one sparkle recipe: a burst of small additive four-point stars
    /// sprayed in a hemisphere around a surface normal — a touchdown, a
    /// pickup, anything that should glitter for a moment. Art is generated
    /// (a star texture, built once and cached), so the effect has no asset
    /// dependency. The spawned system is unparented and destroys itself when
    /// the last particle dies — at ship speed the burst is left behind at the
    /// touchdown point, which is exactly the receding-sparks read we want.
    /// </summary>
    public static class SparkleVfx
    {
        // Built once, kept alive by HideAndDontSave, shared by every burst.
        static Material sparkleMaterial;
        static Texture2D sparkleTexture;

        /// <summary>
        /// One burst at <paramref name="origin"/>, sprayed around
        /// <paramref name="up"/> (the track's up, so it survives loops and
        /// tubes). <paramref name="scale"/> sizes speed, spread and particle
        /// size together, in metres.
        /// </summary>
        public static void SpawnBurst(Vector3 origin, Vector3 up, Color color, float scale, int particleCount)
        {
            if (particleCount <= 0) return;

            var go = new GameObject("Sparkles");
            go.transform.position = origin;
            // Hemisphere shapes emit around local +Z.
            go.transform.rotation = Quaternion.LookRotation(up == Vector3.zero ? Vector3.up : up);

            var particles = go.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem starts playing the moment it is added, and
            // Unity refuses duration changes on a playing system — halt it
            // completely before configuring, then start it by hand at the end.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(scale * 1.5f, scale * 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(scale * 0.12f, scale * 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // radians, and the whole circle
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.gravityModifier = 0.7f; // sparks arc back down
            main.maxParticles = Mathf.Max(1, particleCount);
            main.stopAction = ParticleSystemStopAction.Destroy; // cleans itself up

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(particleCount, 1, 200)) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = scale * 0.2f;

            // Shrink to nothing rather than fade: an additive star that pops
            // out small reads as a dying spark, a fading one as fog.
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = SparkleMaterial();

            particles.Play();
        }

        static Material SparkleMaterial()
        {
            if (sparkleMaterial != null) return sparkleMaterial;
            // Additive: sparkles are light, and additive over the track glows.
            sparkleMaterial = ParticleMaterials.Unlit("Sparkle", SparkleTexture(), additive: true);
            return sparkleMaterial;
        }

        /// <summary>Soft four-point star — a diamond falloff with a hot round core.</summary>
        static Texture2D SparkleTexture()
        {
            if (sparkleTexture != null) return sparkleTexture;
            const int size = 64;
            sparkleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Sparkle (generated)",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float star = Mathf.Exp(-(Mathf.Abs(u) + Mathf.Abs(v)) * 4.5f); // spikes along the axes
                float core = Mathf.Exp(-(u * u + v * v) * 14f);                // round hot centre
                sparkleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(star, core)));
            }
            sparkleTexture.Apply();
            return sparkleTexture;
        }
    }
}
