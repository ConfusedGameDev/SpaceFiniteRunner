using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.Contracts;
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
    /// Light Speed and pulsing on pad hits; the held boost's meter; the hull
    /// bar and ×N lives count;
    /// the Light Speed goal and one line per runner objective / challenge
    /// ("JUMP 1/3  ×2"); the countdown as a plain yellow MM:SS; and floating
    /// "+boost" / gold "+$N" text at the ship.
    /// <para><b>The layout is the designer's.</b> Every element is a scene
    /// object placed by hand — this component never moves, resizes, re-anchors
    /// or re-fonts any of them. The wedge, the boost bar and the hull bar fill the rects they
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
        // What the HUD reads, by contract (refactor Step 8.4): the run through
        // IRunState, the ship through IChaseTarget. The scene wires the objects;
        // left empty, they are discovered by interface — and with no run in the
        // scene the HUD shows nothing live.
        [Tooltip("The run to show (anything implementing IRunState — the GameManager). Empty = found in the scene.")]
        [FormerlySerializedAs("run"), SerializeField] MonoBehaviour runSource;
        [Tooltip("The ship to show (anything implementing IChaseTarget — the ShipMotor). Empty = found in the scene.")]
        [FormerlySerializedAs("motor"), SerializeField] MonoBehaviour shipSource;

        IRunState run;
        IChaseTarget ship;
        IRaceStandings standings; // the race, when the scene has one; only shown while it is active
        ITimeTrialState trial;    // the time trial, when the scene has one; only shown while it is active

        void ResolveSources()
        {
            run ??= runSource as IRunState ?? ContractLookup.Find<IRunState>();
            ship ??= shipSource as IChaseTarget ?? ContractLookup.Find<IChaseTarget>();
            standings ??= ContractLookup.Find<IRaceStandings>();
            trial ??= ContractLookup.Find<ITimeTrialState>();
        }

        // A collector is ours when it lives on the ship's object (the pickup names the HoverShip beside the motor).
        bool IsMine(object collector) =>
            collector is Component c && ship is Component mine && c != null && mine != null && c.gameObject == mine.gameObject;

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

        [Header("Time trial clock")]
        [Tooltip("A time trial's clock (TimeTrialPRD R5.1) under its low-time warning: this colour, pulsing.")]
        [SerializeField] Color lowTimeColor = new(1f, 0.25f, 0.2f);
        [Tooltip("The flash, and the floating text, when the trial adds time.")]
        [SerializeField] Color timeGainColor = new(0.3f, 1f, 0.45f);
        [Tooltip("The flash, and the floating text, when the trial takes time away.")]
        [SerializeField] Color timeLossColor = new(1f, 0.3f, 0.25f);
        [Tooltip("Scale the clock jumps to when time is added or taken, settling back to its authored scale.")]
        [SerializeField, Min(1f)] float clockPunch = 1.35f;
        [Tooltip("Pulses per second of the low-time warning.")]
        [SerializeField, Range(0.5f, 10f)] float lowTimePulseHz = 2.5f;

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

        [Header("Boost bar")]
        [Tooltip("The rect the held boost's meter fills — place and size it by hand (between the speed wedge and the life bar). Hidden while the ship has no boost meter or it is switched off.")]
        [SerializeField] RectTransform boostBarRect;
        [Tooltip("Cells of the boost bar. Many thin cells read as one continuous fill.")]
        [SerializeField, Range(1, 60)] int boostSegments = 24;
        [Tooltip("Gap between cells.")]
        [SerializeField, Range(0f, 30f)] float boostSegmentGap = 2f;
        [Tooltip("Alpha of the cells already spent.")]
        [SerializeField, Range(0f, 1f)] float boostEmptyAlpha = 0.2f;
        [Tooltip("The bar at rest, ready to boost.")]
        [SerializeField] Color boostReadyColor = new(0.3f, 0.8f, 1f);
        [Tooltip("The bar while the boost is held — it pulses toward white.")]
        [SerializeField] Color boostActiveColor = new(0.6f, 1f, 1f);
        [Tooltip("The bar while it waits out its cooldown or refills.")]
        [SerializeField] Color boostRecoverColor = new(0.25f, 0.45f, 0.6f);
        [Tooltip("The flash on a press with the bar empty.")]
        [SerializeField] Color boostEmptyFlashColor = new(1f, 0.25f, 0.2f);
        [Tooltip("Pulses per second while boosting.")]
        [SerializeField, Range(0.5f, 20f)] float boostActivePulseHz = 8f;
        [Tooltip("Scale the bar jumps to on a press with the bar empty.")]
        [SerializeField, Min(1f)] float boostEmptyPunch = 1.15f;

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
        int shownPlace = -1, shownRacers = -1; // what the POS readout reads, so it is rebuilt on a change
        float placePulse = 1f;  // the POS readout's punch, decaying to 1
        Vector3 targetScale = Vector3.one;
        int shownTenths = -1;          // a time trial's clock, in tenths: rebuilt on a change
        int shownCheckpoint = -1, shownCheckpointCount = -1; // what the CHECKPOINT line reads
        float clockFlash;              // 1 -> 0 after the trial moved the clock
        Color clockFlashColor;
        float clockPulse = 1f;         // the clock's punch, decaying to 1
        Vector3 timeScale = Vector3.one;

        // The hull bar and the ×N beside it; null while the hull is off.
        SpeedGauge lifeBar;
        float lastLife = 1f;   // last frame's hull fraction: a drop is a hit
        float lifeFlash;       // 1 → 0 white flash after a hit
        float lifePunch = 1f;  // the bar's scale, decaying to 1
        float healFlash;       // 1 → 0 green flash after a repair orb
        float livesPunch = 1f; // the count's scale, decaying to 1
        int shownLives = -1;   // what the count currently reads

        // The held boost's bar; null while the ship has no meter or it is off.
        IBoostMeter boostMeter;
        SpeedGauge boostBar;
        int shownEmptyPresses; // the meter's empty-press count last seen: a rise is a dry press
        float boostFlash;      // 1 → 0 red flash after a dry press
        float boostPunch = 1f; // the bar's scale, decaying to 1

        // The designer's scales, which the pulses multiply rather than replace.
        Vector3 speedScale = Vector3.one, lifeBarScale = Vector3.one, livesScale = Vector3.one, boostBarScale = Vector3.one;

        void Start()
        {
            if (speedText != null) speedScale = speedText.rectTransform.localScale;
            if (timeText != null) timeScale = timeText.rectTransform.localScale;
            if (qteResultText != null)
            {
                qteResultScale = qteResultText.rectTransform.localScale;
                qteResultText.gameObject.SetActive(false);
            }
            if (objectiveLineTemplate != null) objectiveLineTemplate.gameObject.SetActive(false);
            BuildGauge();
            BuildLifeBar();
            BuildBoostBar();
            if (run == null || targetText == null || run.Level == null) return;
            targetColor = targetText.color;
            targetScale = targetText.rectTransform.localScale;
            RunnerLevelDefinition level = run.Level;
            int slot = 0;
            for (int i = 0; i < level.Count; i++)
            {
                RunnerObjective step = level.objectives[i];
                // The goal line above already shows the Light Speed target.
                if (step.type == RunnerObjectiveType.ReachSpeed && Mathf.Approximately(step.targetSpeedKmh, run.LightSpeedKmh)) continue;
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
            bool hull = run != null && run.HullEnabled && run.HullFraction >= 0f;
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

            float life = run.HullFraction;
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

            int lives = run.LivesLeft;
            if (lives != shownLives)
            {
                if (shownLives >= 0) livesPunch = lifeHitPunch * 1.2f;
                shownLives = lives;
                if (livesText != null) livesText.text = $"×{lives}";
            }
            livesPunch = Mathf.MoveTowards(livesPunch, 1f, pulseDecay * Time.deltaTime);
            if (livesText != null) livesText.rectTransform.localScale = livesScale * livesPunch;
        }

        // Built only when the ship carries a switched-on boost meter (found in
        // Start, after every Awake — the ship's meter is on PF_Ship); otherwise
        // the bar is hidden where it stands.
        void BuildBoostBar()
        {
            boostMeter ??= ContractLookup.Find<IBoostMeter>();
            bool on = boostMeter != null && boostMeter.MeterEnabled;
            if (boostBarRect != null) boostBarRect.gameObject.SetActive(on);
            if (!on || boostBarRect == null) return;

            boostBarScale = boostBarRect.localScale;
            shownEmptyPresses = boostMeter.EmptyPresses;
            boostBar = SpeedGauge.BuildInto(boostBarRect, boostSegments, boostSegmentGap, 1f, boostEmptyAlpha);
        }

        void UpdateBoostBar()
        {
            if (boostBar == null) return;
            if (!boostMeter.MeterEnabled)
            {
                boostBar.SetFill(0f, _ => boostRecoverColor); // switched off mid-run (debug): show it spent
                return;
            }

            if (boostMeter.EmptyPresses != shownEmptyPresses)
            {
                shownEmptyPresses = boostMeter.EmptyPresses;
                boostFlash = 1f;
                boostPunch = boostEmptyPunch;
            }
            boostFlash = Mathf.MoveTowards(boostFlash, 0f, pulseDecay * Time.deltaTime);
            boostPunch = Mathf.MoveTowards(boostPunch, 1f, pulseDecay * Time.deltaTime);
            boostBar.transform.localScale = boostBarScale * boostPunch;

            Color color = boostMeter.IsBoosting
                ? Color.Lerp(boostActiveColor, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * boostActivePulseHz * 2f * Mathf.PI))
                : boostMeter.IsRecovering ? boostRecoverColor : boostReadyColor;
            color = Color.Lerp(color, boostEmptyFlashColor, boostFlash);
            color.a = 1f;
            boostBar.SetFill(boostMeter.Fraction, _ => color);
        }

        /// <summary>Editor preview: draws the wedge, the hull bar and the boost bar into their rects so they can be laid out before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        void RebuildPreview()
        {
            if (gaugeRect != null)
                SpeedGauge.BuildInto(gaugeRect, gaugeSegments, gaugeSegmentGap, gaugeMinHeightFraction, gaugeEmptyAlpha)
                          .SetFill(0.6f, fraction => SpeedColor(fraction, 1f));
            if (lifeBarRect != null)
                SpeedGauge.BuildInto(lifeBarRect, lifeSegments, lifeSegmentGap, 1f, lifeEmptyAlpha)
                          .SetFill(4f / 6f, _ => lifeFullColor);
            if (boostBarRect != null)
                SpeedGauge.BuildInto(boostBarRect, boostSegments, boostSegmentGap, 1f, boostEmptyAlpha)
                          .SetFill(0.75f, _ => boostReadyColor);
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
            ResolveSources();
            if (ship != null) ship.Boosted += OnPadImpulse;
            if (standings != null) standings.PlayerPositionChanged += OnPlaceChanged;
            if (trial != null) trial.ClockAdjusted += OnClockAdjusted;
            CollectibleManager.MoneyChanged += OnMoneyChanged;
            RepairOrb.Collected += OnRepairOrb;
            BoostQte.Graded += OnQteGraded; // static: paired below — domain reload is off
        }

        void OnDisable()
        {
            if (ship != null) ship.Boosted -= OnPadImpulse;
            if (standings != null) standings.PlayerPositionChanged -= OnPlaceChanged;
            if (trial != null) trial.ClockAdjusted -= OnClockAdjusted;
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
            if (!spawnBoostText || delta <= 0 || run == null || run.RunOver) return;
            FloatingTextSystem.Instance.DisplayText(
                $"+${delta}", moneyTextColor, 1f,
                run.BoostTextLeadMeters, boostTextSize);
        }

        // A repair orb: the bar flashes green and punches, and "+N" (hull
        // points) floats up ahead of the ship like the boost text — no "+0"
        // when it was taken at full hull. Keyed on the orb, not on the
        // fraction rising — a restart refills it too.
        void OnRepairOrb(RepairOrb orb, IShip collector, float healed)
        {
            if (ship == null || !IsMine(collector)) return;
            healFlash = 1f;
            lifePunch = lifeHitPunch;
            if (spawnBoostText && healed >= 0.5f && run != null && !run.RunOver)
                FloatingTextSystem.Instance.DisplayText(
                    $"+{healed:0}", repairColor, 1f,
                    run.BoostTextLeadMeters, boostTextSize);
        }

        // A time trial moved the clock (TimeTrialPRD R5.1, R5.3): it flashes and
        // punches in the gain / loss colour, and a "CHECKPOINT +4.2S" line
        // floats up ahead of the ship like the boost text.
        void OnClockAdjusted(float seconds, TimeAwardKind kind)
        {
            clockFlash = 1f;
            clockFlashColor = seconds >= 0f ? timeGainColor : timeLossColor;
            clockPulse = clockPunch;
            shownSeconds = shownTenths = -1; // repaint now, in the flash
            if (!spawnBoostText || run == null || run.RunOver) return;
            MenuTextId id = kind switch
            {
                TimeAwardKind.CleanBonus => MenuTextId.TtClean,
                TimeAwardKind.PatrolKill => MenuTextId.TtPatrolDown,
                TimeAwardKind.LifeLost => MenuTextId.TtLifeLost,
                TimeAwardKind.DirtyPenalty => MenuTextId.TtDirty,
                _ => MenuTextId.TtCheckpoint
            };
            string amount = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}{1:0.0}S",
                                          seconds >= 0f ? "+" : "-", Mathf.Abs(seconds));
            // A clean bonus or dirty penalty lands with its checkpoint: further ahead, so the two lines never overlap.
            float lead = run.BoostTextLeadMeters * (kind == TimeAwardKind.CleanBonus || kind == TimeAwardKind.DirtyPenalty ? 1.15f : 1f);
            FloatingTextSystem.Instance.DisplayText(
                string.Format(MenuTextLibrary.Load().Get(id), amount), clockFlashColor, 1.2f,
                lead, boostTextSize);
        }

        // A place gained or lost: the POS readout punches.
        void OnPlaceChanged(int was, int now) => placePulse = pulseScale;

        void OnPadImpulse(float magnitude)
        {
            currentPulse = pulseScale;

            // Juice: floating text for every booster hit, spawned ahead of the
            // ship (GameSettings.boostTextLeadMeters) so it isn't left behind
            // instantly at speed.
            if (spawnBoostText && magnitude > 0f && run != null)
                FloatingTextSystem.Instance.DisplayText(
                    $"+{magnitude:0}", boostTextColor, 1f,
                    run.BoostTextLeadMeters, boostTextSize);
        }

        void Update()
        {
            UpdateQteResult();
            if (ship == null) return;

            float kmh = ship.Speed * (run != null ? run.SpeedDisplayMultiplier : 3.6f);
            float lightSpeed = run != null ? run.LightSpeedKmh : 0f;

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
            UpdateBoostBar();

            if (targetText != null && standings != null && standings.RaceActive)
            {
                // A race has no Light Speed goal (RaceModePRD D5): the goal
                // line is the player's place instead.
                int place = standings.PlayerPosition, racers = standings.RacerCount;
                if (place != shownPlace || racers != shownRacers)
                {
                    shownPlace = place;
                    shownRacers = racers;
                    targetText.text = string.Format(MenuTextLibrary.Load().Get(MenuTextId.HudRacePosition), place, racers);
                }
                targetText.color = place == 1 ? winColor : targetColor;
                placePulse = Mathf.MoveTowards(placePulse, 1f, pulseDecay * Time.deltaTime);
                targetText.rectTransform.localScale = targetScale * placePulse;
            }
            else if (targetText != null && trial != null && trial.TrialActive)
            {
                // A time trial has no Light Speed goal (TimeTrialPRD D3): Arcade
                // counts its checkpoints, Beat the Clock says what it is.
                int passed = trial.CheckpointsPassed, count = trial.CheckpointCount;
                if (passed != shownCheckpoint || count != shownCheckpointCount)
                {
                    if (shownCheckpoint >= 0 && passed != shownCheckpoint) placePulse = pulseScale;
                    shownCheckpoint = passed;
                    shownCheckpointCount = count;
                    var library = MenuTextLibrary.Load();
                    targetText.text = trial.IsArcade && count > 0
                        ? string.Format(library.Get(MenuTextId.HudCheckpoint), passed, count)
                        : library.Get(MenuTextId.HudBeatTheClock);
                }
                targetText.color = trial.IsArcade && count > 0 && passed >= count ? winColor : targetColor;
                placePulse = Mathf.MoveTowards(placePulse, 1f, pulseDecay * Time.deltaTime);
                targetText.rectTransform.localScale = targetScale * placePulse;
            }
            else if (targetText != null && lightSpeed > 0f)
            {
                targetText.text = $"LIGHT SPEED  {lightSpeed:0} KM/H";
                // Reached once is reached: the goal line stays done while the
                // ship still has to make it to an end ramp.
                if (run != null && run.Level != null)
                    targetText.color = run.LightSpeedReached ? winColor : targetColor;
            }

            ObjectiveContext state = run != null ? run.ObjectiveState : default;
            foreach (var line in objectiveLines)
            {
                bool done = line.challenge ? run.IsChallengeDone(line.index) : run.IsObjectiveDone(line.index);
                string label = line.step.Summary;
                string progress = line.step.Progress(state);
                if (progress.Length > 0) label += "  " + progress;
                if (line.step is RunnerOptionalChallenge challenge) label += $"  \u00d7{challenge.multiplier}";
                line.text.text = label;
                line.text.color = done ? winColor : lineColor;
            }

            UpdateCountdown();
        }

        void UpdateCountdown()
        {
            if (run == null || timeText == null) return;
            if (trial != null && trial.TrialActive)
            {
                UpdateTrialClock();
                return;
            }

            // Whole seconds, rounded up so 00:00 is the moment the clock runs out.
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, run.TimeRemaining));
            if (seconds == shownSeconds) return;
            shownSeconds = seconds;
            timeText.text = $"{seconds / 60:00}:{seconds % 60:00}";
            timeText.color = timerColor;
        }

        // A time trial's clock (TimeTrialPRD R5.1): tenths under a minute, the
        // low-time pulse, the flash and punch when the trial moves it.
        void UpdateTrialClock()
        {
            float left = Mathf.Max(0f, run.TimeRemaining);
            // Tenths rounded up, so 0.0 is the moment the clock runs out.
            int tenths = Mathf.CeilToInt(left * 10f - 1e-3f);
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                shownSeconds = -1;
                timeText.text = tenths < 600
                    ? $"{tenths / 10}.{tenths % 10}"
                    : $"{tenths / 600}:{tenths / 10 % 60:00}";
            }

            bool low = left > 0f && left <= trial.LowTimeSeconds;
            float lowPulse = low ? 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * lowTimePulseHz * 2f * Mathf.PI) : 0f;
            Color color = low ? Color.Lerp(timerColor, lowTimeColor, 0.6f + 0.4f * lowPulse) : timerColor;
            clockFlash = Mathf.MoveTowards(clockFlash, 0f, pulseDecay * 0.5f * Time.unscaledDeltaTime);
            timeText.color = Color.Lerp(color, clockFlashColor, clockFlash);

            clockPulse = Mathf.MoveTowards(clockPulse, 1f, pulseDecay * Time.unscaledDeltaTime);
            float scale = clockPulse * (low ? 1f + 0.08f * lowPulse : 1f);
            timeText.rectTransform.localScale = timeScale * scale;
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
