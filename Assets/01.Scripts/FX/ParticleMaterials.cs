using UnityEngine;
using UnityEngine.Rendering;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// One place that knows how to make a transparent unlit particle material
    /// at runtime. Every code-built effect in the project needs the same thing
    /// and the same three fallbacks, and getting the URP setup subtly wrong
    /// (a missing _Surface, a stale _DstBlend) shows up as an effect that is
    /// invisible in one pipeline and solid black in another — so it is written
    /// once here rather than in each effect.
    ///
    /// The URP <c>_Surface</c>/<c>_Blend</c> pair and the raw blend/ZWrite ints
    /// are both written, because only the URP shader reads the former and only
    /// the built-in particle shaders read the latter.
    /// </summary>
    public static class ParticleMaterials
    {
        /// <summary>
        /// A transparent unlit particle material. <paramref name="additive"/>
        /// adds to the frame (right for sparks and neon-lit rain); otherwise it
        /// blends over it (right for anything carrying its own smoke, which
        /// additive would erase).
        /// </summary>
        public static Material Unlit(string name, Texture texture, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Particles/Standard Unlit")
                            ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };

            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);            // transparent
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 1f : 0f); // additive / alpha
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);                   // double sided
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            if (texture != null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
