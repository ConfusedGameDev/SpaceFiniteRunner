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
        [Tooltip("Cells swallowed by a road feature (bridge shadow, roundabout island) — neither road nor lot.")]
        public Color reservedColor = new(0.20f, 0.22f, 0.27f, 1f);

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
        [Tooltip("The centre crosshair used to aim when placing a marker.")]
        public Color cursorColor = new(1f, 1f, 1f, 0.85f);

        [TitleGroup("Markers")]
        [PropertyRange(8f, 64f), SuffixLabel("px", true)]
        public float playerIconSize = 26f;

        [TitleGroup("Markers")]
        [PropertyRange(8f, 64f), SuffixLabel("px", true)]
        public float markerIconSize = 26f;

        // ---------------------------------------------------------------- zoom
        [TitleGroup("Zoom")]
        [Tooltip("Screen pixels per city cell when the map is opened.")]
        [PropertyRange(1f, 40f), SuffixLabel("px/cell", true)]
        public float defaultPixelsPerCell = 8f;

        [TitleGroup("Zoom")]
        [Tooltip("Most zoomed OUT — smaller means more city on screen.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("px/cell", true)]
        public float minPixelsPerCell = 2f;

        [TitleGroup("Zoom")]
        [Tooltip("Most zoomed IN.")]
        [PropertyRange(2f, 80f), SuffixLabel("px/cell", true)]
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
        [Tooltip("How many chunks may be generated per frame. Generation is pure data (no GameObjects) but it is not free — this bounds the hitch.")]
        [PropertyRange(1, 32)]
        public int chunksPerFrame = 6;

        [TitleGroup("Schematic")]
        [Tooltip("How many generated chunks stay cached before the least recently used ones are dropped.")]
        [PropertyRange(16, 1024)]
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
