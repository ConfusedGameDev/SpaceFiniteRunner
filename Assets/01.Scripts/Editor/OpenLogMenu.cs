using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EditorTools
{
    /// <summary>
    /// Tools menu shortcuts to Unity's log files.
    /// Put this file anywhere under an "Editor" folder, e.g. Assets/Editor/OpenLogMenu.cs
    /// </summary>
    public static class OpenLogMenu
    {
        // ---------------------------------------------------------------- menu

        /// <summary>Reveals Editor.log in Explorer/Finder with the file selected.</summary>
        [MenuItem("Tools/Open Log", priority = 100)]
        private static void OpenLog()
        {
            Reveal(EditorLogPath, "Editor log");
        }

        [MenuItem("Tools/Open Log File in Text Editor", priority = 101)]
        private static void OpenLogInTextEditor()
        {
            OpenFile(EditorLogPath, "Editor log");
        }

        [MenuItem("Tools/Open Log (Previous Session)", priority = 102)]
        private static void OpenPreviousLog()
        {
            Reveal(PreviousEditorLogPath, "previous Editor log");
        }

        [MenuItem("Tools/Open Player Log", priority = 120)]
        private static void OpenPlayerLog()
        {
            Reveal(PlayerLogPath, "Player log");
        }

        [MenuItem("Tools/Copy Editor Log Path", priority = 140)]
        private static void CopyEditorLogPath()
        {
            EditorGUIUtility.systemCopyBuffer = EditorLogPath;
            Debug.Log($"[Open Log] Copied to clipboard: {EditorLogPath}");
        }

        // ---------------------------------------------------------------- paths

        private static string EditorLogPath
        {
            get
            {
                // Authoritative on 2019.1+, but empty in some batch-mode/-logFile setups.
                var path = Application.consoleLogPath;
                return string.IsNullOrEmpty(path)
                    ? Path.Combine(EditorLogFolder, "Editor.log")
                    : path;
            }
        }

        private static string PreviousEditorLogPath
        {
            get
            {
                var dir = Path.GetDirectoryName(EditorLogPath);
                return Path.Combine(string.IsNullOrEmpty(dir) ? EditorLogFolder : dir, "Editor-prev.log");
            }
        }

        private static string EditorLogFolder
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Unity", "Editor");

                    case RuntimePlatform.OSXEditor:
                        return Path.Combine(Home, "Library", "Logs", "Unity");

                    default: // Linux
                        return Path.Combine(Home, ".config", "unity3d");
                }
            }
        }

        private static string PlayerLogPath
        {
            get
            {
                var company = PlayerSettings.companyName;
                var product = PlayerSettings.productName;

                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return Path.Combine(Home, "AppData", "LocalLow", company, product, "Player.log");

                    case RuntimePlatform.OSXEditor:
                        return Path.Combine(Home, "Library", "Logs", company, product, "Player.log");

                    default: // Linux
                        return Path.Combine(Home, ".config", "unity3d", company, product, "Player.log");
                }
            }
        }

        private static string Home =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ---------------------------------------------------------------- helpers

        private static void Reveal(string path, string label)
        {
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Debug.LogWarning($"[Open Log] No {label} at:\n{path}\nOpening the folder instead.");
                // Trailing separator makes RevealInFinder open the folder itself.
                EditorUtility.RevealInFinder(dir + Path.DirectorySeparatorChar);
                return;
            }

            EditorUtility.DisplayDialog(
                "Open Log",
                $"Could not find the {label} or its folder:\n\n{path}",
                "OK");
        }

        private static void OpenFile(string path, string label)
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            Reveal(path, label);
        }
    }
}
