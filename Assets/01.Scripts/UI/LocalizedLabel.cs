using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Keeps one Text in the player's language: re-fetches its string from the
    /// library whenever <see cref="UserSettings.LanguageChanged"/> fires or the
    /// label re-activates. Attach via <see cref="Bind"/> at build time — this
    /// is what lets the code-built menu re-label itself live when the language
    /// row is changed, without rebuilding any screen.
    /// </summary>
    public class LocalizedLabel : MonoBehaviour
    {
        Text target;
        MenuTextId id;

        public static void Bind(Text text, MenuTextId id)
        {
            var label = text.gameObject.AddComponent<LocalizedLabel>();
            label.target = text;
            label.id = id;
            label.Refresh(UserSettings.Language);
        }

        void OnEnable()
        {
            UserSettings.LanguageChanged += Refresh;
            Refresh(UserSettings.Language);
        }

        void OnDisable() => UserSettings.LanguageChanged -= Refresh;

        void Refresh(MenuLanguage lang)
        {
            if (target != null) target.text = MenuTextLibrary.Load().Get(id, lang);
        }
    }
}
