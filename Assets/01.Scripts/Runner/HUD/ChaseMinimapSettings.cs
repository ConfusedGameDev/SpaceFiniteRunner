using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Look of the right-edge track map (ship climbing the track, the patrol
    /// hanging under it on the zoomed <see cref="chaseSpan"/> scale). All minimap look tunables live on
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
