using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The placeholder the Store's START MISSION leads to once every
    /// authored campaign mission is complete — the only door to it. A
    /// menu-theme backdrop, the localized COMING SOON title and one EXIT TO
    /// MAIN MENU row on the themed menu framework, through the loading
    /// curtain like every scene trip. A hand-placed scene-lifetime object
    /// (the project rule) the scene builder puts in <c>ComingSoon.unity</c>;
    /// the canvas is built under it at play.
    /// </summary>
    public class ComingSoonScreen : MonoBehaviour
    {
        /// <summary>Scene name of the placeholder — what the catalog's default points at.</summary>
        public const string SceneName = "ComingSoon";

        const int SortingOrder = 30;
        const int UiLayer = 5;
        const float ContentTop = 40f;

        MenuTheme theme;
        MenuNavigator nav;
        RectTransform root;
        AudioSource ui;
        PromptStrip footer;
        MenuScreen screen;
        float openedTime;
        bool leaving;

        void Start()
        {
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            Build();
            MenuScreenFactory.EnsureEventSystem();
            openedTime = Time.unscaledTime;
            screen.Show(true);
            if (footer != null)
                footer.SetHints((PromptAction.Confirm, MenuTextId.HintSelect), (PromptAction.Back, MenuTextId.HintBack));
        }

        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            Build();
            screen.Show(false);
        }

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            footer = null;
            screen = null;
        }

        void Build()
        {
            TearDown();
            gameObject.layer = UiLayer;

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            GetOrAdd<GraphicRaycaster>(gameObject);
            root = (RectTransform)transform;

            ui = GetOrAdd<AudioSource>(gameObject);
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            // A full backdrop: nothing lives behind this screen.
            Image backdrop = MenuScreen.MakeImage("Backdrop", root, Vector2.zero, new Vector2(4000f, 4000f), null, theme.Backdrop);
            backdrop.raycastTarget = false;

            screen = MenuScreen.Create("ComingSoonScreen", root, theme, 0f, ContentTop);
            screen.SetTitle(MenuTextId.ComingSoon);
            screen.AddRow<MenuRow>(MenuTextId.ExitToMenu).Activated += Leave;
            screen.HideImmediate();

            footer = PromptStrip.Create(root, theme, 56f);
        }

        void Update()
        {
            if (theme == null || screen == null || leaving) return;
            InputPromptBinder.Poll();
            float dt = Time.unscaledDeltaTime;
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            if (MenuNavigator.BackPressed())
            {
                Leave();
                return;
            }

            int vertical = nav.StepVertical(dt);
            if (vertical != 0)
            {
                screen.MoveFocus(-vertical);
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            if (MenuNavigator.ConfirmPressed()) screen.Focused?.Activate();
        }

        // Back or the row: the main menu, through the loading curtain.
        void Leave()
        {
            if (leaving || LoadingScreen.IsLoading) return;
            leaving = true;
            Blip(theme.BackClip);
            LoadingScreen.LoadMainMenu();
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
