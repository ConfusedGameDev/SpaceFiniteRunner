using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Look of the RPG dialogue box — panel colors, portrait frame and text
    /// metrics. All dialogue-box look tunables live on this asset; defaults
    /// mirror the original hardcoded values so a missing asset degrades to
    /// the classic look.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniteRunner/Rpg Message Style")]
    public class RpgMessageStyle : ScriptableObject
    {
        [TitleGroup("Panel")]
        public Color borderColor = new(0.85f, 0.9f, 1f, 0.55f);
        public Color backgroundColor = new(0.05f, 0.07f, 0.18f, 0.92f);
        [PropertyRange(120f, 480f)] public float panelHeight = 256f;
        [PropertyRange(0f, 120f)] public float panelSideMargin = 24f;
        [PropertyRange(0f, 120f)] public float panelBottomMargin = 20f;

        [TitleGroup("Portrait")]
        public Vector2 portraitPosition = new(40f, 28f);
        public Vector2 portraitSize = new(280f, 300f);

        [TitleGroup("Text")]
        [PropertyRange(16, 72)] public int speakerFontSize = 34;
        [PropertyRange(16, 72)] public int bodyFontSize = 30;
        [Tooltip("Left inset that clears the portrait frame.")]
        [PropertyRange(0f, 600f)] public float textLeftInset = 330f;
    }
}
