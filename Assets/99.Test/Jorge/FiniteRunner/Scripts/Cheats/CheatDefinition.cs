using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// One cheat: an id, the keyboard code and the pad code that unlock it.
    /// Every cheat carries BOTH — the two devices are never asked to share a
    /// sequence, because "up up down down" has no keyboard equivalent and
    /// "RUMRUM" has no pad one.
    ///
    /// The keyboard code is authored as a plain string so a designer types it
    /// (letters and digits only); the pad code is a list of buttons. Both are
    /// 4–10 entries long: shorter and the player trips over it by accident,
    /// longer and it outruns the 12-slot buffer's readability.
    /// </summary>
    [System.Serializable]
    public class CheatEntry
    {
        /// <summary>Shortest and longest a code may be. Enforced by the inspector validation, not silently clamped.</summary>
        public const int MinLength = 4;
        public const int MaxLength = 10;

        [InfoBox("$Problem", InfoMessageType.Error, VisibleIf = nameof(HasProblem))]
        [Tooltip("Passed to the cheat event verbatim. Listeners switch on it, so keep it stable once it ships.")]
        public string id = "NewCheat";

        [Tooltip("Letters and digits only, 4-10 of them. Case does not matter: \"RUMRUM\".")]
        public string keyboardCode = string.Empty;

        [ListDrawerSettings(DraggableItems = true, ShowIndexLabels = true, DefaultExpandedState = true)]
        [Tooltip("4-10 pad buttons, in order. B / East is unavailable: it backs out of the menu.")]
        public List<CheatButton> controllerCode = new();

        List<CheatToken> keyTokens;
        List<CheatToken> buttonTokens;
        string parsedFrom;

        /// <summary>The code for one device, as buffer tokens. Rebuilt only when the authored string changes.</summary>
        public IReadOnlyList<CheatToken> Sequence(bool gamepad)
        {
            if (gamepad) return ButtonTokens();
            return KeyTokens();
        }

        IReadOnlyList<CheatToken> KeyTokens()
        {
            if (keyTokens != null && parsedFrom == keyboardCode) return keyTokens;

            parsedFrom = keyboardCode;
            keyTokens = new List<CheatToken>();
            foreach (char character in keyboardCode ?? string.Empty)
            {
                var key = CheatInputReader.Parse(character);
                if (key != CheatKey.None) keyTokens.Add(new CheatToken(key));
            }
            return keyTokens;
        }

        // Rebuilt every call in the editor so inspector edits take effect
        // live; at runtime the list never changes, so it is cached once.
        IReadOnlyList<CheatToken> ButtonTokens()
        {
            if (buttonTokens != null && !Application.isEditor) return buttonTokens;

            buttonTokens = new List<CheatToken>();
            foreach (var button in controllerCode)
                if (button != CheatButton.None) buttonTokens.Add(new CheatToken(button));
            return buttonTokens;
        }

        string Problem
        {
            get
            {
                if (string.IsNullOrWhiteSpace(id)) return "This cheat has no id — the event would fire with an empty string.";

                int keys = 0;
                foreach (char character in keyboardCode ?? string.Empty)
                {
                    if (CheatInputReader.Parse(character) == CheatKey.None)
                        return $"'{character}' is not a letter or a digit — the keyboard code can only use A-Z and 0-9.";
                    keys++;
                }
                if (keys < MinLength || keys > MaxLength)
                    return $"The keyboard code is {keys} long; it must be {MinLength}-{MaxLength}.";

                int buttons = 0;
                foreach (var button in controllerCode)
                {
                    if (button == CheatButton.None) return "The controller code has an empty slot.";
                    buttons++;
                }
                if (buttons < MinLength || buttons > MaxLength)
                    return $"The controller code is {buttons} long; it must be {MinLength}-{MaxLength}.";

                return null;
            }
        }

        bool HasProblem => !string.IsNullOrEmpty(Problem);
    }

    /// <summary>
    /// Every cheat in the game plus the look and timing of the console that
    /// reads them — the one asset a designer opens to add a code. Loaded from
    /// Resources like <see cref="MenuTheme"/> so the cheats page works in any
    /// scene without wiring, and drawn inline on the <see cref="CheatManager"/>
    /// so the codes and the event that fires them sit on one inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_Cheats", menuName = "FiniteRunner/Cheat Definition")]
    public class CheatDefinition : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_Cheats";

        [TitleGroup("Cheats")]
        [ListDrawerSettings(DraggableItems = true, ShowIndexLabels = true,
                            ListElementLabelName = nameof(CheatEntry.id))]
        [SerializeField] List<CheatEntry> cheats = new();

        [TitleGroup("Console — layout")]
        [Tooltip("How many of the most recent presses stay on screen. The matcher only ever looks this far back, so no code may be longer.")]
        [PropertyRange(CheatEntry.MaxLength, 16)]
        [SerializeField] int bufferLength = 12;

        [TitleGroup("Console — layout")]
        [Tooltip("Side of one glyph on the input strip.")]
        [PropertyRange(32f, 120f), SuffixLabel("px", true)]
        [SerializeField] float glyphSize = 72f;

        [TitleGroup("Console — layout")]
        [Tooltip("Gap between glyphs.")]
        [PropertyRange(0f, 40f), SuffixLabel("px", true)]
        [SerializeField] float glyphSpacing = 10f;

        [TitleGroup("Console — reveal")]
        [Tooltip("How long the cheat id stays up before the console wipes itself. Input is blocked for all of it.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        [SerializeField] float holdSeconds = 3f;

        [TitleGroup("Console — reveal")]
        [Tooltip("Length of each glitch burst — one when the code lands, one when the console wipes.")]
        [PropertyRange(0.1f, 1.5f), SuffixLabel("s", true)]
        [SerializeField] float glitchSeconds = 0.45f;

        [TitleGroup("Console — reveal")]
        [Tooltip("How far the page is thrown around at the start of a burst. It decays to nothing over the burst.")]
        [PropertyRange(0f, 60f), SuffixLabel("px", true)]
        [SerializeField] float shakeAmplitude = 26f;

        [TitleGroup("Console — reveal")]
        [Tooltip("Tear bars drawn across the page during a burst. 0 = shake only.")]
        [PropertyRange(0, 12)]
        [SerializeField] int tearBars = 6;

        public IReadOnlyList<CheatEntry> Cheats => cheats;
        public int BufferLength => Mathf.Max(CheatEntry.MaxLength, bufferLength);
        public float GlyphSize => glyphSize;
        public float GlyphSpacing => glyphSpacing;
        public float HoldSeconds => holdSeconds;
        public float GlitchSeconds => glitchSeconds;
        public float ShakeAmplitude => shakeAmplitude;
        public int TearBars => tearBars;

        /// <summary>Replaces the whole cheat list. Used by the editor asset builder to seed the test codes.</summary>
        public void SetCheats(List<CheatEntry> entries) => cheats = entries;

        static CheatDefinition cached;

        /// <summary>
        /// The cheat asset, or an empty throwaway instance if none is in a
        /// Resources folder — the cheats page still draws and echoes input,
        /// it just has nothing to unlock, rather than throwing every frame.
        /// </summary>
        public static CheatDefinition Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<CheatDefinition>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(CheatDefinition)} at Resources/{ResourcePath} — " +
                                 "the cheats page will echo input but unlock nothing.");
                cached = CreateInstance<CheatDefinition>();
            }
            return cached;
        }
    }
}
