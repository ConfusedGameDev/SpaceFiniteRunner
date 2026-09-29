using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Where a boost orb's QTE glyph sits, authored as a child of the orb prefab so it
    /// can be placed and sized by hand. It is only a MARKER: the real prompt
    /// (<c>HUD.BoostQtePrompt</c>) is never a child of the orb — the orb is deactivated
    /// when taken and <see cref="SpeedPad"/> tints every renderer under it — so
    /// <c>GameFlow.BoostQte</c> reads this object's world position and size every frame
    /// and draws the prompt there. The optional <see cref="SpriteRenderer"/> beside it is
    /// an editor preview (hidden in play): what it shows is exactly the glyph's size.
    /// A prefab without an anchor falls back to the ring's centre.
    /// </summary>
    public class BoostQteAnchor : MonoBehaviour
    {
        SpriteRenderer preview;

        /// <summary>The glyph's width in world metres: the preview sprite's drawn width, else the anchor's scale.</summary>
        public float WorldSize
        {
            get
            {
                float scale = Mathf.Abs(transform.lossyScale.x);
                if (preview == null) preview = GetComponent<SpriteRenderer>();
                return preview != null && preview.sprite != null ? preview.sprite.bounds.size.x * scale : scale;
            }
        }

        void Awake()
        {
            preview = GetComponent<SpriteRenderer>();
            if (Application.isPlaying && preview != null) preview.enabled = false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            float s = WorldSize;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(s, s, 0f));
        }
    }
}
