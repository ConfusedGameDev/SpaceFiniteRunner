using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// Pause system: Esc or gamepad Start freezes the game (timeScale 0 +
    /// motor pause) and shows Resume / Settings / Exit on the shared themed
    /// menu framework — same rows, focus easing and slide transitions as the
    /// main menu, and the exact same settings page (volumes, subtitles,
    /// language) via <see cref="MenuScreenFactory.BuildSettings"/>, applied
    /// live and persisted by <see cref="UserSettings"/>. Only opens during
    /// active gameplay — never over the tuning screen, the result screen, or
    /// the main-menu overlay, where those buttons mean other things. Built
    /// from code on its own overlay canvas. Two ways to get one: spawned by
    /// the GameManager in the runner scene (ship-aware — freezes the motor
    /// too), or placed as a bare GameObject in a scene with no ship (the city
    /// chase), where it pauses on timeScale alone. Everything animates on
    /// unscaled time, because the whole point is that scaled time is stopped.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        const int SortingOrder = 20; // above the HUD (10) and messages (15), below the main menu (30)

        GameManager gameManager;
        ShipMotor motor;
        MenuTheme theme;
        MenuNavigator nav;

        GameObject panel;
        RectTransform panelRect;
        MenuScreen pauseScreen;
        MenuScreen settingsScreen;
        MenuScreen confirmMenuScreen; // "exit to main menu — are you sure?"
        MenuScreen confirmQuitScreen; // "quit game — are you sure?"
        MenuScreen current;
        PromptStrip footer;
        AudioSource ui;

        bool isPaused;
        float lockTimer;   // input frozen while a screen transition plays
        float openedTime;  // grace so the press that paused can't also confirm

        public static PauseMenu Spawn(GameManager gameManager, ShipMotor motor)
        {
            var go = new GameObject("PauseMenu");
            var menu = go.AddComponent<PauseMenu>();
            menu.gameManager = gameManager;
            menu.motor = motor;
            menu.theme = MenuTheme.Load();
            menu.nav = new MenuNavigator(menu.theme);
            menu.Build();
            return menu;
        }

        // Spawn() builds synchronously right after AddComponent, so a menu
        // still unbuilt by Start() was placed in a scene by hand (the city
        // chase) — build it there with no ship references.
        void Start()
        {
            if (theme != null) return;
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            Build();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (!isPaused)
            {
                if (MenuNavigator.PauseTogglePressed() && CanPause()) Pause();
                return;
            }

            if (lockTimer > 0f)
            {
                lockTimer -= dt;
                return;
            }
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            // Esc/Start/B all step outward: any sub-screen (settings or a
            // confirm) back to the pause list, the pause list back to the
            // game. Quitting is never one press.
            if (MenuNavigator.PauseTogglePressed() || MenuNavigator.BackPressed())
            {
                if (current != pauseScreen) CloseSub();
                else Resume();
                return;
            }

            int vertical = nav.StepVertical(dt);
            if (vertical != 0)
            {
                current.MoveFocus(-vertical); // rows run top-down, so up is index-1
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            int horizontal = nav.StepHorizontal(dt);
            if (horizontal != 0 && current.Focused != null && current.Focused.Adjust(horizontal))
                Blip(theme.MoveClip);

            if (MenuNavigator.ConfirmPressed()) current.Focused?.Activate();
        }

        // Active gameplay only. With a ship: motor.Paused covers the tuning
        // screen, the main-menu overlay and the frozen end-of-run state;
        // RunOver covers the result screen. Without one (city chase), running
        // scaled time is the only "gameplay is live" signal there is.
        bool CanPause()
        {
            if (MainMenuController.IsOpen) return false;
            if (motor != null) return !motor.Paused && (gameManager == null || !gameManager.RunOver);
            return Time.timeScale > 0f;
        }

        void Pause()
        {
            isPaused = true;
            if (motor != null) motor.Paused = true;
            Time.timeScale = 0f;
            MenuScreenFactory.EnsureEventSystem(); // the city scene has none; mouse clicks need one
            panel.SetActive(true);

            openedTime = Time.unscaledTime;
            lockTimer = 0f;
            settingsScreen.HideImmediate();
            confirmMenuScreen.HideImmediate();
            confirmQuitScreen.HideImmediate();
            current = pauseScreen;
            pauseScreen.Show(staggered: false); // pausing should feel instant, not cinematic
            SetFooterFor(pauseScreen);
            Gamepad.current?.ResetHaptics();
        }

        void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            if (motor != null) motor.Paused = false;
            panel.SetActive(false);
            Blip(theme.BackClip);
        }

        void OpenSub(MenuScreen screen)
        {
            // A confirm page always re-arms on the safe answer, no matter
            // where the focus sat when it was last backed out of.
            if (screen == confirmMenuScreen || screen == confirmQuitScreen) screen.SetFocus(1);

            pauseScreen.SlideOut(-theme.ScreenSlide);
            screen.SlideIn(theme.ScreenSlide);
            current = screen;
            lockTimer = theme.ScreenTransition;
            openedTime = Time.unscaledTime;
            SetFooterFor(screen);
            Blip(theme.ConfirmClip);
        }

        void CloseSub()
        {
            current.SlideOut(theme.ScreenSlide);
            pauseScreen.SlideIn(-theme.ScreenSlide);
            current = pauseScreen;
            lockTimer = theme.ScreenTransition;
            openedTime = Time.unscaledTime;
            SetFooterFor(pauseScreen);
            Blip(theme.BackClip);
        }

        // Abandons the run and returns to the attract screen. The scene load
        // destroys this menu (and the run) — hand the next scene a running
        // clock first, and let the menu scene's own controller take over.
        void ExitToMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(0); // MainMenu is build index 0
        }

        /// <summary>Quits for real in a build, stops play mode in the editor. Shared with the main menu's EXIT.</summary>
        public static void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Safety: never leave the game frozen if this object goes away.
        void OnDestroy()
        {
            if (isPaused) Time.timeScale = 1f;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            gameObject.AddComponent<GraphicRaycaster>();

            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            // Full-screen dim that also blocks clicks reaching the HUD below.
            // Everything hangs off it, so hiding the panel hides the menu.
            panel = new GameObject("Panel", typeof(RectTransform));
            panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var dim = panel.AddComponent<Image>();
            var dimColor = theme.Backdrop;
            dimColor.a = 0.78f; // the frozen run stays faintly visible behind the menu
            dim.color = dimColor;

            pauseScreen = MenuScreen.Create("PauseScreen", panelRect, theme, 0f, 90f);
            pauseScreen.SetTitle(MenuTextId.Paused);
            pauseScreen.AddRow<MenuRow>(MenuTextId.Resume).Activated += Resume;
            pauseScreen.AddRow<MenuRow>(MenuTextId.Settings).Activated += () => OpenSub(settingsScreen);
            pauseScreen.AddRow<MenuRow>(MenuTextId.ExitToMenu).Activated += () => OpenSub(confirmMenuScreen);
            pauseScreen.AddRow<MenuRow>(MenuTextId.QuitGame).Activated += () => OpenSub(confirmQuitScreen);

            settingsScreen = MenuScreenFactory.BuildSettings(panelRect, theme);
            confirmMenuScreen = MenuScreenFactory.BuildConfirm(panelRect, theme, MenuTextId.ExitToMenu,
                                                               ExitToMainMenu, CloseSub);
            confirmQuitScreen = MenuScreenFactory.BuildConfirm(panelRect, theme, MenuTextId.QuitGame,
                                                               ExitGame, CloseSub);

            footer = PromptStrip.Create(panelRect, theme, 56f);

            panel.SetActive(false);
        }

        void SetFooterFor(MenuScreen screen)
        {
            if (screen == settingsScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Adjust, MenuTextId.HintChange),
                                (PromptAction.Back, MenuTextId.HintBack));
            else if (screen == confirmMenuScreen || screen == confirmQuitScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintSelect),
                                (PromptAction.Back, MenuTextId.HintCancel));
            else
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintSelect),
                                (PromptAction.Back, MenuTextId.Resume));
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }
    }
}
