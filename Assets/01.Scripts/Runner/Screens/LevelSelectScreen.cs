using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Audio;
using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// SELECT COURSE — the runner's level select, F-Zero GX style: the
    /// picked course's name over a big top-down map of its track on an
    /// oblique grid, its difficulty under it, and a strip of round
    /// thumbnails along the bottom (a tiny map each, or the catalog's
    /// picture) with the picked one ringed in the accent. Left / right on
    /// the d-pad, the left stick or A / D moves the pick with wrap; A /
    /// Enter plays it — <see cref="TrackSelection"/> is set and the runner
    /// scene loads through the loading curtain; B / Esc goes back to the
    /// main menu. The courses are the <see cref="TrackSelectCatalog"/>
    /// (synced from the Tracks folder); the maps are
    /// <see cref="TrackLayoutPreview"/> on the chase minimap's
    /// <see cref="TrackMapLine"/>, so no track is built here. A hand-placed
    /// scene-lifetime object (the project rule) the scene builder puts in
    /// <c>LevelSelect.unity</c>; the canvas is built under it at play, on
    /// the themed menu framework like every other screen.
    /// </summary>
    public class LevelSelectScreen : MonoBehaviour
    {
        /// <summary>Scene name of the level select.</summary>
        public const string SceneName = "LevelSelect";
        /// <summary>The runner scene when the campaign catalog does not name one.</summary>
        public const string FallbackRunnerScene = "FiniteRunner_Test";

        const int SortingOrder = 30;
        const int UiLayer = 5;

        // Layout at the 1920 × 1080 reference. The title plate sits top-left
        // (MenuScreen puts it at columnX, contentTop + 150); everything else
        // is centred on the screen's middle column.
        const float TitleColumnX = -560f;
        const float ContentTop = 300f;
        const float NameY = 330f;
        const float SubtitleY = 268f;
        const int NameFontSize = 64;
        const int SubtitleFontSize = 30;

        static readonly Vector2 MapArea = new(900f, 560f);
        const float MapY = 70f;
        const float MapSquash = 0.6f;       // the grid plane seen obliquely: a 2D squash — overlay canvases are orthographic
        const float MapTiltDegrees = -6f;
        const float MapPadding = 0.1f;
        const float MapLineWidth = 6f;
        const int MapSamples = 600;          // ~2–3 per knot, so a sweep draws as a curve rather than a polygon
        const int GridCell = 40;

        const float DifficultyY = -205f;
        const int DifficultyFontSize = 28;
        const int PipCount = 5;
        const float PipSize = 24f;
        const float PipStep = 34f;

        const float StripY = -345f;         // the picked ring's bottom stays clear of the footer hints
        const float StripMaxWidth = 1500f;
        const float StripMaxStep = 150f;
        const float RingSize = 110f;
        const float RingSelectedScale = 1.3f;
        const float ThumbSize = 92f;
        const float ThumbLineWidth = 2f;
        const int ThumbSamples = 80;
        const float ArrowGap = 110f;
        const int ArrowFontSize = 54;

        const float PopSeconds = 0.18f;
        const float PopScale = 1.08f;
        const float RingEaseSeconds = 0.1f;

        /// <summary>One course of the strip: its catalog entry, its two fitted maps and its widgets.</summary>
        class Slot
        {
            public TrackSelectCatalog.Entry entry;
            public List<Vector2> bigPoints;
            public List<Vector2> thumbPoints;
            public RectTransform ring;
            public Image ringImage;
        }

        MenuTheme theme;
        MenuNavigator nav;
        RectTransform root;
        AudioSource ui;
        PromptStrip footer;
        MenuScreen screen;

        Text nameText;
        Text subtitleText;
        RectTransform mapLineRect;
        TrackMapLine mapLine;
        CanvasGroup mapLineGroup;
        readonly Image[] pips = new Image[PipCount];
        Text arrowLeft;
        Text arrowRight;
        Sprite pipFull;
        Sprite pipEmpty;

        readonly List<Slot> slots = new();
        int index;
        float popTimer = float.MaxValue;
        float openedTime;
        bool leaving;

        static Sprite gridSprite;

        /// <summary>The picked course, or null with an empty catalog.</summary>
        public TrackSelectCatalog.Entry Selected => index >= 0 && index < slots.Count ? slots[index].entry : null;

        void Start()
        {
            theme = MenuTheme.Load();
            nav = new MenuNavigator(theme);
            Build();
            MenuScreenFactory.EnsureEventSystem();
            openedTime = Time.unscaledTime;
            screen.Show(true);
            if (footer != null)
            {
                if (slots.Count > 0)
                    footer.SetHints((PromptAction.Adjust, MenuTextId.HintMove), (PromptAction.Confirm, MenuTextId.HintPlay), (PromptAction.Back, MenuTextId.HintBack));
                else
                    footer.SetHints((PromptAction.Back, MenuTextId.HintBack));
            }
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
            slots.Clear();
            nameText = null;
            subtitleText = null;
            mapLine = null;
            mapLineRect = null;
            mapLineGroup = null;
            arrowLeft = null;
            arrowRight = null;
            for (int i = 0; i < pips.Length; i++) pips[i] = null;
        }

        // ------------------------------------------------------------- build

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

            screen = MenuScreen.Create("LevelSelectScreen", root, theme, TitleColumnX, ContentTop);
            screen.SetTitle(MenuTextId.SelectCourse);

            LoadCourses();
            if (slots.Count == 0)
            {
                screen.AddLabel("Empty", Vector2.zero, new Vector2(1200f, 80f),
                                MenuTextId.NothingHereYet, 36, Color.white, theme.TitleFont, TextAnchor.MiddleCenter, theme.TitleLead);
            }
            else
            {
                BuildCourseCard();
                BuildStrip();
            }
            screen.HideImmediate();

            footer = PromptStrip.Create(root, theme, 56f);

            index = Mathf.Clamp(index, 0, Mathf.Max(0, slots.Count - 1));
            if (slots.Count > 0) Select(index, animate: false);
        }

        // Every valid catalog entry, with its maps sampled once and fitted
        // twice — the big plane and the thumbnail — so a pick never resamples.
        void LoadCourses()
        {
            slots.Clear();
            TrackSelectCatalog catalog = TrackSelectCatalog.Load();
            if (catalog == null) return;
            foreach (TrackSelectCatalog.Entry entry in catalog.entries)
            {
                if (entry == null || !entry.IsValid) continue;
                List<Vector2> raw = TrackLayoutPreview.SampleXZ(entry.track.Layout, MapSamples);
                if (raw.Count < 2) continue;
                // The plane is landscape, so the big map runs left → right; the round thumbnail keeps the minimap's start-at-bottom.
                slots.Add(new Slot
                {
                    entry = entry,
                    bigPoints = TrackLayoutPreview.Fit(raw, MapArea, MapPadding, Vector2.right),
                    thumbPoints = TrackLayoutPreview.Fit(Thin(raw, ThumbSamples), new Vector2(ThumbSize, ThumbSize), 0.12f),
                });
            }
        }

        // The name, the subtitle, the oblique grid plane with the map, and the difficulty line.
        void BuildCourseCard()
        {
            RectTransform page = screen.Root;
            nameText = screen.AddLabel("CourseName", new Vector2(0f, NameY), new Vector2(1400f, 80f), "", NameFontSize,
                                       Color.white, theme.TitleFont, TextAnchor.MiddleCenter, theme.TitleLead);
            subtitleText = screen.AddLabel("CourseSubtitle", new Vector2(0f, SubtitleY), new Vector2(1400f, 40f), "", SubtitleFontSize,
                                           theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter, theme.TitleLead + theme.EntranceStagger);

            // The plane carries the squash and the tilt; the line and the pop
            // animation live on a child, so neither fights the other.
            var plane = new GameObject("MapPlane", typeof(RectTransform));
            var planeRect = (RectTransform)plane.transform;
            planeRect.SetParent(page, false);
            planeRect.anchorMin = planeRect.anchorMax = new Vector2(0.5f, 0.5f);
            planeRect.pivot = new Vector2(0.5f, 0.5f);
            planeRect.anchoredPosition = new Vector2(0f, MapY);
            planeRect.sizeDelta = MapArea;
            planeRect.localScale = new Vector3(1f, MapSquash, 1f);
            planeRect.localEulerAngles = new Vector3(0f, 0f, MapTiltDegrees);
            var planeGroup = plane.AddComponent<CanvasGroup>();
            screen.AddEntranceItem(planeRect, planeGroup, theme.TitleLead + theme.EntranceStagger * 2f);

            Color accent = theme.Accent;
            Image fill = MenuScreen.MakeImage("Fill", planeRect, Vector2.zero, MapArea, null, new Color(accent.r, accent.g, accent.b, 0.08f));
            fill.raycastTarget = false;
            Image grid = MenuScreen.MakeImage("Grid", planeRect, Vector2.zero, MapArea, GridSprite(), new Color(accent.r, accent.g, accent.b, 0.35f));
            grid.type = Image.Type.Tiled;
            grid.raycastTarget = false;

            var lineGo = new GameObject("TrackLine", typeof(RectTransform));
            mapLineRect = (RectTransform)lineGo.transform;
            mapLineRect.SetParent(planeRect, false);
            mapLineRect.anchorMin = mapLineRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapLineRect.pivot = new Vector2(0.5f, 0.5f);
            mapLineRect.anchoredPosition = Vector2.zero;
            mapLineRect.sizeDelta = MapArea;
            mapLine = lineGo.AddComponent<TrackMapLine>();
            mapLine.SetStyle(MapLineWidth, Color.white, Color.white);
            mapLineGroup = lineGo.AddComponent<CanvasGroup>();

            // DIFFICULTY and its pips: a label ending just left of the middle, pips running right of it.
            screen.AddLabel("DifficultyLabel", new Vector2(-190f, DifficultyY), new Vector2(320f, 40f), MenuTextId.Difficulty, DifficultyFontSize,
                            Color.white, theme.BodyFont, TextAnchor.MiddleRight, theme.TitleLead + theme.EntranceStagger * 3f);
            pipFull = UiSprites.Circle(32);
            pipEmpty = UiSprites.Ring(32, 3);
            var pipsGo = new GameObject("Pips", typeof(RectTransform));
            var pipsRect = (RectTransform)pipsGo.transform;
            pipsRect.SetParent(page, false);
            pipsRect.anchorMin = pipsRect.anchorMax = new Vector2(0.5f, 0.5f);
            pipsRect.pivot = new Vector2(0.5f, 0.5f);
            pipsRect.anchoredPosition = new Vector2(0f, DifficultyY);
            pipsRect.sizeDelta = new Vector2(PipStep * PipCount, PipSize);
            var pipsGroup = pipsGo.AddComponent<CanvasGroup>();
            screen.AddEntranceItem(pipsRect, pipsGroup, theme.TitleLead + theme.EntranceStagger * 3f);
            for (int i = 0; i < PipCount; i++)
                pips[i] = MenuScreen.MakeImage($"Pip{i}", pipsRect, new Vector2(10f + i * PipStep + PipSize * 0.5f, 0f), new Vector2(PipSize, PipSize), pipEmpty, theme.TextDim);
        }

        // The bottom strip: one ring per course with a disc-masked thumbnail
        // inside, and the two arrows beside it.
        void BuildStrip()
        {
            RectTransform page = screen.Root;
            var stripGo = new GameObject("Strip", typeof(RectTransform));
            var strip = (RectTransform)stripGo.transform;
            strip.SetParent(page, false);
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
            strip.pivot = new Vector2(0.5f, 0.5f);
            strip.anchoredPosition = new Vector2(0f, StripY);
            float step = Mathf.Min(StripMaxStep, StripMaxWidth / Mathf.Max(1, slots.Count));
            float width = step * slots.Count;
            strip.sizeDelta = new Vector2(width, RingSize * RingSelectedScale);
            var stripGroup = stripGo.AddComponent<CanvasGroup>();
            screen.AddEntranceItem(strip, stripGroup, theme.TitleLead + theme.EntranceStagger * 4f);

            Sprite ringSprite = UiSprites.Ring(128, 5);
            Sprite discSprite = UiSprites.Circle(128);
            Color backdrop = theme.Backdrop;
            Color discColor = new(backdrop.r * 0.6f, backdrop.g * 0.6f, backdrop.b * 0.6f, 1f);
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                float x = -width * 0.5f + step * (i + 0.5f);
                Image ring = MenuScreen.MakeImage($"Course{i}", strip, new Vector2(x, 0f), new Vector2(RingSize, RingSize), ringSprite, theme.TextDim);
                slot.ring = ring.rectTransform;
                slot.ringImage = ring;

                // The disc masks whatever is drawn inside it to the circle.
                Image disc = MenuScreen.MakeImage("Disc", slot.ring, Vector2.zero, new Vector2(RingSize - 10f, RingSize - 10f), discSprite, discColor);
                var mask = disc.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;

                if (slot.entry.thumbnail != null)
                {
                    Image picture = MenuScreen.MakeImage("Thumbnail", disc.rectTransform, Vector2.zero, new Vector2(RingSize - 10f, RingSize - 10f), slot.entry.thumbnail, Color.white);
                    picture.type = Image.Type.Simple;
                    picture.preserveAspect = true;
                }
                else
                {
                    var lineGo = new GameObject("ThumbLine", typeof(RectTransform));
                    var lineRect = (RectTransform)lineGo.transform;
                    lineRect.SetParent(disc.rectTransform, false);
                    lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);
                    lineRect.pivot = new Vector2(0.5f, 0.5f);
                    lineRect.anchoredPosition = Vector2.zero;
                    lineRect.sizeDelta = new Vector2(ThumbSize, ThumbSize);
                    var line = lineGo.AddComponent<TrackMapLine>();
                    line.SetStyle(ThumbLineWidth, Color.white, Color.white);
                    line.SetPoints(slot.thumbPoints);
                }
            }

            // ▲ is known to render in the body font (the menus' overflow cues); turned, it is the arrow.
            float arrowX = width * 0.5f + ArrowGap;
            arrowLeft = MenuScreen.MakeText("ArrowLeft", strip, new Vector2(-arrowX, 0f), new Vector2(80f, 80f), "▲", ArrowFontSize, theme.Accent, theme.BodyFont, TextAnchor.MiddleCenter);
            arrowLeft.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f);
            arrowRight = MenuScreen.MakeText("ArrowRight", strip, new Vector2(arrowX, 0f), new Vector2(80f, 80f), "▲", ArrowFontSize, theme.Accent, theme.BodyFont, TextAnchor.MiddleCenter);
            arrowRight.rectTransform.localEulerAngles = new Vector3(0f, 0f, -90f);
            bool several = slots.Count > 1;
            arrowLeft.gameObject.SetActive(several);
            arrowRight.gameObject.SetActive(several);
        }

        // ------------------------------------------------------------ select

        /// <summary>Makes <paramref name="newIndex"/> the pick: the card, the map, the pips and the ring targets.</summary>
        void Select(int newIndex, bool animate)
        {
            if (slots.Count == 0) return;
            index = ((newIndex % slots.Count) + slots.Count) % slots.Count;
            Slot slot = slots[index];

            if (nameText != null) nameText.text = slot.entry.Label;
            if (subtitleText != null) subtitleText.text = slot.entry.subtitle ?? "";
            if (mapLine != null) mapLine.SetPoints(slot.bigPoints);
            int difficulty = Mathf.Clamp(slot.entry.difficulty, 1, PipCount);
            for (int i = 0; i < PipCount; i++)
            {
                if (pips[i] == null) continue;
                bool lit = i < difficulty;
                pips[i].sprite = lit ? pipFull : pipEmpty;
                pips[i].color = lit ? theme.Accent : theme.TextDim;
            }

            if (animate) popTimer = 0f;
            else
            {
                popTimer = float.MaxValue;
                ApplyPop(1f);
                for (int i = 0; i < slots.Count; i++) ApplyRing(slots[i], i == index, 1f);
            }
        }

        void ApplyPop(float e)
        {
            if (mapLineRect == null) return;
            float scale = Mathf.LerpUnclamped(PopScale, 1f, e);
            mapLineRect.localScale = new Vector3(scale, scale, 1f);
            if (mapLineGroup != null) mapLineGroup.alpha = e;
        }

        void ApplyRing(Slot slot, bool selected, float blend)
        {
            if (slot.ring == null) return;
            float targetScale = selected ? RingSelectedScale : 1f;
            Color targetColor = selected ? theme.Accent : theme.TextDim;
            float scale = Mathf.Lerp(slot.ring.localScale.x, targetScale, blend);
            slot.ring.localScale = new Vector3(scale, scale, 1f);
            slot.ringImage.color = Color.Lerp(slot.ringImage.color, targetColor, blend);
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            if (theme == null || screen == null || leaving) return;
            InputPromptBinder.Poll();
            float dt = Time.unscaledDeltaTime;
            Animate(dt);
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            if (MenuNavigator.BackPressed())
            {
                Leave();
                return;
            }
            if (slots.Count == 0) return;

            int horizontal = nav.StepHorizontal(dt);
            if (horizontal != 0)
            {
                Select(index + horizontal, animate: true);
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(theme.MoveRumblePulse);
            }

            if (MenuNavigator.ConfirmPressed()) Confirm();
        }

        void Animate(float dt)
        {
            if (popTimer < PopSeconds)
            {
                popTimer += dt;
                ApplyPop(theme.Ease(Mathf.Clamp01(popTimer / PopSeconds)));
            }
            float blend = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, RingEaseSeconds));
            for (int i = 0; i < slots.Count; i++) ApplyRing(slots[i], i == index, blend);

            if (arrowLeft != null && arrowRight != null)
            {
                float pulse = Mathf.Lerp(theme.AttractPulseMin, theme.AttractPulseMax,
                                         0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, theme.AttractPulseSeconds)));
                Color c = theme.Accent;
                c.a = pulse;
                arrowLeft.color = c;
                arrowRight.color = c;
            }
        }

        // ------------------------------------------------------------- leave

        // A / Enter: the pick becomes the runner's track and the runner scene loads.
        void Confirm()
        {
            if (leaving || LoadingScreen.IsLoading) return;
            TrackSelectCatalog.Entry entry = Selected;
            if (entry == null || entry.track == null) return;

            CampaignCatalog catalog = CampaignCatalog.Load();
            string scene = catalog != null && !string.IsNullOrEmpty(catalog.runnerSceneName) ? catalog.runnerSceneName : FallbackRunnerScene;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"{nameof(LevelSelectScreen)}: the {scene} scene is not in the build settings — run Tools → FiniteRunner → Register Campaign Scenes.", this);
                return;
            }

            leaving = true;
            Blip(theme.ConfirmClip);
            HapticsSystem.Instance.Pulse(theme.ConfirmRumble, theme.ConfirmRumble * 0.5f, 0.15f);
            MissionSession.Clear();          // a picked course is direct play, never a mission
            TrackSelection.Set(entry.track);
            if (RunnerMusic.Instance != null) RunnerMusic.Instance.FadeOut();
            LoadingScreen.Load(scene);
        }

        // B / Esc: the main menu, through the loading curtain.
        void Leave()
        {
            if (leaving || LoadingScreen.IsLoading) return;
            leaving = true;
            Blip(theme.BackClip);
            if (RunnerMusic.Instance != null) RunnerMusic.Instance.FadeOut();
            LoadingScreen.LoadMainMenu();
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }

        // ----------------------------------------------------------- helpers

        /// <summary>Every k-th point of <paramref name="points"/> so that about <paramref name="count"/> remain, the last one always kept.</summary>
        static List<Vector2> Thin(List<Vector2> points, int count)
        {
            var thinned = new List<Vector2>();
            if (points.Count <= count) { thinned.AddRange(points); return thinned; }
            float stride = (points.Count - 1) / (float)(count - 1);
            for (int i = 0; i < count; i++) thinned.Add(points[Mathf.RoundToInt(i * stride)]);
            return thinned;
        }

        // A tile with a line along its left and bottom edges: tiled, it is the plane's grid.
        static Sprite GridSprite()
        {
            if (gridSprite != null && gridSprite.texture != null) return gridSprite;
            int size = GridCell;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x < 2 || y < 2;
                pixels[y * size + x] = new Color32(255, 255, 255, line ? (byte)255 : (byte)0);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            gridSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            gridSprite.hideFlags = HideFlags.DontSave;
            return gridSprite;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
