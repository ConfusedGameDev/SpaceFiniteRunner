using System.IO;
using ConfusedGameDev.FiniteRunner.SaveData;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Tools → FiniteRunner → Save Data: the developer's handles on the
    /// player profile file. Delete Save also drops the in-memory cache —
    /// with domain reload off, a cached profile would otherwise be written
    /// straight back at the next commit point.
    /// </summary>
    public static class SaveDataMenu
    {
        const string Root = "Tools/FiniteRunner/Save Data/";

        [MenuItem(Root + "Open Folder")]
        static void OpenFolder()
        {
            string dir = Path.GetDirectoryName(PlayerProfileStore.FilePath);
            Directory.CreateDirectory(dir);
            // A trailing separator makes RevealInFinder open the folder itself.
            EditorUtility.RevealInFinder(dir + Path.DirectorySeparatorChar);
        }

        [MenuItem(Root + "Print Profile")]
        static void PrintProfile()
        {
            Debug.Log($"[SaveData] {PlayerProfileStore.FilePath}\n{JsonUtility.ToJson(PlayerProfileStore.Profile, true)}");
        }

        [MenuItem(Root + "Delete Save")]
        static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("Delete Save Data",
                    $"Delete the player profile?\n\n{PlayerProfileStore.FilePath}\n\nStats, records and level progression start from zero. The .bak copy is deleted too.",
                    "Delete", "Cancel"))
                return;
            PlayerProfileStore.DeleteFile();
            Debug.Log("[SaveData] profile deleted — the next play starts fresh.");
        }
    }
}
