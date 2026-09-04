using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The "press a key…" listener behind a rebind: armed for one device, it
    /// scans that device for a fresh press each frame and hands back the
    /// first control that landed. Reserved controls (the menus' own chords,
    /// <see cref="ControlBindings.IsReserved(Key)"/>) are never returned, so
    /// Esc / B can cancel the listen without ever being captured; a grace
    /// window after arming swallows the Confirm press that started it, and
    /// only the FIRST press in a frame counts (the cheat console's rule —
    /// two keys mashed together must not bind one and lose the other).
    /// </summary>
    public sealed class BindingCapture
    {
        float armedAt;

        public bool Listening { get; private set; }
        public BindingDevice Device { get; private set; }

        public void Arm(BindingDevice device)
        {
            Device = device;
            Listening = true;
            armedAt = Time.unscaledTime;
        }

        public void Cancel() => Listening = false;

        /// <summary>
        /// One frame of listening. False until <paramref name="graceSeconds"/>
        /// have passed since <see cref="Arm"/> and until something bindable
        /// is pressed; true hands back the control (the other device's out
        /// value is None) and stops listening.
        /// </summary>
        public bool TryRead(float graceSeconds, out Key key, out PadControl pad)
        {
            key = Key.None;
            pad = PadControl.None;
            if (!Listening || Time.unscaledTime - armedAt < graceSeconds) return false;

            if (Device == BindingDevice.Keyboard)
            {
                var keyboard = Keyboard.current;
                if (keyboard == null) return false;
                foreach (var control in keyboard.allKeys)
                {
                    if (!control.wasPressedThisFrame || ControlBindings.IsReserved(control.keyCode)) continue;
                    key = control.keyCode;
                    Listening = false;
                    return true;
                }
                return false;
            }

            var gamepad = Gamepad.current;
            if (gamepad == null) return false;
            foreach (var control in PadControls.All)
            {
                if (ControlBindings.IsReserved(control) || !PadControls.WasPressedThisFrame(gamepad, control)) continue;
                pad = control;
                Listening = false;
                return true;
            }
            return false;
        }
    }
}
