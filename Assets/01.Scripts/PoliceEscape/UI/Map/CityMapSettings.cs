using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// Every look/feel knob of the full-screen city map in one designer-facing
    /// asset: palette, zoom range, pan feel, how far the schematic is generated
    /// and how the mission list is tinted. Drawn inline on the component that
    /// uses it, same as MinimapSettings and SpeedometerSettings, so the map is
    /// tuned live while it is open.
    ///
    /// The rule this enforces: <b>no map tunable is hardcoded in the screen</b>.
    /// The map is built entirely from code, which makes it very easy to bury
    /// magic numbers in layout maths — they belong here instead.
    /// </summary>
    [CreateAssetMenu(fileName = "CityMapSettings", menuName = "PoliceEscape/City Map Settings")]
    public class CityMapSettings : ScriptableObject
    {
        // ------------------------------------------------------------- rebuild
        /// <summary>
        /// One-click iteration loop for this asset: it is InlineEditor-ed into
        /// the spawned CityMapScreen, and the screen bakes these knobs into
        /// its built hierarchy — so after dragging any slider this rebuilds
        /// the live map right from where you are tuning. Play mode only: the
        /// screen is spawned at play start by the CityManager.
        /// </summary>
        [Button("Rebuild Map", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        [PropertyOrder(-10)]
        void RebuildMap()
        {
            var map = FindFirstObjectByType<CityMapScreen>();
            if (map == null)
            {
                Debug.LogWarning("CityMapSettings: no CityMapScreen alive — it is spawned at play start by a CityManager with this asset wired.");
                return;
            }
            map.Rebuild();
        }

        // ------------------------------------------------------------- palette
        [TitleGroup("Palette")]
        [Tooltip("Behind everything — also what un-generated chunks read as, so keep it clearly 'nothing here'.")]
        public Color backgroundColor = new(0.06f, 0.07f, 0.09f, 1f);

        [TitleGroup("Palette")]
        [Tooltip("City blocks — the buildable land between roads.")]
        public Color blockColor = new(0.13f, 0.15f, 0.19f, 1f);

        [TitleGroup("Palette")]
        [Tooltip("Ordinary streets (connectors).")]
        public Color roadColor = new(0.45f, 0.48f, 0.54f, 1f);

        [TitleGroup("Palette")]
        [Tooltip("Arterials — the long straights that span chunks. Brighter so the city's skeleton reads at a glance.")]
        public Color arterialColor = new(0.72f, 0.76f, 0.82f, 1f);

        [TitleGroup("Palette")]
        [Tooltip("Cells swallowed by a road feature (bridge shadow, roundabout island) — neither road nor lot. A causeway's under-deck cells keep this, so the bridge reads as a shadow over the sea.")]
        public Color reservedColor = new(0.20f, 0.22f, 0.27f, 1f);

        [TitleGroup("Palette")]
        [Tooltip("Open sea — the water blocks that carve the city into islands.")]
        public Color waterColor = new(0.10f, 0.28f, 0.48f, 1f);

        // ------------------------------------------------------------- markers
        [TitleGroup("Markers")]
        [Tooltip("The player's arrow on the map.")]
        public Color playerColor = new(0.4f, 0.85f, 1f, 1f);

        [TitleGroup("Markers")]
        [Tooltip("The interest-point marker.")]
        public Color markerColor = new(1f, 0.78f, 0.25f, 1f);

        [TitleGroup("Markers")]
        [Tooltip("The generated route, drawn along the streets it follows.")]
        public Color routeColor = new(0.35f, 1f, 0.6f, 1f);

        [TitleGroup("Markers")]
        [Tooltip("The escaping car of a Chase Car objective — same yellow family as the minimap's blip, so the prey reads the same on both.")]
        public Color chaseCarColor = new(1f, 0.9f, 0.2f, 1f);

        [TitleGroup("Markers")]
        [Tooltip("The centre crosshair used to aim when placing a marker.")]
        public Color cursorColor = new(1f, 1f, 1f, 0.85f);

        [TitleGroup("Markers")]
        [PropertyRange(8f, 64f), SuffixLabel("px", true)]
        public float playerIconSize = 26f;

        [TitleGroup("Markers")]
        [PropertyRange(8f, 64f), SuffixLabel("px", true)]
        public float markerIconSize = 26f;

        // ------------------------------------------------------------ guidance
        [TitleGroup("Guidance")]
        [Tooltip("How close the car has to get to the marker to count as arrived. On arrival the route and the marker are both cleared — so this is also 'how near is near enough' for the pin to have done its job. Roughly a cell is a good starting point.")]
        [PropertyRange(5f, 150f), SuffixLabel("m", true)]
        public float arrivalRadius = 35f;

        [TitleGroup("Guidance")]
        [Tooltip("How far off the drawn line the car may stray before the route is rebuilt from where it actually is. Keep it wider than a street: swerving round traffic must not count as leaving the route.")]
        [PropertyRange(10f, 200f), SuffixLabel("m", true)]
        public float offRouteDistance = 45f;

        [TitleGroup("Guidance")]
        [Tooltip("How long the car has to stay off the line before it re-paths. Stops a corner cut or a pavement clip from triggering a recalculation.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float offRouteGrace = 0.75f;

        [TitleGroup("Guidance")]
        [Tooltip("Shortest gap between two route calculations. Every one generates the corridor of city between car and marker, so this is the knob that keeps a lost car from re-pathing every frame.")]
        [PropertyRange(0.25f, 10f), SuffixLabel("s", true)]
        public float recalcCooldown = 2f;

        // ---------------------------------------------------------------- zoom
        [TitleGroup("Zoom")]
        [Tooltip("Screen pixels per city cell when the map is opened.")]
        [PropertyRange(1f, 40f), SuffixLabel("px/cell", true)]
        public float defaultPixelsPerCell = 8f;

        [TitleGroup("Zoom")]
        [Tooltip("Most zoomed OUT — the max zoom-out; smaller means more city on screen. The screen additionally clamps zoom-out so every visible chunk fits in the Schematic group's chunk cache — to actually reach low values here, raise Chunk Cache Size too.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("px/cell", true)]
        public float minPixelsPerCell = 2f;

        [TitleGroup("Zoom")]
        [Tooltip("Most zoomed IN — the max zoom. Applies live, no rebuild needed. Safe to push high: zooming in shrinks the painted window, so big values cost nothing (unlike the zoom-out floor, which grows it).")]
        [PropertyRange(2f, 200f), SuffixLabel("px/cell", true)]
        public float maxPixelsPerCell = 28f;

        [TitleGroup("Zoom")]
        [Tooltip("How fast the triggers/scroll wheel change zoom, as a multiplier per second.")]
        [PropertyRange(1.1f, 8f)]
        public float zoomSpeed = 2.6f;

        // ----------------------------------------------------------------- pan
        [TitleGroup("Pan")]
        [Tooltip("Pan rate in SCREEN pixels per second, so panning feels the same at every zoom level.")]
        [PropertyRange(200f, 3000f), SuffixLabel("px/s", true)]
        public float panSpeedPixels = 900f;

        [TitleGroup("Pan")]
        [Tooltip("Stick deflection below this is ignored while panning.")]
        [PropertyRange(0f, 0.6f)]
        public float stickDeadZone = 0.2f;

        // ----------------------------------------------------------- streaming
        [TitleGroup("Schematic")]
        [Tooltip("Extra chunks generated beyond the visible window, so panning reveals city that is already built.")]
        [PropertyRange(0, 3)]
        public int chunkMargin = 1;

        [TitleGroup("Schematic")]
        [Tooltip("How many chunks may be generated per frame. Generation is pure data (no GameObjects) but it is not free — this bounds the hitch. Raise it together with the cache size, or filling a far-zoom view takes seconds.")]
        [PropertyRange(1, 64)]
        public int chunksPerFrame = 6;

        [TitleGroup("Schematic")]
        [Tooltip("How many generated chunks stay cached before the least recently used ones are dropped. This is also the real max zoom-out: the screen refuses to zoom out past what fits in the cache, because evicting chunks that are still on screen would repaint them as void and thrash. ~13 KB per chunk.")]
        [PropertyRange(16, 4096)]
        public int chunkCacheSize = 256;

        // -------------------------------------------------------------- layout
        [TitleGroup("Layout")]
        [Tooltip("Width of the mission list panel down the left side.")]
        [PropertyRange(200f, 700f), SuffixLabel("px", true)]
        public float missionPanelWidth = 420f;

        [TitleGroup("Layout")]
        [Tooltip("Tint of an objective that is finished.")]
        public Color missionDoneColor = new(0.45f, 1f, 0.55f, 1f);

        [TitleGroup("Layout")]
        [Tooltip("Tint of the objective the player is on right now.")]
        public Color missionActiveColor = new(1f, 0.85f, 0.3f, 1f);

        [TitleGroup("Layout")]
        [Tooltip("Tint of objectives that cannot be attempted yet — greyed out.")]
        public Color missionLockedColor = new(0.45f, 0.47f, 0.52f, 1f);

        /// <summary>Clamp a zoom value into the authored range — min/max are two independent sliders and can be dragged past each other.</summary>
        public float ClampZoom(float pixelsPerCell) =>
            Mathf.Clamp(pixelsPerCell, Mathf.Min(minPixelsPerCell, maxPixelsPerCell), Mathf.Max(minPixelsPerCell, maxPixelsPerCell));
    }
}
