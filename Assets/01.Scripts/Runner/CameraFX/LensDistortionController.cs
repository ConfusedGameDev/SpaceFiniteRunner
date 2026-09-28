using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Drives the URP Lens Distortion post effect as a one-shot envelope:
    /// <see cref="Trigger"/> slams the intensity to the max value and the
    /// animation curve brings it back to the default over the duration —
    /// the boost-orb "warp" kick. Works on the scene's global Volume
    /// (auto-found when not wired); the override is added to the volume's
    /// RUNTIME profile copy, so the shared profile asset is never touched.
    /// Singleton like FloatingTextSystem — auto-created on first use, but
    /// pre-place one to wire a specific volume or tune the envelope.
    /// Runs on scaled time, so the effect freezes with the pause menu.
    /// A HELD layer sits under the kicks: <see cref="SetHeld"/> moves the
    /// resting intensity from <see cref="defaultIntensity"/> toward another
    /// value by a 0..1 blend (the runner's Light Speed warp), and every kick
    /// rises from — and settles back to — that rest. Blend 0 is the authored
    /// default exactly.
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

        [Tooltip("Volume carrying the Lens Distortion override. Left empty, the first global Volume in the scene is used (and the override added to its runtime profile if missing).")]
        public Volume volume;

        [TitleGroup("Envelope")]
        [Tooltip("Intensity the lens rests at (and returns to after each kick).")]
        [PropertyRange(-1f, 1f)]
        public float defaultIntensity;

        [TitleGroup("Envelope")]
        [Tooltip("Intensity the lens jumps to on Trigger.")]
        [PropertyRange(-1f, 1f)]
        public float maxIntensity = 1f;

        [TitleGroup("Envelope")]
        [Tooltip("How long one kick takes to settle back to the default.")]
        [PropertyRange(0.05f, 3f), SuffixLabel("s", true)]
        public float duration = 0.6f;

        [TitleGroup("Envelope")]
        [Tooltip("Normalized time → blend between default (0) and max (1). Starts at 1 — the grab slams to max — and falls to 0.")]
        public AnimationCurve envelope = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        LensDistortion lens;
        float timer;
        float peak; // this kick's top, set by Trigger
        float heldBlend; // SetHeld: 0 = defaultIntensity, 1 = heldIntensity
        float heldIntensity;

        /// <summary>Where the lens rests this frame: the default, moved toward the held value by its blend.</summary>
        public float RestIntensity => Mathf.Lerp(defaultIntensity, heldIntensity, heldBlend);

        /// <summary>
        /// The held layer: the lens rests at <paramref name="blend"/> of the
        /// way from <see cref="defaultIntensity"/> to <paramref name="intensity"/>
        /// (clamped to the effect's -1..1). Call with blend 0 to hand the rest
        /// back to the default.
        /// </summary>
        public void SetHeld(float blend, float intensity)
        {
            heldBlend = Mathf.Clamp01(blend);
            heldIntensity = Mathf.Clamp(intensity, -1f, 1f);
        }

        void Awake()
        {
            timer = float.MaxValue; // idle until the first Trigger
            if (volume == null)
            {
                foreach (Volume candidate in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                {
                    if (!candidate.isGlobal) continue;
                    volume = candidate;
                    break;
                }
            }
            if (volume == null)
            {
                volume = new GameObject("LensDistortionVolume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            }

            // volume.profile is the instantiated runtime copy — mutating it
            // never dirties the shared profile asset on disk.
            if (!volume.profile.TryGet(out lens))
                lens = volume.profile.Add<LensDistortion>();
            lens.active = true;
            lens.intensity.overrideState = true;
            lens.intensity.value = defaultIntensity;
        }

        /// <summary>Restart the kick: intensity jumps to max and the curve settles it back to the default.</summary>
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
            peak = Mathf.Clamp(defaultIntensity + (maxIntensity - defaultIntensity) * Mathf.Max(0f, peakScale), -1f, 1f);
        }

        void Update()
        {
            if (lens == null) return;
            float rest = RestIntensity;
            float value = rest;
            if (timer < duration)
            {
                timer += Time.deltaTime;
                // a kick never pulls the lens back past a held rest that is already beyond its peak
                float top = maxIntensity >= defaultIntensity ? Mathf.Max(peak, rest) : Mathf.Min(peak, rest);
                value = Mathf.Lerp(rest, top, envelope.Evaluate(Mathf.Clamp01(timer / duration)));
            }
            lens.intensity.value = value;
        }

        void OnDisable()
        {
            if (lens != null) lens.intensity.value = defaultIntensity;
        }
    }
}
