using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Fills the <see cref="ControlGlyphSet"/> from disk: the Kenney key caps
    /// and Xbox glyphs live outside any Resources folder, so nothing can load
    /// them by path at runtime — this walks the two art folders once and
    /// bakes the references into the asset the CONTROLS screen reads. The
    /// mapping below is the enum → Kenney file name table; re-run after
    /// adding a <see cref="PadControl"/> or when a key is missing art.
    /// </summary>
    public static class ControlGlyphBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";

        // Keyboard: Key → keyboard_<name>.png. Left/right modifiers share one
        // cap, the numpad shares the digit and symbol caps.
        static readonly (Key key, string file)[] KeyFiles =
        {
            (Key.Space, "space"), (Key.Tab, "tab"), (Key.Enter, "enter"), (Key.NumpadEnter, "numpad_enter"),
            (Key.Backspace, "backspace"), (Key.Escape, "escape"),
            (Key.LeftShift, "shift"), (Key.RightShift, "shift"),
            (Key.LeftCtrl, "ctrl"), (Key.RightCtrl, "ctrl"),
            (Key.LeftAlt, "alt"), (Key.RightAlt, "alt"),
            (Key.LeftMeta, "win"), (Key.RightMeta, "win"),
            (Key.LeftArrow, "arrow_left"), (Key.RightArrow, "arrow_right"),
            (Key.UpArrow, "arrow_up"), (Key.DownArrow, "arrow_down"),
            (Key.CapsLock, "capslock"), (Key.NumLock, "numlock"), (Key.ScrollLock, "scroll_lock"),
            (Key.PrintScreen, "printscreen"), (Key.Pause, "pause_break"),
            (Key.Home, "home"), (Key.End, "end"), (Key.Insert, "insert"), (Key.Delete, "delete"),
            (Key.PageUp, "page_up"), (Key.PageDown, "page_down"),
            (Key.Minus, "minus"), (Key.NumpadMinus, "minus"), (Key.Equals, "equals"), (Key.NumpadEquals, "equals"),
            (Key.NumpadPlus, "numpad_plus"), (Key.NumpadMultiply, "asterisk"),
            (Key.Slash, "slash_forward"), (Key.NumpadDivide, "slash_forward"), (Key.Backslash, "slash_back"),
            (Key.LeftBracket, "bracket_open"), (Key.RightBracket, "bracket_close"),
            (Key.Semicolon, "semicolon"), (Key.Quote, "apostrophe"), (Key.Comma, "comma"),
            (Key.Period, "period"), (Key.NumpadPeriod, "period"), (Key.Backquote, "tilde")
        };

        // Gamepad: PadControl → xbox_<name>.png. The colour face buttons read
        // better on the dark menu than the outline set.
        static readonly (PadControl control, string file)[] PadFiles =
        {
            (PadControl.ButtonSouth, "button_color_a"), (PadControl.ButtonEast, "button_color_b"),
            (PadControl.ButtonWest, "button_color_x"), (PadControl.ButtonNorth, "button_color_y"),
            (PadControl.LeftShoulder, "lb"), (PadControl.RightShoulder, "rb"),
            (PadControl.LeftTrigger, "lt"), (PadControl.RightTrigger, "rt"),
            (PadControl.LeftStickPress, "stick_l_press"), (PadControl.RightStickPress, "stick_r_press"),
            (PadControl.DpadUp, "dpad_up"), (PadControl.DpadDown, "dpad_down"),
            (PadControl.DpadLeft, "dpad_left"), (PadControl.DpadRight, "dpad_right"),
            (PadControl.Start, "button_start"), (PadControl.Select, "button_back"),
            (PadControl.LeftStickLeft, "stick_l_left"), (PadControl.LeftStickRight, "stick_l_right"),
            (PadControl.LeftStickUp, "stick_l_up"), (PadControl.LeftStickDown, "stick_l_down"),
            (PadControl.RightStickLeft, "stick_r_left"), (PadControl.RightStickRight, "stick_r_right"),
            (PadControl.RightStickUp, "stick_r_up"), (PadControl.RightStickDown, "stick_r_down")
        };

        [MenuItem("Tools/FiniteRunner/Build Control Glyphs")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets/04.Data", "Resources");

            string path = $"{ResourcesFolder}/{ControlGlyphSet.ResourcePath}.asset";
            var set = AssetDatabase.LoadAssetAtPath<ControlGlyphSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ControlGlyphSet>();
                AssetDatabase.CreateAsset(set, path);
            }

            var missing = new List<string>();
            var keys = new List<KeyGlyph>();

            for (int i = 0; i < 26; i++)
                keys.Add(new KeyGlyph { key = Key.A + i, sprite = LoadSprite(KeyPath(((char)('a' + i)).ToString()), missing) });

            // Digit1..Digit9 are contiguous with Digit0 after them; the numpad row is contiguous 0..9.
            for (int i = 0; i < 9; i++)
                keys.Add(new KeyGlyph { key = Key.Digit1 + i, sprite = LoadSprite(KeyPath((i + 1).ToString()), missing) });
            keys.Add(new KeyGlyph { key = Key.Digit0, sprite = LoadSprite(KeyPath("0"), missing) });
            for (int i = 0; i < 10; i++)
                keys.Add(new KeyGlyph { key = Key.Numpad0 + i, sprite = LoadSprite(KeyPath(i.ToString()), missing) });

            for (int i = 0; i < 12; i++)
                keys.Add(new KeyGlyph { key = Key.F1 + i, sprite = LoadSprite(KeyPath($"f{i + 1}"), missing) });

            foreach (var (key, file) in KeyFiles)
                keys.Add(new KeyGlyph { key = key, sprite = LoadSprite(KeyPath(file), missing) });

            var pads = new List<PadGlyph>();
            foreach (var (control, file) in PadFiles)
                pads.Add(new PadGlyph { control = control, sprite = LoadSprite($"{ControlGlyphSet.GamepadFolder}/xbox_{file}.png", missing) });

            set.SetGlyphs(keys, pads);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (missing.Count > 0)
                Debug.LogWarning($"{missing.Count} control glyph(s) not found — those controls will draw as text:\n" +
                                 string.Join("\n", missing), set);

            Selection.activeObject = set;
            Debug.Log($"Control glyphs ready: {path} ({keys.Count} keys, {pads.Count} pad controls).", set);
        }

        static string KeyPath(string file) => $"{ControlGlyphSet.KeyboardFolder}/keyboard_{file}.png";

        // The Kenney PNGs are imported in Sprite Multiple mode, so the sprite
        // is a sub-asset of the texture rather than the main asset.
        static Sprite LoadSprite(string path, List<string> missing)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Sprite sprite)
                    return sprite;

            missing.Add(path);
            return null;
        }
    }
}
