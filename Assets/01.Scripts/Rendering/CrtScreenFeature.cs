using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// CRT screen as ONE full-screen pass (shader Hidden/FiniteRunner/CrtScreen)
    /// over the finished picture: barrel curvature with rounded glass corners,
    /// phosphor bleed and convergence error that grow toward the edges,
    /// halation, scanlines, an aperture grille, refresh flicker and a vignette.
    /// Like <see cref="VhsTapeFeature"/> it reads the whole picture and
    /// writes the whole picture, so it works off a copy of the camera colour
    /// (no depth). Runs after post-processing and the installer APPENDS it at
    /// the very END of the feature list — after the PsxLook and the VhsTape
    /// (same event, so list order is the tie-break): the tube is the display
    /// everything else is shown on, so the console's picture and the tape's
    /// playback are both on the glass. Self-gating like the others: no driver
    /// alive (<see cref="HasDriver"/>, set by the CrtScreen scene component)
    /// or a material at _Intensity 0 and the pass is not even enqueued, and it
    /// never runs for a render-texture camera (the minimap) or a
    /// preview/reflection camera. Install via Tools → FiniteRunner →
    /// Install CRT Screen Feature.
    /// </summary>
    public class CrtScreenFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class ScreenSettings
        {
            [Tooltip("The CRT screen material (Hidden/FiniteRunner/CrtScreen shader) — the CrtScreen scene component writes its settings asset into it every frame.")]
            public Material material;

            [Tooltip("After post-processing, appended after every other full-screen feature: the tube shows the finished picture, console and tape included.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public ScreenSettings settings = new();

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>
        /// True while a scene CrtScreen driver is alive (it sets this on
        /// enable and clears it on disable). The material's _Intensity is a
        /// shared asset: an edit-mode preview saves a non-zero value into it,
        /// and a scene with no driver would otherwise render curved. No
        /// driver, no pass.
        /// </summary>
        public static bool HasDriver { get; set; }

        CrtScreenPass pass;

        public override void Create() => pass = new CrtScreenPass(settings);

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

        class CrtScreenPass : ScriptableRenderPass
        {
            static readonly Vector4 FullScaleBias = new(1f, 1f, 0f, 0f);

            readonly ScreenSettings settings;

            public CrtScreenPass(ScreenSettings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
                profilingSampler = new ProfilingSampler("CrtScreen");
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
                desc.name = "_CrtScreenSource";
                desc.clearBuffer = false;
                TextureHandle copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "CrtScreen Copy");

                using var builder = renderGraph.AddRasterRenderPass<PassData>("CrtScreen", out PassData passData, profilingSampler);
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
