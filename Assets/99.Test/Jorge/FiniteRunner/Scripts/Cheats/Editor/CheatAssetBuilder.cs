using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FiniteRunner.EditorTools
{
    /// <summary>
    /// Fills the cheat assets from disk. The Kenney key caps and Xbox glyphs
    /// live outside any Resources folder, so nothing can load them by path at
    /// runtime — this walks the two art folders once and bakes the references
    /// into <see cref="CheatGlyphSet"/>, which is what the cheats console
    /// actually reads. Re-run it after adding a key to <see cref="CheatKey"/>
    /// or a button to <see cref="CheatButton"/>.
    ///
    /// It also creates the <see cref="CheatDefinition"/> asset with the two
    /// test codes if it does not exist yet — and never touches it again, so a
    /// designer's list can't be stomped by re-running the builder.
    /// </summary>
    public static class CheatAssetBuilder
    {
        const string ResourcesFolder = "Assets/99.Test/Jorge/FiniteRunner/Data/Resources";

        [MenuItem("Tools/FiniteRunner/Build Cheat Assets")]
        public static void Build()
        {
            EnsureFolder();
            var glyphs = BuildGlyphSet();
            var cheats = EnsureDefinition();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = cheats;
            Debug.Log($"Cheat assets ready: {AssetDatabase.GetAssetPath(cheats)} and {AssetDatabase.GetAssetPath(glyphs)}.", cheats);
        }

        static CheatGlyphSet BuildGlyphSet()
        {
            string path = $"{ResourcesFolder}/{CheatGlyphSet.ResourcePath}.asset";
            var set = AssetDatabase.LoadAssetAtPath<CheatGlyphSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CheatGlyphSet>();
                AssetDatabase.CreateAsset(set, path);
            }

            var keys = new List<CheatKeyGlyph>();
            var missing = new List<string>();

            for (int i = 0; i < 26; i++)
            {
                char letter = (char)('a' + i);
                keys.Add(new CheatKeyGlyph
                {
                    key = CheatKey.A + i,
                    sprite = LoadSprite($"{CheatGlyphSet.KeyboardFolder}/keyboard_{letter}.png", missing)
                });
            }
            for (int i = 0; i < 10; i++)
            {
                keys.Add(new CheatKeyGlyph
                {
                    key = CheatKey.Num0 + i,
                    sprite = LoadSprite($"{CheatGlyphSet.KeyboardFolder}/keyboard_{i}.png", missing)
                });
            }

            // The colour face-button art reads better on the dark menu than
            // the outline set, and the d-pad directions are separate sprites.
            var buttonFiles = new (CheatButton button, string file)[]
            {
                (CheatButton.Up, "xbox_dpad_up"),
                (CheatButton.Down, "xbox_dpad_down"),
                (CheatButton.Left, "xbox_dpad_left"),
                (CheatButton.Right, "xbox_dpad_right"),
                (CheatButton.North, "xbox_button_color_y"),
                (CheatButton.South, "xbox_button_color_a"),
                (CheatButton.West, "xbox_button_color_x"),
                (CheatButton.LeftBumper, "xbox_lb"),
                (CheatButton.RightBumper, "xbox_rb")
            };

            var buttons = new List<CheatButtonGlyph>();
            foreach (var (button, file) in buttonFiles)
                buttons.Add(new CheatButtonGlyph
                {
                    button = button,
                    sprite = LoadSprite($"{CheatGlyphSet.GamepadFolder}/{file}.png", missing)
                });

            set.SetGlyphs(keys, buttons);
            EditorUtility.SetDirty(set);

            if (missing.Count > 0)
                Debug.LogWarning($"{missing.Count} cheat glyph(s) not found — those presses will draw as text:\n" +
                                 string.Join("\n", missing), set);

            return set;
        }

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

        static CheatDefinition EnsureDefinition()
        {
            string path = $"{ResourcesFolder}/{CheatDefinition.ResourcePath}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<CheatDefinition>(path);
            if (definition != null) return definition;

            definition = ScriptableObject.CreateInstance<CheatDefinition>();
            definition.SetCheats(new List<CheatEntry>
            {
                new()
                {
                    id = "MegaCar",
                    keyboardCode = "RUMRUM",
                    controllerCode = new List<CheatButton>
                    {
                        CheatButton.Up, CheatButton.Down, CheatButton.Up, CheatButton.Down,
                        CheatButton.West, CheatButton.South, CheatButton.West
                    }
                },
                new()
                {
                    id = "DebugON",
                    keyboardCode = "ArrayKing",
                    controllerCode = new List<CheatButton>
                    {
                        CheatButton.Down, CheatButton.Right,
                        CheatButton.LeftBumper, CheatButton.RightBumper,
                        CheatButton.West, CheatButton.North, CheatButton.West, CheatButton.South
                    }
                }
            });

            AssetDatabase.CreateAsset(definition, path);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(ResourcesFolder)) return;
            AssetDatabase.CreateFolder("Assets/99.Test/Jorge/FiniteRunner/Data", "Resources");
        }
    }
}
