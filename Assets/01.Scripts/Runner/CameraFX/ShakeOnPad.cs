using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Juice hookup: fires a camera shake whenever the ship takes a pad
    /// impulse — a punchy one for boosts, a heavier one for brakes. The
    /// shake itself is the Cinemachine extension on the chase rig's vcams
    /// (fed from the <see cref="CameraShake"/> bank), so this can sit anywhere in
    /// the scene; it no longer touches the camera transform.
    /// </summary>
    public class ShakeOnPad : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] CameraShakeSettings boostShake;
        [SerializeField] CameraShakeSettings brakeShake;

        void OnEnable()
        {
            if (motor != null) motor.PadImpulse += OnPadImpulse;
        }

        void OnDisable()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
        }

        void OnPadImpulse(float rawMagnitude)
        {
            CameraShake.Shake(rawMagnitude >= 0f ? boostShake : brakeShake);
        }
    }
}
