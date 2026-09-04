using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The menu input poller shared by the main menu and the pause menu:
    /// d-pad / left stick / WASD / arrows collapsed to one step per tap with
    /// hold-to-repeat (delay then interval, both from the theme), plus the
    /// Confirm / Back / pause-toggle chords. Lives outside the MonoBehaviours
    /// so every themed menu navigates identically — and keeps the project's
    /// polling pattern (there is no .inputactions asset).
    /// </summary>
    public class MenuNavigator
    {
        readonly MenuTheme theme;
        float verticalTimer;
        float horizontalTimer;
        int verticalLast;
        int horizontalLast;

        public MenuNavigator(MenuTheme theme) => this.theme = theme;

        /// <summary>-1/0/+1 vertical step this frame, repeat-aware. +1 is up.</summary>
        public int StepVertical(float dt) => Step(RawVertical(), dt, ref verticalTimer, ref verticalLast);

        /// <summary>-1/0/+1 horizontal step this frame, repeat-aware. +1 is right.</summary>
        public int StepHorizontal(float dt) => Step(RawHorizontal(), dt, ref horizontalTimer, ref horizontalLast);

        /// <summary>
        /// Adopts whatever is held RIGHT NOW as already-stepped, so navigation
        /// that resumes after someone else read the devices (a rebind capture
        /// that just bound a d-pad direction) does not fire one more step off
        /// the same press.
        /// </summary>
        public void Sync()
        {
            verticalLast = RawVertical();
            horizontalLast = RawHorizontal();
            verticalTimer = horizontalTimer = theme.RepeatDelay;
        }

        public static bool ConfirmPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame ||
                                     keyboard.numpadEnterKey.wasPressedThisFrame ||
                                     keyboard.spaceKey.wasPressedThisFrame))
                return true;

            return Gamepad.current is { buttonSouth: { wasPressedThisFrame: true } };
        }

        /// <summary>
        /// Is the confirm chord being HELD right now — Enter / numpad Enter or
        /// gamepad A, read as a level rather than an edge, for hold-to-confirm
        /// gestures (the cinema skip). Space is deliberately left out: it is
        /// the city car's handbrake and a menu tap key, and a hold must never
        /// be something the player is already leaning on.
        /// </summary>
        public static bool ConfirmHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed))
                return true;

            return Gamepad.current is { buttonSouth: { isPressed: true } };
        }

        public static bool BackPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame ||
                                     keyboard.backspaceKey.wasPressedThisFrame))
                return true;

            if (Mouse.current is { rightButton: { wasPressedThisFrame: true } }) return true;

            return Gamepad.current is { buttonEast: { wasPressedThisFrame: true } };
        }

        /// <summary>Esc or gamepad Start — the chord that opens and closes the pause menu.</summary>
        public static bool PauseTogglePressed()
        {
            return Keyboard.current is { escapeKey: { wasPressedThisFrame: true } } ||
                   Gamepad.current is { startButton: { wasPressedThisFrame: true } };
        }

        /// <summary>
        /// The city-map chord (<see cref="GameAction.CityMap"/> — M or the
        /// d-pad Up by default; rebindable on the CONTROLS screen). Deliberately
        /// a different chord from <see cref="PauseTogglePressed"/> (Start),
        /// <see cref="BackPressed"/> (B/East) and <see cref="CameraCyclePressed"/>:
        /// the map is its own screen, not a page of the pause menu, and all
        /// of those buttons already mean something everywhere else in the UI.
        /// The d-pad is free in the car — driving is on the stick and the face
        /// buttons — while it is the menus' own navigator inside them, which
        /// is exactly where the map can never open.
        /// </summary>
        public static bool MapTogglePressed() => ControlBindings.WasPressedThisFrame(GameAction.CityMap);

        /// <summary>
        /// The camera-view chord (<see cref="GameAction.CameraCycle"/> — Tab
        /// or the gamepad's Back/View button by default; rebindable) — cycles
        /// the chase camera's view (far / close / first person). Read over
        /// live gameplay only — the rig gates it on the time scale, so a
        /// frozen menu, the map or the game-over prompt never flips the view
        /// underneath.
        /// </summary>
        public static bool CameraCyclePressed() => ControlBindings.WasPressedThisFrame(GameAction.CameraCycle);

        int RawVertical()
        {
            int direction = 0;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) direction++;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) direction--;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.up.isPressed) direction++;
                if (pad.dpad.down.isPressed) direction--;

                float y = pad.leftStick.ReadValue().y;
                if (y > theme.StickDeadZone) direction++;
                else if (y < -theme.StickDeadZone) direction--;
            }

            return Mathf.Clamp(direction, -1, 1);
        }

        int RawHorizontal()
        {
            int direction = 0;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) direction++;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) direction--;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.right.isPressed) direction++;
                if (pad.dpad.left.isPressed) direction--;

                float x = pad.leftStick.ReadValue().x;
                if (x > theme.StickDeadZone) direction++;
                else if (x < -theme.StickDeadZone) direction--;
            }

            return Mathf.Clamp(direction, -1, 1);
        }

        // One step on press, then repeat after a delay — a tap moves exactly
        // one row whether it came from the d-pad or the stick.
        int Step(int raw, float dt, ref float timer, ref int last)
        {
            if (raw == 0)
            {
                last = 0;
                timer = 0f;
                return 0;
            }

            if (raw != last)
            {
                last = raw;
                timer = theme.RepeatDelay;
                return raw;
            }

            timer -= dt;
            if (timer > 0f) return 0;
            timer = theme.RepeatInterval;
            return raw;
        }
    }

    /// <summary>
    /// Builders for screens that appear in more than one menu. The settings
    /// page exists in both the main menu and the pause menu; building it here
    /// keeps the two pixel-identical — a new row (like Language was) is added
    /// once and shows up in both.
    /// </summary>
    public static class MenuScreenFactory
    {
        /// <summary>
        /// Menus need an EventSystem for mouse input, and not every scene
        /// carries one (the main-menu scene, the city chase). Creates a
        /// polling-friendly one on demand; a no-op where the scene has its own.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// A "really do this?" page: title, ARE YOU SURE?, then YES / NO.
        /// Focus starts on NO — destructive actions need a deliberate step.
        /// Used by the main menu's EXIT and both pause-menu exits.
        /// </summary>
        public static MenuScreen BuildConfirm(RectTransform parent, MenuTheme theme, MenuTextId titleId,
                                              System.Action onYes, System.Action onNo)
            => BuildConfirm(parent, theme, titleId, MenuTextId.AreYouSure, onYes, onNo);

        /// <summary>Variant with its own question line (the debug menu's "reload the scene?").</summary>
        public static MenuScreen BuildConfirm(RectTransform parent, MenuTheme theme, MenuTextId titleId,
                                              MenuTextId questionId, System.Action onYes, System.Action onNo)
        {
            var screen = MenuScreen.Create($"Confirm_{titleId}", parent, theme, 0f, 0f);
            screen.SetTitle(titleId);
            screen.AddLabel("Question", new Vector2(0f, 66f), new Vector2(900f, 60f),
                            questionId, 36, theme.TextPrimary, theme.BodyFont,
                            TextAnchor.MiddleCenter, theme.TitleLead);

            screen.AddRow<MenuRow>(MenuTextId.Yes).Activated += onYes;
            screen.AddRow<MenuRow>(MenuTextId.No).Activated += onNo;
            screen.SetFocus(1); // default to the safe answer
            return screen;
        }

        /// <summary>
        /// The settings page. <paramref name="openControls"/> is what its
        /// CONTROLS row does — each host slides its own <see cref="ControlsScreen"/>
        /// in, because the page-to-page transition is the host's.
        /// <paramref name="deleteProgress"/>, when given, adds a last
        /// DELETE CAMPAIGN PROGRESS row (the main menu's page only — the
        /// host owns the confirm) and tightens the row metrics so seven rows
        /// keep the six-row page's reach.
        /// </summary>
        public static MenuScreen BuildSettings(RectTransform parent, MenuTheme theme, System.Action openControls,
                                               System.Action deleteProgress = null)
        {
            var screen = MenuScreen.Create("SettingsScreen", parent, theme, 0f, 130f);
            screen.SetTitle(MenuTextId.Settings);
            // Seven rows at 74/14 run 130 -> -398: the same reach as six at 86/18.
            if (deleteProgress != null) screen.SetRowMetrics(74f, 14f);

            screen.AddRow<MenuSlider>(MenuTextId.MasterVolume)
                  .Configure(UserSettings.MasterVolume, 5, v => UserSettings.MasterVolume = v);
            screen.AddRow<MenuSlider>(MenuTextId.MusicVolume)
                  .Configure(UserSettings.MusicVolume, 5, v => UserSettings.MusicVolume = v);
            screen.AddRow<MenuSlider>(MenuTextId.FxVolume)
                  .Configure(UserSettings.SfxVolume, 5, v => UserSettings.SfxVolume = v);
            screen.AddRow<MenuToggle>(MenuTextId.Subtitles)
                  .Configure(UserSettings.Subtitles, v => UserSettings.Subtitles = v);

            // Each language names itself, so the row stays readable no matter
            // which one is active; every LocalizedLabel refreshes on change.
            var names = new[]
            {
                MenuTextLibrary.LanguageDisplayName(MenuLanguage.English),
                MenuTextLibrary.LanguageDisplayName(MenuLanguage.Spanish),
                MenuTextLibrary.LanguageDisplayName(MenuLanguage.Japanese),
                MenuTextLibrary.LanguageDisplayName(MenuLanguage.French)
            };
            screen.AddRow<MenuChoice>(MenuTextId.Language)
                  .Configure(names, (int)UserSettings.Language, i => UserSettings.Language = (MenuLanguage)i);

            // Six rows at the theme's 86/18 metrics run 130 -> -390, the same
            // reach as the pause list, clear of the footer strip.
            screen.AddRow<MenuRow>(MenuTextId.Controls).Activated += openControls;
            if (deleteProgress != null)
                screen.AddRow<MenuRow>(MenuTextId.DeleteProgress).Activated += deleteProgress;

            return screen;
        }
    }
}
