using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The LOOPING spark rig where a laser sheet meets the road. Unlike
    /// <see cref="DuelContactSparks"/> (one contact POINT, a column jumping up
    /// out of it) the contact here is a LINE — the sheet's whole bottom edge —
    /// so the sparks are emitted from a thin box the width of the sheet and
    /// sprayed out in every direction, low: those fired into the road bounce
    /// off it and skitter along it, so the foot of the beam reads as a ring of
    /// sparks round the sheet at floor level, not a jet in its middle. The
    /// road is a collision PLANE on the rig (no colliders, cheap), so it holds
    /// on a banked or sloped road too.
    ///
    /// Local frame (<see cref="SetPose"/>): X along the sheet's foot, Y the
    /// road's up, Z through the sheet. <see cref="Play"/> starts the stream,
    /// <see cref="Burst"/> throws a handful at once (the touch), and
    /// <see cref="Stop"/> lets the last sparks die on their own. Built in code
    /// from the shared star (<see cref="SparkleVfx.SharedMaterial"/>), on
    /// SCALED time, so a pause freezes it. A per-run object owned by whoever
    /// creates it (the gate parents it to its root, culled with it).
    /// </summary>
    public sealed class LaserFloorSparks : MonoBehaviour
    {
        /// <summary>The rig's look, handed in once at build (the gate's definition owns the knobs).</summary>
        public struct Settings
        {
            /// <summary>Sparks per second per metre of the sheet's width.</summary>
            public float ratePerMeter;
            /// <summary>Start speed (min, max), m/s, in a random direction.</summary>
            public Vector2 speed;
            /// <summary>Lifetime (min, max), seconds.</summary>
            public Vector2 lifetime;
            /// <summary>Spark size (min, max), metres.</summary>
            public Vector2 size;
            /// <summary>Multiplier on gravity: how fast they fall back to the road.</summary>
            public float gravity;
            /// <summary>Share of its speed a spark keeps off the road, 0..1.</summary>
            public float bounce;
            /// <summary>Depth of the emitting box through the sheet, metres.</summary>
            public float depth;
            /// <summary>Peak intensity of the glow light at the foot; 0 = no light.</summary>
            public float lightIntensity;
        }

        const int MaxParticles = 1500;

        ParticleSystem sparks;
        Light glow;
        float rate;
        float lightPeak;
        bool emitting;

        /// <summary>
        /// Builds the rig, idle, as a child of <paramref name="parent"/>, for a
        /// foot <paramref name="width"/> metres wide.
        /// </summary>
        public static LaserFloorSparks Create(Transform parent, string name, Color color, float width, in Settings settings)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<LaserFloorSparks>();
            rig.Build(color, Mathf.Max(0.1f, width), settings);
            return rig;
        }

        void Build(Color color, float width, in Settings settings)
        {
            rate = Mathf.Max(0f, settings.ratePerMeter) * width;
            lightPeak = Mathf.Max(0f, settings.lightIntensity);

            sparks = gameObject.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem starts playing the moment it is added and
            // refuses duration changes while it does — halt it before configuring.
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Min(settings.lifetime.x, settings.lifetime.y), Mathf.Max(settings.lifetime.x, settings.lifetime.y));
            main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Min(settings.speed.x, settings.speed.y), Mathf.Max(settings.speed.x, settings.speed.y));
            main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Min(settings.size.x, settings.size.y), Mathf.Max(settings.size.x, settings.size.y));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // White-hot cores with the tint on the tails, like the duel's.
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.35f), Color.white);
            main.gravityModifier = settings.gravity;
            main.maxParticles = MaxParticles;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = sparks.emission;
            emission.rateOverTime = 0f;

            // The foot line: a thin box along X, every spark leaving it in a
            // random direction — half of them into the road, which throws them
            // back out low and wide.
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.05f, Mathf.Max(0.05f, settings.depth));
            shape.randomDirectionAmount = 1f;

            // The road: a plane through the foot, normal the rig's up.
            var floor = new GameObject("Floor").transform;
            floor.SetParent(transform, false);
            var collision = sparks.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, floor);
            collision.bounce = Mathf.Clamp01(settings.bounce);
            collision.dampen = 0.25f;
            collision.lifetimeLoss = 0.15f;
            collision.radiusScale = 0.1f;

            var sizeOverLife = sparks.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f)));

            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 2f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = SparkleVfx.SharedMaterial;

            if (lightPeak > 0f)
            {
                var lamp = new GameObject("Glow");
                lamp.transform.SetParent(transform, false);
                lamp.transform.localPosition = Vector3.up * 1f;
                glow = lamp.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = Color.Lerp(color, Color.white, 0.45f);
                glow.range = width;
                glow.intensity = 0f;
                glow.shadows = LightShadows.None;
                glow.enabled = false;
            }
        }

        /// <summary>Puts the rig on the foot: X along the sheet's bottom edge, Y the road's up.</summary>
        public void SetPose(Vector3 position, Quaternion rotation) => transform.SetPositionAndRotation(position, rotation);

        /// <summary>Starts the stream (harmless while it runs).</summary>
        public void Play()
        {
            if (sparks == null || emitting) return;
            emitting = true;
            var emission = sparks.emission;
            emission.rateOverTime = rate;
            sparks.Play();
            if (glow != null) glow.enabled = true;
        }

        /// <summary>Throws <paramref name="count"/> sparks at once along the whole foot.</summary>
        public void Burst(int count)
        {
            if (sparks == null || count <= 0) return;
            if (!sparks.isPlaying) sparks.Play();
            sparks.Emit(Mathf.Min(count, MaxParticles));
        }

        /// <summary>Stops emitting; the sparks already out die on their own and the glow goes out with them.</summary>
        public void Stop()
        {
            if (sparks == null || !emitting) return;
            emitting = false;
            var emission = sparks.emission;
            emission.rateOverTime = 0f;
            sparks.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            if (glow == null || !glow.enabled) return;
            if (!emitting)
            {
                glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, 40f * Time.deltaTime);
                if (glow.intensity <= 0f) glow.enabled = false;
                return;
            }
            // A per-frame flicker: real sparks never light evenly.
            glow.intensity = lightPeak * (0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 37f, 0.41f));
        }
    }
}
