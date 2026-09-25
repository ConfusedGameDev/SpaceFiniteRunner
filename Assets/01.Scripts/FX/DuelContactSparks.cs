using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The LOOPING spark rig for two hulls grinding together — the patrol
    /// duel's tug of war, where the cruiser and the ship are pressed side by
    /// side for seconds at a time. <see cref="SparkleVfx"/> is a one-shot
    /// burst left behind at a point; this one rides with the contact and is
    /// fed every frame. The look it is after is the classic cinematic side
    /// swipe: a bright seam between the hulls, a stream of sparks dragged back
    /// along the road, and a COLUMN of sparks that keeps jumping UP out of the
    /// contact point, lighting both cars from between them. Three parts:
    ///
    ///  - the <b>grind</b>: a cone streaming BACK along the direction of travel
    ///    (at speed, sparks stream behind the contact), its rate scaling with
    ///    how hard the push is;
    ///  - the <b>fountain</b>: long stretched streaks sprayed UP and out of the
    ///    seam — a steady trickle while in contact, and a <see cref="Slam"/>
    ///    burst every time the cruiser's lateral hit lands, which is what makes
    ///    the sparks "keep jumping out" instead of hissing evenly;
    ///  - the <b>glow</b>: a point light at the seam, flickering with the rate
    ///    and flaring on each slam, so the sparks light the hulls and the road.
    ///
    /// <see cref="SetContact"/> moves and aims the rig, <see cref="Slam"/> fires
    /// a burst, and <see cref="Stop"/> lets the last sparks die on their own.
    /// Built in code from the same generated star as the bursts, so it has no
    /// asset dependency; it runs on SCALED time on purpose — an exchange plays
    /// at 30 %, and sparks that slow with the world are the look.
    ///
    /// A per-run object owned by whoever creates it (the patrol parents it to
    /// its root, never to the visual, which is switched off across a kill).
    /// </summary>
    public sealed class DuelContactSparks : MonoBehaviour
    {
        const float GrindMinRate = 40f;
        const float GrindMaxRate = 220f;
        const float FountainMinRate = 25f;
        const float FountainMaxRate = 110f;
        const float LightRange = 18f;
        const float LightBase = 3f;       // URP point-light intensity at zero push
        const float LightPush = 9f;       // added at full push
        const float LightFlare = 16f;     // added by a full-strength slam, decaying
        const float FlareSeconds = 0.3f;  // scaled: the flare drifts down with the slowed world
        const int SlamMinBurst = 40;
        const int SlamMaxBurst = 120;

        ParticleSystem grind;
        ParticleSystem fountain;
        Light glow;
        bool emitting;
        float scale = 1f;
        float intensity;   // the last push fed, 0..1
        float flare;       // slam heat still on the light, 0..1
        Color lightColor;

        /// <summary>
        /// Builds the rig, idle, as a child of <paramref name="parent"/>.
        /// <paramref name="scale"/> sizes rate, spark size and light together
        /// (1 = the authored look).
        /// </summary>
        public static DuelContactSparks Create(Transform parent, string name, Color color, float scale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sparks = go.AddComponent<DuelContactSparks>();
            sparks.Build(color, Mathf.Max(0.1f, scale));
            return sparks;
        }

        void Build(Color color, float rigScale)
        {
            scale = rigScale;
            lightColor = Color.Lerp(color, Color.white, 0.45f);

            // The grind rides the root: SetContact points the root's +Z BACK
            // along the travel direction, so its cone streams behind the contact.
            grind = gameObject.AddComponent<ParticleSystem>();
            Configure(grind, color,
                      lifetime: new Vector2(0.25f, 0.6f),
                      speed: new Vector2(8f, 25f) * Mathf.Sqrt(scale),
                      size: new Vector2(0.15f, 0.4f) * scale,
                      coneAngle: 28f, coneRadius: 0.3f, gravity: 0.5f,
                      lengthScale: 1.5f, velocityScale: 0.04f, maxParticles: 400);

            // The fountain sits on a child turned so ITS +Z is the root's +Y:
            // SetContact keeps the root's up on the track's up, so the column
            // stands off the road through loops and tubes as well.
            var column = new GameObject("Fountain");
            column.transform.SetParent(transform, false);
            column.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            fountain = column.AddComponent<ParticleSystem>();
            Configure(fountain, color,
                      lifetime: new Vector2(0.4f, 0.9f),
                      speed: new Vector2(14f, 34f) * Mathf.Sqrt(scale),
                      size: new Vector2(0.12f, 0.32f) * scale,
                      coneAngle: 22f, coneRadius: 0.35f, gravity: 1.2f,
                      lengthScale: 3f, velocityScale: 0.06f, maxParticles: 600);

            // The glow: a point light between the hulls. No shadows — it is a
            // flicker, not a lamp, and it lives for seconds at a time.
            var lamp = new GameObject("Glow");
            lamp.transform.SetParent(transform, false);
            glow = lamp.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = lightColor;
            glow.range = LightRange * Mathf.Sqrt(scale);
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
            glow.enabled = false;
        }

        static void Configure(ParticleSystem particles, Color color, Vector2 lifetime, Vector2 speed, Vector2 size,
                              float coneAngle, float coneRadius, float gravity,
                              float lengthScale, float velocityScale, int maxParticles)
        {
            // A fresh ParticleSystem starts playing the moment it is added and
            // refuses duration changes while it does — halt it before configuring.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // A hot white core with the tint on the tails: most sparks roll
            // toward white, so the seam reads as white-hot, not coloured fog.
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.35f), Color.white);
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = particles.emission;
            emission.rateOverTime = 0f;

            // A cone along local +Z.
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = coneRadius;

            var sizeOverLife = particles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f)));

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = lengthScale;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = SparkleVfx.SharedMaterial;
        }

        /// <summary>
        /// Feed the contact: where the hulls meet, which way they are
        /// travelling, the track's up there, and how hard the push is (0..1
        /// scales the rates and the glow). Call it every frame the contact
        /// lasts; the rates are left where the last call put them until
        /// <see cref="Stop"/>.
        /// </summary>
        public void SetContact(Vector3 worldPosition, Vector3 travelDirection, Vector3 up, float intensity01)
        {
            if (grind == null) return;
            transform.position = worldPosition;
            if (travelDirection.sqrMagnitude > 1e-6f)
            {
                Vector3 upHint = up.sqrMagnitude > 1e-6f ? up : Vector3.up;
                transform.rotation = Quaternion.LookRotation(-travelDirection.normalized, upHint);
            }
            intensity = Mathf.Clamp01(intensity01);
            var grindEmission = grind.emission;
            grindEmission.rateOverTime = Mathf.Lerp(GrindMinRate, GrindMaxRate, intensity) * scale;
            var fountainEmission = fountain.emission;
            fountainEmission.rateOverTime = Mathf.Lerp(FountainMinRate, FountainMaxRate, intensity) * scale;
            if (!emitting)
            {
                emitting = true;
                grind.Play();
                fountain.Play();
                glow.enabled = true;
            }
        }

        /// <summary>
        /// A lateral hit landed: a burst of the column jumps out of the seam
        /// and the glow flares. <paramref name="strength01"/> sizes both.
        /// Harmless while stopped — a slam with no contact spawns nothing.
        /// </summary>
        public void Slam(float strength01)
        {
            if (fountain == null || !emitting) return;
            float strength = Mathf.Clamp01(strength01);
            int count = Mathf.RoundToInt(Mathf.Lerp(SlamMinBurst, SlamMaxBurst, strength) * scale);
            fountain.Emit(Mathf.Clamp(count, 1, 400));
            flare = Mathf.Max(flare, Mathf.Lerp(0.5f, 1f, strength));
        }

        /// <summary>Stops emitting; the sparks already out die on their own and the glow goes out with them.</summary>
        public void Stop()
        {
            if (grind == null || !emitting) return;
            emitting = false;
            var grindEmission = grind.emission;
            grindEmission.rateOverTime = 0f;
            var fountainEmission = fountain.emission;
            fountainEmission.rateOverTime = 0f;
            grind.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            fountain.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            flare = 0f;
        }

        void Update()
        {
            if (glow == null) return;
            if (!emitting)
            {
                // Fade the lamp with the last sparks rather than cutting it.
                if (glow.enabled)
                {
                    glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, 40f * Time.deltaTime);
                    if (glow.intensity <= 0f) glow.enabled = false;
                }
                return;
            }
            if (flare > 0f) flare = Mathf.Max(0f, flare - Time.deltaTime / FlareSeconds);
            // A per-frame flicker: real sparks never light evenly.
            float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 37f, 0.17f);
            glow.intensity = ((LightBase + LightPush * intensity) * flicker + LightFlare * flare) * scale;
        }
    }
}
