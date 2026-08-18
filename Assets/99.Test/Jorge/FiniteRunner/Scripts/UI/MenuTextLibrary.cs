using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>The languages the menu ships in. Order is the save format — never reorder, only append.</summary>
    public enum MenuLanguage { English = 0, Spanish = 1, Japanese = 2, French = 3 }

    /// <summary>Every string the menu can display. New screens add ids here and entries on the library asset.</summary>
    public enum MenuTextId
    {
        Start, Settings, Cheats, Credits, Exit,
        MasterVolume, MusicVolume, FxVolume, Subtitles, Language,
        On, Off,
        NothingHereYet,
        AreYouSure, Yes, No,
        PressEnter, PressStart,
        HintMove, HintSelect, HintBack, HintChange, HintCancel, HintTitle,
        RoleMaster, RoleFool,
        Paused, Resume, ExitToMenu, QuitGame
    }

    /// <summary>One menu string in all four languages. Missing translations fall back to English rather than showing blank.</summary>
    [Serializable]
    public struct LocalizedString
    {
        public string english;
        public string spanish;
        public string japanese;
        public string french;

        public LocalizedString(string en, string es, string ja, string fr)
        {
            english = en;
            spanish = es;
            japanese = ja;
            french = fr;
        }

        public string Get(MenuLanguage language)
        {
            string text = language switch
            {
                MenuLanguage.Spanish => spanish,
                MenuLanguage.Japanese => japanese,
                MenuLanguage.French => french,
                _ => english
            };
            return string.IsNullOrEmpty(text) ? english : text;
        }
    }

    /// <summary>
    /// Every text of the main menu in one designer-facing asset, in all four
    /// languages. The menu never hardcodes a display string: widgets fetch by
    /// <see cref="MenuTextId"/> for the current <see cref="UserSettings.Language"/>,
    /// and a <see cref="LocalizedLabel"/> on each Text re-fetches when the
    /// language row changes — so translations are edited here, not in code.
    /// Loaded from Resources like <see cref="MenuTheme"/>; the C# defaults ARE
    /// the shipped translations, so a fresh asset starts fully translated.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_MenuTexts", menuName = "FiniteRunner/Menu Text Library")]
    public class MenuTextLibrary : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_MenuTexts";

        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString start = new("START", "INICIAR", "スタート", "DÉMARRER");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString settings = new("SETTINGS", "AJUSTES", "設定", "OPTIONS");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString cheats = new("CHEATS", "TRUCOS", "チート", "TRICHES");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString credits = new("CREDITS", "CRÉDITOS", "クレジット", "CRÉDITS");
        [TitleGroup("Main menu rows")]
        [SerializeField] LocalizedString exit = new("EXIT", "SALIR", "終了", "QUITTER");

        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString masterVolume = new("MASTER VOLUME", "VOLUMEN GENERAL", "マスター音量", "VOLUME GÉNÉRAL");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString musicVolume = new("MUSIC VOLUME", "VOLUMEN DE MÚSICA", "ミュージック音量", "VOLUME MUSIQUE");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString fxVolume = new("FX VOLUME", "VOLUMEN DE EFECTOS", "効果音音量", "VOLUME EFFETS");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString subtitles = new("SUBTITLES", "SUBTÍTULOS", "字幕", "SOUS-TITRES");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString language = new("LANGUAGE", "IDIOMA", "言語", "LANGUE");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString on = new("ON", "SÍ", "オン", "OUI");
        [TitleGroup("Settings rows")]
        [SerializeField] LocalizedString off = new("OFF", "NO", "オフ", "NON");

        [TitleGroup("Screens")]
        [SerializeField] LocalizedString nothingHereYet = new("NOTHING HERE YET", "AQUÍ NO HAY NADA AÚN", "まだ何もない", "RIEN ICI POUR L'INSTANT");
        [TitleGroup("Screens")]
        [SerializeField] LocalizedString areYouSure = new("ARE YOU SURE?", "¿SEGURO QUE QUIERES SALIR?", "本当に終了する？", "VOUS ÊTES SÛR ?");
        [TitleGroup("Screens")]
        [SerializeField] LocalizedString yes = new("YES", "SÍ", "はい", "OUI");
        [TitleGroup("Screens")]
        [SerializeField] LocalizedString no = new("NO", "NO", "いいえ", "NON");

        [TitleGroup("Attract prompt")]
        [SerializeField] LocalizedString pressEnter = new("PRESS ENTER", "PULSA ENTER", "ENTERキーを押してください", "APPUYEZ SUR ENTRÉE");
        [TitleGroup("Attract prompt")]
        [SerializeField] LocalizedString pressStart = new("PRESS START", "PULSA START", "スタートボタンを押してください", "APPUYEZ SUR START");

        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintMove = new("MOVE", "MOVER", "移動", "DÉPLACER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintSelect = new("SELECT", "ELEGIR", "決定", "VALIDER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintBack = new("BACK", "ATRÁS", "戻る", "RETOUR");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintChange = new("CHANGE", "CAMBIAR", "変更", "MODIFIER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintCancel = new("CANCEL", "CANCELAR", "キャンセル", "ANNULER");
        [TitleGroup("Footer hints")]
        [SerializeField] LocalizedString hintTitle = new("TITLE", "TÍTULO", "タイトルへ", "TITRE");

        [TitleGroup("Credits")]
        [SerializeField] LocalizedString roleMaster = new("MASTER OF DISASTER", "MAESTRO DEL DESASTRE", "ディザスターマスター", "MAÎTRE DU DÉSASTRE");
        [TitleGroup("Credits")]
        [SerializeField] LocalizedString roleFool = new("TOWN FOOL", "EL TONTO DEL PUEBLO", "町の道化師", "L'IDIOT DU VILLAGE");

        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString paused = new("PAUSED", "PAUSA", "ポーズ", "PAUSE");
        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString resume = new("RESUME", "CONTINUAR", "再開", "REPRENDRE");
        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString exitToMenu = new("EXIT TO MAIN MENU", "SALIR AL MENÚ PRINCIPAL", "メインメニューへ", "RETOUR AU MENU");
        [TitleGroup("Pause menu")]
        [SerializeField] LocalizedString quitGame = new("QUIT GAME", "SALIR DEL JUEGO", "ゲームを終了", "QUITTER LE JEU");

        static MenuTextLibrary cached;

        /// <summary>The library asset, or a throwaway on the C# defaults if none is in a Resources folder.</summary>
        public static MenuTextLibrary Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<MenuTextLibrary>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(MenuTextLibrary)} at Resources/{ResourcePath} — using the built-in defaults.");
                cached = CreateInstance<MenuTextLibrary>();
            }
            return cached;
        }

        /// <summary>The string for an id in the given language.</summary>
        public string Get(MenuTextId id, MenuLanguage lang) => Entry(id).Get(lang);

        /// <summary>The string for an id in the player's current language.</summary>
        public string Get(MenuTextId id) => Get(id, UserSettings.Language);

        /// <summary>
        /// How a language names itself on the selector row — always in its own
        /// language, so a player lost in the wrong one can still find home.
        /// </summary>
        public static string LanguageDisplayName(MenuLanguage lang) => lang switch
        {
            MenuLanguage.Spanish => "ESPAÑOL",
            MenuLanguage.Japanese => "日本語",
            MenuLanguage.French => "FRANÇAIS",
            _ => "ENGLISH"
        };

        LocalizedString Entry(MenuTextId id) => id switch
        {
            MenuTextId.Start => start,
            MenuTextId.Settings => settings,
            MenuTextId.Cheats => cheats,
            MenuTextId.Credits => credits,
            MenuTextId.Exit => exit,
            MenuTextId.MasterVolume => masterVolume,
            MenuTextId.MusicVolume => musicVolume,
            MenuTextId.FxVolume => fxVolume,
            MenuTextId.Subtitles => subtitles,
            MenuTextId.Language => language,
            MenuTextId.On => on,
            MenuTextId.Off => off,
            MenuTextId.NothingHereYet => nothingHereYet,
            MenuTextId.AreYouSure => areYouSure,
            MenuTextId.Yes => yes,
            MenuTextId.No => no,
            MenuTextId.PressEnter => pressEnter,
            MenuTextId.PressStart => pressStart,
            MenuTextId.HintMove => hintMove,
            MenuTextId.HintSelect => hintSelect,
            MenuTextId.HintBack => hintBack,
            MenuTextId.HintChange => hintChange,
            MenuTextId.HintCancel => hintCancel,
            MenuTextId.HintTitle => hintTitle,
            MenuTextId.RoleMaster => roleMaster,
            MenuTextId.RoleFool => roleFool,
            MenuTextId.Paused => paused,
            MenuTextId.Resume => resume,
            MenuTextId.ExitToMenu => exitToMenu,
            MenuTextId.QuitGame => quitGame,
            _ => start
        };
    }

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
