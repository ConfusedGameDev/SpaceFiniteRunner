using ConfusedGameDev.FiniteRunner.Debugging;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Debugging
{
    /// <summary>
    /// Puts the AI overlays on the <see cref="DebugManager"/> automatically, in
    /// any scene that has a city to look at. No scene wiring: a debug view
    /// nobody has to install is a debug view that is actually used, and the
    /// scenes are rebuilt by tooling often enough that a hand-placed object
    /// would keep going missing.
    ///
    /// The overlays are installed even when <see cref="DebugManager.isDebug"/>
    /// is off — they register with the manager and it holds them disabled — so
    /// that ticking the master switch mid-play brings them up instead of doing
    /// nothing until the next scene load. Outside the editor nothing is
    /// created at all, because <see cref="DebugManager.Instance"/> refuses to
    /// exist there.
    /// </summary>
    static class AiDebugInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!Application.isEditor) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            // Only the city scenes have a road graph and AI cars; the runner
            // has neither, and an empty overlay there is just clutter.
            if (Object.FindAnyObjectByType<CityManager>() == null) return;

            DebugManager manager = DebugManager.Instance;
            if (manager == null) return;

            GameObject host = manager.gameObject;
            if (host.GetComponent<RoadGraphVisualizer>() == null) host.AddComponent<RoadGraphVisualizer>();
            if (host.GetComponent<CarAiVisualizer>() == null) host.AddComponent<CarAiVisualizer>();
        }
    }
}
