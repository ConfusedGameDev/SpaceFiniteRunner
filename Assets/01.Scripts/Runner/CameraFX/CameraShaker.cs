using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Applies Perlin-noise camera shakes around the camera's rest pose.
    /// Multiple shakes can overlap; their offsets sum. Works on any
    /// transform whose local pose is otherwise static (our follow camera
    /// is parented to the ship root, so its local pose is the rest pose).
    /// </summary>
    public class CameraShaker : MonoBehaviour
    {
        class ActiveShake
        {
            public CameraShakeSettings settings;
            public float time;
            public float seed;
        }

        readonly List<ActiveShake> shakes = new();
        Vector3 restPosition;
        Quaternion restRotation;

        void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        public void Shake(CameraShakeSettings settings)
        {
            if (settings == null) return;
            shakes.Add(new ActiveShake { settings = settings, seed = Random.value * 100f });
        }

        void LateUpdate()
        {
            Vector3 posOffset = Vector3.zero;
            Vector3 rotOffset = Vector3.zero;

            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                var shake = shakes[i];
                shake.time += Time.deltaTime;
                float normalized = shake.time / shake.settings.duration;
                if (normalized >= 1f)
                {
                    shakes.RemoveAt(i);
                    continue;
                }

                float amp = shake.settings.falloff.Evaluate(normalized);
                float t = shake.time * shake.settings.frequency;

                posOffset += new Vector3(Noise(t, shake.seed), Noise(t, shake.seed + 13f), Noise(t, shake.seed + 29f))
                             * (shake.settings.positionAmplitude * amp);
                rotOffset += new Vector3(Noise(t, shake.seed + 41f), Noise(t, shake.seed + 53f), Noise(t, shake.seed + 67f))
                             * (shake.settings.rotationAmplitude * amp);
            }

            transform.localPosition = restPosition + posOffset;
            transform.localRotation = restRotation * Quaternion.Euler(rotOffset);
        }

        static float Noise(float t, float seed) => (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;
    }
}
