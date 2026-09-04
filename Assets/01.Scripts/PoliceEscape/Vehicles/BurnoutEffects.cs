using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The burnout's juice — tire smoke off the spinning rears plus, on the
    /// built-in backend, a rev loop (EVP mode already flares its live engine
    /// loop, skid audio and tire marks off the real wheel slip, so this
    /// component adds no second engine there). Player-only: added by
    /// CarController.Start behind the same "input is CarInput" gate as
    /// AirTimeSlowMo, and backend-agnostic — it reads only
    /// <see cref="CarController.BurnoutActive"/>, which whichever backend
    /// simulates keeps honest. Everything it builds is code-built at runtime
    /// (the CarHealth plume recipe); the heat ramp mimics EVP's tire-heating,
    /// so smoke pours harder the longer the burnout is held. Constants live
    /// in-file rather than on CarConfig because they are look, not handling.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [DisallowMultipleComponent]
    public class BurnoutEffects : MonoBehaviour
    {
        // Heat: seconds to full smoke while burning, seconds to cool after.
        const float HeatUpSeconds = 1.5f;
        const float CoolDownSeconds = 1f;

        // Smoke particles per second per grounded rear wheel at full heat.
        const float FullHeatEmitRate = 25f;

        // Built-in-backend rev loop envelope (EVP's engine loop owns EVP mode).
        const float RevMinPitch = 1.2f;
        const float RevMaxPitch = 2.2f;
        const float RevMaxVolume = 0.6f;

        CarController car;
        ParticleSystem smoke;
        AudioSource rev;
        float heat;      // 0 cold .. 1 full smoke
        float emitDebt;  // fractional particles carried between frames

        /// <summary>Add the component to a car that has none yet — the CarController.Start hook.</summary>
        public static BurnoutEffects Ensure(CarController car) =>
            car.GetComponent<BurnoutEffects>() ?? car.gameObject.AddComponent<BurnoutEffects>();

        void Awake()
        {
            car = GetComponent<CarController>();
        }

        void Update()
        {
            bool active = car != null && car.BurnoutActive;
            float target = active ? 1f : 0f;
            float rate = active ? 1f / HeatUpSeconds : 1f / CoolDownSeconds;
            heat = Mathf.MoveTowards(heat, target, rate * Time.deltaTime);

            if (active) EmitSmoke();
            UpdateRevLoop();
        }

        void OnDisable()
        {
            // CarHealth strips juice components on a kill — a wreck must not
            // keep revving or smoking off phantom wheelspin.
            heat = 0f;
            if (rev != null && rev.isPlaying) rev.Stop();
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // -------------------------------------------------------------- smoke

        /// <summary>
        /// Manual emission at each grounded rear wheel's contact point — the
        /// emission module stays off because the spawn point moves with the
        /// wheels while the particles simulate in world space and linger as a
        /// cloud behind the car.
        /// </summary>
        void EmitSmoke()
        {
            if (smoke == null) smoke = BuildSmoke();
            // Particles emitted by hand only simulate while the system plays —
            // with the emission module off, playing produces nothing on its own.
            if (!smoke.isPlaying) smoke.Play();

            emitDebt += heat * FullHeatEmitRate * Time.deltaTime;
            int count = Mathf.FloorToInt(emitDebt);
            if (count <= 0) return;
            emitDebt -= count;

            for (int i = 0; i < count; i++)
            {
                WheelCollider wheel = (i & 1) == 0 ? car.rearLeft : car.rearRight;
                if (wheel == null || !wheel.GetGroundHit(out WheelHit hit)) continue;

                var emit = new ParticleSystem.EmitParams
                {
                    position = hit.point,
                    // Up and slightly rearward, plus a share of the body's own
                    // motion so a rolling burnout drags its cloud along.
                    velocity = Vector3.up * Random.Range(0.5f, 1.2f)
                               - transform.forward * Random.Range(0.5f, 1.5f)
                               + car.Velocity * 0.3f
                               + Random.insideUnitSphere * 0.4f,
                    startSize = Random.Range(0.5f, 0.9f) * (0.6f + 0.6f * heat),
                    startLifetime = Random.Range(1f, 1.8f),
                    rotation = Random.Range(0f, 360f),
                };
                smoke.Emit(emit, 1);
            }
        }

        /// <summary>
        /// The CarHealth plume recipe, retuned for ground-level tire smoke:
        /// world-space billboards that grow and fade, emitted by hand rather
        /// than by the emission module.
        /// </summary>
        ParticleSystem BuildSmoke()
        {
            var go = new GameObject("BurnoutSmoke");
            go.transform.SetParent(transform, false);

            var particles = go.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem starts playing the moment it is added —
            // halt it before configuring so no default-material particles slip
            // out (manual Emit works on a stopped system).
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.03f; // smoke drifts up
            main.maxParticles = 128;

            var emission = particles.emission;
            emission.enabled = false; // EmitSmoke drives it by hand

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));

            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f),
                        new GradientAlphaKey(0.35f, 0.5f),
                        new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var smokeRenderer = go.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            smokeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smokeRenderer.receiveShadows = false;
            smokeRenderer.sharedMaterial = SmokeMaterial();
            return particles;
        }

        static Material smokeMaterial;

        static Material SmokeMaterial()
        {
            if (smokeMaterial != null) return smokeMaterial;
            List<Texture2D> textures = VehicleHealthSettings.Load().lightSmokeTextures;
            Texture2D sprite = textures != null && textures.Count > 0
                ? textures[Random.Range(0, textures.Count)]
                : null;
            smokeMaterial = ParticleMaterials.Unlit("BurnoutSmoke", sprite, additive: false);
            return smokeMaterial;
        }

        // ---------------------------------------------------------- rev audio

        /// <summary>
        /// Built-in backend only: the built-in sim has no engine audio at all,
        /// so the burnout brings its own rev loop, pitched up with the heat.
        /// In EVP mode VehicleAudio owns the engine — its RPM averages the
        /// drive wheels, which during a burnout are exactly the spinning
        /// rears, so the flare is already audible and a second source would
        /// double it.
        /// </summary>
        void UpdateRevLoop()
        {
            if (car.UsingEvp || heat <= 0f)
            {
                if (rev != null && rev.isPlaying) rev.Stop();
                return;
            }

            if (rev == null)
            {
                var settings = VehiclePhysicsSettings.Current;
                AudioClip clip = settings.burnoutClip != null ? settings.burnoutClip : settings.engineClip;
                if (clip == null) return; // nothing wired — spin and smoke carry the effect

                // The EvpCarBackend.MakeSource numbers, so the loop sits in the
                // same mix as the EVP car sounds and ducks with the pause snapshot.
                var go = new GameObject("BurnoutRev");
                go.transform.SetParent(transform, false);
                rev = go.AddComponent<AudioSource>();
                rev.clip = clip;
                rev.playOnAwake = false;
                rev.loop = true;
                rev.spatialBlend = 1f;
                rev.minDistance = 1f;
                rev.maxDistance = 110f;
                rev.outputAudioMixerGroup = GameAudio.Fx;
            }

            // The heat decays after release, so the loop tapers out through
            // pitch and volume before stopping instead of cutting hard.
            if (!rev.isPlaying) rev.Play();
            rev.pitch = Mathf.Lerp(RevMinPitch, RevMaxPitch, heat);
            rev.volume = Mathf.Lerp(0f, RevMaxVolume, Mathf.Clamp01(heat * 2f));
        }
    }
}
