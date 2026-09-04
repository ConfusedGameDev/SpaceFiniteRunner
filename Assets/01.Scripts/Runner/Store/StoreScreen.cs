using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// The Store — the hub between missions (main menu START lands here,
    /// the Mission Complete panel's NEXT MISSION returns here). Three tabs
    /// on the themed menu framework, cycled with the bumpers (Q/E): CAR,
    /// SHIP, CHARACTER. Each is a column of rows at the left — the model
    /// row (<c>&lt; QUADRON &gt;</c>, one entry today), one
    /// <see cref="UpgradeRow"/> per category, START MISSION — over the
    /// <see cref="StoreStage"/>'s live model in the open centre, with the
    /// <see cref="StoreMediaPanel"/> at the right showing the piece the
    /// focused row would buy next and the wallet (a <see cref="MoneyHud"/>
    /// pointed at the profile's balance) top-right.
    ///
    /// Buying is one Confirm press on a row: the wallet is charged through
    /// <see cref="PlayerStats.TrySpend"/>, the level written, the profile
    /// saved at once (a purchase is a commit point), the meter and the
    /// wallet punched. No confirm dialog, no refunds. Back leaves for the
    /// main menu; START MISSION names the campaign's FRONTIER (the first
    /// mission of the <see cref="CampaignCatalog"/> not yet completed —
    /// <c>START MISSION — 2: NAME</c>), opens a <see cref="MissionSession"/>
    /// on it and loads its world's scene; greyed with the requirement
    /// printed while the frontier is gated, and leading to the Coming Soon
    /// scene once every mission is done. Without a catalog the settings'
    /// fixed next scene keeps the old flow alive. All through the loading
    /// curtain. The column's X is pre-measured from the
    /// widest row on the tab so the plates stay clear of the left edge and
    /// the model stays visible whatever the language.
    ///
    /// A hand-placed scene-lifetime object (the project rule): the builder
    /// puts it in the Store scene with its settings, stage and wallet wired;
    /// the canvas is built under it at play.
    /// </summary>
    public class StoreScreen : MonoBehaviour
    {
        const int SortingOrder = 30;
        const int UiLayer = 5;
        const float LeftEdge = -920f;   // where the row plates' left edge sits at 1920×1080
        const float RowsTop = 300f;
        const float RowHeight = 64f;
        const float RowSpacing = 10f;
        const float TitleY = 470f;
        const float HintY = 415f;
        static readonly Vector2 MediaPosition = new(660f, 230f);
        static readonly Vector2 MediaSize = new(480f, 270f);

        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)] StoreSettings settings;
        [SerializeField, Required] StoreStage stage;
        [Tooltip("The wallet counter (a MoneyHud beside this object). Pointed at the profile's balance.")]
        [SerializeField] MoneyHud wallet;

        /// <summary>One built tab: its screen and the rows the refresh writes.</summary>
        class Tab
        {
            public StoreSectionKind kind;
            public StoreSection section;
            public MenuScreen screen;
            public MenuChoice modelRow;
            public readonly List<(UpgradeDefinition def, UpgradeRow row)> upgrades = new();
            public MissionRow startRow;
        }

        MenuTheme theme;
        MenuNavigator nav;
        MenuTextLibrary texts;
        RectTransform root;
        AudioSource ui;
        PromptStrip footer;
        StoreMediaPanel media;
        readonly DebugMenu tabs = new();
        readonly List<Tab> built = new();
        Tab current;
        MenuRow lastFocused;
        float lockTimer;
        float openedTime;
        bool leaving;

        void Start()
        {
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            texts = MenuTextLibrary.Load();
            Build();
            MenuScreenFactory.EnsureEventSystem();
            if (wallet != null) wallet.ValueSource = () => PlayerStats.Balance;
            openedTime = Time.unscaledTime;

            current = built.Count > 0 ? built[0] : null;
            if (current != null)
            {
                current.screen.SlideIn(theme.ScreenSlide);
                lockTimer = theme.ScreenTransition;
                if (stage != null) stage.Show(current.kind);
                RefreshRows(current);
            }
            SetFooter();
        }

        void OnDestroy()
        {
            media?.Release();
            if (wallet != null) wallet.ValueSource = null;
        }

        // --------------------------------------------------------------- build

        /// <summary>Editor bake: draws the car tab so the layout can be judged before play (levels read as stock).</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            theme = MenuTheme.Load();
            texts = MenuTextLibrary.Load();
            Build();
            if (built.Count > 0)
            {
                built[0].screen.gameObject.SetActive(true);
                RefreshRows(built[0]);
            }
        }

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            built.Clear();
            media = null;
            footer = null;
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

            // No backdrop: the stage's model shows through the open centre.
            media = new StoreMediaPanel(root, theme, MediaPosition, MediaSize);
            footer = PromptStrip.Create(root, theme, 56f);

            if (settings == null)
            {
                Debug.LogError($"{nameof(StoreScreen)} has no {nameof(StoreSettings)} — run Tools → FiniteRunner → Create Store Scene.", this);
                return;
            }
            for (int k = 0; k < 3; k++)
            {
                StoreSection section = settings.Section((StoreSectionKind)k);
                if (section == null) continue;
                Tab tab = BuildTab((StoreSectionKind)k, section, k);
                built.Add(tab);
                tabs.AddTab(tab.screen);
            }
        }

        Tab BuildTab(StoreSectionKind kind, StoreSection section, int index)
        {
            var tab = new Tab { kind = kind, section = section };

            // Pre-measure the widest row so the column can be placed from
            // the left edge: MenuScreen fits every plate to the widest label
            // in any language plus the row type's widget reserve.
            float width = theme.RowWidth;
            width = Mathf.Max(width, RowWidthFor(MenuTextId.StoreModel, MenuChoice.RightReserve));
            string startLabel = StartLabel();
            width = Mathf.Max(width, RowWidthFor(startLabel, MissionRow.RightReserve));
            foreach (UpgradeDefinition def in section.categories)
                if (def != null) width = Mathf.Max(width, RowWidthFor(def.label, UpgradeRow.RightReserve));
            float columnX = LeftEdge + width * 0.5f;

            var screen = MenuScreen.Create($"Tab_{kind}", root, theme, columnX, RowsTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            screen.AddLabel("Title", new Vector2(0f, TitleY), new Vector2(1200f, 60f), section.title, 44,
                            theme.TextPrimary, theme.TitleFont, TextAnchor.MiddleCenter, 0f);
            screen.AddLabel("Hint", new Vector2(0f, HintY), new Vector2(900f, 40f),
                            $"LB ◀  {texts.Get(MenuTextId.HintSection)} {index + 1}/3  ▶ RB   (Q/E)", 24,
                            theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter, 0f);

            tab.modelRow = screen.AddRow<MenuChoice>(MenuTextId.StoreModel);
            var names = new string[Mathf.Max(1, section.models.Count)];
            for (int i = 0; i < names.Length; i++)
                names[i] = i < section.models.Count && section.models[i] != null && !string.IsNullOrEmpty(section.models[i].displayName)
                    ? section.models[i].displayName : "—";
            tab.modelRow.Configure(names, 0, _ => { }); // one model today; the seam for more

            foreach (UpgradeDefinition def in section.categories)
            {
                if (def == null) continue;
                UpgradeRow row = screen.AddRow<UpgradeRow>(def.label);
                UpgradeDefinition captured = def;
                UpgradeRow capturedRow = row;
                row.Activated += () => Buy(tab, captured, capturedRow);
                tab.upgrades.Add((def, row));
            }

            tab.startRow = screen.AddRow<MissionRow>(startLabel);
            tab.startRow.Activated += StartMission;
            RefreshStartRow(tab);

            screen.HideImmediate();
            tab.screen = screen;
            return tab;
        }

        float RowWidthFor(MenuTextId label, float reserve)
            => Mathf.Ceil(MenuRow.LabelInsetWidth + texts.MaxWidth(label, theme.BodyFont, MenuRow.LabelFontSize) + reserve);

        float RowWidthFor(string label, float reserve)
            => Mathf.Ceil(MenuRow.LabelInsetWidth + MenuTextLibrary.MeasureWidth(label, theme.BodyFont, MenuRow.LabelFontSize) + reserve);

        // The START MISSION row's text: the frontier's number and name, COMING
        // SOON once the campaign is done, plain START MISSION with no catalog
        // (and in the edit-mode preview, which never reads the profile).
        string StartLabel()
        {
            CampaignCatalog catalog = CampaignCatalog.Load();
            if (catalog == null || !Application.isPlaying) return texts.Get(MenuTextId.StartMission);
            CampaignCatalog.Entry frontier = CampaignProgress.Frontier(catalog);
            if (!frontier.IsSet) return texts.Get(MenuTextId.ComingSoon);
            return string.Format(texts.Get(MenuTextId.StartMissionTarget), frontier.number, frontier.mission.DisplayName);
        }

        // ------------------------------------------------------------- refresh

        /// <summary>Redraws every purchase row of a tab from the profile (stock levels and $0 outside play).</summary>
        void RefreshRows(Tab tab)
        {
            if (tab == null) return;
            string modelId = ModelOf(tab);
            long balance = Application.isPlaying ? PlayerStats.Balance : 0;
            string maxLabel = texts.Get(MenuTextId.Max);
            foreach ((UpgradeDefinition def, UpgradeRow row) in tab.upgrades)
            {
                int level = Application.isPlaying && modelId != null ? PlayerStats.UpgradeLevel(modelId, def.id) : 0;
                long cost = def.CostFor(level + 1);
                row.SetState(level, cost, cost > 0 && cost <= balance, maxLabel);
            }
        }

        /// <summary>Greys the START MISSION row with its requirement while the frontier is gated; clear otherwise.</summary>
        void RefreshStartRow(Tab tab)
        {
            if (tab == null || tab.startRow == null) return;
            CampaignCatalog catalog = CampaignCatalog.Load();
            CampaignCatalog.Entry frontier = catalog != null && Application.isPlaying ? CampaignProgress.Frontier(catalog) : default;
            bool met = !frontier.IsSet || CampaignProgress.RequirementsMet(frontier.mission);
            tab.startRow.SetEnabled(met);
            tab.startRow.SetValue(met ? string.Empty
                                      : RequirementText.Describe(CampaignProgress.FirstUnmet(frontier.mission), texts));
        }

        // Every tab carries its own START row and a purchase can cross a
        // money gate either way, so all of them refresh together.
        void RefreshStartRows()
        {
            foreach (Tab tab in built) RefreshStartRow(tab);
        }

        static string ModelOf(Tab tab)
        {
            StoreModel model = tab.section != null ? tab.section.DefaultModel : null;
            return model != null && !string.IsNullOrEmpty(model.modelId) ? model.modelId : null;
        }

        void RefreshMedia(bool force)
        {
            if (media == null || current == null) return;
            MenuRow focused = current.screen.Focused;
            if (!force && focused == lastFocused) return;
            lastFocused = focused;

            UpgradeDefinition def = null;
            int nextLevel = 1;
            if (focused is UpgradeRow upgradeRow)
            {
                foreach ((UpgradeDefinition d, UpgradeRow r) in current.upgrades)
                    if (r == upgradeRow) { def = d; nextLevel = r.Level + 1; break; }
            }
            // The model row and START MISSION show the section's first piece.
            if (def == null && current.section.categories.Count > 0) def = current.section.categories[0];

            if (def == null)
            {
                media.Show(null, null);
                return;
            }
            def.MediaFor(Mathf.Min(nextLevel, UpgradeIds.MaxLevel), out var clip, out var image);
            media.Show(clip, image);
        }

        // --------------------------------------------------------------- input

        void Update()
        {
            if (theme == null || current == null || leaving) return;
            InputPromptBinder.Poll();
            float dt = Time.unscaledDeltaTime;

            PollRotation(dt);

            if (lockTimer > 0f)
            {
                lockTimer -= dt;
                return;
            }
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            if (MenuNavigator.BackPressed())
            {
                Leave(null);
                return;
            }

            // Bumpers (or Q/E) flip between the sections, the debug tabs' slide language.
            if (tabs.Count > 1)
            {
                int step = DebugMenu.TabStepPressed();
                if (step != 0)
                {
                    current.screen.SlideOut(-step * theme.ScreenSlide);
                    MenuScreen next = tabs.Cycle(step);
                    current = built.Find(t => t.screen == next);
                    RefreshRows(current);
                    next.SlideIn(step * theme.ScreenSlide);
                    lockTimer = theme.ScreenTransition;
                    if (stage != null) stage.Show(current.kind);
                    lastFocused = null;
                    RefreshMedia(true);
                    Blip(theme.MoveClip);
                    return;
                }
            }

            int vertical = nav.StepVertical(dt);
            if (vertical != 0)
            {
                current.screen.MoveFocus(-vertical); // rows run top-down, so up is index-1
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(0f, theme.MoveRumble, 0.05f);
            }

            int horizontal = nav.StepHorizontal(dt);
            if (horizontal != 0 && current.screen.Focused != null && current.screen.Focused.Adjust(horizontal))
                Blip(theme.AdjustClip);

            if (MenuNavigator.ConfirmPressed()) current.screen.Focused?.Activate();

            RefreshMedia(false);
        }

        // Right stick or a left-button drag (off the rows — the plates are
        // the rows' hit areas) turns the model; the stage idles back into
        // its spin on its own. First non-zero source wins, the camera rig's rule.
        void PollRotation(float dt)
        {
            if (stage == null || settings == null) return;
            float yaw = 0f;

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUi) yaw = mouse.delta.ReadValue().x * settings.dragDegreesPerPixel;
            }
            if (Mathf.Approximately(yaw, 0f))
            {
                var pad = Gamepad.current;
                if (pad != null)
                {
                    float x = pad.rightStick.ReadValue().x;
                    if (Mathf.Abs(x) > 0.1f) yaw = x * settings.stickDegreesPerSecond * dt;
                }
            }
            if (!Mathf.Approximately(yaw, 0f)) stage.Nudge(yaw);
        }

        // ------------------------------------------------------------ actions

        void Buy(Tab tab, UpgradeDefinition def, UpgradeRow row)
        {
            if (leaving || tab == null || def == null) return;
            string modelId = ModelOf(tab);
            if (modelId == null || string.IsNullOrEmpty(def.id))
            {
                Debug.LogError($"Store: {def.name} or its section has no id — nothing to buy.", def);
                Blip(theme.BackClip);
                return;
            }

            int level = PlayerStats.UpgradeLevel(modelId, def.id);
            long cost = def.CostFor(level + 1);
            if (level >= UpgradeIds.MaxLevel || cost <= 0 || !PlayerStats.TrySpend(cost))
            {
                Blip(theme.BackClip);
                return;
            }

            PlayerStats.SetUpgradeLevel(modelId, def.id, level + 1);
            PlayerProfileStore.Save(); // a purchase is a commit point
            RefreshRows(tab);
            RefreshStartRows();
            row.PunchPips();
            if (wallet != null) wallet.Punch(settings != null ? settings.walletPunchScale : 0f);
            Blip(theme.ConfirmClip);
            HapticsSystem.Instance.Pulse(theme.ConfirmRumble, theme.ConfirmRumble * 0.5f, 0.15f);
            RefreshMedia(true);
        }

        // The frontier from the catalog: a gated one refuses the press (the
        // row is greyed but still focusable), an exhausted campaign leads to
        // Coming Soon, no catalog at all falls back to the settings' scene.
        void StartMission()
        {
            CampaignCatalog catalog = CampaignCatalog.Load();
            if (catalog == null)
            {
                if (settings == null || string.IsNullOrEmpty(settings.nextMissionScene))
                {
                    Debug.LogError("Store: no campaign catalog and no next mission scene on the StoreSettings asset.", this);
                    Blip(theme.BackClip);
                    return;
                }
                MissionSession.Clear();
                Leave(settings.nextMissionScene);
                return;
            }

            CampaignCatalog.Entry frontier = CampaignProgress.Frontier(catalog);
            if (!frontier.IsSet)
            {
                MissionSession.Clear();
                Leave(catalog.comingSoonSceneName);
                return;
            }
            if (!CampaignProgress.RequirementsMet(frontier.mission))
            {
                Blip(theme.BackClip);
                return;
            }
            if (frontier.world == null || string.IsNullOrEmpty(frontier.world.sceneName))
            {
                Debug.LogError($"Store: mission {frontier.mission.name} has no world scene to load.", frontier.mission);
                Blip(theme.BackClip);
                return;
            }

            MissionSession.Begin(frontier.mission, replay: false);
            Leave(frontier.world.sceneName);
        }

        // Null = the main menu. Everything goes through the loading curtain.
        void Leave(string sceneName)
        {
            if (leaving || LoadingScreen.IsLoading) return;
            leaving = true;
            PlayerProfileStore.SaveIfDirty();
            Blip(sceneName == null ? theme.BackClip : theme.ConfirmClip);
            if (sceneName == null) LoadingScreen.LoadMainMenu();
            else LoadingScreen.Load(sceneName);
        }

        void SetFooter()
        {
            if (footer == null) return;
            footer.SetHints((PromptAction.Navigate, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintBuy),
                            (PromptAction.Back, MenuTextId.HintBack));
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
