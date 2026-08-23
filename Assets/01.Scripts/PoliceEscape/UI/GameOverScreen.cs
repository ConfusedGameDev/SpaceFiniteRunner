using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The city chase's death screen: once the glitch has filled and held,
    /// the LevelManager shows GAME OVER — RETRY? YES / NO on the shared
    /// themed menu framework instead of reloading the scene on its own.
    /// YES replays the level, NO returns to the main menu; there is no Back
    /// out — the screen demands an answer, so Esc/B do nothing here. It
    /// freezes scaled time while it is up (which also keeps the pause menu
    /// and the city map from opening — both refuse to stack over a stopped
    /// clock) and animates on unscaled time like every other menu. Built
    /// from code on its own overlay canvas, above the maxed glitch: the
    /// glitch is a render feature on the camera, and an overlay canvas draws
    /// after it, so the choice stays readable through full corruption.
    /// </summary>
    public class GameOverScreen : MonoBehaviour
    {
        const int SortingOrder = 25; // above the pause menu (20), below the main menu (30)

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
            var go = new GameObject("GameOverScreen");
            var over = go.AddComponent<GameOverScreen>();
            over.onRetry = onRetry;
            over.onGiveUp = onGiveUp;
            over.theme = MenuTheme.Load();
            over.nav = new MenuNavigator(over.theme);
            over.Build();
            return over;
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
            Blip(theme.ConfirmClip);
            Time.timeScale = 1f;
            choice?.Invoke();
        }

        // Safety: never leave the game frozen if this object goes away without
        // an answer — this also fires on the way out of play mode.
        void OnDestroy()
        {
            if (!decided) Time.timeScale = 1f;
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }
    }
}
