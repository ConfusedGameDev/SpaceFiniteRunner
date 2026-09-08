using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Audio;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates the music settings assets in Resources, each seeded with its
    /// clip, the first time: the runner's soundtrack and the main menu's.
    /// <see cref="RunnerSceneSystemsPlacer"/> calls <see cref="CreateOrLoad"/>
    /// / <see cref="CreateOrLoadMenu"/> when it places a Music object, so
    /// placing the systems is enough; the menu items exist for creating an
    /// asset on its own. An existing asset is never overwritten — its clip
    /// and fades may be hand-tuned. Mirrors the city's RadioAssetBuilder.
    /// </summary>
    public static class MusicAssetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";
        public const string AssetPath = ResourcesFolder + "/" + MusicSettings.ResourcePath + ".asset";
        public const string MenuAssetPath = ResourcesFolder + "/" + MusicSettings.MenuResourcePath + ".asset";

        /// <summary>The one runner track. Left where it was authored — moving it would only churn its guid.</summary>
        public const string ClipPath = "Assets/07.Audio/01.SFX/FiniteRunner/Music Finite runner.mp3";

        /// <summary>The main menu's loop.</summary>
        public const string MenuClipPath = "Assets/07.Audio/03.Music/MainMenu/MainMenu_Long.mp3";

        [MenuItem("Tools/FiniteRunner/Create Music Settings")]
        public static void CreateFromMenu() => CreateFromMenu(CreateOrLoad(), AssetPath);

        [MenuItem("Tools/FiniteRunner/Create Main Menu Music Settings")]
        public static void CreateMenuFromMenu() => CreateFromMenu(CreateOrLoadMenu(), MenuAssetPath);

        static void CreateFromMenu(MusicSettings settings, string assetPath)
        {
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"MusicAssetBuilder: music settings at {assetPath} (clip: {(settings.clip != null ? settings.clip.name : "none")}).", settings);
        }

        /// <summary>The runner's settings asset — loaded when it exists, otherwise created with the soundtrack clip wired.</summary>
        public static MusicSettings CreateOrLoad() => CreateOrLoad(AssetPath, ClipPath);

        /// <summary>The main menu's settings asset — loaded when it exists, otherwise created with the menu loop wired.</summary>
        public static MusicSettings CreateOrLoadMenu() => CreateOrLoad(MenuAssetPath, MenuClipPath);

        static MusicSettings CreateOrLoad(string assetPath, string clipPath)
        {
            var settings = AssetDatabase.LoadAssetAtPath<MusicSettings>(assetPath);
            if (settings != null) return settings;

            EnsureFolder(ResourcesFolder);
            settings = ScriptableObject.CreateInstance<MusicSettings>();
            settings.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (settings.clip == null)
                Debug.LogWarning($"MusicAssetBuilder: no clip at {clipPath} — assign one on the asset.");
            AssetDatabase.CreateAsset(settings, assetPath);
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
