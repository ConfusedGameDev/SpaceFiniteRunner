using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace FiniteRunner
{
    /// <summary>
    /// The cheat model: a rolling buffer of the player's last presses, the
    /// matcher that watches its tail for a code, and the event that fires when
    /// one lands. It owns no UI and reads no input — <see cref="CheatConsole"/>
    /// feeds it tokens and draws the buffer back — so cheats can just as well
    /// be entered from a future in-game chord or a debug page.
    ///
    /// A token pushed from a different device than the buffer holds wipes the
    /// buffer first: half a pad code followed by half a typed one must never
    /// add up to a match, and the strip on screen has to agree with the glyphs
    /// it is drawing.
    ///
    /// Which cheats are on is static, so it survives the scene load between
    /// the menu and gameplay; the UnityEvent is for scene wiring, and the
    /// static <see cref="CheatActivated"/> for code that spawns later.
    /// </summary>
    [DisallowMultipleComponent]
    public class CheatManager : MonoBehaviour
    {
        /// <summary>UnityEvent carrying the id of the cheat that just unlocked.</summary>
        [System.Serializable]
        public class CheatUnlockedEvent : UnityEvent<string> { }

        static CheatManager instance;

        /// <summary>
        /// The manager, auto-created on first use if the scene has none — the
        /// cheats page must work in the bare MainMenu scene. A hand-placed one
        /// (there to wire <see cref="onCheatActivated"/> in the inspector)
        /// always wins.
        /// </summary>
        public static CheatManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<CheatManager>();
                    if (instance == null)
                    {
                        var go = new GameObject(nameof(CheatManager));
                        instance = go.AddComponent<CheatManager>();
                        instance.autoCreated = true;
                        // Only the auto-created one persists, so a cheat
                        // entered in the menu is still on after the scene
                        // load; a scene object carrying inspector wiring
                        // belongs to its own scene. Guarded because editor
                        // tooling can reach Instance outside play mode, where
                        // DontDestroyOnLoad throws.
                        if (Application.isPlaying) DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [InlineEditor]
        [Tooltip("The codes. Left empty, the asset in Resources is used.")]
        [SerializeField] CheatDefinition definition;

        [Tooltip("Fired with the cheat's id the moment its code lands. Wire gameplay reactions here.")]
        [SerializeField] CheatUnlockedEvent onCheatActivated = new();

        /// <summary>Same as <see cref="onCheatActivated"/>, for objects that are not around at wiring time.</summary>
        public static event System.Action<string> CheatActivated;

        static readonly HashSet<string> activated = new();

        bool autoCreated;

        readonly List<CheatToken> buffer = new();

        /// <summary>Raised whenever the buffer gains an entry or is wiped.</summary>
        public event System.Action BufferChanged;

        /// <summary>The presses still on screen, oldest first.</summary>
        public IReadOnlyList<CheatToken> Buffer => buffer;

        public CheatDefinition Definition => definition != null ? definition : definition = CheatDefinition.Load();

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        [Tooltip("Cheats unlocked so far this session. Static: it survives scene loads.")]
        static IEnumerable<string> ActiveCheats => activated;

        /// <summary>True once the cheat with this id has been entered this session.</summary>
        public static bool IsActive(string id) => id != null && activated.Contains(id);

        // A hand-placed manager always beats the auto-created stub: it is the
        // one carrying the inspector wiring, and the unlock set is static so
        // nothing is lost in the swap.
        void Awake()
        {
            if (instance != null && instance != this)
            {
                if (!instance.autoCreated)
                {
                    Destroy(this);
                    return;
                }
                Destroy(instance.gameObject);
            }
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// Records one press and reports the cheat it completed, if any.
        /// Matching looks only at the tail of the buffer, so a mistyped run of
        /// presses costs nothing: the player just keeps going and the correct
        /// code still lands the moment its last button is pressed.
        /// </summary>
        public bool Push(CheatToken token, out string cheatId)
        {
            cheatId = null;
            if (token.IsEmpty) return false;

            // Device switch: the strip cannot mix key caps and pad glyphs, and
            // a half-typed code must not survive picking up a controller.
            bool gamepad = token.Button != CheatButton.None;
            if (buffer.Count > 0 && (buffer[^1].Button != CheatButton.None) != gamepad) buffer.Clear();

            buffer.Add(token);
            int limit = Definition.BufferLength;
            while (buffer.Count > limit) buffer.RemoveAt(0);

            BufferChanged?.Invoke();

            var match = FindMatch(gamepad);
            if (match == null) return false;

            cheatId = match.id;
            activated.Add(cheatId);
            onCheatActivated?.Invoke(cheatId);
            CheatActivated?.Invoke(cheatId);
            return true;
        }

        /// <summary>Wipes the buffer — the console does this after a code resolves.</summary>
        public void ClearBuffer()
        {
            if (buffer.Count == 0) return;
            buffer.Clear();
            BufferChanged?.Invoke();
        }

        /// <summary>Forgets every unlock. Editor-side convenience for retesting a code.</summary>
        [TitleGroup("Debug"), Button("Reset Activated Cheats")]
        public static void ResetActivated() => activated.Clear();

        CheatEntry FindMatch(bool gamepad)
        {
            foreach (var cheat in Definition.Cheats)
            {
                if (cheat == null) continue;
                var sequence = cheat.Sequence(gamepad);
                if (sequence.Count == 0 || sequence.Count > buffer.Count) continue;

                bool matches = true;
                int offset = buffer.Count - sequence.Count;
                for (int i = 0; i < sequence.Count && matches; i++)
                    matches = buffer[offset + i].Equals(sequence[i]);

                if (matches) return cheat;
            }
            return null;
        }
    }
}
