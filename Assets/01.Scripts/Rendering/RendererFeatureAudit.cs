using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.Rendering
{
    /// <summary>
    /// Startup check for the scene-side dials of the renderer features
    /// (GlitchController, DistanceFog): does the renderer that is ACTUALLY
    /// rendering carry a feature driving the material the dial writes? The
    /// active pipeline asset is the current quality level's when one is set,
    /// the GraphicsSettings default otherwise — and a quality level can point
    /// at a pipeline asset nobody installed the features on without a single
    /// error: the dial keeps writing _Intensity into a material no pass
    /// samples, and the effect silently never shows. That is exactly what
    /// happened when a third-party pack shipped a pipeline asset with the
    /// same GUID as the URP template's PC_RPAsset (asset-store packs built
    /// from the template keep its GUIDs): the project's dangling quality-
    /// level reference came back to life pointing at the pack's renderer,
    /// which had the fog (its installer had stamped every renderer) but
    /// never the GlitchPost. One warning naming the pipeline asset and the
    /// quality level turns that silent failure into a one-line diagnosis.
    /// </summary>
    public static class RendererFeatureAudit
    {
        /// <summary>
        /// Logs a warning when no active feature on ANY renderer of the
        /// current pipeline asset references <paramref name="material"/>;
        /// returns true when one does (or when the check does not apply —
        /// no URP pipeline, no material). The asset's default renderer index
        /// is internal, so the whole renderer list is scanned: the failure
        /// this catches is a pipeline asset none of whose renderers were
        /// ever installed on.
        /// </summary>
        public static bool WarnIfMissing(Material material, string effectName, Object context = null)
        {
            if (material == null) return true;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return true;
            foreach (ScriptableRendererData data in pipeline.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                    if (feature != null && feature.isActive && Drives(feature, material))
                        return true;
            }

            string quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            Debug.LogWarning(
                $"{effectName}: no active renderer feature on any renderer of pipeline asset '{pipeline.name}' " +
                $"(quality level '{quality}') drives material '{material.name}' — the effect cannot render. " +
                "Install the feature on that asset's renderer, or point the quality level's Render Pipeline Asset at the project's own URP Asset.",
                context);
            return false;
        }

        /// <summary>The material each of the project's full-screen features samples — extend when a new feature kind gets a scene dial.</summary>
        static bool Drives(ScriptableRendererFeature feature, Material material) => feature switch
        {
            FullScreenPassRendererFeature fullScreen => fullScreen.passMaterial == material,
            DistanceFogFeature fog => fog.settings.material == material,
            GlitchSilhouetteFeature silhouette => silhouette.settings.material == material,
            _ => false,
        };
    }
}
