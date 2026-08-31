using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.PoliceEscape.Audio;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Creates the radio's settings asset in Resources and fetches the bundled
    /// songs into it the first time. <c>SceneSystemsPlacer</c> calls
    /// <see cref="CreateOrLoad"/> when it places the radio, so placing the
    /// systems is enough; the menu item exists for creating the asset on its
    /// own. An existing asset is never overwritten — its playlist may be
    /// hand-tuned.
    /// </summary>
    public static class RadioAssetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";
        public const string AssetPath = ResourcesFolder + "/" + RadioSettings.ResourcePath + ".asset";

        [MenuItem("Tools/Police Escape/Create Radio Settings")]
        public static void CreateFromMenu()
        {
            RadioSettings settings = CreateOrLoad();
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"RadioAssetBuilder: radio settings at {AssetPath} ({settings.songs.Count} song(s)).", settings);
        }

        /// <summary>The settings asset — loaded when it exists, otherwise created with the InGame folder's songs fetched.</summary>
        public static RadioSettings CreateOrLoad()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RadioSettings>(AssetPath);
            if (settings != null) return settings;

            EnsureFolder(ResourcesFolder);
            settings = ScriptableObject.CreateInstance<RadioSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            settings.FetchSongs();
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
