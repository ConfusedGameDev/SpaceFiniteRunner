using System.Collections.Generic;
using Sirenix.OdinInspector;
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
    /// of Light Speed) beside the km/h number, heating up as you approach
    /// Light Speed and pulsing on pad hits; the hull bar and ×N lives count;
    /// the Light Speed goal and one line per runner objective / challenge
    /// ("JUMP 1/3  ×2"); the countdown as a plain yellow MM:SS; and floating
    /// "+boost" / gold "+$N" text at the ship.
    /// <para><b>The layout is the designer's.</b> Every element is a scene
    /// object placed by hand — this component never moves, resizes, re-anchors
    /// or re-fonts any of them. The wedge and the hull bar fill the rects they
    /// are given (<see cref="SpeedGauge.BuildInto"/>), the objective lines are
    /// clones of a hidden template stacked by the template parent's layout
    /// group, and the pulses scale from each element's authored scale.
    /// <b>Rebuild Preview</b> draws the segments in edit mode.</para>
    /// It offers no retry of its own: a loss ends on the
    /// <see cref="GameOverScreen"/> and a win on the
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
        [Tooltip("The rect the speed wedge fills — place and size it by hand.")]
        [SerializeField] RectTransform gaugeRect;
        [Tooltip("A hidden objective line inside a VerticalLayoutGroup: cloned once per extra objective / challenge into its parent. Style it here.")]
        [SerializeField] Text objectiveLineTemplate;

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

        [Header("Pad pulse")]
        [SerializeField, Min(1f)] float pulseScale = 1.3f;
        [SerializeField, Min(0.1f)] float pulseDecay = 6f;

        [Header("Speed gauge")]
        [Tooltip("Segments of the speed wedge; every one lit = Light Speed.")]
        [SerializeField, Range(4, 60)] int gaugeSegments = 20;
        [Tooltip("Gap between segments, px at 1920×1080. The segments share the rest of the rect's width.")]
        [SerializeField, Range(0f, 30f)] float gaugeSegmentGap = 6f;
        [Tooltip("Height of the leftmost segment as a share of the rect's height (the rightmost is all of it).")]
        [SerializeField, Range(0.05f, 1f)] float gaugeMinHeightFraction = 0.225f;
        [Tooltip("Alpha of the segments not yet reached.")]
        [SerializeField, Range(0f, 1f)] float gaugeEmptyAlpha = 0.2f;

        [Header("Life bar")]
        [Tooltip("The rect the hull bar fills — place and size it by hand. Hidden, with the lives count, while the hull is off.")]
        [SerializeField] RectTransform lifeBarRect;
        [Tooltip("The ×N lives count. Use a font that carries the × glyph.")]
        [SerializeField] Text livesText;
        [Tooltip("Cells of the hull bar. A cell stays lit while any of its share of the hull is left.")]
        [SerializeField, Range(1, 30)] int lifeSegments = 6;
        [Tooltip("Gap between cells.")]
        [SerializeField, Range(0f, 30f)] float lifeSegmentGap = 6f;
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

        [Header("Boost floating text")]
        [SerializeField] bool spawnBoostText = true;
        [SerializeField] Color boostTextColor = new(0.48f, 1f, 0.4f);
        [SerializeField, Min(0.05f)] float boostTextSize = 0.6f;
        [Tooltip("Colour of the floating \"+$N\" popup on a money pickup (same lead and size as the boost text).")]
        [SerializeField] Color moneyTextColor = new(1f, 0.85f, 0.3f);
        [Tooltip("Colour the hull bar flashes, and of the floating \"+N\" popup, when a repair orb gives hull back.")]
        [SerializeField] Color repairColor = new(0.3f, 1f, 0.45f);

        [Header("Boost QTE result")]
        [Tooltip("The boost QTE's verdict (TOO FAST! / TOO LATE! / SWEET! / PERFECT!), in the prompt's colour. Placed by hand like every HUD element; empty = no label.")]
        [SerializeField] Text qteResultText;
        [Tooltip("Seconds the verdict stays fully visible.")]
        [SerializeField, Min(0f)] float qteResultHoldSeconds = 1.5f;
        [Tooltip("Seconds it then takes to dissolve.")]
        [SerializeField, Min(0.01f)] float qteResultFadeSeconds = 0.75f;
        [Tooltip("Scale the label pops to when a verdict lands, settling back to its authored scale.")]
        [SerializeField, Min(1f)] float qteResultPunch = 1.3f;

        float currentPulse = 1f;
        float qteResultShownAt = float.NegativeInfinity;
        float qteResultPunchNow = 1f;
        Vector3 qteResultScale = Vector3.one;
        Color qteResultColor = Color.white;

        // The objective readout: (goal, is it a challenge, its index in the
        // level's list, the line drawn for it), built once in Start.
        readonly List<(RunnerObjective step, bool challenge, int index, Text text)> objectiveLines = new();

        SpeedGauge gauge;
        Color targetColor;      // the goal line's authored colour, before the done tint
        int shownSeconds = -1;  // what the timer text currently reads, so it is rebuilt once a second

        // The hull bar and the ×N beside it; null while the hull is off.
        SpeedGauge lifeBar;
        float lastLife = 1f;   // last frame's hull fraction: a drop is a hit
        float lifeFlash;       // 1 → 0 white flash after a hit
        float lifePunch = 1f;  // the bar's scale, decaying to 1
        float healFlash;       // 1 → 0 green flash after a repair orb
        float livesPunch = 1f; // the count's scale, decaying to 1
        int shownLives = -1;   // what the count currently reads

        // The designer's scales, which the pulses multiply rather than replace.
        Vector3 speedScale = Vector3.one, lifeBarScale = Vector3.one, livesScale = Vector3.one;

        void Start()
        {
            if (speedText != null) speedScale = speedText.rectTransform.localScale;
            if (qteResultText != null)
            {
                qteResultScale = qteResultText.rectTransform.localScale;
                qteResultText.gameObject.SetActive(false);
            }
            if (objectiveLineTemplate != null) objectiveLineTemplate.gameObject.SetActive(false);
            BuildGauge();
            BuildLifeBar();
            if (gameManager == null || targetText == null || gameManager.Level == null) return;
            targetColor = targetText.color;
            RunnerLevelDefinition level = gameManager.Level;
            int slot = 0;
            for (int i = 0; i < level.Count; i++)
            {
                RunnerObjective step = level.objectives[i];
                // The goal line above already shows the Light Speed target.
                if (step.type == RunnerObjectiveType.ReachSpeed && Mathf.Approximately(step.targetSpeedKmh, gameManager.LightSpeedKmh)) continue;
                Text line = MakeObjectiveLine(slot++);
                if (line != null) objectiveLines.Add((step, false, i, line));
            }
            for (int i = 0; i < level.ChallengeCount; i++)
            {
                Text line = MakeObjectiveLine(slot++);
                if (line != null) objectiveLines.Add((level.optionalChallenges[i], true, i, line));
            }
        }

        void BuildGauge()
        {
            if (gaugeRect == null) return;
            gauge = SpeedGauge.BuildInto(gaugeRect, gaugeSegments, gaugeSegmentGap, gaugeMinHeightFraction, gaugeEmptyAlpha);
        }

        // Built only while the hull is on (GameSettings.hullEnabled); otherwise
        // the bar and the count are hidden where they stand.
        void BuildLifeBar()
        {
            bool hull = gameManager != null && gameManager.HullEnabled && gameManager.ShipHealth != null;
            if (lifeBarRect != null) lifeBarRect.gameObject.SetActive(hull);
            if (livesText != null) livesText.gameObject.SetActive(hull);
            if (!hull || lifeBarRect == null) return;

            lifeBarScale = lifeBarRect.localScale;
            if (livesText != null) livesScale = livesText.rectTransform.localScale;
            lifeBar = SpeedGauge.BuildInto(lifeBarRect, lifeSegments, lifeSegmentGap, 1f, lifeEmptyAlpha);
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
            lifeBar.transform.localScale = lifeBarScale * lifePunch;

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
                if (livesText != null) livesText.text = $"×{lives}";
            }
            livesPunch = Mathf.MoveTowards(livesPunch, 1f, pulseDecay * Time.deltaTime);
            if (livesText != null) livesText.rectTransform.localScale = livesScale * livesPunch;
        }

        /// <summary>Editor preview: draws the wedge and the hull bar into their rects so they can be laid out before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        void RebuildPreview()
        {
            if (gaugeRect != null)
                SpeedGauge.BuildInto(gaugeRect, gaugeSegments, gaugeSegmentGap, gaugeMinHeightFraction, gaugeEmptyAlpha)
                          .SetFill(0.6f, fraction => SpeedColor(fraction, 1f));
            if (lifeBarRect != null)
                SpeedGauge.BuildInto(lifeBarRect, lifeSegments, lifeSegmentGap, 1f, lifeEmptyAlpha)
                          .SetFill(4f / 6f, _ => lifeFullColor);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        // A clone of the hidden template, left for its parent's layout group to stack.
        Text MakeObjectiveLine(int slot)
        {
            if (objectiveLineTemplate == null) return null;
            Text text = Instantiate(objectiveLineTemplate, objectiveLineTemplate.transform.parent, false);
            text.name = $"Objective{slot}";
            text.gameObject.SetActive(true);
            return text;
        }

        void OnEnable()
        {
            if (motor != null) motor.PadImpulse += OnPadImpulse;
            CollectibleManager.MoneyChanged += OnMoneyChanged;
            RepairOrb.Collected += OnRepairOrb;
            BoostQte.Graded += OnQteGraded; // static: paired below — domain reload is off
        }

        void OnDisable()
        {
            if (motor != null) motor.PadImpulse -= OnPadImpulse;
            CollectibleManager.MoneyChanged -= OnMoneyChanged;
            RepairOrb.Collected -= OnRepairOrb;
            BoostQte.Graded -= OnQteGraded;
        }

        // The boost QTE's verdict: the word in the prompt's colour, popped in,
        // held, then dissolved (UpdateQteResult). A new verdict restarts it.
        void OnQteGraded(SpeedPad orb, BoostQteVerdict verdict)
        {
            // No label for an orb taken without a press — only a press gets a verdict word.
            if (qteResultText == null || !verdict.Pressed) return;
            MenuTextId id = verdict.Result switch
            {
                BoostQteResult.TooFast => MenuTextId.QteTooFast,
                BoostQteResult.TooLate => MenuTextId.QteTooLate,
                BoostQteResult.Perfect => MenuTextId.QtePerfect,
                _ => MenuTextId.QteSweet
            };
            qteResultText.text = MenuTextLibrary.Load().Get(id);
            qteResultColor = verdict.Color;
            qteResultShownAt = Time.time;
            qteResultPunchNow = qteResultPunch;
            qteResultText.gameObject.SetActive(true);
        }

        void UpdateQteResult()
        {
            if (qteResultText == null || !qteResultText.gameObject.activeSelf) return;
            float since = Time.time - qteResultShownAt;
            float alpha = 1f - Mathf.Clamp01((since - qteResultHoldSeconds) / qteResultFadeSeconds);
            if (alpha <= 0f)
            {
                qteResultText.gameObject.SetActive(false);
                return;
            }
            Color c = qteResultColor;
            c.a *= alpha;
            qteResultText.color = c;
            qteResultPunchNow = Mathf.MoveTowards(qteResultPunchNow, 1f, pulseDecay * Time.deltaTime);
            qteResultText.rectTransform.localScale = qteResultScale * qteResultPunchNow;
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
        // points) floats up ahead of the ship like the boost text — no "+0"
        // when it was taken at full hull. Keyed on the orb, not on the
        // fraction rising — a restart refills it too.
        void OnRepairOrb(RepairOrb orb, IShip collector, float healed)
        {
            if (motor == null || !motor.Is(collector)) return;
            healFlash = 1f;
            lifePunch = lifeHitPunch;
            if (spawnBoostText && healed >= 0.5f && gameManager != null && !gameManager.RunOver)
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
            UpdateQteResult();
            if (motor == null) return;

            float kmh = motor.CurrentSpeed * 3.6f;
            float lightSpeed = gameManager != null ? gameManager.LightSpeedKmh : 0f;

            if (speedText != null)
            {
                speedText.text = $"{kmh:0}";
                speedText.color = SpeedColor(kmh, lightSpeed);

                currentPulse = Mathf.MoveTowards(currentPulse, 1f, pulseDecay * Time.deltaTime);
                speedText.rectTransform.localScale = speedScale * currentPulse;
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
