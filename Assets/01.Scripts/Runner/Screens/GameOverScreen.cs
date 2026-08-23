using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The shared death screen: GAME OVER — RETRY? YES / NO on the themed
    /// menu framework, driven by two callbacks so each game decides what an
    /// answer means. The city chase raises it once the glitch has filled and
    /// held; the runner raises it when the patrol's parting line finishes.
    /// It lives next to the PauseMenu rather than in either game's UI folder
    /// for that reason — both scenes show the same screen.
    /// YES replays, NO returns to the main menu; there is no Back out — the
    /// screen demands an answer, so Esc/B do nothing here. It freezes scaled
    /// time while it is up (which also keeps the pause menu and the city map
    /// from opening — both refuse to stack over a stopped clock) and animates
    /// on unscaled time like every other menu. Built from code on its own
    /// overlay canvas, above the maxed glitch: the glitch is a render feature
    /// on the camera, and an overlay canvas draws after it, so the choice
    /// stays readable through full corruption.
    /// </summary>
    public class GameOverScreen : MonoBehaviour
    {
        const int SortingOrder = 25; // above the pause menu (20), below the main menu (30)

        /// <summary>
        /// True while the screen is waiting for an answer. HUDs poll it: the
        /// runner's own "PRESS R TO RUN AGAIN" prompt would otherwise restart
        /// the run out from under a question the player has not answered yet.
        /// </summary>
        public static bool IsOpen { get; private set; }

        MenuTheme theme;
        MenuNavigator nav;
        MenuScreen screen;
        AudioSource ui;
        System.Action onRetry;
        System.Action onGiveUp;
        float openedTime;
        bool decided;

        /// <summary>Puts the screen up and freezes the game under it. The chosen callback runs with time already unfrozen.</summary>
        public static GameOverScreen Show(System.Action onRetry, System.Action onGiveUp)
        {
            var over = FindFirstObjectByType<GameOverScreen>(FindObjectsInactive.Include);
            if (over == null) over = new GameObject("GameOverScreen").AddComponent<GameOverScreen>();
            over.enabled = true;
            over.decided = false;
            over.onRetry = onRetry;
            over.onGiveUp = onGiveUp;
            over.theme = MenuTheme.Load();
            over.nav = new MenuNavigator(over.theme);
            over.Build();
            return over;
        }

        // A scene-placed (prefab) instance idles until Show(): without a theme
        // its Update would throw, so it disarms itself here.
        void Awake()
        {
            if (theme == null) enabled = false;
        }

        // Root components are reused by Build: play-mode Destroy is deferred,
        // so removing and re-adding them in the same frame would collide.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
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
            canvas.enabled = true; // a previous answer switched it off
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GetOrAdd<GraphicRaycaster>(gameObject);

            ui = GetOrAdd<AudioSource>(gameObject);
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var dim = panel.AddComponent<Image>();
            var dimColor = theme.Backdrop;
            dimColor.a = 0.85f;
            dim.color = dimColor;

            screen = MenuScreenFactory.BuildConfirm(panelRect, theme,
                                                    MenuTextId.GameOver, MenuTextId.RetryPrompt,
                                                    () => Decide(onRetry), () => Decide(onGiveUp));
            screen.SetFocus(0); // retrying is the expected answer, not the dangerous one — focus starts on YES

            PromptStrip.Create(panelRect, theme, 56f)
                       .SetHints((PromptAction.Navigate, MenuTextId.HintMove),
                                 (PromptAction.Confirm, MenuTextId.HintSelect));

            MenuScreenFactory.EnsureEventSystem(); // the city scene has none; mouse clicks need one
            IsOpen = true;
            Time.timeScale = 0f;
            openedTime = Time.unscaledTime;
            screen.Show(staggered: false);
            Gamepad.current?.ResetHaptics();
        }

        void Update()
        {
            if (decided) return;
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            int vertical = nav.StepVertical(Time.unscaledDeltaTime);
            if (vertical != 0)
            {
                screen.MoveFocus(-vertical); // rows run top-down, so up is index-1
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            if (MenuNavigator.ConfirmPressed()) screen.Focused?.Activate();
        }

        void Decide(System.Action choice)
        {
            if (decided) return;
            decided = true;
            IsOpen = false;
            Blip(theme.ConfirmClip);
            Time.timeScale = 1f;
            Hide();
            choice?.Invoke();
        }

        /// <summary>
        /// Clears the overlay before the answer runs. The city reloads its
        /// scene on either answer so it would go away by itself, but the
        /// runner retries IN PLACE — the dim panel would otherwise sit over
        /// the new run still asking a question that has been answered. The
        /// root (canvas, audio source) survives for the next death, which is
        /// also what keeps the confirm blip audible through the teardown.
        /// </summary>
        void Hide()
        {
            enabled = false;
            TearDown();
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = false;
        }

        // Safety: never leave the game frozen if this object goes away without
        // an answer — this also fires on the way out of play mode.
        void OnDestroy()
        {
            IsOpen = false;
            if (!decided) Time.timeScale = 1f;
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }
    }
}
