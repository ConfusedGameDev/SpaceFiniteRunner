using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Material inspector for SH_CharacterColor. The default property GUI does
    /// the drawing (the [Toggle] drawers already manage the feature keywords);
    /// what this adds is the state a shader cannot switch by itself: the
    /// hologram toggle flips the blend mode, ZWrite, RenderType tag and render
    /// queue between opaque and transparent, and turns shadow casting off
    /// while holographic — a projection has no shadow. Keywords are also
    /// re-synced from the toggle floats here, so a material edited from code
    /// heals the next time it is inspected.
    /// </summary>
    public class CharacterColorShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);
            foreach (Object target in materialEditor.targets)
                if (target is Material material) Apply(material);
        }

        public override void ValidateMaterial(Material material) => Apply(material);

        static void Apply(Material material)
        {
            if (!material.HasProperty("_UseHologram")) return;

            bool hologram = material.GetFloat("_UseHologram") > 0.5f;
            material.SetFloat("_SrcBlend", (float)(hologram ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(hologram ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", hologram ? 0f : 1f);
            material.SetOverrideTag("RenderType", hologram ? "Transparent" : "Opaque");
            material.renderQueue = hologram ? (int)RenderQueue.Transparent : -1; // -1 = whatever the shader says
            material.SetShaderPassEnabled("ShadowCaster", !hologram);

            SyncKeyword(material, "_ALBEDO_ON", "_UseAlbedo");
            SyncKeyword(material, "_NOISE_ON", "_UseNoise");
            SyncKeyword(material, "_EMISSION_ON", "_UseEmission");
            SyncKeyword(material, "_HOLOGRAM_ON", "_UseHologram");
        }

        static void SyncKeyword(Material material, string keyword, string property)
        {
            if (!material.HasProperty(property)) return;
            if (material.GetFloat(property) > 0.5f) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }
}
