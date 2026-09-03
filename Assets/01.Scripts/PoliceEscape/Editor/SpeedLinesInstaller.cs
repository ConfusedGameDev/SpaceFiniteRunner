using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click setup for the manga speed lines, since a renderer feature
    /// must live on the URP renderer assets: creates the lines material and
    /// the settings asset (never overwriting either), writes the material
    /// onto the settings asset (that is how a driver spawned at play time
    /// finds it), then installs a <see cref="SpeedLinesFeature"/> on every
    /// UniversalRendererData under <c>Assets/04.Data</c> (the project's own —
    /// <see cref="DistanceFogInstaller.IsProjectRendererAsset"/>), INSERTED
    /// before the GlitchPost full-screen feature through the fog installer's
    /// shared <see cref="DistanceFogInstaller.InsertBeforePostGlitch"/>: both
    /// run at the same event, so list order is what puts the lines under the
    /// death glitch. Re-running updates the existing features' material
    /// instead of duplicating them. The scene side needs nothing placed —
    /// the runner's GameManager creates the <see cref="SpeedLines"/> driver
    /// off its GameSettings (a hand-placed one wins).
    /// </summary>
    public static class SpeedLinesInstaller
    {
        const string ShaderName = "Hidden/FiniteRunner/SpeedLines";
        const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/SpeedLines.mat";
        const string SettingsPath = "Assets/04.Data/Resources/FiniteRunner_SpeedLines.asset";

        [MenuItem("Tools/FiniteRunner/Install Speed Lines Feature")]
        public static void Install()
        {
            Material material = CreateOrLoadMaterial();
            if (material == null) return;
            SpeedLinesSettings settings = CreateOrLoadSettings();
            if (settings.material != material)
            {
                settings.material = material;
                EditorUtility.SetDirty(settings);
            }

            int installed = 0, updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!DistanceFogInstaller.IsProjectRendererAsset(path)) continue;
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null) continue;

                SpeedLinesFeature existing = null;
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature is SpeedLinesFeature lines)
                        existing = lines;

                if (existing != null)
                {
                    existing.settings.material = material;
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(rendererData);
                    updated++;
                    continue;
                }

                var created = ScriptableObject.CreateInstance<SpeedLinesFeature>();
                created.name = "SpeedLines";
                created.settings.material = material;
                DistanceFogInstaller.InsertBeforePostGlitch(rendererData, created);
                installed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"SpeedLinesInstaller: material at {MaterialPath}, settings at {SettingsPath} — feature installed on {installed} renderer asset(s), updated on {updated}. " +
                      "The runner's GameSettings 'Speed lines' group switches it on; assign the settings asset there or leave it to Resources.", settings);
        }

        static Material CreateOrLoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"SpeedLinesInstaller: shader '{ShaderName}' not found — did it compile?");
                return null;
            }
            material = new Material(shader);
            material.SetFloat("_Intensity", 0f); // the SpeedLines driver switches it on
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static SpeedLinesSettings CreateOrLoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SpeedLinesSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<SpeedLinesSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }
    }
}
