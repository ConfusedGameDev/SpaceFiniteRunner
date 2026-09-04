using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// The mission being played right now — how a scene knows which levels
    /// to run. Set by the Store's START MISSION and the MISSIONS map before
    /// the city scene loads; read by the city's <c>LevelManager</c> and the
    /// runner's <c>GameManager</c> in <c>Awake</c>, which swap their
    /// serialized level for the session's. Empty (direct scene play from the
    /// editor) means both managers use their serialized assets exactly as
    /// before, so the campaign never gets in the way of testing a scene.
    /// Cleared whenever the main menu comes up — reaching it ends the
    /// mission — and at subsystem registration, since domain reload is off.
    /// </summary>
    public static class MissionSession
    {
        /// <summary>The mission in play, or null when no mission is live.</summary>
        public static MissionDefinition Current { get; private set; }

        /// <summary>True when the mission was already complete when it was started (a replay from the MISSIONS map).</summary>
        public static bool IsReplay { get; private set; }

        /// <summary>True while a mission is live.</summary>
        public static bool Active => Current != null;

        /// <summary>Starts a mission session; the caller loads the world's scene next.</summary>
        public static void Begin(MissionDefinition mission, bool replay)
        {
            Current = mission;
            IsReplay = replay && mission != null;
        }

        /// <summary>Ends the session — the main menu calls this on entry.</summary>
        public static void Clear()
        {
            Current = null;
            IsReplay = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();
    }
}
