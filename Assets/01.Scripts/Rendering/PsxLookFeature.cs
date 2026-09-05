using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// PlayStation-1 look as ONE full-screen pass (shader
    /// Hidden/FiniteRunner/PsxLook): pixelation to a virtual resolution, a
    /// depth-aware screen-space stand-in for vertex snapping and affine
    /// texture swim, and 15-bit colour under an ordered dither. Like
    /// <see cref="DistanceFogFeature"/> it works off a copy of the camera
    /// colour AND reads depth (requested via ConfigureInput — the pipeline
    /// asset keeps depth off; URP still has the copied depth texture bound
    /// after post-processing), writing _HasDepth onto the material so the
    /// shader can fall back to screen-only wobble when the handle is not
    /// valid. Runs after post-processing; the installer puts it AFTER the
    /// GlitchPost feature and BEFORE the VhsTape (same event, list order is
    /// the tie-break): the console shows the death glitch, the tape records
    /// the console. Self-gating like the others: no driver alive
    /// (<see cref="HasDriver"/>, set by the PsxLook scene component) or a
    /// material at _Intensity 0 and the pass is not even enqueued, and it
    /// never runs for a render-texture camera (the minimap) or a
    /// preview/reflection camera. Install via Tools → FiniteRunner →
    /// Install PSX Look Feature.
    /// </summary>
    public class PsxLookFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class LookSettings
        {
            [Tooltip("The PSX look material (Hidden/FiniteRunner/PsxLook shader) — the PsxLook scene component writes its settings asset into it every frame.")]
            public Material material;

            [Tooltip("After post-processing, between the GlitchPost and VhsTape features: the console shows the glitch, the tape records the console.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public LookSettings settings = new();

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int HasDepthId = Shader.PropertyToID("_HasDepth");

        /// <summary>
        /// True while a scene PsxLook driver is alive (it sets this on enable
        /// and clears it on disable). The material's _Intensity is a shared
        /// asset: an edit-mode preview saves a non-zero value into it, and a
        /// scene with no driver would otherwise pixelate. No driver, no pass.
        /// </summary>
        public static bool HasDriver { get; set; }

        PsxLookPass pass;

        public override void Create() => pass = new PsxLookPass(settings);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Material material = settings.material;
            if (material == null) return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            if (renderingData.cameraData.targetTexture != null) return; // the minimap, or any other render-texture camera
            if (!HasDriver) return;
            if (!material.HasProperty(IntensityId) || material.GetFloat(IntensityId) <= 0f) return;

            pass.renderPassEvent = settings.renderPassEvent;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        class PsxLookPass : ScriptableRenderPass
        {
            static readonly Vector4 FullScaleBias = new(1f, 1f, 0f, 0f);

            readonly LookSettings settings;

            public PsxLookPass(LookSettings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
                profilingSampler = new ProfilingSampler("PsxLook");
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
                desc.name = "_PsxLookSource";
                desc.clearBuffer = false;
                TextureHandle copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "PsxLook Copy");

                using var builder = renderGraph.AddRasterRenderPass<PassData>("PsxLook", out PassData passData, profilingSampler);
                passData.source = copy;
                passData.material = settings.material;
                builder.UseTexture(copy, AccessFlags.Read);

                // The one deviation from the shared pattern: the shader must
                // know whether the depth it would key the wobble on exists.
                bool hasDepth = resourceData.cameraDepthTexture.IsValid();
                if (hasDepth)
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                settings.material.SetFloat(HasDepthId, hasDepth ? 1f : 0f);

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, data.source, FullScaleBias, data.material, 0));
            }
        }
    }
}
