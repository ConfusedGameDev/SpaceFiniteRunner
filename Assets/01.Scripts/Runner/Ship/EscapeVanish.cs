using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The win's exit, Back to the Future style: two fire trails stream off
    /// the ship's wingtips — lit by <see cref="Ignite"/> when a hyperspace
    /// jump engages (so the two burning lines run down the road, up the ramp
    /// and into the air), else from the step the ship leaves an end ramp
    /// with the win (<see cref="Begin"/>, called by the GameManager beside
    /// <c>BeginEscape</c>) — and <see cref="GameSettings.escapeVanishDelaySeconds"/> of
    /// flight later the model vanishes in an electric flash (a spark burst, a
    /// light burst, a glitch pulse) — only the burning lines fly on past the
    /// planted camera. The emitters hang off the ship's ROOT, never its
    /// visual: <see cref="ShipHealth.SetShipVisible"/> switches off every
    /// renderer under the visual, which would take the trails with the model.
    /// Their spots are the barrel-roll ribbons' — the model's measured
    /// half-width (<see cref="BarrelRollTrail.MeasureVisual"/>), a touch
    /// behind the centre. The trails are TrailRenderers (always world space,
    /// so they stay where they burned), plus a world-space flame system each;
    /// materials are built at runtime from the settings' textures, additive.
    /// Scaled time, so a pause freezes it. A launch (restart) stops and clears
    /// everything; the model is handed back by the hull's own reset. Hand-
    /// placed on <c>PF_Ship</c> like the hull; <see cref="Ensure"/> +
    /// <see cref="Configure"/> from the GameManager; inert until configured,
    /// and a no-op while <see cref="GameSettings.escapeVanishEnabled"/> is off.
    /// </summary>
    public class EscapeVanish : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        ShipHealth health;

        TrailRenderer[] trails = System.Array.Empty<TrailRenderer>();
        ParticleSystem[] flames = System.Array.Empty<ParticleSystem>();
        Material trailMaterial;
        Material flameMaterial;
        Texture2D bandTexture; // the built-in fire band, when the settings carry no trail texture

        bool burning;  // the trails are lit
        bool running;  // the win's flight is on: the vanish clock runs
        bool vanished;
        float clock;

        Light flashLight;
        float flashLeft;
        const float FlashSeconds = 0.45f;

        /// <summary>True from the vanish until the next launch: the model is gone, the trails fly on.</summary>
        public bool Vanished => vanished;

        /// <summary>The ship's exit (hand-placed on PF_Ship); added only when the ship has none.</summary>
        public static EscapeVanish Ensure(ShipMotor motor)
        {
            var vanish = motor.GetComponent<EscapeVanish>();
            return vanish != null ? vanish : motor.gameObject.AddComponent<EscapeVanish>();
        }

        /// <summary>Hooks the exit to the run. The emitters are built on the first <see cref="Begin"/>, off the model as it then measures.</summary>
        public void Configure(GameSettings runSettings, ShipMotor ship, ShipHealth hull)
        {
            if (motor != null) motor.Launched -= ResetForRun;
            settings = runSettings;
            motor = ship;
            health = hull;
            if (motor != null) motor.Launched += ResetForRun;
        }

        /// <summary>Lights the two fire trails now (the hyperspace jump engaging); the model stays until <see cref="Begin"/>'s delay. Idempotent.</summary>
        public void Ignite()
        {
            if (settings == null || motor == null || !settings.escapeVanishEnabled || burning) return;
            if (trails.Length == 0) Build();
            burning = true;
            ApplyLook();
            foreach (var trail in trails)
            {
                if (trail == null) continue;
                trail.Clear();
                trail.emitting = true;
            }
            foreach (var flame in flames)
                if (flame != null) flame.Play();
        }

        /// <summary>The ship has left an end ramp with the win: the fire burns (lit now unless a jump already lit it), the model goes after the delay.</summary>
        public void Begin()
        {
            if (settings == null || motor == null || !settings.escapeVanishEnabled || running) return;
            Ignite();
            running = true;
            vanished = false;
            clock = 0f;
        }

        /// <summary>Back to nothing: no fire, no flash, the model visible again (a retry).</summary>
        public void ResetForRun()
        {
            burning = false;
            running = false;
            vanished = false;
            clock = 0f;
            foreach (var trail in trails)
            {
                if (trail == null) continue;
                trail.emitting = false;
                trail.Clear();
            }
            foreach (var flame in flames)
                if (flame != null) flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            EndFlash();
            if (health != null) health.SetShipVisible(true);
        }

        void Update()
        {
            if (flashLeft > 0f)
            {
                flashLeft -= Time.deltaTime;
                if (flashLight != null) flashLight.intensity = settings.escapeFlashLight * Mathf.Clamp01(flashLeft / FlashSeconds);
                if (flashLeft <= 0f) EndFlash();
            }

            if (!running || vanished || motor == null) return;
            clock += Time.deltaTime; // scaled: a pause holds the vanish
            if (clock >= settings.escapeVanishDelaySeconds) Vanish();
        }

        void Vanish()
        {
            vanished = true;
            Vector3 at = motor.Visual != null ? motor.Visual.position : motor.transform.position;
            if (health != null) health.SetShipVisible(false);

            // The electric flash: sparks thrown out of where the ship was, a
            // light burst that lights the road, and a jolt on the picture.
            SparkleVfx.SpawnBurst(at, motor.transform.up, settings.escapeFlashColor, settings.escapeFlashScale, 90);
            SparkleVfx.SpawnBurst(at, -motor.transform.up, Color.white, settings.escapeFlashScale * 0.6f, 50);
            if (settings.escapeFlashLight > 0f)
            {
                if (flashLight == null)
                {
                    var go = new GameObject("EscapeFlash");
                    flashLight = go.AddComponent<Light>();
                    flashLight.type = LightType.Point;
                    flashLight.shadows = LightShadows.None;
                }
                flashLight.transform.position = at;
                flashLight.color = settings.escapeFlashColor;
                flashLight.range = settings.escapeFlashLight * 2f;
                flashLight.intensity = settings.escapeFlashLight;
                flashLight.enabled = true;
                flashLeft = FlashSeconds;
            }
            if (settings.escapeFlashGlitch > 0f && GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.escapeFlashGlitch);
        }

        void EndFlash()
        {
            flashLeft = 0f;
            if (flashLight != null) flashLight.enabled = false;
        }

        // Two emitters at the wingtips, on the ROOT (see the class summary).
        void Build()
        {
            Transform visual = motor.Visual != null ? motor.Visual : motor.transform;
            Bounds bounds = BarrelRollTrail.MeasureVisual(visual);
            ShipSettings shipSettings = motor.ShipSettings;
            float span = shipSettings != null ? shipSettings.barrelRollTrailSpan : 1f;
            float halfWidth = Mathf.Max(bounds.extents.x, 0.25f) * span;
            var anchor = new Vector3(0f, bounds.center.y, bounds.center.z - bounds.extents.z * 0.4f);

            // A trail is ONE stretch of texture over kilometres of line, so the
            // texture must fill it: a sprite with empty margins (the Kenney
            // trace_* sit in the middle of a transparent square) leaves the
            // first stretch behind the ship blank and the rest a hairline.
            Texture trailTexture = settings.escapeTrailTexture != null ? settings.escapeTrailTexture : FireBand();
            trailMaterial = ParticleMaterials.Unlit("EscapeFireTrail", trailTexture, additive: true);
            MakeAdditive(trailMaterial);
            Texture2D flameTexture = settings.escapeFlameTextures != null && settings.escapeFlameTextures.Length > 0
                ? settings.escapeFlameTextures[0] : null;
            if (flameTexture != null)
            {
                flameMaterial = ParticleMaterials.Unlit("EscapeFlame", flameTexture, additive: true);
                MakeAdditive(flameMaterial);
            }

            var left = new System.Collections.Generic.List<TrailRenderer>();
            var fire = new System.Collections.Generic.List<ParticleSystem>();
            foreach (float side in new[] { -1f, 1f })
            {
                var go = new GameObject(side < 0f ? "EscapeFire_L" : "EscapeFire_R");
                go.transform.SetParent(motor.transform, false);
                Vector3 world = visual.TransformPoint(anchor + Vector3.right * (side * halfWidth));
                go.transform.localPosition = motor.transform.InverseTransformPoint(world);

                var trail = go.AddComponent<TrailRenderer>();
                trail.emitting = false;
                trail.minVertexDistance = 1f;
                trail.numCornerVertices = 0;
                trail.numCapVertices = 2;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.generateLightingData = false;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.autodestruct = false;
                trail.sharedMaterial = trailMaterial;
                left.Add(trail);

                if (flameMaterial != null) fire.Add(BuildFlames(go.transform));
            }
            trails = left.ToArray();
            flames = fire.ToArray();
        }

        // A soft band across the trail: a white-hot core fading to nothing at
        // both edges, the same all along it (the gradient colours it).
        Texture2D FireBand()
        {
            if (bandTexture != null) return bandTexture;
            const int size = 64;
            bandTexture = new Texture2D(4, size, TextureFormat.RGBA32, false)
            {
                name = "EscapeFireBand",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < size; y++)
            {
                float across = Mathf.Abs(y / (size - 1f) * 2f - 1f); // 0 centre .. 1 edge
                float glow = Mathf.Exp(-across * across * 5f);       // soft body
                float core = Mathf.Exp(-across * across * 40f);      // hot line down the middle
                float value = Mathf.Clamp01(glow * 0.8f + core);
                var color = new Color(1f, 1f, 1f, value);
                for (int x = 0; x < 4; x++) bandTexture.SetPixel(x, y, color);
            }
            bandTexture.Apply(false, true);
            return bandTexture;
        }

        // URP particles: _Blend 2 is additive (ParticleMaterials writes the
        // premultiply slot for its "additive"); fire wants a true add.
        static void MakeAdditive(Material material)
        {
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.One);
        }

        ParticleSystem BuildFlames(Transform parent)
        {
            var go = new GameObject("Flames");
            go.transform.SetParent(parent, false);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // a fresh system plays at once
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // they stay where they burned
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 600;

            var emission = particles.emission;
            emission.rateOverTime = 160f;
            emission.rateOverDistance = 0.15f; // at Light Speed time alone spaces them ~11 m apart

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;

            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.5f), new GradientColorKey(new Color(0.4f, 0.05f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = flameMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        // The settings are read at each Begin, so the inspector tunes the next exit.
        void ApplyLook()
        {
            foreach (var trail in trails)
            {
                if (trail == null) continue;
                trail.time = settings.escapeTrailSeconds;
                trail.widthMultiplier = settings.escapeTrailWidth;
                // Full width nearly all the way: the OLD end of the line is the
                // stretch on the road beside the planted camera, the part that
                // must read big — only the last tenth burns down.
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0.3f));
                trail.colorGradient = settings.escapeTrailGradient;
            }
            // The flames at half the trails' brightness: a sprite that bright
            // bleaches to white, and the flames should read orange.
            SetTint(trailMaterial, settings.escapeTrailIntensity);
            SetTint(flameMaterial, Mathf.Max(1f, settings.escapeTrailIntensity * 0.5f));
            foreach (var flame in flames)
            {
                if (flame == null) continue;
                var main = flame.main;
                main.startSize = new ParticleSystem.MinMaxCurve(settings.escapeFlameSize * 0.6f, settings.escapeFlameSize);
            }
        }

        static void SetTint(Material material, float intensity)
        {
            if (material == null || !material.HasProperty("_BaseColor")) return;
            Color tint = Color.white * intensity;
            tint.a = 1f;
            material.SetColor("_BaseColor", tint);
        }

        void OnDestroy()
        {
            if (motor != null) motor.Launched -= ResetForRun;
            if (trailMaterial != null) Destroy(trailMaterial);
            if (flameMaterial != null) Destroy(flameMaterial);
            if (bandTexture != null) Destroy(bandTexture);
            if (flashLight != null) Destroy(flashLight.gameObject);
        }
    }
}
