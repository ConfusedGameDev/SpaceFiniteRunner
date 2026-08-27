using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
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
    /// Pausing also crossfades the audio mixer into its Paused snapshot
    /// (<see cref="UI.GameAudio.SetPaused"/>): the Gameplay bus — music, FX,
    /// voice — fades to silence while the UI bus keeps sounding and an
    /// optional pause-music loop (MenuTheme.PauseMusicClip) fades in.
    /// The DEBUG entry follows the same rule: <see cref="BuildDebugTabs"/>
    /// builds only the pages the current scene can actually edit — track,
    /// ship and patrol in the runner, car, chase camera and police in the city.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        const int SortingOrder = 20; // above the HUD (10) and messages (15), below the main menu (30)

        [Tooltip("Show the DEBUG entry (tabbed developer pages — track and ship in the runner, car, camera and police in the city). Turn off for player-facing builds.")]
        public bool debug = true;

        GameManager gameManager;
        ShipMotor motor;
        MenuTheme theme;
        MenuNavigator nav;
        DebugMenu debugMenu;
        TrackDebugSettings debugSettings;
        ShipDebugSettings shipDebugSettings;
        PatrolDebugSettings patrolDebugSettings;
        readonly System.Collections.Generic.List<System.Action> debugRefreshers = new();

        GameObject panel;
        RectTransform panelRect;
        MenuScreen pauseScreen;
        MenuScreen settingsScreen;
        MenuScreen confirmMenuScreen;   // "exit to main menu — are you sure?"
        MenuScreen confirmQuitScreen;   // "quit game — are you sure?"
        MenuScreen confirmReloadScreen; // "debug values changed — reload the scene?"
        MenuScreen current;
        PromptStrip footer;
        AudioSource ui;
        AudioSource pauseMusic;     // loops on the mixer's PauseMusic bus — audible only in the Paused snapshot
        float pauseMusicStopTime;   // resume keeps it playing until the crossfade has carried it out

        bool isPaused;
        bool debugDirty;   // a debug slider moved this pause — offer a reload on the way out
        float lockTimer;   // input frozen while a screen transition plays
        float openedTime;  // grace so the press that paused can't also confirm

        /// <summary>True while the pause menu is up — the city map checks this so the two screens can never stack.</summary>
        public bool IsPaused => isPaused;

        public static PauseMenu Spawn(GameManager gameManager, ShipMotor motor)
        {
            var menu = FindFirstObjectByType<PauseMenu>();
            if (menu == null) menu = new GameObject("PauseMenu").AddComponent<PauseMenu>();
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
                // The resume crossfade already muted it; now it can actually stop.
                if (pauseMusic != null && pauseMusic.isPlaying && Time.unscaledTime >= pauseMusicStopTime)
                    pauseMusic.Stop();
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
            // game. Quitting is never one press. Leaving the debug tabs after
            // changing anything detours through the reload confirmation first.
            if (MenuNavigator.PauseTogglePressed() || MenuNavigator.BackPressed())
            {
                if (current == pauseScreen) Resume();
                else if (debugDirty && debugMenu != null && debugMenu.Contains(current)) OpenReloadConfirm();
                else CloseSub();
                return;
            }

            // Bumpers (or Q/E) flip between debug tabs, same slide language as
            // the other screens. Only while a debug tab is the current screen.
            if (debugMenu != null && debugMenu.Contains(current) && debugMenu.Count > 1)
            {
                int tabStep = DebugMenu.TabStepPressed();
                if (tabStep != 0)
                {
                    current.SlideOut(-tabStep * theme.ScreenSlide);
                    current = debugMenu.Cycle(tabStep);
                    current.SlideIn(tabStep * theme.ScreenSlide);
                    lockTimer = theme.ScreenTransition;
                    SetFooterFor(current);
                    Blip(theme.MoveClip);
                    return;
                }
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
            // The city map is a full-screen takeover with its own toggle; the
            // two must never stack, or Esc would unfreeze the game underneath it.
            if (DebugMenuHooks.FullScreenTakeoverOpen != null && DebugMenuHooks.FullScreenTakeoverOpen()) return false;
            if (motor != null) return !motor.Paused && (gameManager == null || !gameManager.RunOver);
            return Time.timeScale > 0f;
        }

        void Pause()
        {
            isPaused = true;
            if (motor != null) motor.Paused = true;
            Time.timeScale = 0f;

            // Duck the world, not the menu: the Paused snapshot fades the
            // Gameplay bus (music, FX, voice) out and PauseMusic in — the
            // mixer updates on unscaled time, so the timeScale 0 set just
            // above can't freeze the fade. UI blips are on their own bus and
            // keep sounding.
            GameAudio.SetPaused(true, theme.PauseAudioFade);
            if (pauseMusic != null && pauseMusic.clip != null) pauseMusic.Play();

            MenuScreenFactory.EnsureEventSystem(); // the city scene has none; mouse clicks need one
            panel.SetActive(true);

            openedTime = Time.unscaledTime;
            lockTimer = 0f;
            debugDirty = false;
            settingsScreen.HideImmediate();
            confirmMenuScreen.HideImmediate();
            confirmQuitScreen.HideImmediate();
            confirmReloadScreen.HideImmediate();
            debugMenu?.HideAllImmediate();
            // The ship sliders were built before the tuning screen swapped in
            // its runtime clone — re-read the live values on every open.
            foreach (var refresh in debugRefreshers) refresh();
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
            GameAudio.SetPaused(false, theme.PauseAudioFade);
            pauseMusicStopTime = Time.unscaledTime + theme.PauseAudioFade; // stop only once the fade has hidden it
            debugSettings?.Flush();     // commit any debug tweaks to disk
            shipDebugSettings?.Flush();
            patrolDebugSettings?.Flush();
            DebugMenuHooks.Flush?.Invoke();
            RainDebugPage.Flush();
            Blip(theme.BackClip);
        }

        // Backing out of a debug tab with changes lands here instead of the
        // pause list: offer the reload once, then drop the dirty flag so NO
        // (or Esc, which is the same answer) continues without nagging again.
        void OpenReloadConfirm()
        {
            debugDirty = false;
            confirmReloadScreen.SetFocus(1); // default to the safe answer
            current.SlideOut(-theme.ScreenSlide);
            confirmReloadScreen.SlideIn(theme.ScreenSlide);
            current = confirmReloadScreen;
            lockTimer = theme.ScreenTransition;
            openedTime = Time.unscaledTime;
            SetFooterFor(confirmReloadScreen);
            Blip(theme.ConfirmClip);
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
        /// <summary>Back to the attract screen. Static and public: the game-over screen's NO answer is the same trip.</summary>
        public static void ExitToMainMenu()
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

        // Safety: never leave the game frozen if this object goes away, and
        // never lose a debug tweak that was made but not resumed out of —
        // this also fires on the way out of play mode.
        void OnDestroy()
        {
            if (isPaused) Time.timeScale = 1f;
            // The mixer outlives every scene, so a menu destroyed while
            // paused (exit to menu, debug reload) must hand the next scene a
            // gameplay mix, not a muted one.
            if (isPaused) GameAudio.SetPaused(false, 0f);
            debugSettings?.Flush();
            shipDebugSettings?.Flush();
            patrolDebugSettings?.Flush();
            DebugMenuHooks.Flush?.Invoke();
            RainDebugPage.Flush();
        }

        /// <summary>Editor bake: regenerates the menu and leaves the pause page visible, so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            Build();
            panel.SetActive(true);
            pauseScreen.Show(staggered: false);
        }

        // Root components are reused by Build — see RpgMessageSystem.TearDown.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            panel = null;
            panelRect = null;
            pauseScreen = settingsScreen = confirmMenuScreen = confirmQuitScreen = confirmReloadScreen = current = null;
            footer = null;
            ui = null;
            pauseMusic = null;
            debugMenu = null;
            debugRefreshers.Clear();
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
            canvas.sortingOrder = SortingOrder;

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GetOrAdd<GraphicRaycaster>(gameObject);

            ui = GetOrAdd<AudioSource>(gameObject);
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            // Its bus is muted outside the Paused snapshot, so this source is
            // only ever heard through the pause crossfade. Own child object:
            // the root's AudioSource is the UI blip source.
            var pauseMusicGo = new GameObject("PauseMusic");
            pauseMusicGo.transform.SetParent(transform, false);
            pauseMusic = pauseMusicGo.AddComponent<AudioSource>();
            pauseMusic.playOnAwake = false;
            pauseMusic.loop = true;
            pauseMusic.spatialBlend = 0f;
            pauseMusic.clip = theme.PauseMusicClip;
            pauseMusic.outputAudioMixerGroup = GameAudio.PauseMusic;

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

            if (debug) BuildDebugTabs();

            pauseScreen = MenuScreen.Create("PauseScreen", panelRect, theme, 0f, 90f);
            pauseScreen.SetTitle(MenuTextId.Paused);
            pauseScreen.AddRow<MenuRow>(MenuTextId.Resume).Activated += Resume;
            pauseScreen.AddRow<MenuRow>(MenuTextId.Settings).Activated += () => OpenSub(settingsScreen);
            if (debugMenu != null)
                pauseScreen.AddRow<MenuRow>(MenuTextId.Debug).Activated += () => OpenSub(debugMenu.Active);
            pauseScreen.AddRow<MenuRow>(MenuTextId.ExitToMenu).Activated += () => OpenSub(confirmMenuScreen);
            pauseScreen.AddRow<MenuRow>(MenuTextId.QuitGame).Activated += () => OpenSub(confirmQuitScreen);

            settingsScreen = MenuScreenFactory.BuildSettings(panelRect, theme);
            confirmMenuScreen = MenuScreenFactory.BuildConfirm(panelRect, theme, MenuTextId.ExitToMenu,
                                                               ExitToMainMenu, CloseSub);
            confirmQuitScreen = MenuScreenFactory.BuildConfirm(panelRect, theme, MenuTextId.QuitGame,
                                                               ExitGame, CloseSub);
            confirmReloadScreen = MenuScreenFactory.BuildConfirm(panelRect, theme, MenuTextId.ReloadScene,
                                                                 MenuTextId.ReloadScenePrompt,
                                                                 ReloadScene, CloseSub);

            footer = PromptStrip.Create(panelRect, theme, 56f);

            panel.SetActive(false);
        }

        /// <summary>
        /// Assembles the DEBUG tabs out of whatever the current scene has to
        /// edit — the pause menu is shared by both games, and each one only
        /// gets its own pages. The runner: track (a TrackGenerator), ship (a
        /// motor, which a hand-placed menu has no reference to) and patrol (an
        /// initialized scene patrol, whose definition clone already exists
        /// because the GameManager spawns this menu after Init). The city
        /// chase: the player car's config, the chase camera's settings and the police pursuit settings. A
        /// scene with none of them gets no DEBUG entry at all — the row is
        /// only added when a tab was actually built. Every tab prints its own
        /// "TAB n/N", so the total is counted before the first one is made.
        /// </summary>
        void BuildDebugTabs()
        {
            TrackGenerator generator = FindFirstObjectByType<TrackGenerator>();
            PolicePatrol patrol = gameManager != null ? gameManager.Patrol : null;
            bool patrolReady = patrol != null && patrol.Definition != null;
            bool shipReady = generator != null && motor != null;
            // City pages only in the city: the runner scene loads additively
            // over the city it replaces, so for a beat both worlds exist and
            // its menu must not sprout car tabs that are about to unload.
            DebugMenuHooks.IDebugTabs city = generator == null ? DebugMenuHooks.Discover?.Invoke() : null;
            // Weather belongs to neither game — both scenes spawn the same
            // RainSystem — so its page is added here rather than by either
            // side's factory, and only when the scene is actually raining.
            RainSettings rain = RainDebugPage.Discover();

            int tabCount = (generator != null ? 2 : 0) + (shipReady ? 4 : 0)
                         + (patrolReady ? 1 : 0) + (city?.TabCount ?? 0) + (rain != null ? 1 : 0);
            if (tabCount == 0) return;

            debugMenu = new DebugMenu();
            System.Action changed = () => debugDirty = true;
            int tab = 0;

            if (generator != null)
            {
                debugSettings = TrackDebugSettings.Load();
                debugMenu.AddTab(DebugMenuFactory.BuildCoreSettingsTab(
                    panelRect, theme, generator, debugSettings, ReloadScene, changed, tab++, tabCount));
                debugMenu.AddTab(DebugMenuFactory.BuildMultipliersTab(
                    panelRect, theme, generator, debugSettings, changed, tab++, tabCount));
            }
            if (shipReady)
            {
                shipDebugSettings = ShipDebugSettings.Load();
                debugMenu.AddTab(DebugMenuFactory.BuildShipSpeedTab(
                    panelRect, theme, motor, shipDebugSettings, changed, debugRefreshers, tab++, tabCount));
                debugMenu.AddTab(DebugMenuFactory.BuildShipHandlingTab(
                    panelRect, theme, motor, shipDebugSettings, changed, debugRefreshers, tab++, tabCount));
                debugMenu.AddTab(DebugMenuFactory.BuildShipDashTab(
                    panelRect, theme, motor, shipDebugSettings, changed, debugRefreshers, tab++, tabCount));
                debugMenu.AddTab(DebugMenuFactory.BuildShipHoverTab(
                    panelRect, theme, motor, shipDebugSettings, changed, debugRefreshers, tab++, tabCount));
            }
            if (patrolReady)
            {
                patrolDebugSettings = PatrolDebugSettings.Load();
                debugMenu.AddTab(DebugMenuFactory.BuildPatrolTab(
                    panelRect, theme, patrol, patrolDebugSettings, changed, debugRefreshers, tab++, tabCount));
            }

            // No `changed` for the city pages: every car and camera knob they
            // expose applies live, so they never need the reload the runner's
            // track sliders do — and reloading the city would reroll the whole
            // layout under the player for nothing.
            city?.AddTabs(debugMenu, panelRect, theme, debugRefreshers, ref tab, tabCount);

            // Same rule as the city pages: the rain re-reads its asset every
            // frame, so nothing here needs a reload either.
            if (rain != null)
                debugMenu.AddTab(RainDebugPage.Build(panelRect, theme, rain, debugRefreshers, tab++, tabCount));
        }

        // The debug sliders saved their values into the TrackDebugSettings
        // asset as they moved — commit it to disk, then reload; the fresh
        // TrackGenerator re-applies the asset in Generate().
        void ReloadScene()
        {
            debugSettings?.Flush();
            shipDebugSettings?.Flush();
            patrolDebugSettings?.Flush();
            DebugMenuHooks.Flush?.Invoke();
            RainDebugPage.Flush();

            Time.timeScale = 1f;
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        void SetFooterFor(MenuScreen screen)
        {
            if (screen == settingsScreen || (debugMenu != null && debugMenu.Contains(screen)))
                footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Adjust, MenuTextId.HintChange),
                                (PromptAction.Back, MenuTextId.HintBack));
            else if (screen == confirmMenuScreen || screen == confirmQuitScreen || screen == confirmReloadScreen)
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
