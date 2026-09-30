using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Look of the chase minimap: the top-down TRACK MAP (the whole track's
    /// shape, the ship and the patrol on it, ramps, gates and the finish
    /// marked — TrackAuthoringPRD M6) and the older right-edge strip (the ship
    /// climbing a bar, the patrol hanging under it on the zoomed
    /// <see cref="chaseSpan"/> scale), each switchable. All minimap look tunables live on
    /// this asset — add new knobs here, not on the ChaseMinimap component.
    /// Positions and sizes of the strip and labels are NOT here: they are
    /// the prefab children's own RectTransforms, laid out by hand.
    /// Field defaults mirror the original hardcoded values, so a missing
    /// asset degrades to the classic look.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniteRunner/Chase Minimap Settings")]
    public class ChaseMinimapSettings : ScriptableObject
    {
        [TitleGroup("Colors")]
        [Tooltip("Tints the ship icon — and its sprite, when one is assigned (white keeps the sprite's own colours).")]
        public Color shipColor = new(0.48f, 1f, 0.4f);
        [Tooltip("Off: the police icon holds one steady colour.")]
        public bool policeBlink = true;
        [LabelText("$PoliceColorLabel")]
        [Tooltip("The police icon's colour (the first of the blink pair when blinking) and the patrol gap readout's warning colour.")]
        public Color policeRed = new(1f, 0.25f, 0.2f);
        [ShowIf(nameof(policeBlink))]
        public Color policeBlue = new(0.3f, 0.5f, 1f);

        [TitleGroup("Sprites")]
        [InfoBox("Optional. Empty = the default look (the ship's diamond, the patrol's square). Sprites keep their aspect and are still tinted by the colours above. Their rotation also follows the Ship / Police objects' Z rotation, so it can be set here or on the objects.")]
        [PreviewField(48)] public Sprite shipSprite;
        [ShowIf("@shipSprite != null"), Tooltip("Multiplies Ship Icon Size while a ship sprite is assigned.")]
        [PropertyRange(0.25f, 4f)] public float shipSpriteScale = 1f;
        [ShowIf("@shipSprite != null"), Tooltip("Z rotation of the ship icon while a ship sprite is assigned. Kept in sync with the Ship object's own rotation — edit either one.")]
        [PropertyRange(-180f, 180f)] public float shipSpriteRotation;
        [PreviewField(48)] public Sprite policeSprite;
        [ShowIf("@policeSprite != null"), Tooltip("Multiplies Police Icon Size while a police sprite is assigned.")]
        [PropertyRange(0.25f, 4f)] public float policeSpriteScale = 1f;
        [ShowIf("@policeSprite != null"), Tooltip("Z rotation of the police icon while a police sprite is assigned. Kept in sync with the Police object's own rotation — edit either one.")]
        [PropertyRange(-180f, 180f)] public float policeSpriteRotation;

        [TitleGroup("Layout")]
        [InfoBox("The strip, labels and fonts are laid out by hand on the ChaseMinimap prefab's children; only the icons' sizes live here.")]
        [PropertyRange(8f, 64f)] public float shipIconSize = 24f;
        [PropertyRange(8f, 64f)] public float policeIconSize = 20f;
        [Tooltip("Pixels under the ship that stand for the full minimap range (GameSettings.minimapRangeMeters) — the patrol's zoomed gap scale. At track scale the gap would be a pixel or two.")]
        [PropertyRange(20f, 300f)] public float chaseSpan = 120f;

        [TitleGroup("Track map")]
        [Tooltip("Draw the top-down track map in the prefab's TrackMap rect: the whole track, the ship and the patrol on it, ramps, laser gates and the finish.")]
        public bool showTrackMap = true;
        [TitleGroup("Track map")]
        [Tooltip("Also show the old vertical strip's bar and icons. Its distance labels stay either way.")]
        public bool showStrip;
        [TitleGroup("Track map")]
        [Tooltip("Turn the map so the start is at the bottom and the finish at the top. Off = world north up.")]
        public bool mapStartAtBottom = true;
        [TitleGroup("Track map")]
        [Tooltip("Share of the map rect kept empty round the track, each side.")]
        [PropertyRange(0f, 0.3f)] public float mapPadding = 0.08f;
        [TitleGroup("Track map")]
        [Tooltip("Points the track line is drawn with (the whole track, evenly spaced).")]
        [PropertyRange(50, 1000)] public int mapSamples = 300;
        [TitleGroup("Track map")]
        [PropertyRange(1f, 12f), SuffixLabel("px", true)] public float mapLineWidth = 3f;
        [TitleGroup("Track map")]
        [Tooltip("The track still ahead.")]
        public Color mapLineColor = new(1f, 1f, 1f, 0.55f);
        [TitleGroup("Track map")]
        [Tooltip("The stretch already flown.")]
        public Color mapDrivenColor = new(0.48f, 1f, 0.4f, 0.95f);
        [TitleGroup("Track map")]
        [Tooltip("The map rect's backdrop (alpha 0 = none).")]
        public Color mapBackground = new(0f, 0f, 0f, 0.3f);
        [TitleGroup("Track map")]
        [PropertyRange(2f, 32f), SuffixLabel("px", true)] public float markerSize = 6f;
        [TitleGroup("Track map")] public Color rampMarkerColor = new(1f, 0.8f, 0.2f);
        [TitleGroup("Track map")] public Color gateMarkerColor = new(1f, 0.2f, 0.2f);
        [TitleGroup("Track map")] public Color finishMarkerColor = new(1f, 0.25f, 0.9f);
        [TitleGroup("Track map")]
        [Tooltip("Optional marker sprites (empty = a plain square).")]
        [PreviewField(32)] public Sprite rampMarkerSprite;
        [TitleGroup("Track map")] [PreviewField(32)] public Sprite gateMarkerSprite;
        [TitleGroup("Track map")] [PreviewField(32)] public Sprite finishMarkerSprite;

        [TitleGroup("Behaviour")]
        [ShowIf(nameof(policeBlink))]
        [Tooltip("Seconds between red/blue flips — same cadence as the patrol light bar.")]
        [PropertyRange(0.05f, 1f)] public float blinkInterval = 0.25f;

#if UNITY_EDITOR
        // Any edit (undo included) re-applies the style to every live minimap
        // using this asset, in or out of play. Deferred: touching other
        // objects' RectTransforms from inside OnValidate trips Unity's
        // SendMessage warnings.
        void OnValidate() => UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) ChaseMinimap.RebuildAllUsing(this);
        };
#endif

        string PoliceColorLabel => policeBlink ? "Police Red" : "Police Color";

        /// <summary>Ship icon edge in pixels, scaled when a sprite replaces the diamond.</summary>
        public float ShipIconEdge => shipSprite != null ? shipIconSize * shipSpriteScale : shipIconSize;
        /// <summary>Police icon edge in pixels, scaled when a sprite replaces the square.</summary>
        public float PoliceIconEdge => policeSprite != null ? policeIconSize * policeSpriteScale : policeIconSize;
    }
}
