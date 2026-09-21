using UnityEngine;
using UnityEngine.Rendering;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The translucent "ghost of the ship" look shared by the dash's onion
    /// skins and the respawn blink, for when no material asset is assigned:
    /// a runtime URP Unlit set up for alpha blending. The asset is
    /// authoritative — runtime surface-type switching in URP is fragile, so
    /// keep the .mat assigned; this is the safety net. The caller owns (and
    /// destroys) what it gets.
    /// </summary>
    public static class ShipGhostMaterial
    {
        public static Material BuildFallback()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);   // alpha
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.6f, 0.95f, 1f, 0.45f));
            return material;
        }
    }
}
