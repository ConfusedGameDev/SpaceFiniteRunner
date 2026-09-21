using UnityEngine;
using UnityEngine.InputSystem;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Ship
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
    /// latched request; how it is produced (a double tap or a single press
    /// of the bumpers or keys, a VR gesture later) stays an input-side detail.
    /// </summary>
    public interface IDashInput
    {
        /// <summary>Max seconds between two taps that still count as a double tap.</summary>
        float DoubleTapSeconds { get; set; }

        /// <summary>The run's rule: one press is the whole gesture. A run rule (GameSettings), pushed in by the motor every tick so the asset stays live.</summary>
        bool SinglePress { get; set; }

        /// <summary>-1 dash left, +1 dash right, 0 none. Latched; clears on read.</summary>
        int ConsumeDashRequest();
    }

    /// <summary>
    /// Platform-agnostic throttle and brake. Both are 0..1 and analog where
    /// the device is (the triggers); how a key's 0/1 is softened stays an
    /// input-side detail, timed by <see cref="DigitalRampSeconds"/>.
    /// </summary>
    public interface IThrottleInput
    {
        /// <summary>0 (released) .. 1 (full throttle).</summary>
        float Throttle { get; }

        /// <summary>0 (released) .. 1 (full brake).</summary>
        float Brake { get; }

        /// <summary>Seconds a key takes to ease from 0 to full (and back). 0 = a key is instantly 0 or 1. A ship stat, pushed in by the motor.</summary>
        float DigitalRampSeconds { get; set; }
    }

    /// <summary>
    /// Test-phase steering: the bound steer keys / pad controls (A/D and the
    /// left stick by default — <see cref="ControlBindings"/>, rebindable on
    /// the CONTROLS screen) and touch (hold left/right half of the screen).
    /// Also detects the dash double taps (the bound dash controls: N/M, LB/RB
    /// by default) and latches them until the motor consumes the request, so
    /// a tap landing between the motor's reads is never lost. One press is
    /// the whole gesture while EITHER the run's <see cref="SinglePress"/>
    /// rule (GameSettings.dashSinglePress) or the player's
    /// <see cref="UserSettings.DashSinglePress"/> (the CONTROLS page's
    /// toggle, read live) is on.
    /// It is the throttle too (<see cref="IThrottleInput"/>: W / right trigger
    /// accelerates, S / left trigger brakes by default): the triggers are read
    /// analog, a key eases to full over <see cref="DigitalRampSeconds"/>, and
    /// whichever of the two is further down wins. <b>Touch has no throttle
    /// control</b>: on a touch-only device the throttle is held at full and
    /// the brake at 0, so the run plays as steering alone.
    /// </summary>
    public class SteeringInput : MonoBehaviour, ISteeringInput, IDashInput, IThrottleInput
    {
        const float TriggerDeadzone = 0.02f;

        public float SteerAxis { get; private set; }
        public float DoubleTapSeconds { get; set; } = 0.3f;
        public bool SinglePress { get; set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float DigitalRampSeconds { get; set; } = 0.15f;

        float throttleKey, brakeKey; // the keys' eased 0..1

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
            ReadThrottle(Time.deltaTime);

            // No tap collection while paused: the pause menu uses the bumpers
            // for its debug tabs, and frozen Time.time would otherwise make any
            // two paused presses read as a double tap that fires on resume.
            if (Time.timeScale == 0f) return;

            if (LeftTapped()) RegisterTap(ref lastLeftTap, -1);
            if (RightTapped()) RegisterTap(ref lastRightTap, +1);
        }

        void RegisterTap(ref float lastTap, int direction)
        {
            if (SinglePress || UserSettings.DashSinglePress || Time.time - lastTap <= DoubleTapSeconds)
            {
                pendingDash = direction;
                lastTap = float.NegativeInfinity; // a triple tap is not two doubles
            }
            else
            {
                lastTap = Time.time;
            }
        }

        void ReadThrottle(float dt)
        {
            if (TouchOnly)
            {
                Throttle = 1f;
                Brake = 0f;
                return;
            }

            Throttle = Mathf.Max(EaseKey(ref throttleKey, GameAction.ShipAccelerate, dt), PadValue(GameAction.ShipAccelerate));
            Brake = Mathf.Max(EaseKey(ref brakeKey, GameAction.ShipBrake, dt), PadValue(GameAction.ShipBrake));
        }

        // No keyboard and no pad to press: a phone or a tablet.
        static bool TouchOnly =>
            Application.isMobilePlatform ||
            (Touchscreen.current != null && Keyboard.current == null && Gamepad.current == null);

        float EaseKey(ref float eased, GameAction action, float dt)
        {
            var key = ControlBindings.KeyControlFor(ControlBindings.KeyFor(action));
            float target = key != null && key.isPressed ? 1f : 0f;
            eased = DigitalRampSeconds > 0f ? Mathf.MoveTowards(eased, target, dt / DigitalRampSeconds) : target;
            return eased;
        }

        static float PadValue(GameAction action)
        {
            var pad = Gamepad.current;
            if (pad == null) return 0f;
            float value = PadControls.ReadValue(pad, ControlBindings.PadFor(action));
            return value > TriggerDeadzone ? Mathf.Clamp01(value) : 0f;
        }

        static bool LeftTapped() => ControlBindings.WasPressedThisFrame(GameAction.ShipDashLeft);

        static bool RightTapped() => ControlBindings.WasPressedThisFrame(GameAction.ShipDashRight);

        static float ReadSteer()
        {
            // Keyboard wins over the stick (ControlBindings.Axis), then touch.
            float bound = ControlBindings.Axis(GameAction.ShipSteerLeft, GameAction.ShipSteerRight, 0.1f);
            if (bound != 0f) return bound;

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
