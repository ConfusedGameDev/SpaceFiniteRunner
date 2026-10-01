using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Sync Track Catalog</c>: makes the
    /// <see cref="TrackSelectCatalog"/> in Resources mirror the saved tracks
    /// in <c>04.Data/FiniteRunner/Tracks/</c> — an entry added for every
    /// track not yet listed (named off the file, difficulty 1), entries
    /// whose track is gone dropped, everything a designer edited (names,
    /// subtitles, difficulty, pictures, order) kept. The asset is created on
    /// first use and saved only when something changed. The
    /// <see cref="TrackCatalogPostprocessor"/> below runs the same sync
    /// whenever a track asset is added, removed or moved in that folder, so
    /// the SELECT COURSE screen never lags the folder.
    /// </summary>
    public static class TrackCatalogSync
    {
        public const string TracksFolder = "Assets/04.Data/FiniteRunner/Tracks";
        public const string AssetPath = "Assets/04.Data/Resources/" + TrackSelectCatalog.ResourcePath + ".asset";

        [MenuItem("Tools/FiniteRunner/Sync Track Catalog")]
        public static void SyncMenu()
        {
            TrackSelectCatalog catalog = Sync();
            if (catalog != null) Selection.activeObject = catalog;
        }

        /// <summary>Creates or loads the catalog and reconciles it with the Tracks folder. Returns the asset.</summary>
        public static TrackSelectCatalog Sync()
        {
            TrackSelectCatalog catalog = CreateOrLoad();
            if (catalog == null) return null;

            var onDisk = new List<TrackLayoutAsset>();
            if (AssetDatabase.IsValidFolder(TracksFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TrackLayoutAsset)}", new[] { TracksFolder }))
                {
                    var track = AssetDatabase.LoadAssetAtPath<TrackLayoutAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (track != null && !onDisk.Contains(track)) onDisk.Add(track);
                }
            }
            onDisk.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            int removed = catalog.entries.RemoveAll(e => e == null || e.track == null || !onDisk.Contains(e.track));
            int added = 0;
            foreach (TrackLayoutAsset track in onDisk)
            {
                if (catalog.IndexOf(track) >= 0) continue;
                catalog.entries.Add(new TrackSelectCatalog.Entry { track = track, displayName = Humanize(track.name), difficulty = 1 });
                added++;
            }

            if (removed > 0 || added > 0)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                Debug.Log($"Sync Track Catalog: {added} added, {removed} removed, {catalog.entries.Count} course(s) listed ({AssetPath}).", catalog);
            }
            else
            {
                Debug.Log($"Sync Track Catalog: up to date — {catalog.entries.Count} course(s) listed ({AssetPath}).", catalog);
            }
            return catalog;
        }

        static TrackSelectCatalog CreateOrLoad()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrackSelectCatalog>(AssetPath);
            if (catalog != null) return catalog;

            EnsureFolder(Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<TrackSelectCatalog>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
            Debug.Log($"Sync Track Catalog: created {AssetPath}.", catalog);
            return catalog;
        }

        // "Track_FiniteRunner_LevelDefinition_3" → "FINITE RUNNER LEVEL DEFINITION 3"; "test4" → "TEST 4".
        static string Humanize(string assetName)
        {
            string name = assetName ?? "";
            if (name.StartsWith("Track_")) name = name.Substring("Track_".Length);
            name = name.Replace('_', ' ').Replace('-', ' ').Trim();
            name = ObjectNames.NicifyVariableName(name);
            // Nicify leaves digits glued to letters ("test4"): split them.
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsDigit(name[i]) && char.IsLetter(name[i - 1])) sb.Append(' ');
                sb.Append(name[i]);
            }
            name = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return string.IsNullOrEmpty(name) ? "COURSE" : name.ToUpperInvariant();
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    /// <summary>
    /// Re-syncs the <see cref="TrackSelectCatalog"/> whenever a track asset is
    /// imported, deleted or moved under the Tracks folder. Deferred a frame
    /// so the database is settled, and gated on that folder, so saving a
    /// track elsewhere (or the catalog itself) never loops.
    /// </summary>
    class TrackCatalogPostprocessor : AssetPostprocessor
    {
        static bool queued;

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (queued) return;
            if (!Touches(importedAssets) && !Touches(deletedAssets) && !Touches(movedAssets) && !Touches(movedFromAssetPaths)) return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                if (!Application.isPlaying) TrackCatalogSync.Sync();
            };
        }

        static bool Touches(string[] paths)
        {
            if (paths == null) return false;
            foreach (string path in paths)
                if (path.StartsWith(TrackCatalogSync.TracksFolder + "/") && path.EndsWith(".asset")) return true;
            return false;
        }
    }
}
