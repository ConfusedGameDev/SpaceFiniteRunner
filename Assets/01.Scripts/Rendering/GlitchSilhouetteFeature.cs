using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// X-ray juice: draws everything on the configured layer (the player car,
    /// see CarFactory) a second time with the glitch silhouette material.
    /// That material uses ZTest Greater, so pixels only land where the car is
    /// BEHIND already-rendered geometry — drive behind a building and a
    /// glitching hologram of the car shows through it; the visible car stays
    /// untouched. Render Graph implementation for URP 17 (Unity 6). Install
    /// on the URP renderer assets via Tools → Police Escape → Install Glitch
    /// Silhouette Feature.
    /// </summary>
    public class GlitchSilhouetteFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class GlitchSettings
        {
            [Tooltip("The glitch silhouette material (PoliceEscape/GlitchSilhouette shader) drawn where the layer is occluded.")]
            public Material material;

            [Tooltip("Only renderers on these layers get the silhouette — CarFactory puts the player car on the PlayerCar layer.")]
            public LayerMask layerMask;

            [Tooltip("After opaques: buildings are already in the depth buffer (so occlusion works), post-processing still applies on top.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public GlitchSettings settings = new();

        GlitchSilhouettePass pass;

        public override void Create() => pass = new GlitchSilhouettePass(settings);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.material == null || settings.layerMask == 0) return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            renderer.EnqueuePass(pass);
        }

        class GlitchSilhouettePass : ScriptableRenderPass
        {
            // Renderer filtering matches the car's ORIGINAL shader passes; the
            // override material then replaces what actually gets drawn.
            static readonly List<ShaderTagId> ShaderTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
            };

            readonly GlitchSettings settings;

            public GlitchSilhouettePass(GlitchSettings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
                profilingSampler = new ProfilingSampler("GlitchSilhouette");
            }

            class PassData
            {
                public RendererListHandle maskList;
                public RendererListHandle silhouetteList;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                using var builder = renderGraph.AddRasterRenderPass<PassData>("GlitchSilhouette", out PassData passData, profilingSampler);

                var filtering = new FilteringSettings(RenderQueueRange.all, settings.layerMask);

                // Pass 0 of the material: stencil-mark the pixels where the car
                // itself is the visible surface, so the silhouette never draws
                // against the car's own parts (wheels behind the body).
                DrawingSettings maskDrawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                maskDrawing.overrideMaterial = settings.material;
                maskDrawing.overrideMaterialPassIndex = 0;
                passData.maskList = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, maskDrawing, filtering));

                // Pass 1: the glitch silhouette where occluded and not stenciled.
                DrawingSettings silhouetteDrawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                silhouetteDrawing.overrideMaterial = settings.material;
                silhouetteDrawing.overrideMaterialPassIndex = 1;
                passData.silhouetteList = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, silhouetteDrawing, filtering));

                builder.UseRendererList(passData.maskList);
                builder.UseRendererList(passData.silhouetteList);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                // Depth reads for the ZTests, stencil writes for the mask.
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.maskList);
                    context.cmd.DrawRendererList(data.silhouetteList);
                });
            }
        }
    }
}
