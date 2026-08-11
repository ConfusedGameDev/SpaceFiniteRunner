using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Driver abstraction: CarController only ever reads this, so the same
    /// vehicle physics serves the player (this component) and the patrol AI
    /// (a path-following implementation, M5) — "same physics, different
    /// driver".
    /// </summary>
    public interface ICarInput
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        float Steer { get; }

        /// <summary>+1 accelerate .. -1 brake/reverse. Opposing the current travel direction brakes first, then reverses.</summary>
        float Throttle { get; }

        /// <summary>Held handbrake — rear brake plus loosened rear grip for drift turns.</summary>
        bool Handbrake { get; }

        /// <summary>True only on the frame a manual respawn was requested.</summary>
        bool RespawnPressed { get; }
    }

    /// <summary>
    /// Player driver: keyboard (WASD/arrows, Space handbrake, R respawn) and
    /// gamepad (left stick steer, triggers accelerate/brake, South handbrake,
    /// North respawn), polled straight off the new Input System like the
    /// runner's SteeringInput.
    /// </summary>
    public class CarInput : MonoBehaviour, ICarInput
    {
        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake { get; private set; }
        public bool RespawnPressed { get; private set; }

        void Update()
        {
            float steer = 0f;
            float throttle = 0f;
            bool handbrake = false;
            bool respawn = false;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) steer -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) steer += 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) throttle += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) throttle -= 1f;
                handbrake |= keyboard.spaceKey.isPressed;
                respawn |= keyboard.rKey.wasPressedThisFrame;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                float stick = gamepad.leftStick.x.ReadValue();
                if (steer == 0f && Mathf.Abs(stick) > 0.1f) steer = stick;

                float triggers = gamepad.rightTrigger.ReadValue() - gamepad.leftTrigger.ReadValue();
                if (throttle == 0f && Mathf.Abs(triggers) > 0.02f) throttle = triggers;

                handbrake |= gamepad.buttonSouth.isPressed;
                respawn |= gamepad.buttonNorth.wasPressedThisFrame;
            }

            Steer = Mathf.Clamp(steer, -1f, 1f);
            Throttle = Mathf.Clamp(throttle, -1f, 1f);
            Handbrake = handbrake;
            RespawnPressed = respawn;
        }
    }
}
