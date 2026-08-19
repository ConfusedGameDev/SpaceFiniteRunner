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
    /// Platform-agnostic dash trigger. The motor only ever consumes the
    /// latched request; how a "double tap" is produced (bumpers, keys, a VR
    /// gesture later) stays an input-side detail.
    /// </summary>
    public interface IDashInput
    {
        /// <summary>Max seconds between two taps that still count as a double tap.</summary>
        float DoubleTapSeconds { get; set; }

        /// <summary>-1 dash left, +1 dash right, 0 none. Latched; clears on read.</summary>
        int ConsumeDashRequest();
    }

    /// <summary>
    /// Test-phase steering: keyboard (A/D, arrows), gamepad left stick,
    /// and touch (hold left/right half of the screen). Also detects the
    /// dash double taps (LB/RB on pad, N/M on keyboard) and latches them
    /// until the motor consumes the request, so a tap landing between the
    /// motor's reads is never lost.
    /// </summary>
    public class SteeringInput : MonoBehaviour, ISteeringInput, IDashInput
    {
        public float SteerAxis { get; private set; }
        public float DoubleTapSeconds { get; set; } = 0.3f;

        float lastLeftTap = float.NegativeInfinity;
        float lastRightTap = float.NegativeInfinity;
        int pendingDash;

        public int ConsumeDashRequest()
        {
            int request = pendingDash;
            pendingDash = 0;
            return request;
        }

        void Update()
        {
            SteerAxis = ReadSteer();

            // No tap collection while paused: the pause menu uses the bumpers
            // for its debug tabs, and frozen Time.time would otherwise make any
            // two paused presses read as a double tap that fires on resume.
            if (Time.timeScale == 0f) return;

            if (LeftTapped()) RegisterTap(ref lastLeftTap, -1);
            if (RightTapped()) RegisterTap(ref lastRightTap, +1);
        }

        void RegisterTap(ref float lastTap, int direction)
        {
            if (Time.time - lastTap <= DoubleTapSeconds)
            {
                pendingDash = direction;
                lastTap = float.NegativeInfinity; // a triple tap is not two doubles
            }
            else
            {
                lastTap = Time.time;
            }
        }

        static bool LeftTapped() =>
            Keyboard.current is { nKey: { wasPressedThisFrame: true } } ||
            Gamepad.current is { leftShoulder: { wasPressedThisFrame: true } };

        static bool RightTapped() =>
            Keyboard.current is { mKey: { wasPressedThisFrame: true } } ||
            Gamepad.current is { rightShoulder: { wasPressedThisFrame: true } };

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
