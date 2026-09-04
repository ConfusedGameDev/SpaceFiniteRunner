using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Every gamepad control a <see cref="GameAction"/> can be bound to. The
    /// sticks appear as their four half-axes so an axis action (steer left,
    /// steer right) is two ordinary bindings each holding one control — the
    /// "positive / negative" composite done by hand, with no action asset.
    /// Append-only: the names are the save format (<see cref="ControlBindings"/>).
    /// </summary>
    public enum PadControl
    {
        None = 0,
        ButtonSouth, ButtonEast, ButtonWest, ButtonNorth,
        LeftShoulder, RightShoulder, LeftTrigger, RightTrigger,
        LeftStickPress, RightStickPress,
        DpadUp, DpadDown, DpadLeft, DpadRight,
        Start, Select,
        LeftStickLeft, LeftStickRight, LeftStickUp, LeftStickDown,
        RightStickLeft, RightStickRight, RightStickUp, RightStickDown
    }

    /// <summary>
    /// Resolves a <see cref="PadControl"/> to the live gamepad's control. Every
    /// entry is a <see cref="ButtonControl"/> — the triggers are, and a stick's
    /// left / right / up / down children are one-sided buttons with the Input
    /// System's default press point — so "the stick crossed the threshold this
    /// frame" is <c>wasPressedThisFrame</c> natively and a rebind capture can
    /// treat a stick push exactly like a button press. Null pad reads as
    /// released.
    /// </summary>
    public static class PadControls
    {
        /// <summary>Every control except None, in enum order — the capture scans this.</summary>
        public static readonly PadControl[] All = BuildAll();

        static PadControl[] BuildAll()
        {
            var values = (PadControl[])System.Enum.GetValues(typeof(PadControl));
            var list = new System.Collections.Generic.List<PadControl>(values.Length);
            foreach (var value in values)
                if (value != PadControl.None) list.Add(value);
            return list.ToArray();
        }

        /// <summary>The pad's control for an entry, or null (None, or no pad).</summary>
        public static ButtonControl Control(Gamepad pad, PadControl control)
        {
            if (pad == null) return null;
            return control switch
            {
                PadControl.ButtonSouth => pad.buttonSouth,
                PadControl.ButtonEast => pad.buttonEast,
                PadControl.ButtonWest => pad.buttonWest,
                PadControl.ButtonNorth => pad.buttonNorth,
                PadControl.LeftShoulder => pad.leftShoulder,
                PadControl.RightShoulder => pad.rightShoulder,
                PadControl.LeftTrigger => pad.leftTrigger,
                PadControl.RightTrigger => pad.rightTrigger,
                PadControl.LeftStickPress => pad.leftStickButton,
                PadControl.RightStickPress => pad.rightStickButton,
                PadControl.DpadUp => pad.dpad.up,
                PadControl.DpadDown => pad.dpad.down,
                PadControl.DpadLeft => pad.dpad.left,
                PadControl.DpadRight => pad.dpad.right,
                PadControl.Start => pad.startButton,
                PadControl.Select => pad.selectButton,
                PadControl.LeftStickLeft => pad.leftStick.left,
                PadControl.LeftStickRight => pad.leftStick.right,
                PadControl.LeftStickUp => pad.leftStick.up,
                PadControl.LeftStickDown => pad.leftStick.down,
                PadControl.RightStickLeft => pad.rightStick.left,
                PadControl.RightStickRight => pad.rightStick.right,
                PadControl.RightStickUp => pad.rightStick.up,
                PadControl.RightStickDown => pad.rightStick.down,
                _ => null
            };
        }

        /// <summary>0..1 — a button is 0 or 1, a trigger or stick half-axis its travel.</summary>
        public static float ReadValue(Gamepad pad, PadControl control)
        {
            var button = Control(pad, control);
            return button != null ? button.ReadValue() : 0f;
        }

        public static bool IsPressed(Gamepad pad, PadControl control)
        {
            var button = Control(pad, control);
            return button != null && button.isPressed;
        }

        public static bool WasPressedThisFrame(Gamepad pad, PadControl control)
        {
            var button = Control(pad, control);
            return button != null && button.wasPressedThisFrame;
        }

        /// <summary>Text fallback for a control the glyph set has no art for.</summary>
        public static string Label(PadControl control) => control switch
        {
            PadControl.None => "—",
            PadControl.ButtonSouth => "A",
            PadControl.ButtonEast => "B",
            PadControl.ButtonWest => "X",
            PadControl.ButtonNorth => "Y",
            PadControl.LeftShoulder => "LB",
            PadControl.RightShoulder => "RB",
            PadControl.LeftTrigger => "LT",
            PadControl.RightTrigger => "RT",
            PadControl.LeftStickPress => "L3",
            PadControl.RightStickPress => "R3",
            PadControl.DpadUp => "D-PAD UP",
            PadControl.DpadDown => "D-PAD DOWN",
            PadControl.DpadLeft => "D-PAD LEFT",
            PadControl.DpadRight => "D-PAD RIGHT",
            PadControl.Start => "START",
            PadControl.Select => "VIEW",
            PadControl.LeftStickLeft => "LEFT STICK LEFT",
            PadControl.LeftStickRight => "LEFT STICK RIGHT",
            PadControl.LeftStickUp => "LEFT STICK UP",
            PadControl.LeftStickDown => "LEFT STICK DOWN",
            PadControl.RightStickLeft => "RIGHT STICK LEFT",
            PadControl.RightStickRight => "RIGHT STICK RIGHT",
            PadControl.RightStickUp => "RIGHT STICK UP",
            PadControl.RightStickDown => "RIGHT STICK DOWN",
            _ => control.ToString().ToUpperInvariant()
        };
    }
}
