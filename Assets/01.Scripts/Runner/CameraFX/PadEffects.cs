using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Screen feedback for pad pickups, listening to <see cref="ShipMotor.PadImpulse"/>
    /// like the camera shake and haptics do: boost orbs kick the
    /// <see cref="LensDistortionController"/> warp envelope, speed-down pads
    /// pulse the shared fullscreen glitch (the same effect the city chase
    /// uses — a bad pad reads as a hit of signal corruption). Purely
    /// additive feedback: no gameplay values are touched here.
    /// </summary>
    public class PadEffects : MonoBehaviour
    {
        [Tooltip("Motor whose pad impulses drive the effects. Left empty, the scene's ship is found at start.")]
        public ShipMotor motor;

        [TitleGroup("Bad pads")]
        [Tooltip("Glitch pulse strength when a speed-down pad is hit.")]
        [PropertyRange(0f, 1f)]
        public float brakeGlitchPulse = 0.6f;

        void Start()
        {
            if (motor == null) motor = FindAnyObjectByType<ShipMotor>();
            if (motor != null) motor.PadImpulse += OnPadImpulse;
        }

        void OnDestroy()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
        }

        void OnPadImpulse(float rawMagnitude)
        {
            if (rawMagnitude > 0f)
                LensDistortionController.Instance.Trigger();
            else if (rawMagnitude < 0f && GlitchController.Instance != null)
                GlitchController.Instance.Pulse(brakeGlitchPulse);
        }
    }
}
