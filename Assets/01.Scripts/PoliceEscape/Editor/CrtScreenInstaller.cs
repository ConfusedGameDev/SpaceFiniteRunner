using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click setup for the CRT screen, since a renderer feature must live
    /// on the URP renderer assets: creates the screen material and the
    /// settings asset (never overwriting either), writes the material onto
    /// the settings asset (that is how a driver finds it without a scene
    /// reference), then installs a <see cref="CrtScreenFeature"/> on every
    /// UniversalRendererData under <c>Assets/04.Data</c> (the project's own —
    /// <see cref="DistanceFogInstaller.IsProjectRendererAsset"/>), APPENDED at
    /// the very end of the feature list through the fog installer's shared
    /// <see cref="DistanceFogInstaller.InsertAfterPostGlitch"/> (which keeps a
    /// CrtScreen feature last whichever installer runs first): the tube is
    /// the display, so the console's picture and the tape's playback are both
    /// on the glass. Re-running updates the existing features' material
    /// instead of duplicating them. Finally it places a <see cref="CrtScreen"/>
    /// driver in the OPEN scene when it has none (and wires the material and
    /// asset onto an existing one that lacks them): systems are hand-placed
    /// so they can be tuned before play — nothing creates one at play time.
    /// </summary>
    public static class CrtScreenInstaller
    {
        const string ShaderName = "Hidden/FiniteRunner/CrtScreen";
        const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/CrtScreen.mat";
        const string SettingsPath = "Assets/04.Data/Resources/FiniteRunner_CrtScreen.asset";

        [MenuItem("Tools/FiniteRunner/Install CRT Screen Feature")]
        public static void Install()
        {
            Material material = CreateOrLoadMaterial();
            if (material == null) return;
            CrtScreenSettings settings = CreateOrLoadSettings();
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

                CrtScreenFeature existing = null;
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature is CrtScreenFeature crt)
                        existing = crt;

                if (existing != null)
                {
                    existing.settings.material = material;
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(rendererData);
                    updated++;
                    continue;
                }

                var created = ScriptableObject.CreateInstance<CrtScreenFeature>();
                created.name = "CrtScreen";
                created.settings.material = material;
                DistanceFogInstaller.InsertAfterPostGlitch(rendererData, created);
                installed++;
            }

            AssetDatabase.SaveAssets();
            string placed = PlaceInOpenScene(material, settings);
            Debug.Log($"CrtScreenInstaller: material at {MaterialPath}, settings at {SettingsPath} — feature installed on {installed} renderer asset(s), updated on {updated}; {placed}. " +
                      "The runner's GameSettings 'CRT screen' group and the CityManager's 'CRT screen' group switch it on.", settings);
        }

        /// <summary>
        /// The scene half: a hand-placed CrtScreen object at the root of the
        /// active scene, wired to the material and the asset, so the look is
        /// tunable in the inspector before play. Idempotent — an existing one
        /// only gets its empty references filled.
        /// </summary>
        static string PlaceInOpenScene(Material material, CrtScreenSettings settings)
        {
            var existing = Object.FindAnyObjectByType<CrtScreen>(FindObjectsInactive.Include);
            if (existing != null)
            {
                bool changed = false;
                if (existing.screenMaterial == null) { existing.screenMaterial = material; changed = true; }
                if (existing.settings == null) { existing.settings = settings; changed = true; }
                if (changed)
                {
                    EditorUtility.SetDirty(existing);
                    EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                }
                return changed ? $"wired the scene's '{existing.name}' object" : $"scene already has '{existing.name}'";
            }

            var go = new GameObject("CrtScreen");
            var driver = go.AddComponent<CrtScreen>();
            driver.screenMaterial = material;
            driver.settings = settings;
            Undo.RegisterCreatedObjectUndo(go, "Place CrtScreen");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return "placed a CrtScreen object in the open scene (save it)";
        }

        static Material CreateOrLoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"CrtScreenInstaller: shader '{ShaderName}' not found — did it compile?");
                return null;
            }
            material = new Material(shader);
            material.SetFloat("_Intensity", 0f); // the CrtScreen driver switches it on
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static CrtScreenSettings CreateOrLoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CrtScreenSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<CrtScreenSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }
    }
}
