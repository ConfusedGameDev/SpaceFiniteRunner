using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// The front door of the game: an attract screen that eases the menu in on
    /// the first input, then Start / Settings / Cheats / Credits / Exit.
    ///
    /// Two ways to run it:
    /// - **Own scene (the shipping flow)**: placed in MainMenu.unity, scene 0
    ///   of the build. START loads the next scene in the build order (the city
    ///   chase), which later hands off to the finite runner. Nothing is frozen
    ///   because nothing else is running.
    /// - **Overlay (in-scene testing)**: spawned by a GameManager over its own
    ///   gameplay scene (tick mainMenuOnBoot). Holds the run with timeScale 0 +
    ///   motor.Paused, and START hands off to the TuningScreen instead of
    ///   loading a scene. <see cref="TuningScreen"/> checks <see cref="IsOpen"/>
    ///   so it cannot launch the run out from under the menu.
    ///
    /// Input is polled in one place (the project has no .inputactions asset)
    /// and drives whichever screen is current, so pad, keyboard and mouse all
    /// share a single focus index. Every duration and offset comes from the
    /// <see cref="MenuTheme"/> asset.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        /// <summary>True from the moment the menu spawns until it hands off to gameplay.</summary>
        public static bool IsOpen { get; private set; }

        enum Phase { Attract, Browsing, Transitioning, Leaving }

        const int MenuSortingOrder = 30; // the pause menu's canvas is 20
        const float AttractFadeSeconds = 0.35f;

        MenuTheme theme;
        ShipMotor motor;
        TuningScreen tuningScreen;

        RectTransform root;
        CanvasGroup rootGroup;
        CanvasGroup attractGroup;
        Text attractText;
        Image attractGlyph;
        PromptStrip footer;
        CanvasGroup footerGroup;

        MenuScreen mainScreen;
        MenuScreen settingsScreen;
        MenuScreen cheatsScreen;
        MenuScreen creditsScreen;
        MenuScreen exitScreen;
        MenuScreen current;

        Phase phase = Phase.Attract;
        float phaseTimer;
        float openedTime;
        float attractAlpha = 1f;
        float attractTarget = 1f;
        float pulseTime;

        MenuNavigator nav;
        AudioSource ui;
        bool ownsTimeScale;
        bool standalone; // placed in its own scene: START loads a scene instead of un-pausing a run

        /// <summary>Spawned by the GameManager, like the pause menu — no scene wiring.</summary>
        public static MainMenuController Spawn(ShipMotor motor, TuningScreen tuningScreen)
        {
            var go = new GameObject("MainMenu");
            var menu = go.AddComponent<MainMenuController>();
            menu.motor = motor;
            menu.tuningScreen = tuningScreen;
            menu.Open();
            return menu;
        }

        // Spawn() opens the menu synchronously right after AddComponent, so a
        // still-unopened menu by Start() means this one was placed in a scene —
        // the MainMenu scene at the front of the build.
        void Start()
        {
            if (theme != null) return;
            standalone = true;
            Open();
        }

        void Open()
        {
            IsOpen = true;
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);

            Build();

            if (standalone)
            {
                // Own scene: nothing runs behind the menu, so nothing to
                // freeze — but the menu still needs an EventSystem for mouse
                // input, which the gameplay scenes carry and this one may not.
                MenuScreenFactory.EnsureEventSystem();
            }
            else
            {
                // Overlay: hold the run. The ship launches in its own Start(),
                // but at timeScale 0 with the motor paused nothing moves.
                if (motor != null) motor.Paused = true;
                Time.timeScale = 0f;
                ownsTimeScale = true;
            }

            openedTime = Time.unscaledTime;
            Gamepad.current?.ResetHaptics();
        }

        void Update()
        {
            if (theme == null) return;

            InputPromptBinder.Poll();
            float dt = Time.unscaledDeltaTime;

            UpdateAttractVisuals(dt);

            switch (phase)
            {
                case Phase.Attract:
                    if (Ready() && AnyInput()) Wake();
                    return;

                case Phase.Transitioning:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f) phase = Phase.Browsing;
                    return;

                case Phase.Leaving:
                    phaseTimer -= dt;
                    rootGroup.alpha = Mathf.Clamp01(phaseTimer / Mathf.Max(0.01f, theme.ScreenTransition));
                    if (phaseTimer <= 0f) FinishStart();
                    return;
            }

            if (!Ready() || current == null) return;
            UpdateNavigation(dt);
        }

        // Short deaf period after any screen opens: the press that opened it
        // must not also confirm inside it. (TuningScreen does the same.)
        bool Ready() => Time.unscaledTime - openedTime >= theme.InputGrace;

        void UpdateAttractVisuals(float dt)
        {
            pulseTime += dt;
            attractAlpha = Mathf.MoveTowards(attractAlpha, attractTarget, dt / AttractFadeSeconds);

            float wave = 0.5f + 0.5f * Mathf.Sin(pulseTime * Mathf.PI * 2f / Mathf.Max(0.1f, theme.AttractPulseSeconds));
            float pulse = Mathf.Lerp(theme.AttractPulseMin, theme.AttractPulseMax, wave);

            RefreshAttractPrompt();
            if (attractGroup != null) attractGroup.alpha = attractAlpha * pulse;
            if (footerGroup != null) footerGroup.alpha = 1f - attractAlpha;
        }

        // The attract prompt keys off what is PLUGGED IN, not what was last
        // touched: keyboard-only players read PRESS ENTER, and the moment a
        // pad connects the line becomes PRESS START with the physical button
        // drawn under it. Polled every frame so hot-plugging swaps it live.
        void RefreshAttractPrompt()
        {
            if (attractText == null) return;

            bool pad = Gamepad.current != null;
            attractText.text = MenuTextLibrary.Load().Get(pad ? MenuTextId.PressStart : MenuTextId.PressEnter);

            bool showGlyph = pad && theme.GlyphStart != null;
            if (attractGlyph != null && attractGlyph.gameObject.activeSelf != showGlyph)
                attractGlyph.gameObject.SetActive(showGlyph);
        }

        void UpdateNavigation(float dt)
        {
            int vertical = nav.StepVertical(dt);
            if (vertical != 0)
            {
                current.MoveFocus(-vertical); // rows run top-down, so up is index-1
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            int horizontal = nav.StepHorizontal(dt);
            if (horizontal != 0 && current.Focused != null && current.Focused.Adjust(horizontal))
            {
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            if (MenuNavigator.ConfirmPressed()) current.Focused?.Activate();
            else if (MenuNavigator.BackPressed()) Back();
        }

        // ------------------------------------------------------------- flow

        void Wake()
        {
            phase = Phase.Browsing;
            openedTime = Time.unscaledTime;
            attractTarget = 0f;
            ShowScreen(mainScreen, staggered: true);
        }

        void ShowScreen(MenuScreen screen, bool staggered)
        {
            current = screen;
            screen.Show(staggered);
            SetFooterFor(screen);
        }

        void OpenSub(MenuScreen screen)
        {
            if (phase != Phase.Browsing) return;

            // The exit confirm always re-arms on the safe answer.
            if (screen == exitScreen) screen.SetFocus(1);

            current.SlideOut(-theme.ScreenSlide);
            screen.SlideIn(theme.ScreenSlide);
            current = screen;

            phase = Phase.Transitioning;
            phaseTimer = theme.ScreenTransition;
            openedTime = Time.unscaledTime;
            SetFooterFor(screen);
            Blip(theme.ConfirmClip);
            HapticsSystem.Instance.Pulse(theme.ConfirmRumble, theme.ConfirmRumble * 0.5f, 0.12f);
        }

        /// <summary>B / Esc. Backs out of any sub-screen; on the main menu it returns to attract. It never quits.</summary>
        void Back()
        {
            if (phase != Phase.Browsing) return;

            if (current == mainScreen)
            {
                BackToAttract();
                return;
            }

            current.SlideOut(theme.ScreenSlide);
            mainScreen.SlideIn(-theme.ScreenSlide);
            current = mainScreen;

            phase = Phase.Transitioning;
            phaseTimer = theme.ScreenTransition;
            openedTime = Time.unscaledTime;
            SetFooterFor(mainScreen);
            Blip(theme.BackClip);
        }

        void BackToAttract()
        {
            mainScreen.SlideOut(-theme.ScreenSlide);
            current = null;
            phase = Phase.Attract;
            attractTarget = 1f;
            openedTime = Time.unscaledTime; // the same press must not wake it again
            Blip(theme.BackClip);
        }

        void StartGame()
        {
            if (phase != Phase.Browsing) return;

            phase = Phase.Leaving;
            phaseTimer = theme.ScreenTransition;
            current.SlideOut(-theme.ScreenSlide);
            Blip(theme.ConfirmClip);
            HapticsSystem.Instance.Pulse(theme.ConfirmRumble, theme.ConfirmRumble * 0.5f, 0.15f);
        }

        void FinishStart()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            ownsTimeScale = false;

            // Own scene: hand off to the next scene in the build order — the
            // city chase, which later glitches into the finite runner.
            if (standalone)
            {
                int next = gameObject.scene.buildIndex + 1;
                if (next > 0 && next < SceneManager.sceneCountInBuildSettings)
                {
                    SceneManager.LoadScene(next);
                }
                else
                {
                    Debug.LogError("MainMenu has no next scene in the build settings — add the gameplay scene after it.", this);
                    Destroy(gameObject);
                }
                return;
            }

            // Overlay: the tuning screen owns the launch when it is present
            // (and auto-launches when its autoLaunch flag is on); without one,
            // start the run here so a stripped-down scene still plays.
            if (tuningScreen != null)
            {
                tuningScreen.Show();
            }
            else if (motor != null)
            {
                motor.Launch();
                motor.Paused = false;
            }

            Destroy(gameObject);
        }

        void ConfirmExit()
        {
            Time.timeScale = 1f; // never hand a frozen clock to whatever runs next
            ownsTimeScale = false;
            IsOpen = false;
            PauseMenu.ExitGame();
        }

        // Safety net: never leave the game frozen if this object goes away.
        void OnDestroy()
        {
            IsOpen = false;
            if (ownsTimeScale) Time.timeScale = 1f;
        }

        // -------------------------------------------------------------- input
        // Navigation/confirm/back live on the shared MenuNavigator; only the
        // attract screen's wake-on-anything check is menu-specific.

        static bool AnyInput()
        {
            if (Keyboard.current is { anyKey: { wasPressedThisFrame: true } }) return true;
            if (Mouse.current is { leftButton: { wasPressedThisFrame: true } }) return true;
            if (Mouse.current is { rightButton: { wasPressedThisFrame: true } }) return true;

            var pad = Gamepad.current;
            if (pad == null) return false;
            foreach (var control in pad.allControls)
                if (control is ButtonControl button && button.wasPressedThisFrame)
                    return true;

            return false;
        }

        // -------------------------------------------------------------- audio

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }

        // -------------------------------------------------------------- build

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = MenuSortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            gameObject.AddComponent<GraphicRaycaster>();
            rootGroup = gameObject.AddComponent<CanvasGroup>();
            root = (RectTransform)transform;

            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            BuildBackdrop();
            BuildLogo();
            BuildAttractPrompt();

            footer = PromptStrip.Create(root, theme, 56f);
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
            footerGroup.alpha = 0f;

            BuildSettings();
            BuildCheats();
            BuildCredits();
            BuildExit();
            BuildMain(); // last, so its rows draw over the pages behind them
        }

        void BuildBackdrop()
        {
            var go = new GameObject("Backdrop", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = theme.Backdrop;
            image.raycastTarget = true; // swallows clicks meant for the HUD below
        }

        void BuildLogo()
        {
            MenuScreen.MakeText("LogoKicker", root, new Vector2(0f, 452f), new Vector2(900f, 60f),
                                "S P A C E", 34, theme.Accent, theme.TitleFont, TextAnchor.MiddleCenter);
            MenuScreen.MakeText("LogoTitle", root, new Vector2(0f, 380f), new Vector2(1400f, 110f),
                                "FINITE RUNNER", 78, theme.TextPrimary, theme.TitleFont, TextAnchor.MiddleCenter);
        }

        void BuildAttractPrompt()
        {
            var holder = new GameObject("AttractPrompt", typeof(RectTransform));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -260f);
            rect.sizeDelta = new Vector2(900f, 150f);
            attractGroup = holder.AddComponent<CanvasGroup>();

            attractText = MenuScreen.MakeText("PressText", rect, new Vector2(0f, 34f), new Vector2(900f, 60f),
                                              string.Empty, 38, theme.TextPrimary, theme.BodyFont,
                                              TextAnchor.MiddleCenter);

            // The physical Start button, drawn under the line — only while a
            // controller is actually connected (see RefreshAttractPrompt).
            attractGlyph = MenuScreen.MakeImage("StartGlyph", rect, new Vector2(0f, -40f), new Vector2(72f, 72f),
                                                theme.GlyphStart, Color.white);
            attractGlyph.gameObject.SetActive(false);

            RefreshAttractPrompt();
        }

        void BuildMain()
        {
            mainScreen = MenuScreen.Create("MainScreen", root, theme, theme.MainColumnX, 60f);

            mainScreen.AddRow<MenuRow>(MenuTextId.Start).Activated += StartGame;
            mainScreen.AddRow<MenuRow>(MenuTextId.Settings).Activated += () => OpenSub(settingsScreen);
            mainScreen.AddRow<MenuRow>(MenuTextId.Cheats).Activated += () => OpenSub(cheatsScreen);
            mainScreen.AddRow<MenuRow>(MenuTextId.Credits).Activated += () => OpenSub(creditsScreen);
            mainScreen.AddRow<MenuRow>(MenuTextId.Exit).Activated += () => OpenSub(exitScreen);
        }

        // Shared with the pause menu, so the two settings pages never drift.
        void BuildSettings() => settingsScreen = MenuScreenFactory.BuildSettings(root, theme);

        void BuildCheats()
        {
            // Intentionally empty. Rows can be added with AddRow<> exactly like
            // Settings does — the navigation code needs no changes for them.
            cheatsScreen = MenuScreen.Create("CheatsScreen", root, theme, 0f, 100f);
            cheatsScreen.SetTitle(MenuTextId.Cheats);
            cheatsScreen.AddLabel("Placeholder", new Vector2(0f, 20f), new Vector2(900f, 60f),
                                  MenuTextId.NothingHereYet, 34, theme.TextDim, theme.BodyFont,
                                  TextAnchor.MiddleCenter, theme.TitleLead);
        }

        void BuildCredits()
        {
            creditsScreen = MenuScreen.Create("CreditsScreen", root, theme, 0f, 100f);
            creditsScreen.SetTitle(MenuTextId.Credits);

            float delay = theme.TitleLead;
            creditsScreen.AddLabel("Role0", new Vector2(0f, 70f), new Vector2(900f, 50f),
                                   MenuTextId.RoleMaster, 30, theme.Accent, theme.BodyFont,
                                   TextAnchor.MiddleCenter, delay);
            creditsScreen.AddLabel("Name0", new Vector2(0f, 14f), new Vector2(900f, 70f),
                                   "Jorge Pedrero", 54, theme.TextPrimary, theme.TitleFont,
                                   TextAnchor.MiddleCenter, delay + theme.EntranceStagger);
            creditsScreen.AddLabel("Role1", new Vector2(0f, -86f), new Vector2(900f, 50f),
                                   MenuTextId.RoleFool, 30, theme.Accent, theme.BodyFont,
                                   TextAnchor.MiddleCenter, delay + theme.EntranceStagger * 2f);
            creditsScreen.AddLabel("Name1", new Vector2(0f, -142f), new Vector2(900f, 70f),
                                   "Diego Perez", 54, theme.TextPrimary, theme.TitleFont,
                                   TextAnchor.MiddleCenter, delay + theme.EntranceStagger * 3f);
        }

        // Shared confirm shape with the pause menu's exits.
        void BuildExit() => exitScreen = MenuScreenFactory.BuildConfirm(root, theme, MenuTextId.Exit, ConfirmExit, Back);

        void SetFooterFor(MenuScreen screen)
        {
            if (screen == mainScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintSelect),
                                (PromptAction.Back, MenuTextId.HintTitle));
            else if (screen == settingsScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Adjust, MenuTextId.HintChange),
                                (PromptAction.Back, MenuTextId.HintBack));
            else if (screen == exitScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintSelect),
                                (PromptAction.Back, MenuTextId.HintCancel));
            else
                footer.SetHints((PromptAction.Back, MenuTextId.HintBack));
        }
    }
}
