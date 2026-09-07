using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Audio;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates the runner's music settings asset in Resources, seeded with the
    /// soundtrack clip, the first time. <see cref="RunnerSceneSystemsPlacer"/>
    /// calls <see cref="CreateOrLoad"/> when it places the Music object, so
    /// placing the systems is enough; the menu item exists for creating the
    /// asset on its own. An existing asset is never overwritten — its clip
    /// and fades may be hand-tuned. Mirrors the city's RadioAssetBuilder.
    /// </summary>
    public static class MusicAssetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";
        public const string AssetPath = ResourcesFolder + "/" + MusicSettings.ResourcePath + ".asset";

        /// <summary>The one runner track. Left where it was authored — moving it would only churn its guid.</summary>
        public const string ClipPath = "Assets/07.Audio/01.SFX/FiniteRunner/Music Finite runner.mp3";

        [MenuItem("Tools/FiniteRunner/Create Music Settings")]
        public static void CreateFromMenu()
        {
            MusicSettings settings = CreateOrLoad();
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"MusicAssetBuilder: music settings at {AssetPath} (clip: {(settings.clip != null ? settings.clip.name : "none")}).", settings);
        }

        /// <summary>The settings asset — loaded when it exists, otherwise created with the soundtrack clip wired.</summary>
        public static MusicSettings CreateOrLoad()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MusicSettings>(AssetPath);
            if (settings != null) return settings;

            EnsureFolder(ResourcesFolder);
            settings = ScriptableObject.CreateInstance<MusicSettings>();
            settings.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (settings.clip == null)
                Debug.LogWarning($"MusicAssetBuilder: no clip at {ClipPath} — assign one on the asset.");
            AssetDatabase.CreateAsset(settings, AssetPath);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
