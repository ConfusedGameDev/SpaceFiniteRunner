using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.Cheats;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
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
        CheatConsole cheatConsole;
        ControlsScreen controls;   // the CONTROLS page under SETTINGS (its Screen is the MenuScreen)
        MenuScreen creditsScreen;
        MenuScreen exitScreen;
        MenuScreen missionsScreen;      // the campaign map — a row only once a mission is complete
        System.Action refreshMissions;
        MenuScreen deleteProgressScreen; // SETTINGS → DELETE CAMPAIGN PROGRESS → "really?"
        MenuScreen current;
        string pendingScene;            // a scene chosen on the MISSIONS map; null = the Store

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
            var menu = FindFirstObjectByType<MainMenuController>();
            if (menu == null) menu = new GameObject("MainMenu").AddComponent<MainMenuController>();
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
            MissionSession.Clear(); // reaching the main menu ends any campaign mission in flight
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
            // The cheats page reads raw presses instead of navigating: it has
            // no rows, and letting the d-pad drive both a (no-op) focus move
            // and a cheat token would double every blip. Only Back survives —
            // which is exactly why B / Esc can never appear in a code.
            if (current == cheatsScreen && cheatConsole != null)
            {
                cheatConsole.CaptureTick();
                if (MenuNavigator.BackPressed()) Back();
                return;
            }

            // The controls page owns the frame while a rebind is listening
            // (and for a grace after it lands): the press being bound must
            // not also step, confirm or back out of the menu.
            if (controls != null && current == controls.Screen && controls.CaptureTick())
            {
                nav.Sync();
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
            {
                Blip(theme.AdjustClip);
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

            // The destructive confirms always re-arm on the safe answer.
            if (screen == exitScreen || screen == deleteProgressScreen) screen.SetFocus(1);

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

        /// <summary>B / Esc. Backs out of any sub-screen (CONTROLS and the delete-progress confirm to SETTINGS, the rest to the main list); on the main menu it returns to attract. It never quits.</summary>
        void Back()
        {
            if (phase != Phase.Browsing) return;

            if (current == mainScreen)
            {
                BackToAttract();
                return;
            }

            bool underSettings = (controls != null && current == controls.Screen) || current == deleteProgressScreen;
            var target = underSettings ? settingsScreen : mainScreen;
            current.SlideOut(theme.ScreenSlide);
            target.SlideIn(-theme.ScreenSlide);
            current = target;

            phase = Phase.Transitioning;
            phaseTimer = theme.ScreenTransition;
            openedTime = Time.unscaledTime;
            SetFooterFor(target);
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

            // Own scene: hand off to the Store, the hub between missions (its
            // START MISSION row goes on to the city chase, which later
            // glitches into the finite runner) — or straight to the world
            // scene a MISSIONS row chose, skipping the Store. By name, never
            // by build index: only the main menu sits at index 0.
            if (standalone)
            {
                string scene = string.IsNullOrEmpty(pendingScene) ? StoreSettings.SceneName : pendingScene;
                if (Application.CanStreamedLevelBeLoaded(scene))
                {
                    LoadingScreen.Load(scene);
                }
                else
                {
                    Debug.LogError($"MainMenu: the {scene} scene is not in the build settings — run Tools → FiniteRunner → Register Campaign Scenes.", this);
                    MissionSession.Clear();
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

        /// <summary>Editor bake: regenerates the menu with the attract screen visible, so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            Build();
        }

        // Root components are reused by Build — see RpgMessageSystem.TearDown.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            rootGroup = attractGroup = footerGroup = null;
            attractText = null;
            attractGlyph = null;
            footer = null;
            mainScreen = settingsScreen = cheatsScreen = creditsScreen = exitScreen = missionsScreen = deleteProgressScreen = current = null;
            refreshMissions = null;
            cheatConsole = null;
            controls = null;
            ui = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }


        void Build()
        {
            TearDown();
            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = MenuSortingOrder;

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GetOrAdd<GraphicRaycaster>(gameObject);
            rootGroup = GetOrAdd<CanvasGroup>(gameObject);
            root = (RectTransform)transform;

            ui = GetOrAdd<AudioSource>(gameObject);
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            BuildBackdrop();
            BuildLogo();
            BuildAttractPrompt();

            footer = PromptStrip.Create(root, theme, 56f);
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
            footerGroup.alpha = 0f;

            BuildSettings();
            BuildMissions();
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
            // The campaign map appears once there is something on it — a
            // completed mission to replay. The profile is only read in play.
            if (Application.isPlaying && CampaignProgress.AnyCompleted(CampaignCatalog.Load()))
                mainScreen.AddRow<MenuRow>(MenuTextId.Missions).Activated += OpenMissions;
            mainScreen.AddRow<MenuRow>(MenuTextId.Settings).Activated += () => OpenSub(settingsScreen);
            mainScreen.AddRow<MenuRow>(MenuTextId.Cheats).Activated += () => OpenSub(cheatsScreen);
            mainScreen.AddRow<MenuRow>(MenuTextId.Credits).Activated += () => OpenSub(creditsScreen);
            mainScreen.AddRow<MenuRow>(MenuTextId.Exit).Activated += () => OpenSub(exitScreen);
        }

        // Shared with the pause menu, so the two settings pages never drift —
        // and so is the CONTROLS page its last row opens. Only the main menu's
        // page carries DELETE CAMPAIGN PROGRESS: wiping the campaign is not a
        // mid-run action.
        void BuildSettings()
        {
            controls = ControlsScreen.Build(root, theme);
            controls.Captured += () => Blip(theme.ConfirmClip);
            controls.Cancelled += () => Blip(theme.BackClip);
            settingsScreen = MenuScreenFactory.BuildSettings(root, theme, () => OpenSub(controls.Screen),
                                                             () => OpenSub(deleteProgressScreen));

            // The confirm: YES wipes, NO / Back return to SETTINGS. The warning
            // line sits under the two answers (rows at 0 and -104).
            deleteProgressScreen = MenuScreenFactory.BuildConfirm(root, theme, MenuTextId.DeleteProgress,
                                                                  MenuTextId.DeleteProgressQuestion,
                                                                  ConfirmDeleteProgress, Back);
            deleteProgressScreen.AddLabel("Warning", new Vector2(0f, -210f), new Vector2(1400f, 40f),
                                          MenuTextId.DeleteProgressWarning, 24, theme.Accent, theme.BodyFont,
                                          TextAnchor.MiddleCenter, theme.TitleLead);
        }

        // The wipe: missions, unlocks, wallet and every upgrade go; lifetime
        // stats stay. The menu is rebuilt so the MISSIONS row (now empty)
        // disappears and the map re-reads the profile, then SETTINGS comes
        // back so the player sees where they were.
        void ConfirmDeleteProgress()
        {
            if (phase != Phase.Browsing) return;
            PlayerStats.ResetCampaign();
            MissionSession.Clear();
            Blip(theme.ConfirmClip);
            HapticsSystem.Instance.Pulse(theme.ConfirmRumble, theme.ConfirmRumble * 0.5f, 0.2f);
            Build();
            ShowScreen(settingsScreen, true);
        }

        // The campaign map: rebuilt from the profile every time it opens.
        void BuildMissions()
        {
            missionsScreen = MissionSelectScreenFactory.Build(root, theme, PlayMission, out refreshMissions);
        }

        void OpenMissions()
        {
            if (phase != Phase.Browsing) return;
            refreshMissions?.Invoke();
            OpenSub(missionsScreen);
        }

        // A mission chosen on the map: open its session (a replay when it was
        // already complete) and leave for its world's scene directly — the
        // Store is skipped; a replay's NEXT MISSION still returns there.
        void PlayMission(CampaignCatalog.Entry entry, bool replay)
        {
            if (phase != Phase.Browsing || !entry.IsSet || entry.world == null) return;
            MissionSession.Begin(entry.mission, replay);
            pendingScene = entry.world.sceneName;
            StartGame();
        }

        // The page has no rows on purpose: every press on it is a cheat
        // token, so a focus list would be competing for the same buttons.
        // Only Back stays reserved, which is why B / Esc can never appear in
        // a code (see CheatButton / CheatKey).
        void BuildCheats()
        {
            cheatsScreen = MenuScreen.Create("CheatsScreen", root, theme, 0f, 100f);
            cheatsScreen.SetTitle(MenuTextId.Cheats);

            cheatConsole = CheatConsole.Create(cheatsScreen, theme);
            cheatConsole.TokenPushed += () =>
            {
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            };
            cheatConsole.CheatRevealed += _ => Blip(theme.ConfirmClip);
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
            else if (controls != null && screen == controls.Screen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Adjust, MenuTextId.HintDevice),
                                (PromptAction.Confirm, MenuTextId.HintRebind), (PromptAction.Back, MenuTextId.HintBack));
            else if (screen == exitScreen || screen == deleteProgressScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintSelect),
                                (PromptAction.Back, MenuTextId.HintCancel));
            else if (screen == missionsScreen)
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintPlay),
                                (PromptAction.Back, MenuTextId.HintBack));
            else
                footer.SetHints((PromptAction.Back, MenuTextId.HintBack));
        }
    }
}
