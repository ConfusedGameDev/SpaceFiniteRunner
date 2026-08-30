using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click setup for the distance fog + far glitch, since a renderer
    /// feature must live on the URP renderer assets: creates the fog material
    /// and the settings asset (never overwriting either), then installs a
    /// <see cref="DistanceFogFeature"/> on every UniversalRendererData under
    /// <c>Assets/04.Data</c> (the project's own; third-party packs ship their own
    /// pipeline/renderer assets and must never take the feature — see
    /// <see cref="IsProjectRendererAsset"/>) — INSERTED before the GlitchPost full-screen feature, because
    /// list order is the tie-break between passes at the same event and the
    /// world must fog before the signal corrupts. Re-running updates the
    /// existing features' material instead of duplicating them. The scene
    /// side is a hand-placed <see cref="DistanceFog"/> object
    /// (CarTestSceneBuilder creates it).
    /// </summary>
    public static class DistanceFogInstaller
    {
        const string ShaderName = "Hidden/PoliceEscape/DistanceFog";
        const string MaterialPath = "Assets/02.Art/02.Materials/InfiniteCity/DistanceFog.mat";
        const string SettingsPath = "Assets/04.Data/Resources/FiniteRunner_DistanceFog.asset";

        [MenuItem("Tools/Police Escape/Install Distance Fog Feature")]
        public static void Install()
        {
            Material material = CreateOrLoadMaterial();
            if (material == null) return;
            DistanceFogSettings settings = CreateOrLoadSettings();

            int installed = 0, updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsProjectRendererAsset(path)) continue;
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null) continue;

                DistanceFogFeature existing = null;
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature is DistanceFogFeature fog)
                        existing = fog;

                if (existing != null)
                {
                    existing.settings.material = material;
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(rendererData);
                    updated++;
                    continue;
                }

                AddFeature(rendererData, material);
                installed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"DistanceFogInstaller: material at {MaterialPath}, settings at {SettingsPath} — feature installed on {installed} renderer asset(s), updated on {updated}. " +
                      "Add a DistanceFog object to the scene (Create Car Test Scene does) and assign both.", settings);
        }

        /// <summary>
        /// Only the project's own renderer assets (under Assets/04.Data) take a
        /// feature. Package renderers are immutable, and a third-party pack's
        /// (Cyberpunk Megapolis: CP_High and friends) must stay untouched: the
        /// fog stamped onto those is how the fog kept rendering on a renderer
        /// that never had the GlitchPost feature, after a quality level
        /// silently switched to the pack's pipeline asset (see
        /// <see cref="Rendering.RendererFeatureAudit"/>). Shared with the
        /// GlitchSilhouetteInstaller so both tools agree on the target set.
        /// </summary>
        internal static bool IsProjectRendererAsset(string path) => path.StartsWith("Assets/04.Data/");

        /// <summary>
        /// Renderer features have no public add API — insert via the renderer
        /// data's serialized m_RendererFeatures / m_RendererFeatureMap pair,
        /// with the feature stored as a sub-asset (the same layout the
        /// inspector's Add Renderer Feature button produces). Inserted just
        /// before the first AfterRenderingPostProcessing full-screen pass
        /// (the GlitchPost) so the fog is in the picture the glitch corrupts.
        /// </summary>
        static void AddFeature(UniversalRendererData rendererData, Material material)
        {
            var feature = ScriptableObject.CreateInstance<DistanceFogFeature>();
            feature.name = "DistanceFog";
            feature.settings.material = material;

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
            int index = features.arraySize;
            for (int i = 0; i < features.arraySize; i++)
            {
                if (features.GetArrayElementAtIndex(i).objectReferenceValue is FullScreenPassRendererFeature fullScreen
                    && fullScreen.injectionPoint == FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing)
                {
                    index = i;
                    break;
                }
            }
            features.InsertArrayElementAtIndex(index);
            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;
            map.InsertArrayElementAtIndex(index);
            map.GetArrayElementAtIndex(index).longValue = localId;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(rendererData);
        }

        static Material CreateOrLoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"DistanceFogInstaller: shader '{ShaderName}' not found — did it compile?");
                return null;
            }
            material = new Material(shader);
            material.SetFloat("_Intensity", 0f); // the scene's DistanceFog object switches it on
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static DistanceFogSettings CreateOrLoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DistanceFogSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<DistanceFogSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }
    }
}
