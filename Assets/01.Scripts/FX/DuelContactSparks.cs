using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// A LOOPING spark emitter for two hulls grinding together — the patrol
    /// duel's tug of war, where the cruiser and the ship are pressed side by
    /// side for seconds at a time. <see cref="SparkleVfx"/> is a one-shot
    /// burst left behind at a point; this one rides with the contact and is
    /// fed every frame: <see cref="SetContact"/> moves it, aims the spray back
    /// along the direction of travel (at speed, sparks stream BEHIND the
    /// contact) and scales the rate with how hard the push is, and
    /// <see cref="Stop"/> lets the last sparks die on their own. Built in code
    /// from the same generated star as the bursts, so it has no asset
    /// dependency; it runs on SCALED time on purpose — an exchange plays at
    /// 30 %, and sparks that slow with the world are the look.
    ///
    /// A per-run object owned by whoever creates it (the patrol parents it to
    /// its root, never to the visual, which is switched off across a kill).
    /// </summary>
    public sealed class DuelContactSparks : MonoBehaviour
    {
        const float MinRate = 40f;
        const float MaxRate = 200f;

        ParticleSystem particles;
        bool emitting;

        /// <summary>Builds the emitter, idle, as a child of <paramref name="parent"/>.</summary>
        public static DuelContactSparks Create(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sparks = go.AddComponent<DuelContactSparks>();
            sparks.Build(color);
            return sparks;
        }

        void Build(Color color)
        {
            particles = gameObject.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem starts playing the moment it is added and
            // refuses duration changes while it does — halt it before configuring.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.gravityModifier = 0.5f;
            main.maxParticles = 400;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = particles.emission;
            emission.rateOverTime = 0f;

            // A cone along local +Z: SetContact points +Z BACK along the travel
            // direction, so the spray streams behind the contact.
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.3f;

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f)));

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.5f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = SparkleVfx.SharedMaterial;
        }

        /// <summary>
        /// Feed the contact: where the hulls meet, which way they are
        /// travelling and how hard the push is (0..1 scales the rate). Call it
        /// every frame the contact lasts; the rate is left where the last call
        /// put it until <see cref="Stop"/>.
        /// </summary>
        public void SetContact(Vector3 worldPosition, Vector3 travelDirection, float intensity01)
        {
            if (particles == null) return;
            transform.position = worldPosition;
            if (travelDirection.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(-travelDirection.normalized);
            var emission = particles.emission;
            emission.rateOverTime = Mathf.Lerp(MinRate, MaxRate, Mathf.Clamp01(intensity01));
            if (!emitting)
            {
                emitting = true;
                particles.Play();
            }
        }

        /// <summary>Stops emitting; the sparks already out die on their own.</summary>
        public void Stop()
        {
            if (particles == null || !emitting) return;
            emitting = false;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
