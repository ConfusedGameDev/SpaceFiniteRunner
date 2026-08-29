using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// Distance fog + far glitch as ONE depth-based full-screen pass (shader
    /// Hidden/PoliceEscape/DistanceFog): both key on the same distance from
    /// the camera, so a single read of the depth buffer serves both. Runs
    /// before post-processing, so bloom and tonemapping treat the fog as
    /// scene light and the GlitchPost feature corrupts a fogged picture.
    /// Self-gating: the pass is not even enqueued while the material's
    /// _Intensity is 0 (the DistanceFog scene component drives it), so the
    /// menu and the runner pay nothing; and it never runs for a camera that
    /// renders into a texture (the minimap's top-down radar would fog over)
    /// or for preview/reflection cameras. The depth texture is requested via
    /// ConfigureInput, which makes URP produce it even though the pipeline
    /// asset has it switched off. Render Graph implementation for URP 17
    /// (Unity 6) after the stock FullScreenPassRendererFeature: copy the
    /// camera colour, then draw the material back over the active target.
    /// Install on the URP renderer assets via Tools → Police Escape →
    /// Install Distance Fog Feature.
    /// </summary>
    public class DistanceFogFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class FogSettings
        {
            [Tooltip("The distance fog material (Hidden/PoliceEscape/DistanceFog shader) — DistanceFog writes its settings asset into it every frame.")]
            public Material material;

            [Tooltip("Before post-processing: fog is scene light, so bloom and tonemapping should see it; the GlitchPost feature (after post) then corrupts the fogged picture.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public FogSettings settings = new();

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        DistanceFogPass pass;

        public override void Create() => pass = new DistanceFogPass(settings);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Material material = settings.material;
            if (material == null) return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            if (renderingData.cameraData.targetTexture != null) return; // the minimap, or any other render-texture camera
            if (!material.HasProperty(IntensityId) || material.GetFloat(IntensityId) <= 0f) return;

            pass.renderPassEvent = settings.renderPassEvent;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        class DistanceFogPass : ScriptableRenderPass
        {
            static readonly Vector4 FullScaleBias = new(1f, 1f, 0f, 0f);

            readonly FogSettings settings;

            public DistanceFogPass(FogSettings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
                profilingSampler = new ProfilingSampler("DistanceFog");
            }

            class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return; // nothing to read back from; requiresIntermediateTexture should have prevented this

                // The pass reads the whole picture and writes the whole picture,
                // so it works off a copy of the camera colour.
                TextureHandle source = resourceData.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_DistanceFogSource";
                desc.clearBuffer = false;
                TextureHandle copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "DistanceFog Copy");

                using var builder = renderGraph.AddRasterRenderPass<PassData>("DistanceFog", out PassData passData, profilingSampler);
                passData.source = copy;
                passData.material = settings.material;
                builder.UseTexture(copy, AccessFlags.Read);
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, data.source, FullScaleBias, data.material, 0));
            }
        }
    }
}
