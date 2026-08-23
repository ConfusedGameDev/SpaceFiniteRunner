using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.CameraFX
{
    /// <summary>
    /// Juice hookup: fires a camera shake whenever the ship takes a pad
    /// impulse — a punchy one for boosts, a heavier one for brakes.
    /// </summary>
    public class ShakeOnPad : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] CameraShaker shaker;
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
            if (shaker == null) return;
            shaker.Shake(rawMagnitude >= 0f ? boostShake : brakeShake);
        }
    }
}
