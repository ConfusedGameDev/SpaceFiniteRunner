using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// Every look/feel knob of the circular radar in one designer-facing
    /// asset: screen layout, world view, and blip styling. The Minimap draws
    /// it inline so the radar is tuned live in play mode, same as every
    /// other settings asset in the project.
    /// </summary>
    [CreateAssetMenu(fileName = "MinimapSettings", menuName = "PoliceEscape/Minimap Settings")]
    public class MinimapSettings : ScriptableObject
    {
        // -------------------------------------------------------------- layout
        [TitleGroup("Layout")]
        [Tooltip("Diameter of the radar circle, in reference-resolution (1920×1080) pixels.")]
        [PropertyRange(100f, 600f), SuffixLabel("px", true)]
        public float sizePixels = 280f;

        [TitleGroup("Layout")]
        [Tooltip("Gap between the radar and the bottom-right screen corner.")]
        [PropertyRange(0f, 100f), SuffixLabel("px", true)]
        public float marginPixels = 24f;

        [TitleGroup("Layout")]
        [Tooltip("Width of the ring drawn around the radar.")]
        [PropertyRange(0f, 20f), SuffixLabel("px", true)]
        public float borderWidth = 6f;

        [TitleGroup("Layout")]
        [Tooltip("Color of the ring around the radar.")]
        public Color borderColor = new(0.05f, 0.05f, 0.08f, 0.9f);

        // ---------------------------------------------------------------- view
        [TitleGroup("View")]
        [Tooltip("World radius shown by the radar — how many meters from the player fit inside the circle.")]
        [PropertyRange(30f, 500f), SuffixLabel("m", true)]
        public float viewRadius = 120f;

        [TitleGroup("View")]
        [Tooltip("GTA-style rotation: the map turns with the player so 'up' is always ahead. Off = north-up map with a rotating player arrow.")]
        public bool rotateWithPlayer = true;

        [TitleGroup("View")]
        [Tooltip("Height of the top-down radar camera above the player — keep above the tallest building so nothing clips.")]
        [PropertyRange(50f, 500f), SuffixLabel("m", true)]
        public float cameraHeight = 180f;

        [TitleGroup("View")]
        [Tooltip("Render texture resolution — the radar is small on screen, so 512 is plenty.")]
        [PropertyRange(128, 1024)]
        public int renderTextureSize = 512;

        [TitleGroup("View")]
        [Tooltip("Radar background where nothing is rendered (beyond the city).")]
        public Color backgroundColor = new(0.06f, 0.07f, 0.08f, 1f);

        // --------------------------------------------------------------- route
        [TitleGroup("Route")]
        [Tooltip("Colour of the map route drawn on the radar. The route is set on the full-screen map and followed while driving.")]
        public Color routeColor = new(0.35f, 1f, 0.6f, 0.95f);

        [TitleGroup("Route")]
        [Tooltip("Size of each route dot on the radar.")]
        [PropertyRange(2f, 20f), SuffixLabel("px", true)]
        public float routeDotSize = 7f;

        [TitleGroup("Route")]
        [Tooltip("Spacing between route dots along the path, in meters. Lower = a more solid line, at the cost of more dots.")]
        [PropertyRange(2f, 60f), SuffixLabel("m", true)]
        public float routeDotSpacing = 12f;

        [TitleGroup("Route")]
        [Tooltip("Colour of the destination pin at the end of the route. It clears itself the moment the car arrives, because the map clears route and marker together.")]
        public Color destinationColor = new(1f, 0.78f, 0.25f, 1f);

        [TitleGroup("Route")]
        [Tooltip("Size of the destination pin. Bigger than a route dot — it is the thing you are steering at, and it sits on the rim while it is out of range.")]
        [PropertyRange(4f, 32f), SuffixLabel("px", true)]
        public float destinationSize = 12f;

        [TitleGroup("Route")]
        [Tooltip("Most route dots drawn at once — the cap that keeps a cross-city route from flooding the radar.")]
        [PropertyRange(10, 400)]
        public int routeMaxDots = 160;

        // --------------------------------------------------------------- blips
        [TitleGroup("Blips")]
        [Tooltip("Player arrow color.")]
        public Color playerColor = Color.white;

        [TitleGroup("Blips")]
        [Tooltip("Player arrow length in radar pixels.")]
        [PropertyRange(10f, 60f), SuffixLabel("px", true)]
        public float playerArrowSize = 26f;

        [TitleGroup("Blips")]
        [Tooltip("Police blip diameter in radar pixels.")]
        [PropertyRange(6f, 40f), SuffixLabel("px", true)]
        public float blipSize = 14f;

        [TitleGroup("Blips")]
        [Tooltip("Blip color for cruisers wandering on Patrol.")]
        public Color patrolColor = new(0.3f, 0.55f, 1f, 1f);

        [TitleGroup("Blips")]
        [Tooltip("Blip color while a cruiser is Searching your last known position.")]
        public Color searchColor = new(1f, 0.8f, 0.2f, 1f);

        [TitleGroup("Blips")]
        [Tooltip("Chase blips flash between these two colors, GTA-style.")]
        public Color chaseColorA = new(1f, 0.15f, 0.15f, 1f);

        [TitleGroup("Blips")]
        public Color chaseColorB = new(0.2f, 0.4f, 1f, 1f);

        [TitleGroup("Blips")]
        [Tooltip("Seconds per half-cycle of the chase blip flash.")]
        [PropertyRange(0.05f, 1f), SuffixLabel("s", true)]
        public float chaseFlashInterval = 0.25f;

        [TitleGroup("Blips")]
        [Tooltip("Blip color of the escaping car of a Chase Car objective — yellow, so the prey never reads as a cop.")]
        public Color escapeColor = new(1f, 0.9f, 0.2f, 1f);
    }
}
