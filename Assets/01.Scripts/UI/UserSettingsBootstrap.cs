using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The one MonoBehaviour <see cref="UserSettings"/> needs: a hidden,
    /// scene-persistent object that re-pushes the saved volumes at the mixer
    /// <b>one frame after</b> every scene load. Everything else in
    /// UserSettings is static and runs before the scene exists — and an
    /// AudioMixer applies its start snapshot on its first audio update,
    /// which lands after BeforeSceneLoad and after sceneLoaded, silently
    /// overwriting any SetFloat made before it. Waiting a frame is what turns
    /// "the slider values are stored" into "the slider values are heard"
    /// on the first frame the player can act.
    /// Created by <see cref="UserSettings"/> on boot; never place one by hand.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class UserSettingsBootstrap : MonoBehaviour
    {
        static UserSettingsBootstrap instance;

        /// <summary>Creates the singleton if missing. Safe to call every boot — statics outlive play sessions with domain reload off.</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject(nameof(UserSettingsBootstrap))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UserSettingsBootstrap>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(PushNextFrame());
        }

        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(PushNextFrame());

        IEnumerator PushNextFrame()
        {
            // Unscaled: a scene can load straight into a paused (timeScale 0)
            // state and the push must still happen.
            yield return null;
            UserSettings.PushAll();
        }
    }
}
