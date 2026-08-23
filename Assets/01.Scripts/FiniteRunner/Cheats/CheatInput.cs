using UnityEngine.InputSystem;

namespace FiniteRunner
{
    /// <summary>
    /// A keyboard key a cheat code can be built from. Deliberately only the
    /// letters and digits: those are the keys the Kenney prompt kit has art
    /// for, and a code the player cannot see drawn is a code they cannot
    /// learn. Escape is absent on purpose — it is reserved for leaving the
    /// menu, so it can never be swallowed by a code.
    /// </summary>
    public enum CheatKey
    {
        None = 0,
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Num0, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9
    }

    /// <summary>
    /// A gamepad button a cheat code can be built from. The d-pad counts as
    /// four ordinary buttons (the stick is deliberately not read: a code has
    /// to be pressed, not waggled), and the East face button — Xbox B — is
    /// absent for the same reason Escape is: it backs out of the menu.
    /// </summary>
    public enum CheatButton
    {
        None = 0,
        Up, Down, Left, Right,
        North, South, West,
        LeftBumper, RightBumper
    }

    /// <summary>
    /// One entry of the input buffer: either a key or a button, never both.
    /// Keeping them in a single token means the buffer, the matcher and the
    /// on-screen strip are all written once instead of once per device.
    /// </summary>
    public readonly struct CheatToken : System.IEquatable<CheatToken>
    {
        public readonly CheatKey Key;
        public readonly CheatButton Button;

        public CheatToken(CheatKey key)
        {
            Key = key;
            Button = CheatButton.None;
        }

        public CheatToken(CheatButton button)
        {
            Key = CheatKey.None;
            Button = button;
        }

        public bool IsEmpty => Key == CheatKey.None && Button == CheatButton.None;

        public bool Equals(CheatToken other) => Key == other.Key && Button == other.Button;
        public override bool Equals(object obj) => obj is CheatToken other && Equals(other);
        public override int GetHashCode() => ((int)Key << 8) ^ (int)Button;
        public override string ToString() => Key != CheatKey.None ? Key.ToString() : Button.ToString();
    }

    /// <summary>
    /// Polls the two devices for a cheat token this frame. Follows the
    /// project's pattern of reading <see cref="Keyboard.current"/> /
    /// <see cref="Gamepad.current"/> directly — there is no .inputactions
    /// asset, and a cheat code is a raw physical sequence rather than a
    /// rebindable action anyway.
    ///
    /// Only ever returns the FIRST token found in a frame: mashing two keys
    /// together must not push two entries and desync the strip from what the
    /// player thinks they typed.
    /// </summary>
    public static class CheatInputReader
    {
        /// <summary>The letter or digit pressed this frame, or None.</summary>
        public static CheatKey ReadKey()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return CheatKey.None;

            for (int i = 0; i < 26; i++)
                if (keyboard[Key.A + i].wasPressedThisFrame)
                    return CheatKey.A + i;

            // Digit1..Digit9 are contiguous in the Input System's Key enum and
            // Digit0 sits after them, so the row cannot be walked in one loop.
            for (int i = 0; i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    return CheatKey.Num1 + i;

            return keyboard[Key.Digit0].wasPressedThisFrame ? CheatKey.Num0 : CheatKey.None;
        }

        /// <summary>The cheat-legal pad button pressed this frame, or None.</summary>
        public static CheatButton ReadButton()
        {
            var pad = Gamepad.current;
            if (pad == null) return CheatButton.None;

            if (pad.dpad.up.wasPressedThisFrame) return CheatButton.Up;
            if (pad.dpad.down.wasPressedThisFrame) return CheatButton.Down;
            if (pad.dpad.left.wasPressedThisFrame) return CheatButton.Left;
            if (pad.dpad.right.wasPressedThisFrame) return CheatButton.Right;
            if (pad.buttonNorth.wasPressedThisFrame) return CheatButton.North;
            if (pad.buttonSouth.wasPressedThisFrame) return CheatButton.South;
            if (pad.buttonWest.wasPressedThisFrame) return CheatButton.West;
            if (pad.leftShoulder.wasPressedThisFrame) return CheatButton.LeftBumper;
            if (pad.rightShoulder.wasPressedThisFrame) return CheatButton.RightBumper;

            // buttonEast is intentionally not read: it is Back.
            return CheatButton.None;
        }

        /// <summary>
        /// Parses one authored character into a key. Codes are written as
        /// plain strings on the definition asset ("RUMRUM"), so a designer
        /// types a cheat rather than filling a list of enum entries.
        /// </summary>
        public static CheatKey Parse(char character)
        {
            char upper = char.ToUpperInvariant(character);
            if (upper >= 'A' && upper <= 'Z') return CheatKey.A + (upper - 'A');
            if (upper >= '0' && upper <= '9') return CheatKey.Num0 + (upper - '0');
            return CheatKey.None;
        }
    }
}
