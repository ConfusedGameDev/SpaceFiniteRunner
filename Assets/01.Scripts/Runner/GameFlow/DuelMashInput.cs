using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The tug of war's mash button, read raw.
    ///
    /// This is a deliberate exception to the project rule that gameplay input
    /// goes through <c>ControlBindings</c> — the fourth, after the dialogue
    /// advance on gamepad A, the camera's mouse and touch steering. It is
    /// fixed and non-bindable on purpose: the mash is a panic input, and
    /// asking a player to read a glyph at the one moment the road is the thing
    /// worth watching would be the wrong trade. Both controls ARE in the
    /// binding table's reserved set, so nothing else can ever land on them and
    /// fire a ship or camera action on every press.
    ///
    /// Presses are counted in <c>Update</c> and drained by the fixed tick, the
    /// same shape the ship's dash request uses — a frame that produces two
    /// presses must be worth two, and a fixed tick that spans three frames
    /// must not lose any. The count is capped so a held turbo controller
    /// cannot bank an unbounded score, and it is dropped rather than banked
    /// while nothing is reading it.
    /// </summary>
    public static class DuelMashInput
    {
        /// <summary>The keyboard mash key. Reserved in <c>ControlBindings</c>.</summary>
        public const Key MashKey = Key.X;

        // Enough to cover a slow frame at a plausible mashing rate; past this
        // the player is not mashing, the hardware is.
        const int MaxBanked = 6;

        static int banked;
        static int lastPolledFrame = -1;

        /// <summary>True while a gamepad is present — the prompt reads this to pick glyph or key label.</summary>
        public static bool UsingGamepad => Gamepad.current != null;

        /// <summary>
        /// Counts presses since the last drain. Safe to call many times a
        /// frame: it only samples the devices once per frame.
        /// </summary>
        public static void Poll()
        {
            if (Time.frameCount == lastPolledFrame) return;
            lastPolledFrame = Time.frameCount;

            bool pressed = (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame)
                        || (Keyboard.current != null && Keyboard.current[MashKey].wasPressedThisFrame);
            if (pressed && banked < MaxBanked) banked++;
        }

        /// <summary>Takes every press counted since the last call and clears the count.</summary>
        public static int ConsumePresses()
        {
            int count = banked;
            banked = 0;
            return count;
        }

        /// <summary>
        /// Throws away anything counted so far. Called when a contest starts,
        /// so presses made before the bar appeared do not win it instantly,
        /// and whenever one ends.
        /// </summary>
        public static void Clear() => banked = 0;
    }
}
