using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The pre-run mission brief for the city chase, raised by the
    /// LevelManager before any objective plays (unless its Skip Mission Brief
    /// is on): a video panel (RawImage fed by a runtime VideoPlayer — a dead
    /// NO SIGNAL screen when the level has no clip), the objective list, one
    /// toggle row per <see cref="OptionalChallenge"/> (label ×multiplier —
    /// each accepted one multiplies the payout), the live REWARD readout
    /// under the video, and ACCEPT. Built on the themed menu framework like
    /// GameOverScreen: it freezes scaled time while up (which also keeps the
    /// pause menu and city map from stacking over it), animates on unscaled
    /// time, and demands an answer — Back does nothing. Accepting plays a
    /// CRT-style collapse (the page squashes to a line and fades), and only
    /// then unfreezes time and hands the accepted challenges + final reward
    /// to the callback — the game starts as the UI lands. The video runs on
    /// the VideoPlayer's own clock, so it plays through the freeze.
    /// </summary>
    public class MissionBriefScreen : MonoBehaviour
    {
        const int SortingOrder = 24;      // above HUD and thunder (18), below the game-over screen (25)
        const float CollapseSeconds = 0.3f;
        const float ColumnX = -430f;      // left column: objectives, challenges, ACCEPT
        const float PanelX = 450f;        // right side: video panel + reward
        const float ObjectiveStep = 46f;

        /// <summary>True while the brief is waiting for ACCEPT.</summary>
        public static bool IsOpen { get; private set; }

        MenuTheme theme;
        MenuNavigator nav;
        MenuScreen screen;
        AudioSource ui;
        RectTransform content;
        CanvasGroup contentGroup;
        VideoPlayer video;
        RenderTexture videoTexture;
        Text rewardValue;
        LevelDefinition level;
        System.Action<List<OptionalChallenge>, int> onAccept;
        readonly List<(OptionalChallenge challenge, MenuToggle row)> challengeRows = new();
        float openedTime;
        float collapseTimer;
        bool accepted;

        /// <summary>
        /// Puts the brief up and freezes the game under it. The callback runs
        /// with time already unfrozen, carrying the challenges the player
        /// toggled on and the multiplied reward.
        /// </summary>
        public static MissionBriefScreen Show(LevelDefinition level, System.Action<List<OptionalChallenge>, int> onAccept)
        {
            var brief = new GameObject("MissionBriefScreen").AddComponent<MissionBriefScreen>();
            SceneHierarchy.Adopt(brief.gameObject, SceneHierarchy.Systems(brief.gameObject.scene), worldPositionStays: false);
            brief.level = level;
            brief.onAccept = onAccept;
            brief.theme = MenuTheme.Load();
            brief.nav = new MenuNavigator(brief.theme);
            brief.Build();
            return brief;
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

            // Everything hangs off one full-screen child: the collapse scales
            // it as a unit, dim backdrop included.
            var contentGo = new GameObject("Content", typeof(RectTransform));
            content = (RectTransform)contentGo.transform;
            content.SetParent(transform, false);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;
            contentGroup = contentGo.AddComponent<CanvasGroup>();

            var dimGo = new GameObject("Backdrop", typeof(RectTransform));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(content, false);
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            var dim = dimGo.AddComponent<Image>();
            var dimColor = theme.Backdrop;
            dimColor.a = 0.88f;
            dim.color = dimColor;

            // Left column, top-down: objectives are plain labels (nothing to
            // answer on them), then the challenge toggles and ACCEPT as rows —
            // so their vertical start depends on how many objectives there are.
            int objectiveCount = level.Count;
            float objectivesTop = 330f;
            float headerY = objectivesTop - objectiveCount * ObjectiveStep - 30f;
            float rowsTop = headerY - 76f;

            screen = MenuScreen.Create("BriefScreen", content, theme, ColumnX, rowsTop);
            screen.SetRowMetrics(64f, 12f);

            screen.AddLabel("Title", new Vector2(0f, 462f), new Vector2(1200f, 80f),
                            MenuTextId.MissionBrief, 52, theme.TextPrimary, theme.TitleFont,
                            TextAnchor.MiddleCenter, 0f);
            screen.AddLabel("LevelName", new Vector2(0f, 404f), new Vector2(1200f, 44f),
                            level.levelName.ToUpperInvariant(), 30, theme.Accent, theme.BodyFont,
                            TextAnchor.MiddleCenter, 0.04f);

            for (int i = 0; i < objectiveCount; i++)
            {
                LevelObjective step = level.objectives[i];
                screen.AddLabel($"Objective{i}", new Vector2(ColumnX, objectivesTop - i * ObjectiveStep),
                                new Vector2(760f, 40f), $"{i + 1}. {step.Summary}", 30, step.Accent,
                                theme.BodyFont, TextAnchor.MiddleLeft, theme.TitleLead + i * theme.EntranceStagger);
            }

            if (level.optionalChallenges != null && level.optionalChallenges.Count > 0)
            {
                screen.AddLabel("ChallengesHeader", new Vector2(ColumnX, headerY), new Vector2(760f, 40f),
                                MenuTextId.OptionalChallenges, 28, theme.TextDim, theme.TitleFont,
                                TextAnchor.MiddleLeft, theme.TitleLead);

                foreach (OptionalChallenge challenge in level.optionalChallenges)
                {
                    var row = screen.AddRow<MenuToggle>(challenge.ChallengeSummary); // the objective's own summary, ×multiplier
                    row.Configure(false, _ => RefreshReward());
                    challengeRows.Add((challenge, row));
                }
            }

            screen.AddRow<MenuRow>(MenuTextId.Accept).Activated += Accept;
            screen.SetFocus(0);

            BuildVideoPanel();

            screen.AddLabel("RewardTitle", new Vector2(PanelX, -160f), new Vector2(600f, 44f),
                            MenuTextId.Reward, 34, theme.TextDim, theme.TitleFont,
                            TextAnchor.MiddleCenter, theme.TitleLead);
            rewardValue = screen.AddLabel("RewardValue", new Vector2(PanelX, -224f), new Vector2(600f, 70f),
                                          "$0", 58, theme.Accent, theme.TitleFont,
                                          TextAnchor.MiddleCenter, theme.TitleLead);
            RefreshReward();

            PromptStrip.Create(content, theme, 56f)
                       .SetHints((PromptAction.Navigate, MenuTextId.HintMove),
                                 (PromptAction.Confirm, MenuTextId.HintSelect));

            MenuScreenFactory.EnsureEventSystem(); // the city scene has none; mouse clicks need one
            IsOpen = true;
            Time.timeScale = 0f;
            openedTime = Time.unscaledTime;
            screen.Show(staggered: true);
            Gamepad.current?.ResetHaptics();
        }

        /// <summary>
        /// The video holder: a plate framing a RawImage. With a clip, a
        /// VideoPlayer renders it into a runtime RenderTexture (looping, no
        /// audio — nothing here knows the mixer); without one the holder
        /// stays, showing a dead screen so the layout never changes.
        /// </summary>
        void BuildVideoPanel()
        {
            var plate = MenuScreen.MakeImage("VideoPlate", screen.Root, new Vector2(PanelX, 150f),
                                             new Vector2(830f, 480f), theme.RowPlate, theme.PlateIdle);
            screen.AddEntranceItem(plate.rectTransform, plate.gameObject.AddComponent<CanvasGroup>(), theme.TitleLead);

            var rawGo = new GameObject("Video", typeof(RectTransform));
            var rawRect = (RectTransform)rawGo.transform;
            rawRect.SetParent(plate.rectTransform, false);
            rawRect.anchorMin = rawRect.anchorMax = new Vector2(0.5f, 0.5f);
            rawRect.sizeDelta = new Vector2(806f, 456f);
            var raw = rawGo.AddComponent<RawImage>();
            raw.raycastTarget = false;

            if (level.briefVideo != null)
            {
                videoTexture = new RenderTexture(1024, 576, 0);
                video = gameObject.AddComponent<VideoPlayer>();
                video.playOnAwake = false;
                video.source = VideoSource.VideoClip;
                video.clip = level.briefVideo;
                video.isLooping = true;
                video.renderMode = VideoRenderMode.RenderTexture;
                video.targetTexture = videoTexture;
                video.audioOutputMode = VideoAudioOutputMode.None;
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

        /// <summary>Base reward × every accepted challenge's multiplier.</summary>
        int ComputeReward()
        {
            long reward = level.baseReward;
            foreach ((OptionalChallenge challenge, MenuToggle row) in challengeRows)
                if (row.IsOn) reward *= Mathf.Max(1, challenge.multiplier);
            return (int)System.Math.Min(reward, int.MaxValue);
        }

        void RefreshReward()
        {
            if (rewardValue != null)
                rewardValue.text = "$" + ComputeReward().ToString("N0", CultureInfo.InvariantCulture);
        }

        void Update()
        {
            if (accepted)
            {
                Collapse();
                return;
            }
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            float dt = Time.unscaledDeltaTime;
            int vertical = nav.StepVertical(dt);
            if (vertical != 0)
            {
                screen.MoveFocus(-vertical); // rows run top-down, so up is index-1
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            int horizontal = nav.StepHorizontal(dt);
            if (horizontal != 0 && screen.Focused != null && screen.Focused.Adjust(horizontal))
                Blip(theme.AdjustClip);

            if (MenuNavigator.ConfirmPressed()) screen.Focused?.Activate();
        }

        void Accept()
        {
            if (accepted) return;
            accepted = true;
            IsOpen = false;
            collapseTimer = 0f;
            Blip(theme.ConfirmClip);
        }

        /// <summary>
        /// The CRT power-off: the whole page squashes vertically (stretching a
        /// touch sideways) and fades, then time unfreezes, the callback runs
        /// and the screen destroys itself — the game starts as the UI lands.
        /// </summary>
        void Collapse()
        {
            collapseTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(collapseTimer / CollapseSeconds);
            float e = theme.Ease(t);
            content.localScale = new Vector3(1f + 0.15f * e, 1f - e, 1f);
            contentGroup.alpha = 1f - e * e;
            if (t < 1f) return;

            Time.timeScale = 1f;
            var acceptedChallenges = new List<OptionalChallenge>();
            foreach ((OptionalChallenge challenge, MenuToggle row) in challengeRows)
                if (row.IsOn) acceptedChallenges.Add(challenge);
            var callback = onAccept;
            onAccept = null; // Destroy is deferred — Update must not land here twice
            callback?.Invoke(acceptedChallenges, ComputeReward());
            Destroy(gameObject);
        }

        // Safety: never leave the game frozen (or the video texture alive) if
        // this object goes away without an answer — also fires leaving play mode.
        void OnDestroy()
        {
            IsOpen = false;
            if (!accepted) Time.timeScale = 1f;
            if (video != null) video.Stop();
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
            }
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }
    }
}
