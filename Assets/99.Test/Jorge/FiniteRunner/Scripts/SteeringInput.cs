using UnityEngine;
using UnityEngine.InputSystem;

namespace FiniteRunner
{
    /// <summary>
    /// Platform-agnostic steering source. The motor only ever reads
    /// <see cref="SteerAxis"/>; swap the implementation for VR later.
    /// </summary>
    public interface ISteeringInput
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        float SteerAxis { get; }
    }

    /// <summary>
    /// Test-phase steering: keyboard (A/D, arrows), gamepad left stick,
    /// and touch (hold left/right half of the screen).
    /// </summary>
    public class SteeringInput : MonoBehaviour, ISteeringInput
    {
        public float SteerAxis { get; private set; }

        void Update()
        {
            SteerAxis = ReadSteer();
        }

        static float ReadSteer()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float axis = 0f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) axis -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) axis += 1f;
                if (axis != 0f) return axis;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                float stick = gamepad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > 0.1f) return stick;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                float axis = 0f;
                float half = Screen.width * 0.5f;
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.isPressed) continue;
                    axis += touch.position.ReadValue().x < half ? -1f : 1f;
                }
                return Mathf.Clamp(axis, -1f, 1f);
            }

            return 0f;
        }
    }
}
