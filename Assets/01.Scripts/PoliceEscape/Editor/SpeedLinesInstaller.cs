using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
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
    /// instead of duplicating them. Finally it places a <see cref="SpeedLines"/>
    /// driver in the OPEN scene when it has none (and wires the material and
    /// asset onto an existing one that lacks them): systems are hand-placed
    /// so they can be tuned before play — nothing creates one at play time.
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
            string placed = PlaceInOpenScene(material, settings);
            Debug.Log($"SpeedLinesInstaller: material at {MaterialPath}, settings at {SettingsPath} — feature installed on {installed} renderer asset(s), updated on {updated}; {placed}. " +
                      "The runner's GameSettings 'Speed lines' group switches it on.", settings);
        }

        /// <summary>
        /// The scene half: a hand-placed SpeedLines object at the root of the
        /// active scene, wired to the material and the asset, so the effect is
        /// tunable in the inspector before play. Idempotent — an existing one
        /// only gets its empty references filled.
        /// </summary>
        static string PlaceInOpenScene(Material material, SpeedLinesSettings settings)
        {
            var existing = Object.FindAnyObjectByType<SpeedLines>(FindObjectsInactive.Include);
            if (existing != null)
            {
                bool changed = false;
                if (existing.linesMaterial == null) { existing.linesMaterial = material; changed = true; }
                if (existing.settings == null) { existing.settings = settings; changed = true; }
                if (changed)
                {
                    EditorUtility.SetDirty(existing);
                    EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                }
                return changed ? $"wired the scene's '{existing.name}' object" : $"scene already has '{existing.name}'";
            }

            var go = new GameObject("SpeedLines");
            var driver = go.AddComponent<SpeedLines>();
            driver.linesMaterial = material;
            driver.settings = settings;
            Undo.RegisterCreatedObjectUndo(go, "Place SpeedLines");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return "placed a SpeedLines object in the open scene (save it)";
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
