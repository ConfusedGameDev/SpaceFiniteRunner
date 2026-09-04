using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// What the Mission Complete panel prints — assembly-neutral rows, so the
    /// city (which cannot be referenced from here) and the runner each fill
    /// it from their own level types. The city's rows arrive through the
    /// profile's last-level record; the runner's come off its live state.
    /// </summary>
    public sealed class MissionCompleteData
    {
        /// <summary>The campaign mission's authored id ("" outside a campaign session) — what the slam latches complete.</summary>
        public string missionId = "";
        public string title = "";
        public VideoClip video;
        /// <summary>The city level's flat bonus (0 when no city level preceded the run).</summary>
        public long baseReward;
        public readonly List<ObjectiveResult> mainObjectives = new();
        public readonly List<ObjectiveResult> runObjectives = new();
        public readonly List<ChallengeResult> challenges = new();
        public RankTable rank = new();

        /// <summary>(bonus + every done objective's reward) × every done challenge's multiplier.</summary>
        public long FinalTotal
        {
            get
            {
                var all = new List<ObjectiveResult>(mainObjectives.Count + runObjectives.Count);
                all.AddRange(mainObjectives);
                all.AddRange(runObjectives);
                return MissionPayout.Total(baseReward, all, challenges);
            }
        }
    }

    /// <summary>
    /// The mission's results screen, raised by the runner once the pilot's
    /// win line has cleared: MISSION COMPLETE in the mission brief's clothes
    /// — a left column of objective rows, the video holder on the right —
    /// revealed as a sequence rather than a page: each section header pops
    /// in, each row's name is TYPED (block cursor, decode scramble on the
    /// newest character, rising blip, a punch per letter) and its money or
    /// multiplier COUNTS UP, a TOTAL line keeps recomputing from whatever the
    /// rows currently show, then the RANK slams onto the middle of the
    /// screen and settles beside the video, and finally NEXT MISSION / RETRY
    /// / EXIT TO MENU slide in. A long press of A / Enter (the cinema's ring)
    /// jumps the reveal to the slam.
    ///
    /// It lives here, beside <see cref="GameOverScreen"/>, because the city
    /// assembly references this one and not the other way round: the panel
    /// takes <see cref="MissionCompleteData"/> rows, never a level asset.
    /// The mission is PAID here, in full — <see cref="PlayerStats.RecordMissionCompleted"/>
    /// on the slam, skipped or not — so every number the player watched
    /// climb is what the save gets, and the campaign mission latches
    /// complete at the same moment. Freezes scaled time while up (the video
    /// runs on its own clock), animates on unscaled time like every menu,
    /// and demands an answer: Back does nothing.
    /// </summary>
    public class MissionCompleteScreen : MonoBehaviour
    {
        const int SortingOrder = 25;      // the game-over tier: the two never coexist
        const float ColumnX = -430f;      // left column: the result rows
        const float PanelX = 450f;        // right side: video holder, rank, buttons
        const float RowsTop = 330f;
        const float RowHeight = 44f;
        const float RowSpacing = 6f;
        const float ButtonsTop = -300f;
        const int TitleFontSize = 52;
        const int TotalFontSize = 34;
        const int TotalValueFontSize = 40;
        const float CharsPerSecond = 70f;
        const float CountSeconds = 0.5f;
        const float RowFadeSeconds = 0.12f;
        const float BeatSeconds = 0.1f;
        const float SkipHoldSeconds = 1f;      // the cinema's knob lives in the city assembly — a constant here
        const float HoldDrainSeconds = 0.15f;  // a released hold empties the ring this fast
        const float RingSize = 96f;
        const float RingMargin = 44f;
        const float TickInterval = 0.06f;      // count-up blips
        const int RankFontSize = 150;
        const float SlamSeconds = 0.3f;
        const float SlamHoldSeconds = 0.8f;
        const float SlamTravelSeconds = 0.4f;
        static readonly Vector2 SlamCentre = new(0f, 40f);
        static readonly Vector2 RankHome = new(PanelX, -180f);
        const float RankHomeScale = 0.72f;

        /// <summary>True while the panel is waiting for an answer — the HUD's own retry shortcut stands down.</summary>
        public static bool IsOpen { get; private set; }

        MenuTheme theme;
        MenuNavigator nav;
        MissionCompleteData data;
        System.Action onNext, onRetry, onExit;

        RectTransform content;
        MenuScreen results;
        MenuScreen buttons;
        AudioSource ui;
        AudioClip blip;
        float openedTime;
        bool decided;

        // The reveal.
        readonly RevealSequencer sequence = new();
        int slamMarker;                 // sequence count when the slam was added — the skip's target
        readonly List<Line> lines = new();
        ResultRow totalRow;
        bool totalRevealed;
        float totalAlpha;
        float tickTimer;
        bool banked;

        // Title block (built outside the MenuScreen so the rows can be
        // revealed by hand — a staggered entrance would rewrite their alpha).
        CanvasGroup titleGroup;

        // The rank.
        Text rankCaption;
        Text rankLetter;
        CanvasGroup rankGroup;
        RectTransform rankRect;
        float slamTimer = -1f;

        // Skip ring.
        RectTransform skipRoot;
        Image ring;
        Image glyph;
        Text keyLabel;
        float hold;
        bool holdArmed;
        bool revealDone;
        bool buttonsUp;
        bool buttonsArmed;

        // Video.
        VideoPlayer video;
        RenderTexture videoTexture;

        /// <summary>One printed row and the numbers it currently shows — the TOTAL line is recomputed from these.</summary>
        sealed class Line
        {
            public ResultRow row;
            public bool challenge;
            public bool done;
            public double shownReward;
            public double shownMultiplier;
        }

        /// <summary>Puts the panel up and freezes the game under it. The chosen callback runs with time already unfrozen.</summary>
        public static MissionCompleteScreen Show(MissionCompleteData data, System.Action onNext, System.Action onRetry, System.Action onExit)
        {
            var screen = new GameObject("MissionCompleteScreen").AddComponent<MissionCompleteScreen>();
            screen.data = data ?? new MissionCompleteData();
            screen.onNext = onNext;
            screen.onRetry = onRetry;
            screen.onExit = onExit;
            screen.theme = MenuTheme.Load();
            screen.nav = new MenuNavigator(screen.theme);
            screen.Build();
            return screen;
        }

        void OnEnable() => InputPromptBinder.DeviceChanged += RefreshGlyph;

        void OnDisable() => InputPromptBinder.DeviceChanged -= RefreshGlyph;

        // ------------------------------------------------------------- build

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
            blip = RpgMessageSystem.PlaceholderBlip();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            content = (RectTransform)contentGo.transform;
            content.SetParent(transform, false);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;

            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            var backdropRect = (RectTransform)backdropGo.transform;
            backdropRect.SetParent(content, false);
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;
            var backdrop = backdropGo.AddComponent<Image>();
            var backdropColor = theme.Backdrop;
            backdropColor.a = 0.88f;
            backdrop.color = backdropColor;

            BuildTitle();
            BuildRows();
            BuildVideoPanel();
            BuildRank();
            BuildButtons();
            BuildSkipPrompt();

            PromptStrip.Create(content, theme, 56f)
                       .SetHints((PromptAction.Navigate, MenuTextId.HintMove),
                                 (PromptAction.Confirm, MenuTextId.HintSelect));

            BuildSequence();

            MenuScreenFactory.EnsureEventSystem(); // mouse clicks on the buttons need one
            IsOpen = true;
            Time.timeScale = 0f;
            openedTime = Time.unscaledTime;
            Gamepad.current?.ResetHaptics();
        }

        void BuildTitle()
        {
            var go = new GameObject("Title", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(content, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1920f, 1080f);
            titleGroup = go.AddComponent<CanvasGroup>();
            titleGroup.alpha = 0f;

            var title = MenuScreen.MakeText("MissionComplete", rect, new Vector2(ColumnX, 462f), new Vector2(900f, 70f),
                                            MenuTextLibrary.Load().Get(MenuTextId.MissionComplete), TitleFontSize,
                                            theme.TextPrimary, theme.TitleFont, TextAnchor.MiddleLeft);
            LocalizedLabel.Bind(title, MenuTextId.MissionComplete);
            MenuScreen.MakeText("LevelName", rect, new Vector2(ColumnX, 404f), new Vector2(900f, 44f),
                                (data.title ?? string.Empty).ToUpperInvariant(), 30, theme.Accent, theme.BodyFont, TextAnchor.MiddleLeft);
        }

        // Every row is added up front with its FINAL label (the screen sizes
        // its plates off the widest label), then blanked and hidden; the
        // sequence types the labels back and fades the rows in.
        void BuildRows()
        {
            results = MenuScreen.Create("Results", content, theme, ColumnX, RowsTop);
            results.SetRowMetrics(RowHeight, RowSpacing);

            var library = MenuTextLibrary.Load();
            var typed = new List<(ResultRow row, string text)>();

            // MAIN OBJECTIVES — the city level's rows, with its flat bonus first.
            if (data.baseReward > 0 || data.mainObjectives.Count > 0)
            {
                results.AddRow<StatHeaderRow>(MenuTextId.MainObjectives);
                if (data.baseReward > 0)
                {
                    var bonus = results.AddRow<ResultRow>(MenuTextId.MissionBonus);
                    typed.Add((bonus, library.Get(MenuTextId.MissionBonus)));
                    lines.Add(new Line { row = bonus, done = true });
                }
                foreach (var objective in data.mainObjectives)
                    AddObjectiveRow(objective, typed);
            }

            // ESCAPE RUN — the runner's own objectives.
            if (data.runObjectives.Count > 0)
            {
                results.AddRow<StatHeaderRow>(MenuTextId.FiniteRunObjectives);
                foreach (var objective in data.runObjectives)
                    AddObjectiveRow(objective, typed);
            }

            // OPTIONAL CHALLENGES — city accepted ones, then the run's.
            if (data.challenges.Count > 0)
            {
                results.AddRow<StatHeaderRow>(MenuTextId.OptionalChallenges);
                foreach (var challenge in data.challenges)
                {
                    var row = results.AddRow<ResultRow>(challenge.label);
                    typed.Add((row, challenge.label));
                    lines.Add(new Line { row = row, challenge = true, done = challenge.done, shownMultiplier = 1.0 });
                }
            }

            // TOTAL — live from the first count-up.
            totalRow = results.AddRow<ResultRow>(MenuTextId.Total);
            totalRow.SetLabelFontSize(TotalFontSize);
            totalRow.SetValueFontSize(TotalValueFontSize);
            totalRow.SetTint(theme.Accent, theme.Accent);
            totalRow.SetValueText(StatFormat.Money(0));

            results.Show(staggered: false);
            foreach (var row in results.Rows) row.EntranceAlpha = 0f;
            foreach (var (row, _) in typed) row.SetLabelText(string.Empty);

            // The sequence types these back, in this order.
            orderedTyped = typed;
        }

        List<(ResultRow row, string text)> orderedTyped;

        void AddObjectiveRow(ObjectiveResult objective, List<(ResultRow row, string text)> typed)
        {
            var row = results.AddRow<ResultRow>(objective.label);
            typed.Add((row, objective.label));
            lines.Add(new Line { row = row, done = objective.done });
        }

        /// <summary>
        /// The video holder: a plate framing a RawImage. With a clip, a
        /// VideoPlayer renders it into a runtime RenderTexture on its own
        /// clock (looping, no audio — nothing here knows the mixer); without
        /// one the holder stays, showing a dead screen so the layout never
        /// changes. The brief's holder, verbatim.
        /// </summary>
        void BuildVideoPanel()
        {
            var plate = MenuScreen.MakeImage("VideoPlate", content, new Vector2(PanelX, 170f),
                                             new Vector2(830f, 420f), theme.RowPlate, theme.PlateIdle);

            var rawGo = new GameObject("Video", typeof(RectTransform));
            var rawRect = (RectTransform)rawGo.transform;
            rawRect.SetParent(plate.rectTransform, false);
            rawRect.anchorMin = rawRect.anchorMax = new Vector2(0.5f, 0.5f);
            rawRect.sizeDelta = new Vector2(806f, 396f);
            var raw = rawGo.AddComponent<RawImage>();
            raw.raycastTarget = false;

            if (data.video != null)
            {
                videoTexture = new RenderTexture(1024, 576, 0);
                video = gameObject.AddComponent<VideoPlayer>();
                video.playOnAwake = false;
                video.source = VideoSource.VideoClip;
                video.clip = data.video;
                video.isLooping = true;
                video.renderMode = VideoRenderMode.RenderTexture;
                video.targetTexture = videoTexture;
                video.audioOutputMode = VideoAudioOutputMode.None;
                video.timeUpdateMode = VideoTimeUpdateMode.DSPTime; // immune to the frozen clock
                raw.texture = videoTexture;
                raw.color = Color.white;
                video.Play();
            }
            else
            {
                raw.color = new Color(0.01f, 0.02f, 0.03f, 0.92f);
                MenuScreen.MakeText("NoSignal", rawRect, Vector2.zero, new Vector2(700f, 60f),
                                    "— NO SIGNAL —", 30, theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);
            }
        }

        // The rank: caption + big letter in one group, hidden until the slam,
        // which drops it onto the screen centre before it settles under the video.
        void BuildRank()
        {
            var go = new GameObject("Rank", typeof(RectTransform));
            rankRect = (RectTransform)go.transform;
            rankRect.SetParent(content, false);
            rankRect.anchorMin = rankRect.anchorMax = new Vector2(0.5f, 0.5f);
            rankRect.pivot = new Vector2(0.5f, 0.5f);
            rankRect.sizeDelta = new Vector2(800f, 260f);
            rankRect.anchoredPosition = SlamCentre;
            rankGroup = go.AddComponent<CanvasGroup>();
            rankGroup.alpha = 0f;
            rankGroup.blocksRaycasts = false;

            rankCaption = MenuScreen.MakeText("Caption", rankRect, new Vector2(0f, 95f), new Vector2(800f, 44f),
                                              MenuTextLibrary.Load().Get(MenuTextId.Rank), 34, theme.TextDim,
                                              theme.TitleFont, TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(rankCaption, MenuTextId.Rank);
            rankLetter = MenuScreen.MakeText("Letter", rankRect, new Vector2(0f, -20f), new Vector2(800f, 200f),
                                             "", RankFontSize, theme.Accent, theme.TitleFont, TextAnchor.MiddleCenter);
        }

        void BuildButtons()
        {
            buttons = MenuScreen.Create("Buttons", content, theme, PanelX, ButtonsTop);
            buttons.SetRowMetrics(54f, 8f);
            buttons.AddRow<MenuRow>(MenuTextId.NextMission).Activated += () => Decide(onNext);
            buttons.AddRow<MenuRow>(MenuTextId.Retry).Activated += () => Decide(onRetry);
            buttons.AddRow<MenuRow>(MenuTextId.ExitToMenu).Activated += () => Decide(onExit);
            buttons.HideImmediate();
        }

        /// <summary>
        /// The skip widget, bottom-right: a dim ring under the filling ring
        /// (Radial360 from the top, set AFTER MakeImage, which forces Simple),
        /// the confirm glyph or key name inside it — whichever matches the
        /// device — and the localized HOLD TO SKIP caption to its left. The
        /// cinema's widget, verbatim; it hides once the reveal is over.
        /// </summary>
        void BuildSkipPrompt()
        {
            var go = new GameObject("SkipPrompt", typeof(RectTransform));
            skipRoot = (RectTransform)go.transform;
            skipRoot.SetParent(content, false);
            skipRoot.anchorMin = skipRoot.anchorMax = new Vector2(1f, 0f);
            skipRoot.pivot = new Vector2(1f, 0f);
            skipRoot.anchoredPosition = new Vector2(-RingMargin, RingMargin + 70f);
            skipRoot.sizeDelta = new Vector2(RingSize, RingSize);
            go.AddComponent<CanvasGroup>().blocksRaycasts = false;

            Sprite ringSprite = UiSprites.Ring(128, 14);
            var ringSize = new Vector2(RingSize, RingSize);
            var track = MenuScreen.MakeImage("Track", skipRoot, Vector2.zero, ringSize, ringSprite, theme.TextDim);
            var trackColor = track.color;
            trackColor.a = 0.3f;
            track.color = trackColor;

            ring = MenuScreen.MakeImage("Fill", skipRoot, Vector2.zero, ringSize, ringSprite, theme.Accent);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;

            glyph = MenuScreen.MakeImage("Glyph", skipRoot, Vector2.zero, new Vector2(44f, 44f),
                                         InputPromptBinder.Glyph(theme, PromptAction.Confirm), theme.TextPrimary);
            keyLabel = MenuScreen.MakeText("Key", skipRoot, Vector2.zero, new Vector2(RingSize, 30f),
                                           InputPromptBinder.KeyLabel(PromptAction.Confirm), 18,
                                           theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            var caption = MenuScreen.MakeText("Caption", skipRoot, new Vector2(-RingSize * 0.5f - 18f, 0f),
                                              new Vector2(600f, 40f), MenuTextLibrary.Load().Get(MenuTextId.HoldToSkip),
                                              26, theme.TextDim, theme.BodyFont, TextAnchor.MiddleRight);
            caption.rectTransform.pivot = new Vector2(1f, 0.5f);
            LocalizedLabel.Bind(caption, MenuTextId.HoldToSkip);

            RefreshGlyph(InputPromptBinder.Device);
        }

        void RefreshGlyph(PromptDevice device)
        {
            if (glyph == null || keyLabel == null) return;
            bool usePad = device == PromptDevice.Gamepad && InputPromptBinder.Glyph(theme, PromptAction.Confirm) != null;
            glyph.gameObject.SetActive(usePad);
            keyLabel.gameObject.SetActive(!usePad);
        }

        // ---------------------------------------------------------- sequence

        /// <summary>
        /// The reveal, beat by beat: title, then per section its header and
        /// each row (fade, type, count), the TOTAL joining at the first
        /// count-up, then the slam (which banks) and the buttons.
        /// </summary>
        void BuildSequence()
        {
            var library = MenuTextLibrary.Load();
            string failed = library.Get(MenuTextId.ChallengeFailed);
            Color failColor = new(1f, 0.3f, 0.25f);

            sequence.Add(new FadeGroupStep(titleGroup, 0.35f));
            sequence.Add(new DelayStep(0.2f));

            int lineIndex = 0;
            int typedIndex = 0;
            var rows = results.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                MenuRow row = rows[i];
                if (row is StatHeaderRow headerRow)
                {
                    StatHeaderRow captured = headerRow;
                    sequence.Add(new ActionStep(() => { captured.EntranceAlpha = 1f; Blip(theme.MoveClip); }));
                    sequence.Add(new DelayStep(0.18f));
                    continue;
                }
                if (row == totalRow) continue;

                Line line = lines[lineIndex++];
                string text = orderedTyped[typedIndex++].text;
                ResultRow result = line.row;

                sequence.Add(new RevealRowStep(result, RowFadeSeconds, () => result.PunchLabel(1.08f)));
                sequence.Add(new TypewriterStep(result, text, CharsPerSecond, (index, length) => OnTyped(result, index, length)));

                if (line.challenge)
                {
                    ChallengeResult challenge = data.challenges[ChallengeIndex(line)];
                    if (challenge.done)
                    {
                        result.SetTint(theme.TextPrimary, theme.Accent);
                        sequence.Add(new ActionStep(RevealTotal));
                        sequence.Add(new CountUpStep(result, challenge.multiplier, CountSeconds,
                                                     v => "×" + v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                                                     v => { line.shownMultiplier = System.Math.Max(1.0, v); OnCounted(result); }));
                        sequence.Add(new ActionStep(() => result.SetValueText("×" + challenge.multiplier)));
                    }
                    else
                    {
                        sequence.Add(new ActionStep(() =>
                        {
                            result.SetDim(true);
                            result.SetTint(theme.TextDim, failColor);
                            result.SetValueText(failed);
                            result.PunchValue(1.15f);
                        }));
                    }
                }
                else
                {
                    long reward = RewardOf(line);
                    if (line.done && reward > 0)
                    {
                        sequence.Add(new ActionStep(RevealTotal));
                        sequence.Add(new CountUpStep(result, reward, CountSeconds,
                                                     v => StatFormat.Money((long)System.Math.Round(v)),
                                                     v => { line.shownReward = v; OnCounted(result); }));
                    }
                    else
                    {
                        sequence.Add(new ActionStep(() =>
                        {
                            if (!line.done) { result.SetDim(true); result.SetTint(theme.TextDim, failColor); result.SetValueText(failed); }
                            else result.SetValueText(StatFormat.Money(0));
                        }));
                    }
                }
                sequence.Add(new DelayStep(BeatSeconds));
            }

            sequence.Add(new ActionStep(RevealTotal));
            sequence.Add(new DelayStep(0.35f));
            sequence.Add(new ActionStep(Slam));
            slamMarker = sequence.Count;
            sequence.Add(new DelayStep(SlamSeconds + SlamHoldSeconds + SlamTravelSeconds));
            sequence.Add(new ActionStep(ShowButtons));
        }

        // The city's flat bonus is a line without an ObjectiveResult behind it.
        long RewardOf(Line line)
        {
            int index = lines.IndexOf(line);
            int offset = data.baseReward > 0 ? 1 : 0;
            if (data.baseReward > 0 && index == 0) return data.baseReward;
            int objectiveIndex = index - offset;
            if (objectiveIndex < data.mainObjectives.Count) return data.mainObjectives[objectiveIndex].reward;
            objectiveIndex -= data.mainObjectives.Count;
            return objectiveIndex < data.runObjectives.Count ? data.runObjectives[objectiveIndex].reward : 0;
        }

        int ChallengeIndex(Line line)
        {
            int count = 0;
            foreach (var other in lines)
            {
                if (other == line) return count;
                if (other.challenge) count++;
            }
            return 0;
        }

        void OnTyped(ResultRow row, int index, int length)
        {
            row.PunchLabel(1.12f);
            if (index % 2 == 0 && blip != null && ui != null)
            {
                ui.pitch = Mathf.Lerp(0.9f, 1.4f, length > 1 ? index / (float)(length - 1) : 1f);
                ui.PlayOneShot(blip, theme.UiVolume * 0.6f);
            }
        }

        void OnCounted(ResultRow row)
        {
            tickTimer -= Time.unscaledDeltaTime;
            if (tickTimer > 0f) return;
            tickTimer = TickInterval;
            row.PunchValue(1.06f);
            totalRow.PunchValue(1.04f);
            ui.pitch = 1f;
            Blip(theme.AdjustClip, 0.5f);
        }

        // The TOTAL line joins at the first count-up and follows the rows from then on.
        void RevealTotal() => totalRevealed = true;

        /// <summary>What the rows currently show, added up the mission's way — the TOTAL line follows it every frame.</summary>
        long DisplayedTotal()
        {
            double sum = 0;
            double product = 1.0;
            foreach (var line in lines)
            {
                if (line.challenge) { if (line.done) product *= System.Math.Max(1.0, line.shownMultiplier); }
                else sum += line.shownReward;
            }
            return (long)System.Math.Round(sum * product);
        }

        // The slam: the letter hits the middle of the screen at 3× and lands,
        // the pad kicks, the mission is banked — once, skip or no skip.
        void Slam()
        {
            long total = data.FinalTotal;
            Rank rank = (data.rank ?? new RankTable()).RankFor(total);
            rankLetter.text = RankTable.Letter(rank);
            rankLetter.color = RankColor(rank);
            slamTimer = 0f;
            HapticsSystem.Instance.Pulse(0.9f, 0.6f, 0.4f);
            ui.pitch = 1f;
            Blip(theme.ConfirmClip);

            if (!banked)
            {
                banked = true;
                PlayerStats.RecordMissionCompleted(data.missionId, total, RankTable.Letter(rank));
            }
        }

        Color RankColor(Rank rank) => rank switch
        {
            Rank.S => new Color(1f, 0.84f, 0.25f),
            Rank.A => new Color(0.48f, 0.83f, 0.32f),
            Rank.B => new Color(0.31f, 0.76f, 1f),
            Rank.C => theme.TextPrimary,
            _ => new Color(1f, 0.3f, 0.25f)
        };

        void ShowButtons()
        {
            revealDone = true;
            if (skipRoot != null) skipRoot.gameObject.SetActive(false);
            buttons.SlideIn(theme.ScreenSlide);
            buttons.SetFocus(0); // NEXT MISSION is the expected answer
            buttonsUp = true;
            buttonsArmed = false;
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            if (decided) return;
            float dt = Time.unscaledDeltaTime;

            sequence.Tick(dt);
            TickTotal(dt);
            TickSlam(dt);

            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            if (!revealDone) TickSkip(dt);
            else if (buttonsUp) TickButtons(dt);
        }

        void TickTotal(float dt)
        {
            if (totalRow == null) return;
            if (totalRevealed) totalAlpha = Mathf.MoveTowards(totalAlpha, 1f, dt / RowFadeSeconds);
            totalRow.EntranceAlpha = totalAlpha;
            if (totalRevealed) totalRow.SetValueText(StatFormat.Money(DisplayedTotal()));
        }

        void TickSlam(float dt)
        {
            if (slamTimer < 0f) return;
            slamTimer += dt;

            if (slamTimer <= SlamSeconds)
            {
                float e = theme.Ease(Mathf.Clamp01(slamTimer / SlamSeconds));
                rankGroup.alpha = e;
                rankRect.anchoredPosition = SlamCentre;
                rankRect.localScale = Vector3.one * Mathf.Lerp(3f, 1f, e);
            }
            else if (slamTimer <= SlamSeconds + SlamHoldSeconds)
            {
                rankGroup.alpha = 1f;
                rankRect.localScale = Vector3.one;
            }
            else
            {
                float e = theme.Ease(Mathf.Clamp01((slamTimer - SlamSeconds - SlamHoldSeconds) / SlamTravelSeconds));
                rankRect.anchoredPosition = Vector2.Lerp(SlamCentre, RankHome, e);
                rankRect.localScale = Vector3.one * Mathf.Lerp(1f, RankHomeScale, e);
            }
        }

        // The hold-to-skip: armed only on a release seen after the grace (the
        // press that cleared the win line must not start charging the ring),
        // charges while held, drains fast on release, fires at full.
        void TickSkip(float dt)
        {
            bool held = MenuNavigator.ConfirmHeld();
            if (!holdArmed)
            {
                if (!held) holdArmed = true;
            }
            else if (held) hold += dt;
            else hold = Mathf.MoveTowards(hold, 0f, dt * SkipHoldSeconds / HoldDrainSeconds);

            if (ring != null) ring.fillAmount = Mathf.Clamp01(hold / SkipHoldSeconds);
            if (holdArmed && hold >= SkipHoldSeconds)
            {
                hold = 0f;
                ui.pitch = 1f;
                Blip(theme.ConfirmClip);
                sequence.SkipTo(slamMarker);
                // The rest (the slam's beat, the buttons) plays out on its own;
                // the ring goes away with the buttons.
                revealDone = true;
                if (skipRoot != null) skipRoot.gameObject.SetActive(false);
            }
        }

        void TickButtons(float dt)
        {
            // The press that finished the hold (or an early mash) must not
            // land on NEXT MISSION: confirm only after one seen release.
            if (!buttonsArmed)
            {
                if (!MenuNavigator.ConfirmHeld()) buttonsArmed = true;
                return;
            }
            if (!buttons.Interactive) return;

            int vertical = nav.StepVertical(dt);
            if (vertical != 0)
            {
                buttons.MoveFocus(-vertical); // rows run top-down, so up is index-1
                ui.pitch = 1f;
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            if (MenuNavigator.ConfirmPressed()) buttons.Focused?.Activate();
        }

        // ------------------------------------------------------------ answer

        void Decide(System.Action choice)
        {
            if (decided) return;
            decided = true;
            IsOpen = false;
            ui.pitch = 1f;
            Blip(theme.ConfirmClip);
            Time.timeScale = 1f;
            // Hide now, destroy once the blip has played: the runner retries IN
            // PLACE, so the panel must not linger over the new run.
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = false;
            ReleaseVideo();
            Destroy(gameObject, 1f);
            choice?.Invoke();
        }

        // Safety: never leave the game frozen if this object goes away without
        // an answer — this also fires on the way out of play mode.
        void OnDestroy()
        {
            IsOpen = false;
            if (!decided) Time.timeScale = 1f;
            ReleaseVideo();
        }

        void ReleaseVideo()
        {
            if (video != null) { video.Stop(); video = null; }
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
                videoTexture = null;
            }
        }

        void Blip(AudioClip clip, float volumeScale = 1f)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume * volumeScale);
        }

        /// <summary>Fades a CanvasGroup in — the title block, which lives outside the row screen.</summary>
        sealed class FadeGroupStep : IRevealStep
        {
            readonly CanvasGroup group;
            readonly float seconds;
            float t;
            public FadeGroupStep(CanvasGroup group, float seconds) { this.group = group; this.seconds = Mathf.Max(0.01f, seconds); }
            public bool Tick(float dt)
            {
                t += dt;
                group.alpha = Mathf.Clamp01(t / seconds);
                return t >= seconds;
            }
            public void Finish() { t = seconds; group.alpha = 1f; }
        }
    }
}
