using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The saved track the player picked on the SELECT COURSE screen — how
    /// the runner knows which course to play in direct play. Set by
    /// <c>LevelSelectScreen</c> before the runner scene loads; read by
    /// <see cref="GameManager"/> in <c>ResolveRunData</c>, which plays it
    /// over its level's own track. A live <c>MissionSession</c> wins (a
    /// mission is authored whole, track included), so this never reaches a
    /// campaign run. Gameplay never writes the level asset for this: the
    /// pick is a runtime override that lasts until the main menu comes up
    /// (reaching it ends the pick, like the mission session) or the
    /// domain-reload-off subsystem reset.
    /// </summary>
    public static class TrackSelection
    {
        /// <summary>The picked track, or null when the level's own track plays.</summary>
        public static TrackLayoutAsset Current { get; private set; }

        /// <summary>True while a pick is live.</summary>
        public static bool Active => Current != null;

        /// <summary>Sets the pick; the caller loads the runner scene next.</summary>
        public static void Set(TrackLayoutAsset track) => Current = track;

        /// <summary>Ends the pick — the main menu calls this on entry.</summary>
        public static void Clear() => Current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();
    }
}
