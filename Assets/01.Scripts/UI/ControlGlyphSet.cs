using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>One keyboard key drawn as one sprite.</summary>
    [System.Serializable]
    public struct KeyGlyph
    {
        public Key key;
        [PreviewField(38f)] public Sprite sprite;
    }

    /// <summary>One gamepad control drawn as one sprite.</summary>
    [System.Serializable]
    public struct PadGlyph
    {
        public PadControl control;
        [PreviewField(38f)] public Sprite sprite;
    }

    /// <summary>
    /// The art the CONTROLS screen (and any gameplay prompt reading a live
    /// binding) draws keys and pad controls with: the Kenney "Keyboard &amp;
    /// Mouse / Double" key caps and "Xbox Series / Double" glyphs. Those PNGs
    /// live outside any Resources folder, so this asset is the hand-off —
    /// <c>Tools ▸ FiniteRunner ▸ Build Control Glyphs</c> fills it from the
    /// two folders. It is the UI assembly's twin of the cheats' glyph set
    /// (Cheats references UI, never the reverse) and is keyed by the Input
    /// System's <see cref="Key"/> and this project's <see cref="PadControl"/>,
    /// so ANY bindable control can be drawn; a control with no art falls back
    /// to its <see cref="Label(Key)"/> text.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_ControlGlyphs", menuName = "FiniteRunner/Control Glyph Set")]
    public class ControlGlyphSet : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_ControlGlyphs";

        /// <summary>Where the builder reads the key caps from.</summary>
        public const string KeyboardFolder = "Assets/06.UI/01.Sprites/Keyboard & Mouse/Double";

        /// <summary>Where the builder reads the pad glyphs from.</summary>
        public const string GamepadFolder = "Assets/06.UI/01.Sprites/Xbox Series/Double";

        [TitleGroup("Keyboard (06.UI/01.Sprites/Keyboard & Mouse/Double)")]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = false)]
        [SerializeField] List<KeyGlyph> keys = new();

        [TitleGroup("Gamepad (06.UI/01.Sprites/Xbox Series/Double)")]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = false)]
        [SerializeField] List<PadGlyph> pads = new();

        Dictionary<Key, Sprite> keyLookup;
        Dictionary<PadControl, Sprite> padLookup;

        /// <summary>The key cap for a key, or null when the set has no art for it.</summary>
        public Sprite For(Key key)
        {
            if (key == Key.None) return null;
            keyLookup ??= BuildKeys();
            return keyLookup.TryGetValue(key, out var sprite) ? sprite : null;
        }

        /// <summary>The glyph for a pad control, or null when the set has no art for it.</summary>
        public Sprite For(PadControl control)
        {
            if (control == PadControl.None) return null;
            padLookup ??= BuildPads();
            return padLookup.TryGetValue(control, out var sprite) ? sprite : null;
        }

        /// <summary>
        /// Text fallback for a key: the enum name spaced and upper-cased
        /// ("RIGHT SHIFT", "LEFT ARROW"), digits bare ("5"), the numpad
        /// prefixed ("NUM 3"), None as a dash.
        /// </summary>
        public static string Label(Key key)
        {
            if (key == Key.None) return "—";
            string name = key.ToString();
            if (name.StartsWith("Digit")) return name.Substring(5);
            if (name.StartsWith("Numpad")) return "NUM " + Spaced(name.Substring(6));
            return Spaced(name);
        }

        /// <summary>Text fallback for a pad control.</summary>
        public static string Label(PadControl control) => PadControls.Label(control);

        static string Spaced(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(pascal[i - 1])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Drops the lookups so an inspector edit shows up without a domain reload.</summary>
        public void Invalidate()
        {
            keyLookup = null;
            padLookup = null;
        }

        /// <summary>Replaces the whole set. Used by the editor builder.</summary>
        public void SetGlyphs(List<KeyGlyph> keyGlyphs, List<PadGlyph> padGlyphs)
        {
            keys = keyGlyphs;
            pads = padGlyphs;
            Invalidate();
        }

        void OnValidate() => Invalidate();

        Dictionary<Key, Sprite> BuildKeys()
        {
            var map = new Dictionary<Key, Sprite>();
            foreach (var entry in keys)
                if (entry.key != Key.None && entry.sprite != null) map[entry.key] = entry.sprite;
            return map;
        }

        Dictionary<PadControl, Sprite> BuildPads()
        {
            var map = new Dictionary<PadControl, Sprite>();
            foreach (var entry in pads)
                if (entry.control != PadControl.None && entry.sprite != null) map[entry.control] = entry.sprite;
            return map;
        }

        static ControlGlyphSet cached;
        static bool warned;

        /// <summary>
        /// The glyph asset, or an empty throwaway instance if none is in a
        /// Resources folder — every control then draws as its plain name,
        /// which is ugly but still tells the player what is bound.
        /// </summary>
        public static ControlGlyphSet Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<ControlGlyphSet>(ResourcePath);
            if (cached == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"No {nameof(ControlGlyphSet)} at Resources/{ResourcePath} — " +
                                     "the controls screen falls back to text instead of key art. " +
                                     "Run Tools > FiniteRunner > Build Control Glyphs.");
                }
                cached = CreateInstance<ControlGlyphSet>();
            }
            return cached;
        }
    }
}
