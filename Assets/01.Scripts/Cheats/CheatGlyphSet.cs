using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Cheats
{
    /// <summary>One key drawn as one sprite.</summary>
    [System.Serializable]
    public struct CheatKeyGlyph
    {
        public CheatKey key;
        [PreviewField(38f)] public Sprite sprite;
    }

    /// <summary>One pad button drawn as one sprite.</summary>
    [System.Serializable]
    public struct CheatButtonGlyph
    {
        public CheatButton button;
        [PreviewField(38f)] public Sprite sprite;
    }

    /// <summary>
    /// The art the cheats console echoes input with: the Kenney "Keyboard &amp;
    /// Mouse / Double" key caps and the "Xbox Series / Double" button glyphs.
    /// Those PNGs live outside any Resources folder, so they cannot be loaded
    /// by path at runtime — this asset is the hand-off, and
    /// <c>Tools ▸ FiniteRunner ▸ Build Cheat Glyph Set</c> fills it from the
    /// two folders rather than asking anyone to drag 45 sprites in by hand.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_CheatGlyphs", menuName = "FiniteRunner/Cheat Glyph Set")]
    public class CheatGlyphSet : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_CheatGlyphs";

        /// <summary>Where the builder reads the key caps from.</summary>
        public const string KeyboardFolder = "Assets/06.UI/01.Sprites/Keyboard & Mouse/Double";

        /// <summary>Where the builder reads the pad glyphs from.</summary>
        public const string GamepadFolder = "Assets/06.UI/01.Sprites/Xbox Series/Double";

        [TitleGroup("Keyboard (03.UI/Keyboard & Mouse/Double)")]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = false)]
        [SerializeField] List<CheatKeyGlyph> keys = new();

        [TitleGroup("Gamepad (03.UI/Xbox Series/Double)")]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = true)]
        [SerializeField] List<CheatButtonGlyph> buttons = new();

        Dictionary<CheatKey, Sprite> keyLookup;
        Dictionary<CheatButton, Sprite> buttonLookup;

        /// <summary>The sprite for a token, or null when the set is missing that entry.</summary>
        public Sprite Glyph(CheatToken token)
        {
            if (token.Key != CheatKey.None)
            {
                keyLookup ??= Build(keys);
                return keyLookup.TryGetValue(token.Key, out var sprite) ? sprite : null;
            }

            if (token.Button != CheatButton.None)
            {
                buttonLookup ??= Build(buttons);
                return buttonLookup.TryGetValue(token.Button, out var sprite) ? sprite : null;
            }

            return null;
        }

        /// <summary>Drops the lookups so an inspector edit shows up without a domain reload.</summary>
        public void Invalidate()
        {
            keyLookup = null;
            buttonLookup = null;
        }

        /// <summary>Replaces the whole set. Used by the editor builder.</summary>
        public void SetGlyphs(List<CheatKeyGlyph> keyGlyphs, List<CheatButtonGlyph> buttonGlyphs)
        {
            keys = keyGlyphs;
            buttons = buttonGlyphs;
            Invalidate();
        }

        void OnValidate() => Invalidate();

        static Dictionary<CheatKey, Sprite> Build(List<CheatKeyGlyph> source)
        {
            var map = new Dictionary<CheatKey, Sprite>();
            foreach (var entry in source)
                if (entry.key != CheatKey.None) map[entry.key] = entry.sprite;
            return map;
        }

        static Dictionary<CheatButton, Sprite> Build(List<CheatButtonGlyph> source)
        {
            var map = new Dictionary<CheatButton, Sprite>();
            foreach (var entry in source)
                if (entry.button != CheatButton.None) map[entry.button] = entry.sprite;
            return map;
        }

        static CheatGlyphSet cached;

        /// <summary>
        /// The glyph asset, or an empty throwaway instance if none is in a
        /// Resources folder — the console then falls back to drawing each
        /// press as its plain name, which is ugly but still tells the player
        /// what landed.
        /// </summary>
        public static CheatGlyphSet Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<CheatGlyphSet>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(CheatGlyphSet)} at Resources/{ResourcePath} — " +
                                 "the cheats console falls back to text instead of key art. " +
                                 "Run Tools > FiniteRunner > Build Cheat Glyph Set.");
                cached = CreateInstance<CheatGlyphSet>();
            }
            return cached;
        }
    }
}
