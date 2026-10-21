using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The LOOPING spark rig where a laser sheet meets the road. Unlike
    /// <see cref="DuelContactSparks"/> (one contact POINT, a column jumping up
    /// out of it) the contact here is a LINE — the sheet's whole bottom edge —
    /// so the sparks are emitted from a thin box the width of the sheet and
    /// sprayed out low: those fired into the road bounce off it and skitter
    /// along it, so the foot of the beam reads as a ring of sparks round the
    /// sheet at floor level. The road is a collision PLANE on the rig (its
    /// <c>Floor</c> child — no colliders, cheap), so it holds on a banked or
    /// sloped road too.
    ///
    /// <b>The look is AUTHORED, never built in code</b>: this sits on the
    /// <c>PF_LaserFloorSparks</c> prefab (nested in <c>PF_LaserSystem</c> at
    /// the sheet's foot), and everything about the sparks — speed, lifetime,
    /// size, colour, gravity, bounce, material — is its ParticleSystem's, so a
    /// designer tunes it in the prefab with the Particle System preview. The
    /// code touches only what depends on the gate: <see cref="Fit"/> stretches
    /// the emitting box's X to the sheet's width and scales the authored Rate
    /// over Time and the glow's range with it (so the density per metre is
    /// what the prefab shows), and <see cref="Play"/> / <see cref="Burst"/> /
    /// <see cref="Stop"/> switch it. The emitter's rate is held at 0 between,
    /// so Play On Awake is harmless.
    ///
    /// Local frame (<see cref="SetPose"/>): X along the sheet's foot, Y the
    /// road's up, Z through the sheet. Scaled time, so a pause freezes it.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class LaserFloorSparks : MonoBehaviour
    {
        [Tooltip("Sparks thrown all at once along the foot when the sheet first hits the road. 0 = no burst.")]
        [SerializeField, PropertyRange(0, 500)] int impactBurstCount = 60;

        [Tooltip("The flickering light at the foot. Optional. Its authored intensity is the peak, and its range grows with the sheet's width like the emitter box.")]
        [SerializeField] Light glow;

        [Tooltip("How fast the glow flickers.")]
        [SerializeField, PropertyRange(0f, 80f), SuffixLabel("Hz", true)] float flickerFrequency = 37f;

        [Tooltip("How far the flicker dips below the peak, 0..1. 0 = a steady light.")]
        [SerializeField, PropertyRange(0f, 1f)] float flickerDepth = 0.25f;

        ParticleSystem sparks;
        float authoredRate, authoredWidth, authoredRange;
        float rate;
        float lightPeak;
        bool emitting;

        void Awake()
        {
            sparks = GetComponent<ParticleSystem>();
            var emission = sparks.emission;
            authoredRate = emission.rateOverTimeMultiplier;
            authoredWidth = Mathf.Max(0.01f, sparks.shape.scale.x);
            rate = authoredRate;
            emission.rateOverTimeMultiplier = 0f;
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (glow != null)
            {
                lightPeak = glow.intensity;
                authoredRange = glow.range;
                glow.intensity = 0f;
                glow.enabled = false;
            }
        }

        /// <summary>
        /// Moves the rig under <paramref name="parent"/> at unit scale (the
        /// laser prefab it is nested in is scaled) and stretches it to a foot
        /// <paramref name="width"/> metres wide: the box's X, the rate and the
        /// glow's range, in proportion to the width it was authored at.
        /// </summary>
        public void Fit(Transform parent, float width)
        {
            if (sparks == null) return;
            transform.SetParent(parent, false);
            transform.localScale = Vector3.one;

            width = Mathf.Max(0.1f, width);
            float k = width / authoredWidth;
            var shape = sparks.shape;
            Vector3 box = shape.scale;
            shape.scale = new Vector3(width, box.y, box.z);
            rate = authoredRate * k;
            if (emitting)
            {
                var emission = sparks.emission;
                emission.rateOverTimeMultiplier = rate;
            }
            if (glow != null) glow.range = authoredRange * k;
        }

        /// <summary>Puts the rig on the foot: X along the sheet's bottom edge, Y the road's up.</summary>
        public void SetPose(Vector3 position, Quaternion rotation) => transform.SetPositionAndRotation(position, rotation);

        /// <summary>Starts the stream (harmless while it runs).</summary>
        public void Play()
        {
            if (sparks == null || emitting) return;
            emitting = true;
            var emission = sparks.emission;
            emission.rateOverTimeMultiplier = rate;
            sparks.Play();
            if (glow != null) glow.enabled = true;
        }

        /// <summary>Throws the impact burst (<c>impactBurstCount</c> sparks) along the whole foot at once.</summary>
        public void Burst()
        {
            if (sparks == null || impactBurstCount <= 0) return;
            if (!sparks.isPlaying) sparks.Play();
            sparks.Emit(Mathf.Min(impactBurstCount, sparks.main.maxParticles));
        }

        /// <summary>Stops emitting; the sparks already out die on their own and the glow goes out with them.</summary>
        public void Stop()
        {
            if (sparks == null || !emitting) return;
            emitting = false;
            var emission = sparks.emission;
            emission.rateOverTimeMultiplier = 0f;
            sparks.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            if (glow == null || !glow.enabled) return;
            if (!emitting)
            {
                glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, lightPeak * 5f * Time.deltaTime);
                if (glow.intensity <= 0f) glow.enabled = false;
                return;
            }
            // A per-frame flicker: real sparks never light evenly.
            glow.intensity = lightPeak * (1f - flickerDepth + flickerDepth * Mathf.PerlinNoise(Time.time * flickerFrequency, 0.41f));
        }
    }
}
