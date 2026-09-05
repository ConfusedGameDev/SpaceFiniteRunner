using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click setup for the PlayStation-1 look, since a renderer feature
    /// must live on the URP renderer assets: creates the look material and
    /// the settings asset (never overwriting either), writes the material
    /// onto the settings asset (that is how a driver finds it without a
    /// scene reference), then installs a <see cref="PsxLookFeature"/> on
    /// every UniversalRendererData under <c>Assets/04.Data</c> (the project's
    /// own — <see cref="DistanceFogInstaller.IsProjectRendererAsset"/>),
    /// inserted right AFTER the GlitchPost full-screen feature through the
    /// fog installer's shared <see cref="DistanceFogInstaller.InsertAfterPostGlitch"/>
    /// — which also keeps the VhsTape feature after it whichever installer
    /// runs first: the console shows the glitch, the tape records the
    /// console. Re-running updates the existing features' material instead
    /// of duplicating them. Finally it places a <see cref="PsxLook"/> driver
    /// in the OPEN scene when it has none (and wires the material and asset
    /// onto an existing one that lacks them): systems are hand-placed so
    /// they can be tuned before play — nothing creates one at play time.
    /// </summary>
    public static class PsxLookInstaller
    {
        const string ShaderName = "Hidden/FiniteRunner/PsxLook";
        const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/PsxLook.mat";
        const string SettingsPath = "Assets/04.Data/Resources/FiniteRunner_PsxLook.asset";

        [MenuItem("Tools/FiniteRunner/Install PSX Look Feature")]
        public static void Install()
        {
            Material material = CreateOrLoadMaterial();
            if (material == null) return;
            PsxLookSettings settings = CreateOrLoadSettings();
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

                PsxLookFeature existing = null;
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature is PsxLookFeature look)
                        existing = look;

                if (existing != null)
                {
                    existing.settings.material = material;
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(rendererData);
                    updated++;
                    continue;
                }

                var created = ScriptableObject.CreateInstance<PsxLookFeature>();
                created.name = "PsxLook";
                created.settings.material = material;
                DistanceFogInstaller.InsertAfterPostGlitch(rendererData, created);
                installed++;
            }

            AssetDatabase.SaveAssets();
            string placed = PlaceInOpenScene(material, settings);
            Debug.Log($"PsxLookInstaller: material at {MaterialPath}, settings at {SettingsPath} — feature installed on {installed} renderer asset(s), updated on {updated}; {placed}. " +
                      "The runner's GameSettings 'PSX look' group and the CityManager's 'PSX look' group switch it on.", settings);
        }

        /// <summary>
        /// The scene half: a hand-placed PsxLook object at the root of the
        /// active scene, wired to the material and the asset, so the look is
        /// tunable in the inspector before play. Idempotent — an existing one
        /// only gets its empty references filled.
        /// </summary>
        static string PlaceInOpenScene(Material material, PsxLookSettings settings)
        {
            var existing = Object.FindAnyObjectByType<PsxLook>(FindObjectsInactive.Include);
            if (existing != null)
            {
                bool changed = false;
                if (existing.lookMaterial == null) { existing.lookMaterial = material; changed = true; }
                if (existing.settings == null) { existing.settings = settings; changed = true; }
                if (changed)
                {
                    EditorUtility.SetDirty(existing);
                    EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                }
                return changed ? $"wired the scene's '{existing.name}' object" : $"scene already has '{existing.name}'";
            }

            var go = new GameObject("PsxLook");
            var driver = go.AddComponent<PsxLook>();
            driver.lookMaterial = material;
            driver.settings = settings;
            Undo.RegisterCreatedObjectUndo(go, "Place PsxLook");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return "placed a PsxLook object in the open scene (save it)";
        }

        static Material CreateOrLoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"PsxLookInstaller: shader '{ShaderName}' not found — did it compile?");
                return null;
            }
            material = new Material(shader);
            material.SetFloat("_Intensity", 0f); // the PsxLook driver switches it on
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static PsxLookSettings CreateOrLoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PsxLookSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PsxLookSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }
    }
}
