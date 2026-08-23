using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Tunable camera shake profile. Make one asset per event type
    /// (boost pad, brake pad, crash, ...) and hand it to a CameraShaker.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraShakeSettings", menuName = "FiniteRunner/Camera Shake Settings")]
    public class CameraShakeSettings : ScriptableObject
    {
        [Tooltip("How long the shake lasts, in seconds.")]
        [Min(0.05f)] public float duration = 0.35f;

        [Tooltip("Maximum positional offset in local units.")]
        [Min(0f)] public float positionAmplitude = 0.35f;

        [Tooltip("Maximum rotational offset in degrees.")]
        [Min(0f)] public float rotationAmplitude = 1.5f;

        [Tooltip("How fast the shake oscillates. Higher = buzzier, lower = heavier.")]
        [Min(0.1f)] public float frequency = 20f;

        [Tooltip("Amplitude over the shake's normalized lifetime (1 at start, 0 at end for a natural decay).")]
        public AnimationCurve falloff = new(
            new Keyframe(0f, 1f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f));
    }
}
