using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Cameras
{
    /// <summary>
    /// The camera-shake bank: the one entry point gameplay fires shakes at
    /// (pad hits, side hits, landings), reading the same
    /// <see cref="CameraShakeSettings"/> assets the runner's old
    /// <c>CameraShaker</c> did, so the feel carries over untouched. It is a
    /// plain static class — no Cinemachine type in sight — so a caller's
    /// assembly needs no Cinemachine reference; the offsets are applied to
    /// the picture by <see cref="CinemachineCameraShake"/>, the extension the
    /// <see cref="OrbitCameraRig"/> puts on each of its vcams. The bank is
    /// ticked once per frame however many vcams sample it, so a blend
    /// between the orbit and the first-person view shakes as one picture; a
    /// burst fired before the rig exists is simply dropped. Scaled time, so
    /// shakes freeze with the pause menu.
    /// </summary>
    public static class CameraShake
    {
        class ActiveShake
        {
            public CameraShakeSettings settings;
            public float time;
            public float seed;
        }

        static readonly List<ActiveShake> shakes = new();
        static int tickedFrame = -1;

        /// <summary>This frame's summed positional offset, camera space (metres).</summary>
        public static Vector3 PositionOffset { get; private set; }

        /// <summary>This frame's summed rotational offset, Euler degrees.</summary>
        public static Vector3 RotationOffset { get; private set; }

        /// <summary>Start a shake; overlapping shakes sum. Null settings are ignored.</summary>
        public static void Shake(CameraShakeSettings settings)
        {
            if (settings == null) return;
            shakes.Add(new ActiveShake { settings = settings, seed = Random.value * 100f });
        }

        /// <summary>Drop every running shake (a scene change, a restart).</summary>
        public static void Clear()
        {
            shakes.Clear();
            PositionOffset = Vector3.zero;
            RotationOffset = Vector3.zero;
        }

        /// <summary>Advance the bank — once per frame, whoever calls first does the work.</summary>
        public static void Tick()
        {
            if (tickedFrame == Time.frameCount) return;
            tickedFrame = Time.frameCount;
            float dt = Time.deltaTime;

            Vector3 pos = Vector3.zero;
            Vector3 rot = Vector3.zero;
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                var shake = shakes[i];
                shake.time += dt;
                float normalized = shake.time / Mathf.Max(shake.settings.duration, 0.001f);
                if (normalized >= 1f)
                {
                    shakes.RemoveAt(i);
                    continue;
                }

                float amp = shake.settings.falloff.Evaluate(normalized);
                float t = shake.time * shake.settings.frequency;
                pos += new Vector3(Noise(t, shake.seed), Noise(t, shake.seed + 13f), Noise(t, shake.seed + 29f))
                       * (shake.settings.positionAmplitude * amp);
                rot += new Vector3(Noise(t, shake.seed + 41f), Noise(t, shake.seed + 53f), Noise(t, shake.seed + 67f))
                       * (shake.settings.rotationAmplitude * amp);
            }
            PositionOffset = pos;
            RotationOffset = rot;
        }

        static float Noise(float t, float seed) => (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;
    }
}
