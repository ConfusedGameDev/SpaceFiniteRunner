using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// Pause system: Esc or gamepad Start freezes the game (timeScale 0 +
    /// motor pause) and shows a Resume / Restart / Exit menu. Only opens
    /// during active gameplay — never over the tuning screen or the result
    /// screen, where those buttons mean other things. Menu is mouse-clickable
    /// and fully driveable by shortcuts (keyboard and gamepad, shown on the
    /// buttons). Built from code on its own overlay canvas, spawned by the
    /// GameManager — no scene wiring needed.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        GameManager gameManager;
        ShipMotor motor;
        GameObject panel;
        bool isPaused;

        public static PauseMenu Spawn(GameManager gameManager, ShipMotor motor)
        {
            var go = new GameObject("PauseMenu");
            var menu = go.AddComponent<PauseMenu>();
            menu.gameManager = gameManager;
            menu.motor = motor;
            menu.Build();
            return menu;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool toggle = kb is { escapeKey: { wasPressedThisFrame: true } } ||
                          pad is { startButton: { wasPressedThisFrame: true } };

            if (!isPaused)
            {
                if (toggle && CanPause()) Pause();
                return;
            }

            if (toggle ||
                pad is { buttonSouth: { wasPressedThisFrame: true } } or { buttonEast: { wasPressedThisFrame: true } })
            {
                Resume();
                return;
            }
            if (kb is { rKey: { wasPressedThisFrame: true } } ||
                pad is { buttonWest: { wasPressedThisFrame: true } })
            {
                RestartRun();
                return;
            }
            if (kb is { qKey: { wasPressedThisFrame: true } } ||
                pad is { buttonNorth: { wasPressedThisFrame: true } })
                ExitGame();
        }

        // Active gameplay only: motor.Paused covers the tuning screen and the
        // frozen end-of-run state; RunOver covers the result screen.
        bool CanPause() =>
            motor != null && !motor.Paused && (gameManager == null || !gameManager.RunOver);

        void Pause()
        {
            isPaused = true;
            motor.Paused = true;
            Time.timeScale = 0f;
            panel.SetActive(true);
            Gamepad.current?.ResetHaptics();
        }

        void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            motor.Paused = false;
            panel.SetActive(false);
        }

        void RestartRun()
        {
            Resume(); // restore timeScale before the run resets
            if (gameManager != null) gameManager.Restart();
            else motor.Launch();
        }

        static void ExitGame()
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
            canvas.sortingOrder = 20;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            gameObject.AddComponent<GraphicRaycaster>();

            // Full-screen dim that also blocks clicks reaching the HUD below.
            panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            MakeText("Title", panelRect, new Vector2(0f, 160f), new Vector2(600f, 80f), "PAUSED", 56, Color.white);

            MakeButton(panelRect, new Vector2(0f, 40f), "RESUME  (A / ESC)", Resume);
            MakeButton(panelRect, new Vector2(0f, -50f), "RESTART  (X / R)", RestartRun);
            MakeButton(panelRect, new Vector2(0f, -140f), "EXIT  (Y / Q)", ExitGame);

            panel.SetActive(false);
        }

        static Text MakeText(string name, Transform parent, Vector2 position, Vector2 size,
                             string content, int fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            return text;
        }

        void MakeButton(Transform parent, Vector2 position, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(420f, 70f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.12f, 0.14f, 0.2f, 0.95f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            MakeText("Label", rect, Vector2.zero, rect.sizeDelta, label, 30, Color.white);
        }
    }
}
