using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The race HUD (uGUI): the speed as a <see cref="SpeedGauge"/> wedge
    /// (segments growing taller to the right, lit up to the ship's fraction
    /// of Light Speed, built here in code at the top-left) with the scene's
    /// km/h number re-seated at its right end at Start — smaller font, its
    /// baseline on the wedge's — heating up as you approach Light Speed and
    /// pulsing on pad hits, the Light Speed goal, a countdown
    /// bar (time is the limit, not distance), and
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
        [FormerlySerializedAs("distanceFill")]
        [SerializeField] Image timeFill;

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
        [SerializeField] Color failColor = new(1f, 0.3f, 0.25f);

        [Header("Countdown")]
        [Tooltip("The timer text tints with the fail color below this many seconds.")]
        [SerializeField, Min(0f)] float lowTimeWarning = 10f;
        [SerializeField] Color timeColor = Color.white;

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

        [Header("Boost floating text")]
        [SerializeField] bool spawnBoostText = true;
        [SerializeField] Color boostTextColor = new(0.48f, 1f, 0.4f);
        [SerializeField, Min(0.05f)] float boostTextSize = 0.6f;
        [Tooltip("Colour of the floating \"+$N\" popup on a money pickup (same lead and size as the boost text).")]
        [SerializeField] Color moneyTextColor = new(1f, 0.85f, 0.3f);

        float currentPulse = 1f;

        // The objective readout: (goal, is it a challenge, its index in the
        // level's list, the line drawn for it), built once in Start.
        readonly List<(RunnerObjective step, bool challenge, int index, Text text)> objectiveLines = new();

        SpeedGauge gauge;

        void Start()
        {
            BuildGauge();
            if (gameManager == null || targetText == null || gameManager.Level == null) return;
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
            text.color = timeColor;
            text.raycastTarget = false;
            return text;
        }

        void OnEnable()
        {
            if (motor != null) motor.PadImpulse += OnPadImpulse;
            CollectibleManager.MoneyChanged += OnMoneyChanged;
        }

        void OnDisable()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
            CollectibleManager.MoneyChanged -= OnMoneyChanged;
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

            if (targetText != null && lightSpeed > 0f)
                targetText.text = $"LIGHT SPEED  {lightSpeed:0} KM/H";

            foreach (var line in objectiveLines)
            {
                bool done = line.challenge ? gameManager.IsChallengeDone(line.index) : gameManager.IsObjectiveDone(line.index);
                string label = line.step.Summary;
                string progress = line.step.Progress(kmh, gameManager.JumpCount);
                if (progress.Length > 0) label += "  " + progress;
                if (line.step is RunnerOptionalChallenge challenge) label += $"  \u00d7{challenge.multiplier}";
                line.text.text = label;
                line.text.color = done ? winColor : timeColor;
            }

            UpdateCountdown();
        }

        void UpdateCountdown()
        {
            if (gameManager == null) return;

            float remaining = gameManager.TimeRemaining;
            if (timeText != null)
            {
                timeText.text = $"{remaining:0.0} S";
                timeText.color = remaining <= lowTimeWarning ? failColor : timeColor;
            }
            if (timeFill != null)
                timeFill.fillAmount = gameManager.TimeLimit > 0f ? remaining / gameManager.TimeLimit : 0f;
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
