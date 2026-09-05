using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>Which of the two bindable devices a control belongs to.</summary>
    public enum BindingDevice { Keyboard, Gamepad }

    /// <summary>
    /// The context a <see cref="GameAction"/> lives in. Two actions may share
    /// a control when they can never be read at the same time — the ship and
    /// the car are different scenes, so both steer on A/D — while General
    /// actions (the camera, shared by both games) conflict with everything.
    /// </summary>
    public enum BindingSection { Ship, Car, General }

    /// <summary>
    /// Every rebindable gameplay action. Append-only: the names are the save
    /// format, and the enum order is the order the CONTROLS screen lists
    /// them in. Axis inputs are two directional actions (steer left / steer
    /// right, accelerate / brake), each bound to one key and one pad control.
    /// </summary>
    public enum GameAction
    {
        ShipSteerLeft, ShipSteerRight, ShipDashLeft, ShipDashRight,
        CarSteerLeft, CarSteerRight, CarAccelerate, CarBrake, CarHandbrake, CarRespawn,
        CityMap, RadioPrevious, RadioNext,
        CameraCycle, CameraLookBack,
        CameraPanLeft, CameraPanRight, CameraPanUp, CameraPanDown
    }

    /// <summary>
    /// The player's key and gamepad bindings — the one action → control table
    /// every gameplay reader polls through, so the CONTROLS screen can change
    /// what drives the ship, the car, the camera and the radio. The rules it
    /// enforces:
    /// <list type="bullet">
    /// <item><b>Menu chords are never bindable.</b> Esc / Enter / Backspace and
    /// Start are the menus' Confirm, Back and pause-open, hard-wired in
    /// <see cref="MenuNavigator"/>; <see cref="IsReserved(Key)"/> keeps them
    /// out of every binding, so a bad rebind can never lock the player out of
    /// a menu. Menus keep polling the devices directly — only gameplay reads
    /// through here. Gamepad B is the menus' Back but NOT reserved, for the
    /// same reason Space is not: menus only run with gameplay frozen, so the
    /// handbrake (its default) and Back never meet. A is left free of any
    /// default because the dialogue box and the cinema skip read it while the
    /// world runs.</item>
    /// <item><b>A control is unique inside its context</b> (<see cref="BindingSection"/>):
    /// binding a control another action of the same context already holds
    /// SWAPS the two (the other action takes this one's old control) —
    /// never a silent overwrite, never two actions on one button.</item>
    /// <item><b>Keyboard wins over the pad</b> on an axis: a held key is the
    /// digital value, the stick only counts when no key is down — the rule
    /// the ship and car readers always had.</item>
    /// <item>One key and one pad control per action, stored by NAME as a
    /// single JSON string in PlayerPrefs (the <see cref="UserSettings"/>
    /// shape), so a future enum edit still loads what it can.</item>
    /// </list>
    /// </summary>
    public static class ControlBindings
    {
        const string PrefsKey = "settings.bindings";

        /// <summary>Raised after any binding changes (a rebind, a swap, a reset). Rows and prompts re-read on this.</summary>
        public static event Action Changed;

        struct Binding
        {
            public Key key;
            public PadControl pad;
        }

        readonly struct Default
        {
            public readonly GameAction action;
            public readonly BindingSection section;
            public readonly Key key;
            public readonly PadControl pad;

            public Default(GameAction action, BindingSection section, Key key, PadControl pad)
            {
                this.action = action;
                this.section = section;
                this.key = key;
                this.pad = pad;
            }
        }

        // No default collides inside any context: Ship {A D N M}, Car {A D W
        // S Space R M 5 6}, General {Tab RShift arrows} — and the pads likewise.
        static readonly Default[] Defaults =
        {
            new(GameAction.ShipSteerLeft, BindingSection.Ship, Key.A, PadControl.LeftStickLeft),
            new(GameAction.ShipSteerRight, BindingSection.Ship, Key.D, PadControl.LeftStickRight),
            new(GameAction.ShipDashLeft, BindingSection.Ship, Key.N, PadControl.LeftShoulder),
            new(GameAction.ShipDashRight, BindingSection.Ship, Key.M, PadControl.RightShoulder),
            new(GameAction.CarSteerLeft, BindingSection.Car, Key.A, PadControl.LeftStickLeft),
            new(GameAction.CarSteerRight, BindingSection.Car, Key.D, PadControl.LeftStickRight),
            new(GameAction.CarAccelerate, BindingSection.Car, Key.W, PadControl.RightTrigger),
            new(GameAction.CarBrake, BindingSection.Car, Key.S, PadControl.LeftTrigger),
            new(GameAction.CarHandbrake, BindingSection.Car, Key.Space, PadControl.ButtonEast),
            new(GameAction.CarRespawn, BindingSection.Car, Key.R, PadControl.ButtonNorth),
            new(GameAction.CityMap, BindingSection.Car, Key.M, PadControl.DpadUp),
            new(GameAction.RadioPrevious, BindingSection.Car, Key.Digit5, PadControl.DpadLeft),
            new(GameAction.RadioNext, BindingSection.Car, Key.Digit6, PadControl.DpadRight),
            new(GameAction.CameraCycle, BindingSection.General, Key.Tab, PadControl.Select),
            new(GameAction.CameraLookBack, BindingSection.General, Key.RightShift, PadControl.RightStickPress),
            new(GameAction.CameraPanLeft, BindingSection.General, Key.LeftArrow, PadControl.RightStickLeft),
            new(GameAction.CameraPanRight, BindingSection.General, Key.RightArrow, PadControl.RightStickRight),
            new(GameAction.CameraPanUp, BindingSection.General, Key.UpArrow, PadControl.RightStickUp),
            new(GameAction.CameraPanDown, BindingSection.General, Key.DownArrow, PadControl.RightStickDown)
        };

        // Confirm (Enter / numpad Enter), Back (Esc / Backspace), the
        // pause-open Start, and keys the OS or IME owns. Space and gamepad B
        // are deliberately NOT here: they are the handbrake, and menus only
        // run with gameplay frozen, so the two never meet. Nor is A: it is
        // the dialogue advance / cinema skip, read over live gameplay, so no
        // default sits on it — but a player who wants it there may bind it.
        static readonly HashSet<Key> ReservedKeys = new()
        {
            Key.None, Key.Escape, Key.Enter, Key.NumpadEnter, Key.Backspace,
            Key.LeftMeta, Key.RightMeta, Key.ContextMenu, Key.IMESelected
        };

        static readonly HashSet<PadControl> ReservedPads = new()
        {
            PadControl.None, PadControl.Start
        };

        static readonly int ActionCount = Enum.GetValues(typeof(GameAction)).Length;
        static Binding[] bindings;
        static bool loaded;

        /// <summary>Every action, in screen order.</summary>
        public static IEnumerable<GameAction> Actions
        {
            get
            {
                for (int i = 0; i < ActionCount; i++) yield return (GameAction)i;
            }
        }

        public static Key KeyFor(GameAction action)
        {
            EnsureLoaded();
            return bindings[(int)action].key;
        }

        public static PadControl PadFor(GameAction action)
        {
            EnsureLoaded();
            return bindings[(int)action].pad;
        }

        public static BindingSection SectionOf(GameAction action) => Defaults[(int)action].section;
        public static Key DefaultKey(GameAction action) => Defaults[(int)action].key;
        public static PadControl DefaultPad(GameAction action) => Defaults[(int)action].pad;

        /// <summary>A key the menus own — never capturable, never bindable.</summary>
        public static bool IsReserved(Key key) => ReservedKeys.Contains(key);

        /// <summary>A pad button the menus own — never capturable, never bindable.</summary>
        public static bool IsReserved(PadControl control) => ReservedPads.Contains(control);

        /// <summary>Do two actions compete for one control? Same context, or either is General.</summary>
        public static bool Conflicts(GameAction a, GameAction b)
        {
            if (a == b) return false;
            var sa = SectionOf(a);
            var sb = SectionOf(b);
            return sa == sb || sa == BindingSection.General || sb == BindingSection.General;
        }

        /// <summary>
        /// Binds a key to an action. An action of the same context already on
        /// that key takes this action's old key instead (a swap) and is
        /// returned, so the screen can say so; null when nothing was swapped.
        /// A reserved key or an unchanged binding is a no-op.
        /// </summary>
        public static GameAction? Set(GameAction action, Key key)
        {
            EnsureLoaded();
            if (IsReserved(key) || bindings[(int)action].key == key) return null;

            GameAction? swapped = null;
            Key old = bindings[(int)action].key;
            foreach (var other in Actions)
            {
                if (!Conflicts(action, other) || bindings[(int)other].key != key) continue;
                swapped = other;
                // The old key goes to the swapped action — unless a third
                // action in ITS context already holds it, which would put two
                // actions on one key; then it is simply unbound ("—").
                bindings[(int)other].key = HeldByAnother(other, old, action) ? Key.None : old;
                break;
            }

            bindings[(int)action].key = key;
            Commit();
            return swapped;
        }

        /// <summary>Pad twin of <see cref="Set(GameAction, Key)"/>.</summary>
        public static GameAction? Set(GameAction action, PadControl control)
        {
            EnsureLoaded();
            if (IsReserved(control) || bindings[(int)action].pad == control) return null;

            GameAction? swapped = null;
            PadControl old = bindings[(int)action].pad;
            foreach (var other in Actions)
            {
                if (!Conflicts(action, other) || bindings[(int)other].pad != control) continue;
                swapped = other;
                bindings[(int)other].pad = HeldByAnother(other, old, action) ? PadControl.None : old;
                break;
            }

            bindings[(int)action].pad = control;
            Commit();
            return swapped;
        }

        /// <summary>Back to the authored table. Saves and raises <see cref="Changed"/>.</summary>
        public static void ResetDefaults()
        {
            EnsureLoaded();
            ApplyDefaults();
            Commit();
        }

        // ------------------------------------------------------------ reading

        /// <summary>Held on either device.</summary>
        public static bool IsPressed(GameAction action)
        {
            EnsureLoaded();
            var b = bindings[(int)action];
            var key = KeyControlFor(b.key);
            if (key != null && key.isPressed) return true;
            return PadControls.IsPressed(Gamepad.current, b.pad);
        }

        /// <summary>Pressed this frame on either device.</summary>
        public static bool WasPressedThisFrame(GameAction action)
        {
            EnsureLoaded();
            var b = bindings[(int)action];
            var key = KeyControlFor(b.key);
            if (key != null && key.wasPressedThisFrame) return true;
            return PadControls.WasPressedThisFrame(Gamepad.current, b.pad);
        }

        /// <summary>-1 / 0 / +1 from the two keys alone.</summary>
        public static float KeyboardAxis(GameAction negative, GameAction positive)
        {
            EnsureLoaded();
            float axis = 0f;
            var neg = KeyControlFor(bindings[(int)negative].key);
            var pos = KeyControlFor(bindings[(int)positive].key);
            if (neg != null && neg.isPressed) axis -= 1f;
            if (pos != null && pos.isPressed) axis += 1f;
            return axis;
        }

        /// <summary>positive − negative off the pad (analog for sticks and triggers), 0 inside the deadzone.</summary>
        public static float PadAxis(GameAction negative, GameAction positive, float deadzone)
        {
            EnsureLoaded();
            var pad = Gamepad.current;
            if (pad == null) return 0f;
            float axis = PadControls.ReadValue(pad, bindings[(int)positive].pad) -
                         PadControls.ReadValue(pad, bindings[(int)negative].pad);
            return Mathf.Abs(axis) > deadzone ? Mathf.Clamp(axis, -1f, 1f) : 0f;
        }

        /// <summary>The keyboard's digital value when a key is down, else the pad's analog value.</summary>
        public static float Axis(GameAction negative, GameAction positive, float deadzone = 0.1f)
        {
            float keys = KeyboardAxis(negative, positive);
            return keys != 0f ? keys : PadAxis(negative, positive, deadzone);
        }

        /// <summary>The live key control, or null for None, an out-of-range key or no keyboard.</summary>
        public static KeyControl KeyControlFor(Key key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || key == Key.None) return null;
            int index = (int)key - 1; // the indexer throws on None and past the table
            var all = keyboard.allKeys;
            return index >= 0 && index < all.Count ? all[index] : null;
        }

        // ---------------------------------------------------------- lifecycle

        // Domain reload is off in this project: statics survive a play
        // session, so the file is re-read and stale listeners dropped here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Changed = null;
            loaded = false;
            EnsureLoaded();
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            bindings = new Binding[ActionCount];
            ApplyDefaults();
            Load();
            Sanitize();
        }

        static void ApplyDefaults()
        {
            foreach (var d in Defaults)
                bindings[(int)d.action] = new Binding { key = d.key, pad = d.pad };
        }

        static bool HeldByAnother(GameAction owner, Key key, GameAction except)
        {
            if (key == Key.None) return false;
            foreach (var third in Actions)
                if (third != owner && third != except && Conflicts(owner, third) && bindings[(int)third].key == key)
                    return true;
            return false;
        }

        static bool HeldByAnother(GameAction owner, PadControl control, GameAction except)
        {
            if (control == PadControl.None) return false;
            foreach (var third in Actions)
                if (third != owner && third != except && Conflicts(owner, third) && bindings[(int)third].pad == control)
                    return true;
            return false;
        }

        // Walks the actions in order: a reserved control, or one an EARLIER
        // action of the same context already holds, falls back to the
        // default — and to None if the default is taken too. So a hand-edited
        // or stale file can never put two actions on one control.
        static void Sanitize()
        {
            for (int i = 0; i < ActionCount; i++)
            {
                var action = (GameAction)i;

                Key key = bindings[i].key;
                if (IsReserved(key) || TakenBefore(action, key))
                {
                    key = DefaultKey(action);
                    if (TakenBefore(action, key)) key = Key.None;
                }
                bindings[i].key = key;

                PadControl pad = bindings[i].pad;
                if (IsReserved(pad) || TakenBefore(action, pad))
                {
                    pad = DefaultPad(action);
                    if (TakenBefore(action, pad)) pad = PadControl.None;
                }
                bindings[i].pad = pad;
            }
        }

        static bool TakenBefore(GameAction action, Key key)
        {
            if (key == Key.None) return false;
            for (int j = 0; j < (int)action; j++)
                if (Conflicts(action, (GameAction)j) && bindings[j].key == key) return true;
            return false;
        }

        static bool TakenBefore(GameAction action, PadControl control)
        {
            if (control == PadControl.None) return false;
            for (int j = 0; j < (int)action; j++)
                if (Conflicts(action, (GameAction)j) && bindings[j].pad == control) return true;
            return false;
        }

        static void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------- storage

        // Bumped when a default moves. A file older than the bump has its
        // entries that still sit on the RETIRED default moved to the new one
        // — a deliberate rebind (anything else) is left alone.
        const int FileVersion = 1;

        static readonly (GameAction action, PadControl retiredPad)[] RetiredPadDefaults =
        {
            (GameAction.CarHandbrake, PadControl.ButtonSouth), // v1: handbrake moved A → B, freeing A for dialogue / cinema skip
        };

        [Serializable]
        class BindingFile
        {
            public int version;
            public List<BindingEntry> entries = new();
        }

        [Serializable]
        struct BindingEntry
        {
            public string action;
            public string key;
            public string pad;
        }

        static void Save()
        {
            var file = new BindingFile { version = FileVersion };
            foreach (var action in Actions)
                file.entries.Add(new BindingEntry
                {
                    action = action.ToString(),
                    key = bindings[(int)action].key.ToString(),
                    pad = bindings[(int)action].pad.ToString()
                });
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(file));
            PlayerPrefs.Save();
        }

        // Overlays whatever the file names onto the defaults; an entry that
        // no longer parses (a renamed action, a dropped control) is skipped.
        static void Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            BindingFile file;
            try { file = JsonUtility.FromJson<BindingFile>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"{nameof(ControlBindings)}: saved bindings unreadable, using defaults ({e.Message}).");
                return;
            }
            if (file?.entries == null) return;

            foreach (var entry in file.entries)
            {
                if (!Enum.TryParse(entry.action, out GameAction action)) continue;
                if (Enum.TryParse(entry.key, out Key key)) bindings[(int)action].key = key;
                if (Enum.TryParse(entry.pad, out PadControl pad)) bindings[(int)action].pad = pad;
            }

            if (file.version >= FileVersion) return;
            foreach (var (action, retiredPad) in RetiredPadDefaults)
                if (bindings[(int)action].pad == retiredPad)
                    bindings[(int)action].pad = DefaultPad(action);
        }
    }
}
