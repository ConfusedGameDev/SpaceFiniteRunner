using ConfusedGameDev.FiniteRunner.PoliceEscape.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click setup for the occluded-car glitch effect, since a renderer
    /// feature must live on the URP renderer assets and the car needs a
    /// dedicated layer: ensures the PlayerCar layer exists (CarFactory puts
    /// spawned player cars on it), creates the glitch material, and installs
    /// a GlitchSilhouetteFeature on every UniversalRendererData in the
    /// project (all quality levels). Re-running updates the existing
    /// features' settings instead of duplicating them.
    /// </summary>
    public static class GlitchSilhouetteInstaller
    {
        public const string LayerName = "PlayerCar";
        const string ShaderName = "PoliceEscape/GlitchSilhouette";
        const string MaterialPath = "Assets/02.Art/02.Materials/InfiniteCity/GlitchSilhouette.mat";

        [MenuItem("Tools/Police Escape/Install Glitch Silhouette Feature")]
        public static void Install()
        {
            int layer = EnsureLayer(LayerName);
            if (layer < 0)
            {
                Debug.LogError("GlitchSilhouetteInstaller: no free layer slot found — free one in Project Settings → Tags and Layers, then re-run.");
                return;
            }

            Material material = CreateOrLoadMaterial();
            if (material == null) return;

            int installed = 0, updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue; // never touch package (immutable) renderer assets
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null) continue;

                GlitchSilhouetteFeature existing = null;
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature is GlitchSilhouetteFeature glitch)
                        existing = glitch;

                if (existing != null)
                {
                    existing.settings.material = material;
                    existing.settings.layerMask = 1 << layer;
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(rendererData);
                    updated++;
                    continue;
                }

                AddFeature(rendererData, material, layer);
                installed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"GlitchSilhouetteInstaller: layer '{LayerName}' = {layer}, material at {MaterialPath} — feature installed on {installed} renderer asset(s), updated on {updated}.");
        }

        /// <summary>
        /// Renderer features have no public add API — append via the renderer
        /// data's serialized m_RendererFeatures / m_RendererFeatureMap pair,
        /// with the feature stored as a sub-asset (the same layout the
        /// inspector's Add Renderer Feature button produces).
        /// </summary>
        static void AddFeature(UniversalRendererData rendererData, Material material, int layer)
        {
            var feature = ScriptableObject.CreateInstance<GlitchSilhouetteFeature>();
            feature.name = "GlitchSilhouette";
            feature.settings.material = material;
            feature.settings.layerMask = 1 << layer;

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
            int index = features.arraySize;
            features.arraySize++;
            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;
            map.arraySize++;
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
                Debug.LogError($"GlitchSilhouetteInstaller: shader '{ShaderName}' not found — did it compile?");
                return null;
            }
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>Name an empty layer slot (top-down, away from gameplay layers) if the layer doesn't already exist.</summary>
        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 31; i >= 8; i--)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
            return -1;
        }
    }
}
