using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>Which family of hardware the on-screen prompts should be drawn for.</summary>
    public enum PromptDevice { KeyboardMouse, Gamepad }

    /// <summary>The four things a menu prompt can ask the player to do.</summary>
    public enum PromptAction { Navigate, Adjust, Confirm, Back }

    /// <summary>
    /// Tracks which device the player last actually touched and hands out the
    /// matching prompt art: Xbox glyph sprites for the pad, plain key names for
    /// keyboard and mouse. Everything that draws a prompt goes through here, so
    /// swapping the Xbox PNGs for an inline glyph font later is a change to this
    /// one file.
    ///
    /// The project has no .inputactions asset and every existing script polls
    /// Gamepad.current / Keyboard.current directly (PauseMenu, TuningScreen);
    /// this follows that pattern rather than introducing an action asset just
    /// for the menu.
    /// </summary>
    public static class InputPromptBinder
    {
        public static PromptDevice Device { get; private set; } = PromptDevice.KeyboardMouse;

        /// <summary>Raised when the player switches between pad and keyboard/mouse.</summary>
        public static event System.Action<PromptDevice> DeviceChanged;

        /// <summary>Call once per frame from whoever owns the menu.</summary>
        public static void Poll()
        {
            if (GamepadActive()) Set(PromptDevice.Gamepad);
            else if (KeyboardOrMouseActive()) Set(PromptDevice.KeyboardMouse);
        }

        public static Sprite Glyph(MenuTheme theme, PromptAction action) => action switch
        {
            PromptAction.Confirm => theme.GlyphConfirm,
            PromptAction.Back => theme.GlyphBack,
            PromptAction.Navigate => theme.GlyphNavigate,
            PromptAction.Adjust => theme.GlyphAdjust,
            _ => null
        };

        public static string KeyLabel(PromptAction action) => action switch
        {
            PromptAction.Confirm => "[ENTER]",
            PromptAction.Back => "[ESC]",
            PromptAction.Navigate => "[W/S]",
            PromptAction.Adjust => "[A/D]",
            _ => string.Empty
        };

        static void Set(PromptDevice device)
        {
            if (Device == device) return;
            Device = device;
            DeviceChanged?.Invoke(device);
        }

        static bool GamepadActive()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;

            if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
                pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
                pad.dpad.up.isPressed || pad.dpad.down.isPressed ||
                pad.dpad.left.isPressed || pad.dpad.right.isPressed)
                return true;

            return pad.leftStick.ReadValue().sqrMagnitude > 0.09f;
        }

        static bool KeyboardOrMouseActive()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            if (mouse == null) return false;
            return mouse.delta.ReadValue().sqrMagnitude > 4f ||
                   mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
        }
    }
}
