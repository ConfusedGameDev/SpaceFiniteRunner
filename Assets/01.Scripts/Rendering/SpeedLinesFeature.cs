using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// Manga "speed lines" as ONE full-screen pass (shader
    /// Hidden/FiniteRunner/SpeedLines) that BLENDS over the post-processed
    /// picture: the wedges are drawn on top with fixed-function alpha
    /// blending, so unlike <see cref="DistanceFogFeature"/> the pass takes no
    /// copy of the camera colour and reads no depth — a raster pass with the
    /// active colour texture as its only attachment, drawn with the
    /// source-less <c>Blitter.BlitTexture(cmd, scaleBias, material, pass)</c>
    /// (Blit.hlsl's Vert never reads _BlitTexture). Runs after
    /// post-processing so bloom never smears the hard edges, and the installer
    /// puts it BEFORE the GlitchPost full-screen feature (same event, so list
    /// order is the tie-break) so the death glitch corrupts the lines with the
    /// rest of the picture. <c>requiresIntermediateTexture</c> stays on: the
    /// pass then never targets the backbuffer, and on an intermediate target
    /// <c>texcoord</c> is the y-up viewport space the driver writes the focus
    /// point in. Self-gating like the fog: no driver alive
    /// (<see cref="HasDriver"/>, set by the SpeedLines scene component) or a
    /// material at _Intensity 0 and the pass is not even enqueued, and it
    /// never runs for a render-texture camera (the minimap) or a
    /// preview/reflection camera. Install via Tools → FiniteRunner →
    /// Install Speed Lines Feature.
    /// </summary>
    public class SpeedLinesFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class LinesSettings
        {
            [Tooltip("The speed lines material (Hidden/FiniteRunner/SpeedLines shader) — the SpeedLines scene component writes its settings asset into it every frame.")]
            public Material material;

            [Tooltip("After post-processing: the lines are a hard-edged overlay that bloom must not smear; the GlitchPost feature after it in the list corrupts them with the picture.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public LinesSettings settings = new();

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>
        /// True while a scene SpeedLines driver is alive (it sets this on
        /// enable and clears it on disable). The material's _Intensity is a
        /// shared asset: an edit-mode preview saves a non-zero value into it,
        /// and a scene with no driver would otherwise draw lines over
        /// everything. No driver, no pass.
        /// </summary>
        public static bool HasDriver { get; set; }

        SpeedLinesPass pass;

        public override void Create() => pass = new SpeedLinesPass(settings);

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
            // No copy is taken, but the blend must land on an intermediate
            // target: never the backbuffer, and y-up texcoords for the focus.
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        class SpeedLinesPass : ScriptableRenderPass
        {
            static readonly Vector4 FullScaleBias = new(1f, 1f, 0f, 0f);

            readonly LinesSettings settings;

            public SpeedLinesPass(LinesSettings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
                profilingSampler = new ProfilingSampler("SpeedLines");
            }

            class PassData
            {
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return; // requiresIntermediateTexture should have prevented this

                // Write, not ReadWrite: the attachment keeps its contents and
                // the shader's alpha blend composites over them — the same
                // contract URP's own FullScreenPassRendererFeature relies on.
                using var builder = renderGraph.AddRasterRenderPass<PassData>("SpeedLines", out PassData passData, profilingSampler);
                passData.material = settings.material;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, FullScaleBias, data.material, 0));
            }
        }
    }
}
