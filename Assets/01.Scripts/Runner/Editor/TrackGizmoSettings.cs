using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Per-user switches for the track's Scene-view drawing
    /// (<see cref="TrackGizmos"/>) and the stretch an edit-mode preview builds
    /// (<see cref="TrackGenerator.PreviewRange"/>), kept in EditorPrefs — a
    /// viewing preference, never project data. Edited from the
    /// TrackGenerator's inspector; the range is handed to the generator on
    /// every editor load.
    /// </summary>
    [InitializeOnLoad]
    public static class TrackGizmoSettings
    {
        const string Prefix = "FiniteRunner.TrackGizmos.";

        static TrackGizmoSettings() => TrackGenerator.PreviewRange = BuildRange;

        /// <summary>Metres from the start an edit-mode preview builds (x = from, y = to). Setting it hands it to the generator.</summary>
        public static Vector2 BuildRange
        {
            get => new(EditorPrefs.GetFloat(Prefix + "BuildFrom", 0f), EditorPrefs.GetFloat(Prefix + "BuildTo", 30000f));
            set
            {
                EditorPrefs.SetFloat(Prefix + "BuildFrom", value.x);
                EditorPrefs.SetFloat(Prefix + "BuildTo", value.y);
                TrackGenerator.PreviewRange = value;
            }
        }

        /// <summary>Draw the track at all.</summary>
        public static bool Show
        {
            get => EditorPrefs.GetBool(Prefix + "Show", true);
            set => EditorPrefs.SetBool(Prefix + "Show", value);
        }

        /// <summary>Metres round the Scene camera the edges, colours and placements are drawn in (the centre line is always drawn whole).</summary>
        public static float DetailRadius
        {
            get => EditorPrefs.GetFloat(Prefix + "DetailRadius", 3000f);
            set => EditorPrefs.SetFloat(Prefix + "DetailRadius", value);
        }

        /// <summary>Distance and feature labels (only inside a third of the detail radius).</summary>
        public static bool Labels
        {
            get => EditorPrefs.GetBool(Prefix + "Labels", true);
            set => EditorPrefs.SetBool(Prefix + "Labels", value);
        }

        /// <summary>Icons for the orbs, gates, ramps and coins.</summary>
        public static bool Placements
        {
            get => EditorPrefs.GetBool(Prefix + "Placements", true);
            set => EditorPrefs.SetBool(Prefix + "Placements", value);
        }
    }
}
