using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Creates the <see cref="CinemaFormatLibrary"/> asset the objective
    /// inspector's format dropdown and the <see cref="CinemaSystem"/> read:
    /// <c>Assets/04.Data/Resources/PoliceEscape_CinemaFormats.asset</c>,
    /// seeded with the four authored formats ONLY when freshly created — an
    /// existing library is never overwritten, the same rule as every other
    /// asset builder here. SceneSystemsPlacer calls <see cref="CreateOrLoad"/>
    /// when it places the cinema system, so placing the systems is enough;
    /// the menu item exists for creating the asset on its own.
    /// </summary>
    public static class CinemaAssetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";
        public const string LibraryPath = ResourcesFolder + "/" + CinemaFormatLibrary.ResourcePath + ".asset";

        [MenuItem("Tools/Police Escape/Create Cinema Format Library")]
        public static void CreateFromMenu()
        {
            CinemaFormatLibrary library = CreateOrLoad();
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(library);
            Debug.Log($"CinemaAssetBuilder: cinema format library at {LibraryPath} ({library.formats.Count} format(s)).", library);
        }

        /// <summary>The library asset — loaded when it exists, otherwise created and seeded with the defaults.</summary>
        public static CinemaFormatLibrary CreateOrLoad()
        {
            var library = AssetDatabase.LoadAssetAtPath<CinemaFormatLibrary>(LibraryPath);
            if (library != null) return library;

            EnsureFolder(ResourcesFolder);
            library = ScriptableObject.CreateInstance<CinemaFormatLibrary>();
            CinemaFormatLibrary.SeedDefaults(library);
            AssetDatabase.CreateAsset(library, LibraryPath);
            EditorUtility.SetDirty(library);
            return library;
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
