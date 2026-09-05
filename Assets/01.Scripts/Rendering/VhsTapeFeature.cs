using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// VHS tape look as ONE full-screen pass (shader
    /// Hidden/FiniteRunner/VhsTape) over the finished picture: chroma bleed
    /// and lag, row jitter, a crawling tracking band, head-switch noise,
    /// grain, scanlines, wash and vignette. Like <see cref="DistanceFogFeature"/>
    /// it reads the whole picture and writes the whole picture, so it works
    /// off a copy of the camera colour (no depth). Runs after post-processing
    /// and the installer APPENDS it after the GlitchPost full-screen feature
    /// (same event, so list order is the tie-break): the tape is the
    /// recording medium, so the death glitch, the fog and the speed lines
    /// are all on the tape. Self-gating like the others: no driver alive
    /// (<see cref="HasDriver"/>, set by the VhsTape scene component) or a
    /// material at _Intensity 0 and the pass is not even enqueued, and it
    /// never runs for a render-texture camera (the minimap) or a
    /// preview/reflection camera. Install via Tools → FiniteRunner →
    /// Install VHS Tape Feature.
    /// </summary>
    public class VhsTapeFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class TapeSettings
        {
            [Tooltip("The VHS tape material (Hidden/FiniteRunner/VhsTape shader) — the VhsTape scene component writes its settings asset into it every frame.")]
            public Material material;

            [Tooltip("After post-processing, appended after the GlitchPost feature: the tape records the finished picture, glitch included.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public TapeSettings settings = new();

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>
        /// True while a scene VhsTape driver is alive (it sets this on enable
        /// and clears it on disable). The material's _Intensity is a shared
        /// asset: an edit-mode preview saves a non-zero value into it, and a
        /// scene with no driver would otherwise play back as a tape. No
        /// driver, no pass.
        /// </summary>
        public static bool HasDriver { get; set; }

        VhsTapePass pass;

        public override void Create() => pass = new VhsTapePass(settings);

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
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        class VhsTapePass : ScriptableRenderPass
        {
            static readonly Vector4 FullScaleBias = new(1f, 1f, 0f, 0f);

            readonly TapeSettings settings;

            public VhsTapePass(TapeSettings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
                profilingSampler = new ProfilingSampler("VhsTape");
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
                desc.name = "_VhsTapeSource";
                desc.clearBuffer = false;
                TextureHandle copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "VhsTape Copy");

                using var builder = renderGraph.AddRasterRenderPass<PassData>("VhsTape", out PassData passData, profilingSampler);
                passData.source = copy;
                passData.material = settings.material;
                builder.UseTexture(copy, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, data.source, FullScaleBias, data.material, 0));
            }
        }
    }
}
