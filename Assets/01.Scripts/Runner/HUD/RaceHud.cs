using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The race HUD (uGUI): the speed as a <see cref="SpeedGauge"/> wedge
    /// (segments growing taller to the right, lit up to the ship's fraction
    /// of Light Speed, built here in code at the top-left) with the scene's
    /// km/h number re-seated at its right end at Start — smaller font, its
    /// baseline on the wedge's — heating up as you approach Light Speed and
    /// pulsing on pad hits, the Light Speed goal, the countdown as a plain
    /// yellow MM:SS at the bottom centre (the scene's timer text re-seated
    /// at Start, sized like the speed number — no bar; the distance left is
    /// the <see cref="ChaseMinimap"/>'s), and
    /// floating "+boost" text spawned at the ship on every booster hit (and a
    /// gold "+$N" on every money pickup, off the CollectibleManager), plus
    /// one code-built line per runner objective / challenge under the goal
    /// ("JUMP 1/3  ×2"). It offers no retry of its own any more: a loss ends
    /// on the <see cref="GameOverScreen"/> and a win on the
    /// <see cref="MissionCompleteScreen"/>, which own that answer.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] GameManager gameManager;

        [Header("Widgets")]
        [SerializeField] Text speedText;
        [SerializeField] Text targetText;
        [FormerlySerializedAs("distanceText")]
        [SerializeField] Text timeText;

        [Header("Speed colors")]
        [Tooltip("Far below Light Speed.")]
        [SerializeField] Color slowColor = new(0.31f, 0.76f, 1f);      // blue
        [Tooltip("Making good progress toward Light Speed.")]
        [SerializeField] Color onTargetColor = new(0.48f, 0.83f, 0.32f); // green
        [Tooltip("Closing in on Light Speed.")]
        [SerializeField] Color fastColor = new(1f, 0.35f, 0.25f);      // hot

        [Header("Status colors")]
        [FormerlySerializedAs("perfectColor")]
        [SerializeField] Color winColor = new(0.48f, 0.83f, 0.32f);
        [Tooltip("Objective / challenge lines not yet done.")]
        [FormerlySerializedAs("timeColor")]
        [SerializeField] Color lineColor = Color.white;

        [Header("Countdown")]
        [Tooltip("The MM:SS readout's colour — always, it never tints.")]
        [SerializeField] Color timerColor = new(1f, 0.9f, 0.2f);
        [Tooltip("Gap between the bottom of the screen and the timer's baseline, px at 1920×1080. Its font size is the speed number's.")]
        [SerializeField, Range(0f, 300f)] float timerBottomMargin = 40f;

        [Header("Pad pulse")]
        [SerializeField, Min(1f)] float pulseScale = 1.3f;
        [SerializeField, Min(0.1f)] float pulseDecay = 6f;

        [Header("Speed gauge")]
        [Tooltip("Segments of the speed wedge; every one lit = Light Speed.")]
        [SerializeField, Range(4, 60)] int gaugeSegments = 20;
        [Tooltip("Width of one segment, px at 1920×1080.")]
        [SerializeField, Range(4f, 60f)] float gaugeSegmentWidth = 20f;
        [Tooltip("Gap between segments.")]
        [SerializeField, Range(0f, 30f)] float gaugeSegmentGap = 6f;
        [Tooltip("Height of the leftmost segment.")]
        [SerializeField, Range(4f, 200f)] float gaugeMinHeight = 18f;
        [Tooltip("Height of the rightmost segment — the wedge's height.")]
        [SerializeField, Range(4f, 300f)] float gaugeMaxHeight = 80f;
        [Tooltip("Alpha of the segments not yet reached.")]
        [SerializeField, Range(0f, 1f)] float gaugeEmptyAlpha = 0.2f;
        [Tooltip("Font size the km/h number is re-seated with at the wedge's right end.")]
        [SerializeField, Range(20, 200)] int gaugeNumberFontSize = 84;
        [Tooltip("Gap between the wedge and the number.")]
        [SerializeField, Range(0f, 100f)] float gaugeNumberGap = 24f;

        [Header("Life bar")]
        [Tooltip("The KM/H caption under the wedge — pushed down with the goal lines to make room for the life bar. Empty = found by name (KmhLabel) beside the speed text.")]
        [SerializeField] Text unitText;
        [Tooltip("Cells of the hull bar under the speed wedge. A cell stays lit while any of its share of the hull is left.")]
        [SerializeField, Range(1, 30)] int lifeSegments = 6;
        [Tooltip("Height of the bar, px at 1920×1080. Its width is the wedge's.")]
        [SerializeField, Range(4f, 80f)] float lifeBarHeight = 22f;
        [Tooltip("Gap between cells.")]
        [SerializeField, Range(0f, 30f)] float lifeSegmentGap = 6f;
        [Tooltip("Gap between the wedge and the bar, and between the bar and the KM/H caption.")]
        [SerializeField, Range(0f, 60f)] float lifeBarGap = 12f;
        [Tooltip("Alpha of the cells already lost.")]
        [SerializeField, Range(0f, 1f)] float lifeEmptyAlpha = 0.2f;
        [SerializeField] Color lifeFullColor = new(0.48f, 0.83f, 0.32f);
        [SerializeField] Color lifeMidColor = new(1f, 0.85f, 0.3f);
        [SerializeField] Color lifeLowColor = new(1f, 0.25f, 0.2f);
        [Tooltip("Hull fraction under which the bar blinks.")]
        [SerializeField, Range(0f, 1f)] float lifeLowFraction = 0.34f;
        [Tooltip("Blinks per second while low.")]
        [SerializeField, Range(0.5f, 10f)] float lifeLowBlinkHz = 3f;
        [Tooltip("Scale the bar (and the lives count, when a life is lost) jumps to on a hit.")]
        [SerializeField, Min(1f)] float lifeHitPunch = 1.25f;
        [Tooltip("Font size of the ×N lives count at the bar's right end.")]
        [SerializeField, Range(12, 120)] int livesFontSize = 40;
        [Tooltip("Gap between the bar and the lives count.")]
        [SerializeField, Range(0f, 100f)] float livesGap = 18f;
        [SerializeField] Color livesColor = Color.white;

        [Header("Boost floating text")]
        [SerializeField] bool spawnBoostText = true;
        [SerializeField] Color boostTextColor = new(0.48f, 1f, 0.4f);
        [SerializeField, Min(0.05f)] float boostTextSize = 0.6f;
        [Tooltip("Colour of the floating \"+$N\" popup on a money pickup (same lead and size as the boost text).")]
        [SerializeField] Color moneyTextColor = new(1f, 0.85f, 0.3f);
        [Tooltip("Colour the hull bar flashes, and of the floating \"+N\" popup, when a repair orb gives hull back.")]
        [SerializeField] Color repairColor = new(0.3f, 1f, 0.45f);

        float currentPulse = 1f;

        // The objective readout: (goal, is it a challenge, its index in the
        // level's list, the line drawn for it), built once in Start.
        readonly List<(RunnerObjective step, bool challenge, int index, Text text)> objectiveLines = new();

        SpeedGauge gauge;
        Color targetColor;      // the goal line's authored colour, before the done tint
        int shownSeconds = -1;  // what the timer text currently reads, so it is rebuilt once a second

        // The hull bar under the wedge and the ×N beside it; null while the hull is off.
        SpeedGauge lifeBar;
        Text livesText;
        float lastLife = 1f;   // last frame's hull fraction: a drop is a hit
        float lifeFlash;       // 1 → 0 white flash after a hit
        float lifePunch = 1f;  // the bar's scale, decaying to 1
        float healFlash;       // 1 → 0 green flash after a repair orb
        float livesPunch = 1f; // the count's scale, decaying to 1
        int shownLives = -1;   // what the count currently reads

        void Start()
        {
            BuildGauge();
            BuildLifeBar();
            SeatTimer();
            if (gameManager == null || targetText == null || gameManager.Level == null) return;
            targetColor = targetText.color;
            RunnerLevelDefinition level = gameManager.Level;
            int slot = 0;
            for (int i = 0; i < level.Count; i++)
            {
                RunnerObjective step = level.objectives[i];
                // The goal line above already shows the Light Speed target.
                if (step.type == RunnerObjectiveType.ReachSpeed && Mathf.Approximately(step.targetSpeedKmh, gameManager.LightSpeedKmh)) continue;
                objectiveLines.Add((step, false, i, MakeObjectiveLine(slot++)));
            }
            for (int i = 0; i < level.ChallengeCount; i++)
                objectiveLines.Add((level.optionalChallenges[i], true, i, MakeObjectiveLine(slot++)));
        }

        // The wedge takes the number's row: it is built where the scene's
        // speed text sits (top-left), and the text is re-seated beside it —
        // by code, so the scene wiring stays untouched and the knobs above
        // stay live.
        void BuildGauge()
        {
            if (speedText == null) return;
            RectTransform number = speedText.rectTransform;
            Vector2 topLeft = number.anchoredPosition;
            gauge = SpeedGauge.Build((RectTransform)number.parent, topLeft, new SpeedGauge.Layout
            {
                segments = gaugeSegments,
                segmentWidth = gaugeSegmentWidth,
                gap = gaugeSegmentGap,
                minHeight = gaugeMinHeight,
                maxHeight = gaugeMaxHeight,
                emptyAlpha = gaugeEmptyAlpha,
            });

            number.anchoredPosition = topLeft + new Vector2(gauge.Width + gaugeNumberGap, 0f);
            number.sizeDelta = new Vector2(number.sizeDelta.x, gauge.Height);
            speedText.fontSize = gaugeNumberFontSize;
            speedText.alignment = TextAnchor.LowerLeft; // baseline on the wedge's baseline
        }

        // The hull bar takes a row of its own right under the wedge — the
        // wedge's width in flat cells, the ×N lives count at its right end —
        // and everything that stood there (the KM/H caption, the goal line
        // and so the objective lines stacked off it) moves down by that row.
        // Built only while the hull is on (GameSettings.hullEnabled).
        void BuildLifeBar()
        {
            if (gauge == null || gameManager == null || !gameManager.HullEnabled || gameManager.ShipHealth == null) return;

            var gaugeRect = (RectTransform)gauge.transform;
            int cells = Mathf.Max(1, lifeSegments);
            Vector2 topLeft = gaugeRect.anchoredPosition - new Vector2(0f, gauge.Height + lifeBarGap);
            lifeBar = SpeedGauge.Build((RectTransform)gaugeRect.parent, topLeft, new SpeedGauge.Layout
            {
                segments = cells,
                segmentWidth = (gauge.Width - (cells - 1) * lifeSegmentGap) / cells,
                gap = lifeSegmentGap,
                minHeight = lifeBarHeight,
                maxHeight = lifeBarHeight,
                emptyAlpha = lifeEmptyAlpha,
            });
            lifeBar.name = "LifeBar";

            var go = new GameObject("Lives", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(gaugeRect.parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f); // centred on the bar, punching from its own middle
            rect.anchoredPosition = topLeft + new Vector2(lifeBar.Width + livesGap, -lifeBarHeight * 0.5f);
            rect.sizeDelta = new Vector2(200f, lifeBarHeight);
            livesText = go.AddComponent<Text>();
            // The goal line's font: it is the one known to carry the × glyph (the challenge lines print it).
            livesText.font = targetText != null ? targetText.font : speedText.font;
            livesText.fontSize = livesFontSize;
            livesText.alignment = TextAnchor.MiddleLeft;
            livesText.horizontalOverflow = HorizontalWrapMode.Overflow;
            livesText.verticalOverflow = VerticalWrapMode.Overflow;
            livesText.color = livesColor;
            livesText.raycastTarget = false;

            Vector2 push = new(0f, lifeBarHeight + lifeBarGap);
            if (unitText == null)
            {
                Transform found = gaugeRect.parent.Find("KmhLabel");
                if (found != null) unitText = found.GetComponent<Text>();
            }
            if (unitText != null) unitText.rectTransform.anchoredPosition -= push;
            if (targetText != null) targetText.rectTransform.anchoredPosition -= push;
        }

        void UpdateLifeBar()
        {
            if (lifeBar == null) return;

            float life = gameManager.ShipHealth.Fraction;
            if (life < lastLife - 1e-4f)
            {
                lifeFlash = 1f;
                lifePunch = lifeHitPunch;
            }
            lastLife = life;
            lifeFlash = Mathf.MoveTowards(lifeFlash, 0f, pulseDecay * Time.deltaTime);
            healFlash = Mathf.MoveTowards(healFlash, 0f, pulseDecay * Time.deltaTime);
            lifePunch = Mathf.MoveTowards(lifePunch, 1f, pulseDecay * Time.deltaTime);
            lifeBar.transform.localScale = Vector3.one * lifePunch;

            // One colour for the whole bar — how hurt the ship is, not a scale.
            Color color = life > 0.5f
                ? Color.Lerp(lifeMidColor, lifeFullColor, (life - 0.5f) / 0.5f)
                : Color.Lerp(lifeLowColor, lifeMidColor, life / 0.5f);
            if (life > 0f && life <= lifeLowFraction && Mathf.Repeat(Time.time * lifeLowBlinkHz, 1f) > 0.5f)
                color *= 0.45f;
            color = Color.Lerp(color, repairColor, healFlash);
            color = Color.Lerp(color, Color.white, lifeFlash);
            color.a = 1f;

            // A cell stays lit while any of its share is left: round UP to cells.
            int cells = Mathf.Max(1, lifeSegments);
            float shown = Mathf.Ceil(life * cells - 1e-4f) / cells;
            lifeBar.SetFill(shown, _ => color);

            int lives = gameManager.LivesLeft;
            if (lives != shownLives)
            {
                if (shownLives >= 0) livesPunch = lifeHitPunch * 1.2f;
                shownLives = lives;
                livesText.text = $"×{lives}";
            }
            livesPunch = Mathf.MoveTowards(livesPunch, 1f, pulseDecay * Time.deltaTime);
            livesText.rectTransform.localScale = Vector3.one * livesPunch;
        }

        // The countdown is the scene's timer text, re-seated by code like the
        // speed number: bottom centre, the speed number's size, always yellow.
        void SeatTimer()
        {
            if (timeText == null) return;
            RectTransform rect = timeText.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, timerBottomMargin);
            timeText.fontSize = gaugeNumberFontSize;
            timeText.alignment = TextAnchor.LowerCenter;
            timeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            timeText.verticalOverflow = VerticalWrapMode.Overflow;
            timeText.color = timerColor;
        }

        // A smaller sibling of the goal text, stacked under it — cloned from
        // its font and anchors so the scene wiring stays untouched.
        Text MakeObjectiveLine(int slot)
        {
            RectTransform source = targetText.rectTransform;
            var go = new GameObject($"Objective{slot}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(source.parent, false);
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.sizeDelta = source.sizeDelta;
            float step = Mathf.Max(24f, source.sizeDelta.y * 0.8f);
            rect.anchoredPosition = source.anchoredPosition - new Vector2(0f, step * (slot + 1));

            var text = go.AddComponent<Text>();
            text.font = targetText.font;
            text.fontSize = Mathf.Max(12, Mathf.RoundToInt(targetText.fontSize * 0.75f));
            text.alignment = targetText.alignment;
            text.horizontalOverflow = targetText.horizontalOverflow;
            text.verticalOverflow = targetText.verticalOverflow;
            text.color = lineColor;
            text.raycastTarget = false;
            return text;
        }

        void OnEnable()
        {
            if (motor != null) motor.PadImpulse += OnPadImpulse;
            CollectibleManager.MoneyChanged += OnMoneyChanged;
            RepairOrb.Collected += OnRepairOrb;
        }

        void OnDisable()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
            CollectibleManager.MoneyChanged -= OnMoneyChanged;
            RepairOrb.Collected -= OnRepairOrb;
        }

        // The money twin of the boost popup: "+$3" in gold ahead of the ship.
        // A reset (delta 0) and a finished run show nothing.
        void OnMoneyChanged(int total, int delta)
        {
            if (!spawnBoostText || delta <= 0 || gameManager == null || gameManager.RunOver) return;
            FloatingTextSystem.Instance.DisplayText(
                $"+${delta}", moneyTextColor, 1f,
                gameManager.BoostTextLeadMeters, boostTextSize);
        }

        // A repair orb: the bar flashes green and punches, and "+N" (hull
        // points) floats up ahead of the ship like the boost text. Keyed on
        // the orb, not on the fraction rising — a restart refills it too.
        void OnRepairOrb(RepairOrb orb, IShip collector, float healed)
        {
            if (motor == null || !motor.Is(collector)) return;
            healFlash = 1f;
            lifePunch = lifeHitPunch;
            if (spawnBoostText && gameManager != null && !gameManager.RunOver)
                FloatingTextSystem.Instance.DisplayText(
                    $"+{healed:0}", repairColor, 1f,
                    gameManager.BoostTextLeadMeters, boostTextSize);
        }

        void OnPadImpulse(float magnitude)
        {
            currentPulse = pulseScale;

            // Juice: floating text for every booster hit, spawned ahead of the
            // ship (GameSettings.boostTextLeadMeters) so it isn't left behind
            // instantly at speed.
            if (spawnBoostText && magnitude > 0f && gameManager != null)
                FloatingTextSystem.Instance.DisplayText(
                    $"+{magnitude:0}", boostTextColor, 1f,
                    gameManager.BoostTextLeadMeters, boostTextSize);
        }

        void Update()
        {
            if (motor == null) return;

            float kmh = motor.CurrentSpeed * 3.6f;
            float lightSpeed = gameManager != null ? gameManager.LightSpeedKmh : 0f;

            if (speedText != null)
            {
                speedText.text = $"{kmh:0}";
                speedText.color = SpeedColor(kmh, lightSpeed);

                currentPulse = Mathf.MoveTowards(currentPulse, 1f, pulseDecay * Time.deltaTime);
                speedText.rectTransform.localScale = Vector3.one * currentPulse;
            }

            if (gauge != null)
                gauge.SetFill(lightSpeed > 0f ? Mathf.Clamp01(kmh / lightSpeed) : 0f,
                              fraction => SpeedColor(fraction * lightSpeed, lightSpeed));

            UpdateLifeBar();

            if (targetText != null && lightSpeed > 0f)
            {
                targetText.text = $"LIGHT SPEED  {lightSpeed:0} KM/H";
                // Reached once is reached: the goal line stays done while the
                // ship still has to make it to an end ramp.
                if (gameManager != null && gameManager.Level != null)
                    targetText.color = gameManager.LightSpeedReached ? winColor : targetColor;
            }

            foreach (var line in objectiveLines)
            {
                bool done = line.challenge ? gameManager.IsChallengeDone(line.index) : gameManager.IsObjectiveDone(line.index);
                string label = line.step.Summary;
                string progress = line.step.Progress(kmh, gameManager.JumpCount);
                if (progress.Length > 0) label += "  " + progress;
                if (line.step is RunnerOptionalChallenge challenge) label += $"  \u00d7{challenge.multiplier}";
                line.text.text = label;
                line.text.color = done ? winColor : lineColor;
            }

            UpdateCountdown();
        }

        void UpdateCountdown()
        {
            if (gameManager == null || timeText == null) return;

            // Whole seconds, rounded up so 00:00 is the moment the clock runs out.
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, gameManager.TimeRemaining));
            if (seconds == shownSeconds) return;
            shownSeconds = seconds;
            timeText.text = $"{seconds / 60:00}:{seconds % 60:00}";
            timeText.color = timerColor;
        }

        // The readout heats up as speed climbs toward Light Speed: blue when
        // slow, green mid-climb, hot near the goal. Faster is always better now.
        Color SpeedColor(float kmh, float lightSpeed)
        {
            if (lightSpeed <= 0f) return onTargetColor;
            float progress = Mathf.Clamp01(kmh / lightSpeed);
            return progress < 0.6f
                ? Color.Lerp(slowColor, onTargetColor, progress / 0.6f)
                : Color.Lerp(onTargetColor, fastColor, (progress - 0.6f) / 0.4f);
        }

    }
}
