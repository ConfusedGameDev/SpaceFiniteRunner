using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The full-screen city map: M (or the gamepad's d-pad Up)
    /// freezes the game and shows a schematic of the whole city, with the
    /// mission list down the side.
    ///
    /// Two rules it enforces. First, <b>it draws the baked city's data, never
    /// a camera render</b> — see <see cref="CityMapModel"/>, a thin adapter
    /// over the CityRoot's serialized block layouts. The city is a fixed grid
    /// now, and that frames the whole view: opening the map shows the block
    /// the player is in, zooming out stops exactly where the full city fits,
    /// and panning cannot leave the city. Second, <b>it is the only
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
        readonly List<RectTransform> chaseIcons = new();
        readonly List<TrafficCarInput> escapees = new();
        Text titleLabel;
        Text statusLabel;
        readonly List<Text> missionRows = new();
        RectTransform missionPanel;

        CarController player;
        float refreshTimer;

        Vector3 viewCenterWorld;      // world point at the middle of the viewport
        float pixelsPerCell;
        int modelSeed;                // city the cached chunk data belongs to

        // Routing. Crossing the whole baked city is fine — this only bounds a
        // degenerate search (start and goal on disconnected islands).
        const int RouteMaxExpansions = 200_000;
        // How long the status line keeps saying ARRIVED after the marker is
        // cleared. Long enough to still be there when the player opens the map
        // to ask what happened to their pin.
        const float ArrivedMessageSeconds = 6f;

        readonly MapRoute route = new();
        readonly List<RoadNode> pathBuffer = new();
        readonly List<Vector2Int> routeCells = new();
        readonly List<Vector3> routePoints = new();
        bool routeFailed;
        bool routeDirty;
        Vector2Int lastRouteCell;
        float offRouteTimer;          // how long the car has been off the line
        float recalcTimer;            // cooldown before another path may be built
        float arrivedTimer;           // how long "ARRIVED" stays on the status line
        bool routeStale;              // the marker moved — the path no longer leads to it
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

        void OnEnable() => MapMarkerStore.Changed += OnMarkerChanged;

        void OnDisable() => MapMarkerStore.Changed -= OnMarkerChanged;

        /// <summary>
        /// A marker that moved invalidates the path to it. Going through the
        /// store's own event rather than the place that moved it means every
        /// route the game ever builds is triggered from one spot, whoever set
        /// the marker.
        /// </summary>
        void OnMarkerChanged() => routeStale = true;

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
            // The baked CityRoot is the only city data the map needs — the
            // manager's generation settings asset is not read here anymore.
            if (city == null || city.Root == null) return;
            if (!built) Build();
            // Build is one-shot; if anything in it failed the screen has no
            // model to draw and must stay out of the way rather than throw
            // every frame.
            if (model == null || renderer == null) return;

            if (!open)
            {
                // The GPS keeps working with the map shut — that is when it is
                // actually used. Everything else on this screen sleeps.
                RefreshTargets();
                TickGuidance();
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
                HandleMarker();
            }

            RefreshTargets();
            TickGuidance();
            UpdateSchematic();
            UpdateIcons();
            UpdateMissions();
        }

        // Only over live gameplay — never on top of the pause menu or the main
        // menu, where the d-pad is the row navigator.
        bool CanOpen()
        {
            if (MainMenuController.IsOpen) return false;
            return Time.timeScale > 0f;
        }

        void Open()
        {
            if (!built || model == null) return;   // nothing to show yet
            open = true;
            IsOpen = true;
            openedTime = Time.unscaledTime;
            Time.timeScale = 0f;
            MenuScreenFactory.EnsureEventSystem();
            panel.SetActive(true);
            Gamepad.current?.ResetHaptics();

            // Default view: the block the player is standing in, filling the
            // viewport — zooming out from there goes all the way to the whole
            // baked city (see ClampZoomToCity).
            pixelsPerCell = ClampZoomToCity(BlockFitZoom());
            // The city can be regenerated under us ("Clear & Generate New
            // City"), and cached chunk data is only valid for the seed it was
            // generated from — so both the schematic and the marker are
            // rebuilt against whatever city is actually in force now.
            SyncToCity();
            RefreshTargets();
            CenterOnPlayer();
            ClampViewCenter();
        }

        void Close()
        {
            open = false;
            IsOpen = false;
            Time.timeScale = 1f;
            panel.SetActive(false);
        }

        /// <summary>
        /// Throw away anything generated for a different city. Chunk data is a
        /// pure function of the seed, so a stale cache would draw streets that
        /// no longer exist — and a marker placed in the old city would point at
        /// one of them.
        /// </summary>
        void SyncToCity()
        {
            int seed = city.Root != null ? city.Root.citySeed : 0;
            if (seed != modelSeed)
            {
                model.Clear();
                renderer.Release();
                ClearRoute();
                modelSeed = seed;
            }
            MapMarkerStore.DiscardIfForeign(seed);
        }

        void CenterOnPlayer()
        {
            viewCenterWorld = player != null ? player.transform.position : model.CityWorldCenter;
        }

        /// <summary>
        /// Re-find the car (and, once, the level) on a one-second tick. Strictly
        /// timer-bound: this now runs during gameplay for the guidance tick, and
        /// a scene with no LevelManager must not turn that into an object sweep
        /// every frame.
        /// </summary>
        void RefreshTargets()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f && player != null) return;
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
            pixelsPerCell = ClampZoomToCity(pixelsPerCell * Mathf.Pow(settings.zoomSpeed, step));
        }

        /// <summary>
        /// Zoom limits come from the BAKED CITY, not the settings alone. The
        /// city is a fixed grid now (no streaming, no chunk cache), so the max
        /// zoom-out is exactly "the whole city fits in the viewport" and the
        /// max zoom-in is the authored slider — raised to block-fit if needed,
        /// so the default current-block view is always inside the range.
        /// Falls back to the authored clamp while the viewport has no size yet.
        /// </summary>
        float ClampZoomToCity(float value)
        {
            float min = CityFitZoom();
            if (min <= 0f) return settings.ClampZoom(value);
            float max = Mathf.Max(settings.maxPixelsPerCell, BlockFitZoom());
            return Mathf.Clamp(value, Mathf.Min(min, max), max);
        }

        /// <summary>Zoom at which the whole baked city just fits the viewport — the map's max zoom-out.</summary>
        float CityFitZoom()
        {
            if (model == null || mapViewport == null) return 0f;
            Vector2 size = mapViewport.rect.size;
            RectInt bounds = model.CityCellBounds;
            if (size.x <= 0f || size.y <= 0f || bounds.width <= 0 || bounds.height <= 0) return 0f;
            return Mathf.Min(size.x / bounds.width, size.y / bounds.height);
        }

        /// <summary>Zoom at which one city block fills the viewport's short side — the default view.</summary>
        float BlockFitZoom()
        {
            if (model == null || mapViewport == null) return settings.defaultPixelsPerCell;
            Vector2 size = mapViewport.rect.size;
            if (size.x <= 0f || size.y <= 0f) return settings.defaultPixelsPerCell;
            // A touch under a full fill, so the neighbouring streets peek in
            // and the block reads as part of a city rather than an island.
            return Mathf.Min(size.x, size.y) * 0.9f / Mathf.Max(1, model.ChunkSizeInCells);
        }

        void HandlePan(float dt)
        {
            Vector2 axis = ReadPanAxis();
            if (axis.sqrMagnitude < 0.0001f) return;

            // Pan is authored in SCREEN pixels/second, so it feels identical at
            // every zoom; convert to world metres through the current zoom.
            float metresPerPixel = model.CellSize / Mathf.Max(0.01f, pixelsPerCell);
            float distance = settings.panSpeedPixels * metresPerPixel * dt;
            viewCenterWorld += new Vector3(axis.x, 0f, axis.y) * distance;
        }

        /// <summary>
        /// Keep the view centre inside the baked city — but only just: the
        /// centre is also the aiming cursor, so it must be able to reach EVERY
        /// cell, city corners included. Clamping to the city rect itself (not
        /// shrunk by the visible extent) means the map can show some void past
        /// the edge, and that is the correct trade — a frame that can never
        /// show void is a cursor that can never touch the border streets.
        /// </summary>
        void ClampViewCenter()
        {
            if (model == null) return;
            RectInt bounds = model.CityCellBounds;
            float cell = model.CellSize;
            Vector3 centre = model.CityWorldCenter;
            float extentX = bounds.width * cell * 0.5f;
            float extentZ = bounds.height * cell * 0.5f;

            viewCenterWorld = new Vector3(
                Mathf.Clamp(viewCenterWorld.x, centre.x - extentX, centre.x + extentX),
                0f,
                Mathf.Clamp(viewCenterWorld.z, centre.z - extentZ, centre.z + extentZ));
        }

        // ------------------------------------------------------------- marker

        void HandleMarker()
        {
            if (MenuNavigator.ConfirmPressed())
            {
                PlaceMarkerAtCursor();
                return;
            }
            if (DeleteMarkerPressed() && MapMarkerStore.HasMarker)
            {
                MapMarkerStore.ClearMarker();
                ClearRoute();
            }
        }

        /// <summary>X / gamepad West removes the marker. Not B/East — that is Back everywhere in this UI.</summary>
        static bool DeleteMarkerPressed() =>
            Keyboard.current is { deleteKey: { wasPressedThisFrame: true } } ||
            Keyboard.current is { xKey: { wasPressedThisFrame: true } } ||
            Gamepad.current is { buttonWest: { wasPressedThisFrame: true } };

        /// <summary>
        /// Drop the marker on whatever the centre crosshair is over, snapped to
        /// the nearest road. Snapping is the point: a marker in the middle of a
        /// city block has no route to it, so aiming roughly at a district still
        /// gives a marker you can actually drive to.
        /// </summary>
        void PlaceMarkerAtCursor()
        {
            Vector3 target = viewCenterWorld;
            if (TrySnapToRoad(target, out Vector2Int snapped))
            {
                // No RebuildRoute here: setting the marker raises Changed, and
                // the guidance tick later this frame builds the path from it.
                MapMarkerStore.SetMarker(snapped, city.Root != null ? city.Root.citySeed : 0);
            }
            else
            {
                statusLabel.text = "NO ROAD HERE";
            }
        }

        /// <summary>
        /// Nearest road cell to a world point, searched outward through the
        /// map model's generated data. Rings out from the aimed cell rather
        /// than scanning the whole graph, which would be an O(n) sweep over
        /// every chunk the player has ever looked at.
        /// </summary>
        bool TrySnapToRoad(Vector3 world, out Vector2Int roadCell)
        {
            Vector2Int centre = model.WorldToCell(world);
            roadCell = centre;
            if (model.TryGetCell(centre, out _, out bool isRoad) && isRoad) return true;

            const int maxRadius = 12;
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    // Only the ring itself — the inside was covered by smaller r.
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var candidate = new Vector2Int(centre.x + dx, centre.y + dy);
                    if (!model.TryGetCell(candidate, out _, out bool road) || !road) continue;
                    roadCell = candidate;
                    return true;
                }
            }
            return false;
        }

        string MarkerStatusText()
        {
            if (player == null) return string.Empty;
            Vector3 markerWorld = model.CellToWorld(MapMarkerStore.Cell);
            Vector3 delta = markerWorld - player.transform.position;
            float straight = new Vector2(delta.x, delta.z).magnitude;
            return route.HasRoute
                ? $"MARKER  {straight:0} M      ROUTE  {route.RemainingMeters:0} M"
                : $"MARKER  {straight:0} M";
        }

        /// <summary>
        /// The guidance tick — the one part of this screen that also runs
        /// while the map is closed, because following a route is something you
        /// do with the map shut.
        ///
        /// Three jobs, in this order. <b>Arrive</b>: the marker exists to be
        /// driven to, so reaching it clears both the marker and the route
        /// rather than leaving a spent destination on every screen.
        /// <b>Consume</b>: <see cref="MapRoute.Advance"/> drops the part
        /// already driven, so the schematic and the radar only ever show what
        /// is left. <b>Recover</b>: a car that has been off the line for longer
        /// than the grace period gets a new path from where it actually is.
        ///
        /// Re-pathing is never done per frame or per cell: each attempt has to
        /// generate the corridor of city between the car and the marker, which
        /// is far too heavy to spend on a frame that is only going to reach the
        /// same answer. The cooldown is what bounds that.
        /// </summary>
        void TickGuidance()
        {
            float dt = Time.unscaledDeltaTime;
            if (arrivedTimer > 0f) arrivedTimer -= dt;

            if (!MapMarkerStore.HasMarker)
            {
                if (route.HasRoute) ClearRoute();
                routeStale = false;
                return;
            }
            if (player == null) return;

            if (recalcTimer > 0f) recalcTimer -= dt;

            Vector3 position = player.transform.position;
            if (routeStale)
            {
                // A brand-new marker gets its path immediately — the cooldown
                // exists to bound automatic re-pathing, not to make the player
                // wait for the route they just asked for.
                routeStale = false;
                RebuildRoute();
                return;
            }

            if (FlatDistance(position, model.CellToWorld(MapMarkerStore.Cell)) <= settings.arrivalRadius)
            {
                Arrive();
                return;
            }

            if (!route.HasRoute)
            {
                // No route yet, or the last attempt failed (marker out of
                // corridor range). Retry on the cooldown, and only once the car
                // has moved somewhere new when the last try genuinely failed.
                Vector2Int cell = model.WorldToCell(position);
                if (recalcTimer <= 0f && (!routeFailed || cell != lastRouteCell)) RebuildRoute();
                return;
            }

            if (route.Advance(position)) routeDirty = true;

            if (route.OffRouteMeters <= settings.offRouteDistance)
            {
                offRouteTimer = 0f;
                return;
            }

            // Off the line — but not on the first frame of it: swerving round
            // a prop, cutting a corner or clipping the pavement all read as off
            // route for a moment, and re-pathing on those would thrash.
            offRouteTimer += dt;
            if (offRouteTimer < settings.offRouteGrace || recalcTimer > 0f) return;
            RebuildRoute();
        }

        /// <summary>
        /// Destination reached. The marker goes with the route: it was the
        /// request, and the request has been served — leaving it would put a
        /// dead pin on the map and a line the car is already standing on.
        /// </summary>
        void Arrive()
        {
            ClearRoute();
            arrivedTimer = ArrivedMessageSeconds;
            MapMarkerStore.ClearMarker();   // fires Changed, which TickGuidance consumes
            routeStale = false;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // -------------------------------------------------------------- route

        void ClearRoute()
        {
            route.Clear();
            routeFailed = false;
            routeDirty = true;
            offRouteTimer = 0f;
        }

        /// <summary>
        /// Path from the car to the marker over the map's own graph.
        ///
        /// It cannot use <see cref="CityManager.Graph"/>: that only holds
        /// streamed chunks, so it does not even contain the marker's end of the
        /// city. Instead the corridor between the two is generated first (the
        /// chunk bounding box, inflated by a chunk so the route can bend around
        /// obstacles), then A* runs over <see cref="CityMapModel.Graph"/>.
        /// </summary>
        void RebuildRoute()
        {
            routeFailed = false;
            offRouteTimer = 0f;
            recalcTimer = settings.recalcCooldown;
            if (!MapMarkerStore.HasMarker || player == null)
            {
                route.Clear();
                return;
            }

            Vector3 from = player.transform.position;
            lastRouteCell = model.WorldToCell(from);
            Vector3 to = model.CellToWorld(MapMarkerStore.Cell);

            // No corridor generation any more: the baked city's graph covers
            // everything from the first frame, so A* can always reach the
            // marker — the only "too far" left is the expansion budget.
            RoadGraph graph = model.Graph;
            if (!TryGetNodeOn(graph, from, out RoadNode start) ||
                !TryGetNodeOn(graph, to, out RoadNode goal) ||
                !graph.TryFindPath(start, goal, pathBuffer, RouteMaxExpansions))
            {
                route.Clear();
                routeFailed = true;
                return;
            }

            routeCells.Clear();
            routePoints.Clear();
            foreach (RoadNode node in pathBuffer)
            {
                routeCells.Add(node.Cell);
                routePoints.Add(graph.Center(node));
            }
            route.Set(routeCells, routePoints);
            routeDirty = true;
        }

        /// <summary>The node a world position sits on, falling back to the nearest. Same rule PoliceCarInput uses.</summary>
        static bool TryGetNodeOn(RoadGraph graph, Vector3 position, out RoadNode node) =>
            graph.TryGetNodeAt(position, out node) || graph.TryGetNearestNode(position, out node);

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
            // After pan and zoom have both landed — the zoom changes how much
            // of the city the viewport covers, which changes the clamp band.
            ClampViewCenter();

            RectInt cellWindow = VisibleCellWindow();
            RectInt chunkWindow = ChunkWindowFor(cellWindow);

            model.EnsureArea(chunkWindow, settings.chunkMargin);
            bool grew = model.Pump(settings.chunksPerFrame, settings.chunkCacheSize);

            if (grew || routeDirty || renderer.NeedsRepaint(cellWindow))
            {
                renderer.Paint(model, cellWindow, route, MarkerCell());
                routeDirty = false;
            }

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

        /// <summary>
        /// World to cell space without flooring — the map needs sub-cell
        /// precision to place icons. Delegated to the model: the ONE origin
        /// and cell size are the baked CityRoot's, and deriving them from the
        /// CityManager's transform (whose position is unrelated to the city
        /// prefab's) is exactly the bug that shifted every icon off its street.
        /// </summary>
        Vector2 WorldToCellFloat(Vector3 world) => model.WorldToCellFloat(world);

        /// <summary>Viewport-local UI position for a world point, at the current zoom.</summary>
        Vector2 WorldToViewport(Vector3 world)
        {
            Vector2 cell = WorldToCellFloat(world);
            Vector2 centre = WorldToCellFloat(viewCenterWorld);
            return (cell - centre) * pixelsPerCell;
        }

        static Vector2Int? MarkerCell() =>
            MapMarkerStore.HasMarker ? MapMarkerStore.Cell : null;

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

            bool hasMarker = MapMarkerStore.HasMarker;
            markerIcon.gameObject.SetActive(hasMarker);
            if (hasMarker)
                markerIcon.anchoredPosition = WorldToViewport(model.CellToWorld(MapMarkerStore.Cell));

            // The escaping car(s) of Chase Car objectives — live positions, the
            // one thing on this screen that isn't generated data; the viewport
            // mask clips them once they're outside the drawn city.
            TrafficCarInput.GetEscaping(escapees);
            int chaseUsed = 0;
            foreach (TrafficCarInput escapee in escapees)
            {
                if (escapee == null) continue;
                RectTransform icon = GetChaseIcon(chaseUsed++);
                icon.gameObject.SetActive(true);
                icon.anchoredPosition = WorldToViewport(escapee.transform.position);
            }
            for (int i = chaseUsed; i < chaseIcons.Count; i++) chaseIcons[i].gameObject.SetActive(false);

            // The cursor is pinned at the viewport centre: one aiming mechanism
            // that behaves identically on stick, keyboard and mouse, so the map
            // needs no second focus system.
            cursorIcon.gameObject.SetActive(true);
            cursorIcon.anchoredPosition = Vector2.zero;

            int pending = model.PendingCount;
            if (pending > 0) statusLabel.text = $"MAPPING… {pending}";
            else if (!hasPlayer) statusLabel.text = "NO VEHICLE";
            else if (!hasMarker && arrivedTimer > 0f) statusLabel.text = "ARRIVED";
            else if (routeFailed) statusLabel.text = "NO ROUTE — TOO FAR";
            else if (hasMarker) statusLabel.text = MarkerStatusText();
            else statusLabel.text = string.Empty;
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

        /// <summary>
        /// Lazily-built yellow diamond for an escaping car. Kept under the
        /// cursor in the hierarchy so the aiming crosshair always draws on top.
        /// </summary>
        RectTransform GetChaseIcon(int index)
        {
            while (chaseIcons.Count <= index)
            {
                RectTransform icon = CreateIconRect($"ChaseIcon_{chaseIcons.Count}", mapViewport,
                    settings.chaseCarColor, settings.markerIconSize, CreateDiamondSprite(64));
                icon.SetSiblingIndex(cursorIcon.GetSiblingIndex());
                chaseIcons.Add(icon);
            }
            return chaseIcons[index];
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

        // ------------------------------------------------------------ rebuild

        /// <summary>
        /// Tear the built UI down and build it again from the settings asset
        /// as it stands now. Build() bakes much of the asset into the
        /// hierarchy — panel width, backdrop colours, icon sprites and sizes,
        /// viewport offsets — so slider tweaks on <see cref="CityMapSettings"/>
        /// only show after this runs; the asset's own Rebuild Map button calls
        /// it. If the map was open it comes straight back up, so tuning with
        /// the map on screen is a one-click loop.
        /// </summary>
        public void Rebuild()
        {
            if (!built) return; // nothing built yet — the next Update builds fresh anyway
            bool wasOpen = open;
            if (wasOpen) Close();
            renderer?.Release();
            renderer = null;
            model = null;
            missionRows.Clear();
            chaseIcons.Clear();
            if (panel != null) Destroy(panel);
            panel = null;
            built = false;
            Build();
            if (wasOpen) Open();
        }

        // -------------------------------------------------------------- build

        void Build()
        {
            built = true;
            theme = MenuTheme.Load();
            model = new CityMapModel(city.Root);
            renderer = new CityMapRenderer(settings);
            modelSeed = city.Root != null ? city.Root.citySeed : 0;
            // A stored marker outlives the session, and guidance now runs
            // before the map is ever opened — so a marker from another city
            // has to be dropped here, not left for the first Open().
            MapMarkerStore.DiscardIfForeign(modelSeed);
            pixelsPerCell = settings.ClampZoom(settings.defaultPixelsPerCell);

            canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

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
            hints.text = "WASD / STICK  PAN      +/- / LT-RT  ZOOM      ENTER / A  SET MARKER      X  DELETE      M / D-PAD UP  CLOSE";
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
