using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.SaveData
{
    /// <summary>
    /// The one MonoBehaviour the save system needs: a hidden, scene-persistent
    /// object (the <c>UserSettingsBootstrap</c> shape) that ticks total play
    /// time and makes sure recorded stats reach the disk even when no menu
    /// commit point does — a periodic autosave, and a flush when the app
    /// pauses, loses focus, quits, or a scene unloads.
    ///
    /// <b>Play-time rule</b>: unscaled seconds accumulate only while a
    /// gameplay scene is active (build index ≠ 0 — the main menu is 0) and
    /// scaled time runs. Every menu, the game-over screen, the mission brief
    /// and a cinema write <c>timeScale = 0</c>, so none of them count; the
    /// air-time slow-mo (0.35) counts at real time; the loading curtain sets
    /// timeScale 1 but flags <see cref="PlayerStats.SuspendPlayTime"/>.
    /// Seconds are handed to the profile once a second, not every frame.
    /// Created from <see cref="Boot"/> on every play; never place one by hand.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PlayerProfileBootstrap : MonoBehaviour
    {
        const float AutosaveSeconds = 60f;

        static PlayerProfileBootstrap instance;

        double pendingSeconds;
        float autosaveTimer;

        // Every play session starts from the file, never from a cache the
        // previous session (domain reload is off) or an editor preview left.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            PlayerProfileStore.Invalidate();
            Ensure();
        }

        /// <summary>Creates the singleton if missing. Play mode only — an editor preview of a menu must not spawn it.</summary>
        public static void Ensure()
        {
            if (instance != null || !Application.isPlaying) return;
            var go = new GameObject(nameof(PlayerProfileBootstrap))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PlayerProfileBootstrap>();
        }

        /// <summary>True while the current frame counts as play (see the class rule).</summary>
        public static bool CountsPlayTime =>
            !PlayerStats.SuspendPlayTime
            && Time.timeScale > 0f
            && SceneManager.GetActiveScene().buildIndex != 0;

        void OnEnable() => SceneManager.sceneUnloaded += OnSceneUnloaded;

        void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Flush();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (CountsPlayTime) pendingSeconds += dt;
            if (pendingSeconds >= 1d)
            {
                PlayerStats.AddPlayTime(pendingSeconds);
                pendingSeconds = 0d;
            }

            autosaveTimer += dt;
            if (autosaveTimer >= AutosaveSeconds)
            {
                autosaveTimer = 0f;
                PlayerProfileStore.SaveIfDirty();
            }
        }

        void OnSceneUnloaded(Scene scene) => Flush();

        void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) Flush();
        }

        void OnApplicationQuit() => Flush();

        // Bank the partial second first so a quit never drops it, then write.
        void Flush()
        {
            if (pendingSeconds > 0d)
            {
                PlayerStats.AddPlayTime(pendingSeconds);
                pendingSeconds = 0d;
            }
            PlayerProfileStore.SaveIfDirty();
        }
    }
}
