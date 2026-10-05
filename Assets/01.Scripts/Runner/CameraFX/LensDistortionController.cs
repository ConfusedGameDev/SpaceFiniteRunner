using ConfusedGameDev.FiniteRunner.FX;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Drives the URP Lens Distortion post effect as a one-shot envelope:
    /// <see cref="Trigger"/> slams the intensity to the max value and the
    /// animation curve brings it back to rest over the duration — the
    /// boost-orb "warp" kick. It never touches the Volume: the value goes to
    /// the <see cref="PostProcessManager"/> as a request, which clamps it to
    /// the effect's band and drops it while runtime adjustments are off.
    /// The lens RESTS at the manager's baseline (the Volume profile's own
    /// lens intensity), and while it rests no request is held at all.
    /// Singleton like FloatingTextSystem — auto-created on first use, but
    /// pre-place one to tune the envelope.
    /// Runs on scaled time, so the effect freezes with the pause menu.
    /// A HELD layer sits under the kicks: <see cref="SetHeld"/> moves the
    /// resting intensity from the baseline toward another value by a 0..1
    /// blend (the runner's Light Speed warp), and every kick rises from — and
    /// settles back to — that rest. Blend 0 is the baseline exactly.
    /// </summary>
    public class LensDistortionController : MonoBehaviour
    {
        static LensDistortionController instance;

        public static LensDistortionController Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<LensDistortionController>();
                    if (instance == null)
                        instance = new GameObject("LensDistortionController").AddComponent<LensDistortionController>();
                }
                return instance;
            }
        }

        [TitleGroup("Envelope")]
        [Tooltip("Intensity the lens jumps to on Trigger. The post-processing manager clamps it to the lens band.")]
        [PropertyRange(-1f, 1f)]
        public float maxIntensity = 1f;

        [TitleGroup("Envelope")]
        [Tooltip("How long one kick takes to settle back to rest.")]
        [PropertyRange(0.05f, 3f), SuffixLabel("s", true)]
        public float duration = 0.6f;

        [TitleGroup("Envelope")]
        [Tooltip("Normalized time → blend between rest (0) and max (1). Starts at 1 — the grab slams to max — and falls to 0.")]
        public AnimationCurve envelope = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        float timer = float.MaxValue; // idle until the first Trigger
        float peak; // this kick's top, set by Trigger
        float heldBlend; // SetHeld: 0 = the baseline, 1 = heldIntensity
        float heldIntensity;

        float Baseline => PostProcessManager.Baseline(this, PostEffect.LensDistortion);

        /// <summary>Where the lens rests this frame: the baseline, moved toward the held value by its blend.</summary>
        public float RestIntensity => Mathf.Lerp(Baseline, heldIntensity, heldBlend);

        /// <summary>
        /// The held layer: the lens rests at <paramref name="blend"/> of the
        /// way from the baseline to <paramref name="intensity"/> (clamped to
        /// the effect's -1..1). Call with blend 0 to hand the rest back to
        /// the baseline.
        /// </summary>
        public void SetHeld(float blend, float intensity)
        {
            heldBlend = Mathf.Clamp01(blend);
            heldIntensity = Mathf.Clamp(intensity, -1f, 1f);
        }

        /// <summary>Restart the kick: intensity jumps to max and the curve settles it back to rest.</summary>
        [TitleGroup("Actions")]
        [Button("Trigger", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
        public void Trigger() => Trigger(1f);

        /// <summary>
        /// Restart the kick with its peak scaled: 1 = <see cref="maxIntensity"/>,
        /// above 1 pushes past it (clamped to the effect's -1..1) — the boost
        /// QTE's graded warp.
        /// </summary>
        public void Trigger(float peakScale)
        {
            timer = 0f;
            float baseline = Baseline;
            peak = Mathf.Clamp(baseline + (maxIntensity - baseline) * Mathf.Max(0f, peakScale), -1f, 1f);
        }

        void Update()
        {
            bool kicking = timer < duration;
            if (!kicking && heldBlend <= 0f)
            {
                // At rest: no request, so the baseline shows and stays tunable.
                PostProcessManager.Clear(this, PostEffect.LensDistortion);
                return;
            }

            float rest = RestIntensity;
            float value = rest;
            if (kicking)
            {
                timer += Time.deltaTime;
                // a kick never pulls the lens back past a held rest that is already beyond its peak
                float top = maxIntensity >= Baseline ? Mathf.Max(peak, rest) : Mathf.Min(peak, rest);
                value = Mathf.Lerp(rest, top, envelope.Evaluate(Mathf.Clamp01(timer / duration)));
            }
            PostProcessManager.Set(this, PostEffect.LensDistortion, value);
        }

        void OnDisable() => PostProcessManager.Clear(this, PostEffect.LensDistortion);
    }
}
