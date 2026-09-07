using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Audio;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates the runner's sound-effects settings asset in Resources, seeded
    /// with the power-up, engine and jump clips, the first time. The
    /// <c>ShipAudio</c> component is added to the ship at play time and finds
    /// the asset by name, so nothing places it in a scene — this menu item is
    /// the whole setup. An existing asset is never overwritten — its clips
    /// and bands may be hand-tuned. Mirrors <see cref="MusicAssetBuilder"/>.
    /// </summary>
    public static class SfxAssetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";
        public const string AssetPath = ResourcesFolder + "/" + RunnerSfxSettings.ResourcePath + ".asset";

        const string ClipFolder = "Assets/07.Audio/01.SFX/FiniteRunner/";
        public const string PowerUpClipPath = ClipFolder + "PowerUpFX.ogg";
        public const string EngineClipPath = ClipFolder + "FiniteRunnerEngine.ogg";
        public const string JumpClipPath = ClipFolder + "doorOpen_002.ogg";

        [MenuItem("Tools/FiniteRunner/Create Sound Effects Settings")]
        public static void CreateFromMenu()
        {
            RunnerSfxSettings settings = CreateOrLoad();
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"SfxAssetBuilder: sound effects settings at {AssetPath}.", settings);
        }

        /// <summary>The settings asset — loaded when it exists, otherwise created with the three clips wired.</summary>
        public static RunnerSfxSettings CreateOrLoad()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RunnerSfxSettings>(AssetPath);
            if (settings != null) return settings;

            EnsureFolder(ResourcesFolder);
            settings = ScriptableObject.CreateInstance<RunnerSfxSettings>();
            settings.powerUpClip = LoadClip(PowerUpClipPath);
            settings.engineClip = LoadClip(EngineClipPath);
            settings.jumpClip = LoadClip(JumpClipPath);
            AssetDatabase.CreateAsset(settings, AssetPath);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static AudioClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"SfxAssetBuilder: no clip at {path} — assign one on the asset.");
            return clip;
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
