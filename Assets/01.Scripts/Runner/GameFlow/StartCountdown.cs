using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The start of a race or a time trial: READY!!, 3, 2, 1, GO! over the
    /// grid, with the whole run held on the line until GO. The hold IS
    /// <see cref="ShipMotor.Paused"/>, which everything that moves already
    /// obeys: the ship's sim and its controls, the patrol, the traffic, the
    /// race (whose lights out is the tick the ship moves, so the rivals fire
    /// their launch on GO with no hook of their own), the countdown clock and
    /// the engine sound. On GO the motor is released and any dash pressed on
    /// the line is drained, so the first input that counts is GO's.
    ///
    /// Ensured beside the <see cref="GameManager"/> and bound in its Awake;
    /// <see cref="Begin"/> runs on the first frame and on every retry
    /// (<see cref="GameManager.Restart"/>) — only for a level that races
    /// (<c>race</c> or a time trial) with <see cref="GameSettings.startCountdownEnabled"/>
    /// on. Ticks on SCALED time, so the pause menu freezes it. The words are
    /// <see cref="MenuTextId.CountdownReady"/> / <see cref="MenuTextId.CountdownGo"/>
    /// in the theme's title font on its own overlay canvas, under the endings'
    /// banner; the numbers are numbers.
    /// </summary>
    public class StartCountdown : MonoBehaviour
    {
        const int SortingOrder = 21;       // above the HUD (10) and messages (15), under the endings' banner (22)
        const int FontSize = 180;
        const float CenterY = 120f;        // a little above centre, over the ship
        const float SlamFromScale = 2.4f;
        const float SlamSeconds = 0.16f;

        ShipMotor motor;
        GameSettings settings;
        bool wanted;          // the level races and the countdown is on
        bool holding;         // the run is held on the line
        bool started;         // the first Begin has run (Start, or a retry before it)
        float clock;          // seconds since Begin, scaled
        int shownBeat = -2;   // -1 READY, 3..1 the numbers, 0 GO

        Canvas canvas;
        Text word;
        AudioSource ui;
        AudioClip blip;

        /// <summary>True while the run is held for the countdown (the ship, rivals, patrol and clock wait).</summary>
        public bool Holding => holding;

        public static StartCountdown Ensure(Component host)
        {
            var countdown = host.GetComponent<StartCountdown>();
            return countdown != null ? countdown : host.gameObject.AddComponent<StartCountdown>();
        }

        /// <summary>Binds it to the run. <paramref name="racing"/> = the level is a race or a time trial; anything else launches at once.</summary>
        public void Bind(ShipMotor ship, GameSettings runSettings, bool racing)
        {
            motor = ship;
            settings = runSettings;
            wanted = racing && motor != null && settings != null && settings.startCountdownEnabled;
        }

        // Start runs before the first physics tick, so the ship never moves a step before READY.
        void Start()
        {
            if (!started) Begin();
        }

        /// <summary>Holds the run on the line and plays the count from READY. A level that does not race is left alone.</summary>
        public void Begin()
        {
            started = true;
            if (!wanted)
            {
                Hide();
                return;
            }
            holding = true;
            clock = 0f;
            shownBeat = -2;
            motor.Paused = true;
            EnsureCanvas();
        }

        /// <summary>Drops the count without releasing anything (the run is ending or being torn down).</summary>
        public void Cancel()
        {
            holding = false;
            Hide();
        }

        void Update()
        {
            if (!holding && (word == null || !word.enabled)) return;
            clock += Time.deltaTime;

            float ready = settings.countdownReadySeconds;
            float beat = settings.countdownBeatSeconds;
            float goAt = ready + beat * 3f;

            if (holding)
            {
                // Held every frame: anything that unpauses the motor meanwhile (a
                // launch, a menu closing) must not start the run early.
                motor.Paused = true;
                if (clock >= goAt)
                {
                    holding = false;
                    motor.Paused = false;
                    motor.ConsumeDashRequest(); // a dash pressed on the line is not GO's input
                    HapticsSystem.Instance.Pulse(0.6f, 0.9f, 0.25f);
                }
            }

            int beatNow = clock < ready ? -1 : clock < goAt ? 3 - Mathf.FloorToInt((clock - ready) / beat) : 0;
            if (beatNow != shownBeat)
            {
                shownBeat = beatNow;
                Show(beatNow);
            }

            // Each word slams in from SlamFromScale; GO fades out over its hold.
            float since = beatNow == -1 ? clock : beatNow == 0 ? clock - goAt : clock - ready - beat * (3 - beatNow);
            float slam = Mathf.Clamp01(since / SlamSeconds);
            float scale = Mathf.Lerp(SlamFromScale, 1f, slam * slam);
            word.rectTransform.localScale = new Vector3(scale, scale, 1f);
            if (beatNow == 0)
            {
                float hold = settings.countdownGoSeconds;
                Color c = word.color;
                c.a = 1f - Mathf.Clamp01((since - hold * 0.5f) / (hold * 0.5f));
                word.color = c;
                if (since >= hold) Hide();
            }
        }

        void Show(int beat)
        {
            EnsureCanvas();
            word.enabled = true;
            MenuTextLibrary texts = MenuTextLibrary.Load();
            word.text = beat == -1 ? texts.Get(MenuTextId.CountdownReady)
                      : beat == 0 ? texts.Get(MenuTextId.CountdownGo)
                      : beat.ToString();
            word.color = beat == 0 ? settings.countdownGoColor : settings.countdownNumberColor;
            if (ui != null && blip != null)
            {
                // Rising pitch toward GO, the GO an octave up.
                ui.pitch = beat == -1 ? 0.8f : beat == 0 ? 2f : 1f + (3 - beat) * 0.15f;
                ui.PlayOneShot(blip);
            }
            if (beat > 0) HapticsSystem.Instance.Pulse(0.2f, 0.3f, 0.08f);
        }

        void Hide()
        {
            if (word != null) word.enabled = false;
        }

        void EnsureCanvas()
        {
            if (canvas != null) return;
            MenuTheme theme = MenuTheme.Load();

            var go = new GameObject("StartCountdown");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var textGo = new GameObject("Word", typeof(RectTransform));
            var rect = (RectTransform)textGo.transform;
            rect.SetParent(go.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, CenterY);
            rect.sizeDelta = new Vector2(1800f, 300f);
            word = textGo.AddComponent<Text>();
            word.font = theme.TitleFont;
            word.fontSize = FontSize;
            word.alignment = TextAnchor.MiddleCenter;
            word.horizontalOverflow = HorizontalWrapMode.Wrap; // best fit only shrinks a wrapped text
            word.verticalOverflow = VerticalWrapMode.Overflow;
            word.raycastTarget = false;
            // Fits every language: a long READY shrinks rather than running off screen.
            word.resizeTextForBestFit = true;
            word.resizeTextMinSize = 60;
            word.resizeTextMaxSize = FontSize;
            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(4f, -4f);
            word.enabled = false;

            ui = go.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;
            blip = RpgMessageSystem.PlaceholderBlip();
        }
    }
}
