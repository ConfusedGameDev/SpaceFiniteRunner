using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Look of the right-edge chase gauge. All minimap look tunables live on
    /// this asset — add new knobs here, not on the ChaseMinimap component.
    /// Field defaults mirror the original hardcoded values, so a missing
    /// asset degrades to the classic look.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniteRunner/Chase Minimap Settings")]
    public class ChaseMinimapSettings : ScriptableObject
    {
        [TitleGroup("Colors")]
        public Color shipColor = new(0.48f, 1f, 0.4f);
        public Color policeRed = new(1f, 0.25f, 0.2f);
        public Color policeBlue = new(0.3f, 0.5f, 1f);
        [PropertyRange(0f, 1f)] public float barAlpha = 0.18f;

        [TitleGroup("Layout")]
        [Tooltip("Offset of the strip from the right edge / vertical center.")]
        public Vector2 barOffset = new(-60f, 0f);
        public Vector2 barSize = new(10f, 480f);
        [PropertyRange(8f, 64f)] public float shipIconSize = 24f;
        [PropertyRange(8f, 64f)] public float policeIconSize = 20f;
        [PropertyRange(10, 64)] public int fontSize = 28;

        [TitleGroup("Behaviour")]
        [Tooltip("Seconds between red/blue flips — same cadence as the patrol light bar.")]
        [PropertyRange(0.05f, 1f)] public float blinkInterval = 0.25f;
    }
}
