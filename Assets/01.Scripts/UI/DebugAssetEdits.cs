using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Persistence for debug-menu edits. The rule every debug page follows:
    /// gameplay never writes a settings asset, the debug menu does — it writes
    /// the asset, then mirrors the value onto the run's live clone. This class
    /// is the "writes the asset" half: <see cref="Touch"/> marks an edited
    /// asset dirty, and <see cref="Flush"/> saves every touched asset at the
    /// pause menu's commit points (resume, reload scene), not on every slider
    /// tick. In a build nothing can be saved, so edits last for the session.
    /// </summary>
    public static class DebugAssetEdits
    {
#if UNITY_EDITOR
        static readonly List<Object> touched = new();
#endif

        /// <summary>Marks <paramref name="asset"/> dirty so the next <see cref="Flush"/> writes it. Runtime-only objects (clones) are ignored.</summary>
        public static void Touch(Object asset)
        {
#if UNITY_EDITOR
            if (asset == null || !UnityEditor.EditorUtility.IsPersistent(asset)) return;
            UnityEditor.EditorUtility.SetDirty(asset);
            if (!touched.Contains(asset)) touched.Add(asset);
#endif
        }

        /// <summary>Writes every touched asset to disk (editor only).</summary>
        public static void Flush()
        {
#if UNITY_EDITOR
            foreach (var asset in touched)
                if (asset != null) UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
            touched.Clear();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
#if UNITY_EDITOR
            touched.Clear();
#endif
        }
    }
}
