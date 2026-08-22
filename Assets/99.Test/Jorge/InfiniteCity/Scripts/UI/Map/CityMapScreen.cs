using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using FiniteRunner;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The full-screen city map: Tab (or the gamepad's Back/View button)
    /// freezes the game and shows a schematic of the whole city, with the
    /// mission list down the side.
    ///
    /// Two rules it enforces. First, <b>it draws generated data, never the
    /// streamed world</b> — see <see cref="CityMapModel"/>; the city only
    /// exists as geometry for a few hundred metres around the car, so a camera
    /// render would show a small island in a void. Second, <b>it is the only
    /// input owner while it is open</b>: like every other screen in this
    /// project it polls devices directly (there is no .inputactions asset) and
    /// runs entirely on unscaled time, because the whole point is that scaled
    /// time is stopped.
    ///
    /// Built from code on its own overlay canvas, spawned by CityManager when
    /// its map settings field is wired — the same "spawned when wired" rule as
    /// Minimap and Speedometer. It sorts above the HUDs so its opaque
    /// background covers them without either side needing to know.
    /// </summary>
    public class CityMapScreen : MonoBehaviour
    {
        const int UiLayer = 5;
        // Above the city HUDs (40), the pause menu (20) and the main menu (30):
        // the map is a full takeover of the screen, so nothing should sit on it.
        const int SortingOrder = 60;

        [Required, InlineEditor]
        [Tooltip("All map tunables live on this asset — add new knobs there, not here.")]
        public CityMapSettings settings;

        /// <summary>True while the map is up. PauseMenu checks this so the two screens can never stack.</summary>
        public static bool IsOpen { get; private set; }

        CityManager city;
        CityMapModel model;
        CityMapRenderer renderer;
        MenuTheme theme;
        LevelManager level;

        Canvas canvas;
        GameObject panel;
        RectTransform mapViewport;
        RawImage schematic;
        RectTransform playerIcon;
        RectTransform markerIcon;
        RectTransform cursorIcon;
        Text titleLabel;
        Text statusLabel;
        readonly List<Text> missionRows = new();
        RectTransform missionPanel;

        CarController player;
        float refreshTimer;

        Vector3 viewCenterWorld;      // world point at the middle of the viewport
        float pixelsPerCell;
        bool built;
        bool open;
        float openedTime;

        // ------------------------------------------------------------- spawn

        /// <summary>Create the map for a city. Called by CityManager when its map settings are wired.</summary>
        public static CityMapScreen Spawn(CityManager city, CityMapSettings settings)
        {
            var go = new GameObject("CityMap");
            var map = go.AddComponent<CityMapScreen>();
            map.city = city;
            map.settings = settings;
            return map;
        }

        void OnDestroy()
        {
            renderer?.Release();
            if (!open) return;
            // Never leave the game frozen because the map was destroyed while up.
            Time.timeScale = 1f;
            IsOpen = false;
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            if (settings == null) return;
            if (city == null) city = FindAnyObjectByType<CityManager>();
            if (city == null || city.settings == null) return;
            if (!built) Build();

            if (!open)
            {
                if (MenuNavigator.MapTogglePressed() && CanOpen()) Open();
                return;
            }

            float dt = Time.unscaledDeltaTime;

            // The press that opened the map must not also act inside it.
            if (Time.unscaledTime - openedTime >= theme.InputGrace)
            {
                if (MenuNavigator.MapTogglePressed() || MenuNavigator.BackPressed())
                {
                    Close();
                    return;
                }
                HandleZoom(dt);
                HandlePan(dt);
            }

            RefreshTargets();
            UpdateSchematic();
            UpdateIcons();
            UpdateMissions();
        }

        // Only over live gameplay — never on top of the pause menu or the main
        // menu, where Tab/Back mean other things.
        bool CanOpen()
        {
            if (MainMenuController.IsOpen) return false;
            return Time.timeScale > 0f;
        }

        void Open()
        {
            open = true;
            IsOpen = true;
            openedTime = Time.unscaledTime;
            Time.timeScale = 0f;
            MenuScreenFactory.EnsureEventSystem();
            panel.SetActive(true);
            Gamepad.current?.ResetHaptics();

            pixelsPerCell = settings.ClampZoom(settings.defaultPixelsPerCell);
            RefreshTargets();
            CenterOnPlayer();
        }

        void Close()
        {
            open = false;
            IsOpen = false;
            Time.timeScale = 1f;
            panel.SetActive(false);
        }

        void CenterOnPlayer()
        {
            if (player != null) viewCenterWorld = player.transform.position;
        }

        void RefreshTargets()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f && player != null && level != null) return;
            refreshTimer = 1f;
            player = PatrolManager.FindPlayerCar();
            if (level == null) level = FindFirstObjectByType<LevelManager>();
        }

        // -------------------------------------------------------------- input

        void HandleZoom(float dt)
        {
            float step = 0f;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) step += Mathf.Sign(scroll) * 6f * dt;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                // RT zooms in, LT zooms out — the plan's LT/RT binding.
                step += (pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue()) * dt;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed) step += dt;
                if (keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed) step -= dt;
            }
            if (Mathf.Abs(step) < 0.0001f) return;

            // Multiplicative, so each notch feels the same at every zoom level.
            pixelsPerCell = settings.ClampZoom(pixelsPerCell * Mathf.Pow(settings.zoomSpeed, step));
        }

        void HandlePan(float dt)
        {
            Vector2 axis = ReadPanAxis();
            if (axis.sqrMagnitude < 0.0001f) return;

            // Pan is authored in SCREEN pixels/second, so it feels identical at
            // every zoom; convert to world metres through the current zoom.
            float metresPerPixel = city.settings.cellSize / Mathf.Max(0.01f, pixelsPerCell);
            float distance = settings.panSpeedPixels * metresPerPixel * dt;
            viewCenterWorld += new Vector3(axis.x, 0f, axis.y) * distance;
        }

        Vector2 ReadPanAxis()
        {
            var axis = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) axis.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) axis.x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) axis.y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) axis.y += 1f;
            }
            if (axis.sqrMagnitude > 1f) axis = axis.normalized;

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.magnitude > settings.stickDeadZone) axis += stick;
            }
            return Vector2.ClampMagnitude(axis, 1f);
        }

        // ---------------------------------------------------------- schematic

        void UpdateSchematic()
        {
            RectInt cellWindow = VisibleCellWindow();
            RectInt chunkWindow = ChunkWindowFor(cellWindow);

            model.EnsureArea(chunkWindow, settings.chunkMargin);
            bool grew = model.Pump(settings.chunksPerFrame, settings.chunkCacheSize);

            if (grew || renderer.NeedsRepaint(cellWindow))
                renderer.Paint(model, cellWindow, MapRoute.Current, MarkerCell());

            schematic.texture = renderer.Texture;

            // The RawImage is exactly the painted window, scaled by zoom, and
            // offset so the view centre lands in the middle of the viewport.
            RectInt painted = renderer.Window;
            schematic.rectTransform.sizeDelta =
                new Vector2(painted.width * pixelsPerCell, painted.height * pixelsPerCell);

            Vector2 centreCell = WorldToCellFloat(viewCenterWorld);
            float offsetX = (painted.xMin + painted.width * 0.5f - centreCell.x) * pixelsPerCell;
            float offsetY = (painted.yMin + painted.height * 0.5f - centreCell.y) * pixelsPerCell;
            schematic.rectTransform.anchoredPosition = new Vector2(offsetX, offsetY);
        }

        /// <summary>Cell window the viewport can currently see, padded by a cell so edges are never blank.</summary>
        RectInt VisibleCellWindow()
        {
            Vector2 size = mapViewport.rect.size;
            float cellsWide = size.x / Mathf.Max(0.01f, pixelsPerCell);
            float cellsHigh = size.y / Mathf.Max(0.01f, pixelsPerCell);

            Vector2 centre = WorldToCellFloat(viewCenterWorld);
            int minX = Mathf.FloorToInt(centre.x - cellsWide * 0.5f) - 1;
            int minY = Mathf.FloorToInt(centre.y - cellsHigh * 0.5f) - 1;
            int w = Mathf.CeilToInt(cellsWide) + 3;
            int h = Mathf.CeilToInt(cellsHigh) + 3;
            return new RectInt(minX, minY, Mathf.Max(1, w), Mathf.Max(1, h));
        }

        RectInt ChunkWindowFor(RectInt cellWindow)
        {
            Vector2Int min = model.CellToChunk(new Vector2Int(cellWindow.xMin, cellWindow.yMin));
            Vector2Int max = model.CellToChunk(new Vector2Int(cellWindow.xMax, cellWindow.yMax));
            return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        /// <summary>World to cell space without flooring — the map needs sub-cell precision to place icons.</summary>
        Vector2 WorldToCellFloat(Vector3 world)
        {
            Vector3 origin = city.transform.position;
            float cell = city.settings.cellSize;
            return new Vector2((world.x - origin.x) / cell, (world.z - origin.z) / cell);
        }

        /// <summary>Viewport-local UI position for a world point, at the current zoom.</summary>
        Vector2 WorldToViewport(Vector3 world)
        {
            Vector2 cell = WorldToCellFloat(world);
            Vector2 centre = WorldToCellFloat(viewCenterWorld);
            return (cell - centre) * pixelsPerCell;
        }

        Vector2Int? MarkerCell() => null;   // M3 supplies this

        // -------------------------------------------------------------- icons

        void UpdateIcons()
        {
            bool hasPlayer = player != null;
            playerIcon.gameObject.SetActive(hasPlayer);
            if (hasPlayer)
            {
                playerIcon.anchoredPosition = WorldToViewport(player.transform.position);
                // Map is north-up, so the arrow carries the heading. UI Z spins
                // counter-clockwise while world yaw spins clockwise — hence the sign.
                playerIcon.localEulerAngles = new Vector3(0f, 0f, -player.transform.eulerAngles.y);
            }

            markerIcon.gameObject.SetActive(false);   // M3
            cursorIcon.gameObject.SetActive(false);   // M3

            int pending = model.PendingCount;
            statusLabel.text = pending > 0
                ? $"MAPPING… {pending}"
                : hasPlayer ? string.Empty : "NO VEHICLE";
        }

        // ----------------------------------------------------------- missions

        void UpdateMissions()
        {
            LevelDefinition definition = level != null ? level.Level : null;
            int count = definition != null ? definition.Count : 0;

            for (int i = 0; i < missionRows.Count; i++)
                missionRows[i].gameObject.SetActive(i < count);

            for (int i = 0; i < count; i++)
            {
                Text row = GetMissionRow(i);
                LevelObjective objective = definition.objectives[i];

                bool done = level.Completed || level.IsDone(i);
                bool active = !done && level.CurrentIndex == i;

                // Same three-state rule the debug menu's LEVEL page uses:
                // finished, the one you are on, and everything after it — which
                // is what "greyed out, cannot do yet" means.
                string prefix = done ? "[x] " : active ? "> " : "-  ";
                row.text = prefix + objective.Summary;
                row.color = done ? settings.missionDoneColor
                    : active ? settings.missionActiveColor
                    : settings.missionLockedColor;
            }
        }

        Text GetMissionRow(int index)
        {
            while (missionRows.Count <= index)
            {
                var row = CreateText($"Mission_{missionRows.Count}", missionPanel, 26,
                    TextAnchor.MiddleLeft, settings.missionLockedColor);
                var rect = row.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.offsetMin = new Vector2(18f, 0f);
                rect.offsetMax = new Vector2(-18f, 0f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, 40f);
                rect.anchoredPosition = new Vector2(18f, -70f - missionRows.Count * 44f);
                missionRows.Add(row);
            }
            return missionRows[index];
        }

        // -------------------------------------------------------------- build

        void Build()
        {
            built = true;
            theme = MenuTheme.Load();
            model = new CityMapModel(city.settings, city.transform.position, city.DeckWorldHeight);
            renderer = new CityMapRenderer(settings);
            pixelsPerCell = settings.ClampZoom(settings.defaultPixelsPerCell);

            canvas = gameObject.AddComponent<Canvas>();
            gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(transform, false);
            panel.layer = UiLayer;
            Stretch(panelRect);
            var backdrop = panel.AddComponent<Image>();
            backdrop.color = settings.backgroundColor;

            BuildMapViewport(panelRect);
            BuildMissionPanel(panelRect);
            BuildChrome(panelRect);

            panel.SetActive(false);
        }

        void BuildMapViewport(RectTransform parent)
        {
            var viewportGo = new GameObject("MapViewport", typeof(RectTransform));
            mapViewport = (RectTransform)viewportGo.transform;
            mapViewport.SetParent(parent, false);
            viewportGo.layer = UiLayer;
            Stretch(mapViewport);
            mapViewport.offsetMin = new Vector2(settings.missionPanelWidth, 70f);
            mapViewport.offsetMax = new Vector2(-40f, -110f);

            // Masked so the schematic cannot spill over the mission list.
            var mask = viewportGo.AddComponent<Image>();
            mask.color = settings.backgroundColor;
            viewportGo.AddComponent<RectMask2D>();

            schematic = new GameObject("Schematic").AddComponent<RawImage>();
            schematic.gameObject.layer = UiLayer;
            schematic.transform.SetParent(mapViewport, false);
            schematic.raycastTarget = false;
            var schematicRect = schematic.rectTransform;
            schematicRect.anchorMin = schematicRect.anchorMax = schematicRect.pivot = new Vector2(0.5f, 0.5f);

            playerIcon = CreateIconRect("PlayerIcon", mapViewport, settings.playerColor,
                settings.playerIconSize, CreateArrowSprite(64));
            markerIcon = CreateIconRect("MarkerIcon", mapViewport, settings.markerColor,
                settings.markerIconSize, CreateDiamondSprite(64));
            cursorIcon = CreateIconRect("Cursor", mapViewport, settings.cursorColor,
                settings.markerIconSize, CreateCrosshairSprite(64));
        }

        void BuildMissionPanel(RectTransform parent)
        {
            var panelGo = new GameObject("Missions", typeof(RectTransform));
            missionPanel = (RectTransform)panelGo.transform;
            missionPanel.SetParent(parent, false);
            panelGo.layer = UiLayer;
            missionPanel.anchorMin = new Vector2(0f, 0f);
            missionPanel.anchorMax = new Vector2(0f, 1f);
            missionPanel.pivot = new Vector2(0f, 0.5f);
            missionPanel.offsetMin = new Vector2(0f, 70f);
            missionPanel.offsetMax = new Vector2(0f, -110f);
            missionPanel.sizeDelta = new Vector2(settings.missionPanelWidth, missionPanel.sizeDelta.y);

            var plate = panelGo.AddComponent<Image>();
            Color plateColor = settings.blockColor;
            plateColor.a = 0.55f;
            plate.color = plateColor;

            Text header = CreateText("MissionsHeader", missionPanel, 30, TextAnchor.MiddleLeft, theme.TextDim);
            header.text = "OBJECTIVES";
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 40f);
            headerRect.anchoredPosition = new Vector2(18f, -20f);
        }

        void BuildChrome(RectTransform parent)
        {
            titleLabel = CreateText("Title", parent, 44, TextAnchor.UpperLeft, theme.TextPrimary);
            titleLabel.text = "CITY MAP";
            var titleRect = titleLabel.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(600f, 60f);
            titleRect.anchoredPosition = new Vector2(40f, -30f);

            statusLabel = CreateText("Status", parent, 24, TextAnchor.UpperRight, theme.TextDim);
            var statusRect = statusLabel.rectTransform;
            statusRect.anchorMin = new Vector2(1f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(1f, 1f);
            statusRect.sizeDelta = new Vector2(500f, 40f);
            statusRect.anchoredPosition = new Vector2(-40f, -40f);

            Text hints = CreateText("Hints", parent, 22, TextAnchor.MiddleLeft, theme.TextDim);
            hints.text = "WASD / STICK  PAN     +/- / LT-RT  ZOOM     TAB / BACK  CLOSE";
            var hintRect = hints.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0f, 0f);
            hintRect.offsetMin = new Vector2(40f, 20f);
            hintRect.offsetMax = new Vector2(-40f, 20f);
            hintRect.sizeDelta = new Vector2(hintRect.sizeDelta.x, 34f);
        }

        // ------------------------------------------------------- UI helpers

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        RectTransform CreateIconRect(string name, Transform parent, Color color, float size, Sprite sprite)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.gameObject.layer = UiLayer;
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor, Color color)
        {
            var text = new GameObject(name).AddComponent<Text>();
            text.gameObject.layer = UiLayer;
            text.transform.SetParent(parent, false);
            text.font = theme.BodyFont;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        // ------------------------------------------------------------ sprites

        /// <summary>White triangle pointing up — the player arrow. Same trick Minimap uses.</summary>
        static Sprite CreateArrowSprite(int size)
        {
            var pixels = new Color32[size * size];
            float centerX = size * 0.5f - 0.5f;
            for (int y = 0; y < size; y++)
            {
                float y01 = y / (size - 1f);
                float halfWidth = (1f - y01) * size * 0.42f;
                for (int x = 0; x < size; x++)
                {
                    byte alpha = (byte)(255f * Mathf.Clamp01(halfWidth - Mathf.Abs(x - centerX) + 0.5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            return BuildSprite(pixels, size);
        }

        static Sprite CreateDiamondSprite(int size)
        {
            var pixels = new Color32[size * size];
            float c = size * 0.5f - 0.5f;
            float radius = size * 0.46f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float manhattan = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                byte alpha = (byte)(255f * Mathf.Clamp01(radius - manhattan + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            return BuildSprite(pixels, size);
        }

        static Sprite CreateCrosshairSprite(int size)
        {
            var pixels = new Color32[size * size];
            float c = size * 0.5f - 0.5f;
            float arm = size * 0.45f;
            float thickness = Mathf.Max(1.2f, size * 0.05f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - c);
                float dy = Mathf.Abs(y - c);
                bool horizontal = dy <= thickness && dx <= arm;
                bool vertical = dx <= thickness && dy <= arm;
                bool hole = dx < arm * 0.28f && dy < arm * 0.28f;
                byte alpha = (byte)((horizontal || vertical) && !hole ? 255 : 0);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            return BuildSprite(pixels, size);
        }

        static Sprite BuildSprite(Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
