using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

namespace FiniteRunner
{
    /// <summary>
    /// Everything the main menu needs from disk, in one designer-facing asset:
    /// fonts, the Kenney panel and Xbox prompt sprites, the palette, every
    /// duration/offset/curve of the motion spec, and the audio mixer.
    ///
    /// The menu builds its whole UI from code on its own canvas (the PauseMenu
    /// pattern this project uses for menus), which means it has no scene
    /// references to hang art or tunables on. This asset is its single hand-off
    /// point — loaded from Resources — and because the values live here rather
    /// than on a runtime-spawned object, retuning the feel in the inspector
    /// survives exiting play mode.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_MenuTheme", menuName = "FiniteRunner/Menu Theme")]
    public class MenuTheme : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_MenuTheme";

        // ------------------------------------------------------------- fonts
        [TitleGroup("Fonts")]
        [Tooltip("Big headings. 03.UI/Font/Kenney Future.ttf.")]
        [SerializeField] Font titleFont;

        [TitleGroup("Fonts")]
        [Tooltip("Menu rows and body copy. 03.UI/Font/Kenney Future Narrow.ttf.")]
        [SerializeField] Font bodyFont;

        // ------------------------------------------------------------ sprites
        [TitleGroup("Panel sprites (03.UI/PNG/Red/Double)")]
        [Tooltip("Background plate behind a menu row. Suggested: button_square_header_notch_rectangle.")]
        [SerializeField] Sprite rowPlate;

        [TitleGroup("Panel sprites (03.UI/PNG/Red/Double)")]
        [Tooltip("Title bar of a sub-screen. Suggested: button_square_header_blade_rectangle_screws.")]
        [SerializeField] Sprite titlePlate;

        [TitleGroup("Panel sprites (03.UI/PNG/Red/Double)")]
        [Tooltip("Empty slider track. Suggested: bar_round_large.")]
        [SerializeField] Sprite sliderTrack;

        [TitleGroup("Panel sprites (03.UI/PNG/Red/Double)")]
        [Tooltip("Filled portion of a slider. Suggested: bar_round_gloss_large.")]
        [SerializeField] Sprite sliderFill;

        [TitleGroup("Panel sprites (03.UI/PNG/Red/Double)")]
        [Tooltip("Box of the subtitles toggle — the kit has no checkbox sprite, so a small bar end stands in. Suggested: bar_square_small_square.")]
        [SerializeField] Sprite toggleBox;

        [TitleGroup("Panel sprites (03.UI/PNG/Red/Double)")]
        [Tooltip("Marker beside the focused row, and the tick inside the toggle. Suggested: crosshair_color_a.")]
        [SerializeField] Sprite selectionMarker;

        // ------------------------------------------------------------- glyphs
        [TitleGroup("Xbox prompt glyphs (03.UI/Xbox Series/Double)")]
        [SerializeField] Sprite glyphConfirm;   // xbox_button_a

        [TitleGroup("Xbox prompt glyphs (03.UI/Xbox Series/Double)")]
        [SerializeField] Sprite glyphBack;      // xbox_button_b

        [TitleGroup("Xbox prompt glyphs (03.UI/Xbox Series/Double)")]
        [SerializeField] Sprite glyphNavigate;  // xbox_dpad_vertical

        [TitleGroup("Xbox prompt glyphs (03.UI/Xbox Series/Double)")]
        [SerializeField] Sprite glyphAdjust;    // xbox_dpad_horizontal

        [TitleGroup("Xbox prompt glyphs (03.UI/Xbox Series/Double)")]
        [Tooltip("Shown under PRESS START on the attract screen while a controller is connected.")]
        [SerializeField] Sprite glyphStart;     // xbox_button_start

        // ------------------------------------------------------------ palette
        [TitleGroup("Palette")]
        [Tooltip("Full-screen backdrop. Keep it near-opaque: the run is frozen behind the menu, not hidden.")]
        [SerializeField] Color backdrop = new(0.03f, 0.04f, 0.07f, 0.97f);

        [TitleGroup("Palette")]
        [SerializeField] Color accent = new(1f, 0.29f, 0.16f, 1f);

        [TitleGroup("Palette")]
        [SerializeField] Color textPrimary = Color.white;

        [TitleGroup("Palette")]
        [SerializeField] Color textDim = new(0.62f, 0.66f, 0.74f, 1f);

        [TitleGroup("Palette")]
        [Tooltip("Tint of an unfocused row plate.")]
        [SerializeField] Color plateIdle = new(1f, 1f, 1f, 0.35f);

        [TitleGroup("Palette")]
        [Tooltip("Tint of the focused row plate.")]
        [SerializeField] Color plateFocused = new(1f, 1f, 1f, 1f);

        // ------------------------------------------------------------- layout
        [TitleGroup("Layout")]
        [PropertyRange(320f, 900f), SuffixLabel("px", true)]
        [SerializeField] float rowWidth = 620f;

        [TitleGroup("Layout")]
        [PropertyRange(48f, 140f), SuffixLabel("px", true)]
        [SerializeField] float rowHeight = 86f;

        [TitleGroup("Layout")]
        [PropertyRange(0f, 60f), SuffixLabel("px", true)]
        [SerializeField] float rowSpacing = 18f;

        [TitleGroup("Layout")]
        [Tooltip("X of the main menu's column at 1920x1080, measured from screen centre.")]
        [PropertyRange(-800f, 0f), SuffixLabel("px", true)]
        [SerializeField] float mainColumnX = -430f;

        // ---------------------------------------------------- motion: entrance
        [TitleGroup("Motion — entrance (attract to main menu)")]
        [Tooltip("How long one item takes to arrive. The whole entrance is this plus the stagger of the last item.")]
        [PropertyRange(0.2f, 2.5f), SuffixLabel("s", true)]
        [SerializeField] float entranceDuration = 0.8f;

        [TitleGroup("Motion — entrance (attract to main menu)")]
        [Tooltip("How far left of its resting place an item starts.")]
        [PropertyRange(0f, 400f), SuffixLabel("px", true)]
        [SerializeField] float entranceSlide = 120f;

        [TitleGroup("Motion — entrance (attract to main menu)")]
        [Tooltip("Delay added per item, so rows arrive one after another instead of as a block.")]
        [PropertyRange(0f, 0.3f), SuffixLabel("s", true)]
        [SerializeField] float entranceStagger = 0.06f;

        [TitleGroup("Motion — entrance (attract to main menu)")]
        [Tooltip("Head start the screen title gets over the rows.")]
        [PropertyRange(0f, 0.5f), SuffixLabel("s", true)]
        [SerializeField] float titleLead = 0.12f;

        [TitleGroup("Motion — entrance (attract to main menu)")]
        [Tooltip("Shape of every eased move. Ease out: quick departure, soft landing.")]
        [SerializeField] AnimationCurve entranceCurve = new(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

        // ------------------------------------------------------- motion: focus
        [TitleGroup("Motion — focus")]
        [Tooltip("Smoothing time of the highlight and of a row's scale/alpha. The highlight never teleports.")]
        [PropertyRange(0.02f, 0.6f), SuffixLabel("s", true)]
        [SerializeField] float focusEaseSeconds = 0.15f;

        [TitleGroup("Motion — focus")]
        [PropertyRange(1f, 1.3f), SuffixLabel("x", true)]
        [SerializeField] float focusScale = 1.06f;

        [TitleGroup("Motion — focus")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float unfocusedAlpha = 0.75f;

        // ----------------------------------------------------- motion: screens
        [TitleGroup("Motion — screen transitions")]
        [Tooltip("Main to sub-screen and back. Input is locked for exactly this long.")]
        [PropertyRange(0.1f, 1f), SuffixLabel("s", true)]
        [SerializeField] float screenTransition = 0.35f;

        [TitleGroup("Motion — screen transitions")]
        [PropertyRange(100f, 1200f), SuffixLabel("px", true)]
        [SerializeField] float screenSlide = 520f;

        // ----------------------------------------------------- motion: attract
        [TitleGroup("Motion — attract prompt")]
        [PropertyRange(0.4f, 4f), SuffixLabel("s per loop", true)]
        [SerializeField] float attractPulseSeconds = 1.6f;

        [TitleGroup("Motion — attract prompt")]
        [Tooltip("Alpha the PRESS prompt pulses between.")]
        [MinMaxSlider(0f, 1f, true)]
        [SerializeField] Vector2 attractPulseAlpha = new(0.35f, 1f);

        // -------------------------------------------------------------- input
        [TitleGroup("Input")]
        [Tooltip("Left stick deflection that counts as a direction.")]
        [PropertyRange(0.1f, 0.9f)]
        [SerializeField] float stickDeadZone = 0.5f;

        [TitleGroup("Input")]
        [Tooltip("How long a direction must be held before it starts repeating.")]
        [PropertyRange(0.1f, 1f), SuffixLabel("s", true)]
        [SerializeField] float repeatDelay = 0.45f;

        [TitleGroup("Input")]
        [Tooltip("Repeat rate once it starts.")]
        [PropertyRange(0.03f, 0.4f), SuffixLabel("s", true)]
        [SerializeField] float repeatInterval = 0.12f;

        [TitleGroup("Input")]
        [Tooltip("Deaf period after any screen opens, so the press that opened it cannot also confirm inside it.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        [SerializeField] float inputGrace = 0.3f;

        // -------------------------------------------------------------- audio
        [TitleGroup("Audio")]
        [Tooltip("Master / Music / SFX mixer. Must expose MasterVolume, MusicVolume and SFXVolume.")]
        [SerializeField] AudioMixer mixer;

        [TitleGroup("Audio")]
        [Tooltip("Group the menu's own blips route through — normally SFX.")]
        [SerializeField] AudioMixerGroup uiOutput;

        [TitleGroup("Audio")]
        [Tooltip("Left empty on purpose: no placeholder audio ships with the menu.")]
        [SerializeField] AudioClip moveClip;

        [TitleGroup("Audio")]
        [SerializeField] AudioClip confirmClip;

        [TitleGroup("Audio")]
        [SerializeField] AudioClip backClip;

        [TitleGroup("Audio")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float uiVolume = 0.8f;

        // ------------------------------------------------------------ haptics
        [TitleGroup("Haptics")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float moveRumble = 0.12f;

        [TitleGroup("Haptics")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float confirmRumble = 0.4f;

        static MenuTheme cached;
        static Font legacyFont;

        /// <summary>
        /// The theme asset, or a throwaway instance on defaults if none is in a
        /// Resources folder — the menu stays usable (flat colours, built-in
        /// font) rather than throwing every frame.
        /// </summary>
        public static MenuTheme Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<MenuTheme>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(MenuTheme)} at Resources/{ResourcePath} — " +
                                 "the main menu falls back to the built-in font and flat colours.");
                cached = CreateInstance<MenuTheme>();
            }
            return cached;
        }

        public Font TitleFont => titleFont != null ? titleFont : Legacy;
        public Font BodyFont => bodyFont != null ? bodyFont : Legacy;

        public Sprite RowPlate => rowPlate;
        public Sprite TitlePlate => titlePlate;
        public Sprite SliderTrack => sliderTrack;
        public Sprite SliderFill => sliderFill;
        public Sprite ToggleBox => toggleBox;
        public Sprite SelectionMarker => selectionMarker;

        public Sprite GlyphConfirm => glyphConfirm;
        public Sprite GlyphBack => glyphBack;
        public Sprite GlyphNavigate => glyphNavigate;
        public Sprite GlyphAdjust => glyphAdjust;
        public Sprite GlyphStart => glyphStart;

        public Color Backdrop => backdrop;
        public Color Accent => accent;
        public Color TextPrimary => textPrimary;
        public Color TextDim => textDim;
        public Color PlateIdle => plateIdle;
        public Color PlateFocused => plateFocused;

        public float RowWidth => rowWidth;
        public float RowHeight => rowHeight;
        public float RowSpacing => rowSpacing;
        public float MainColumnX => mainColumnX;

        public float EntranceDuration => entranceDuration;
        public float EntranceSlide => entranceSlide;
        public float EntranceStagger => entranceStagger;
        public float TitleLead => titleLead;

        public float FocusEaseSeconds => focusEaseSeconds;
        public float FocusScale => focusScale;
        public float UnfocusedAlpha => unfocusedAlpha;

        public float ScreenTransition => screenTransition;
        public float ScreenSlide => screenSlide;

        public float AttractPulseSeconds => attractPulseSeconds;
        public float AttractPulseMin => attractPulseAlpha.x;
        public float AttractPulseMax => attractPulseAlpha.y;

        public float StickDeadZone => stickDeadZone;
        public float RepeatDelay => repeatDelay;
        public float RepeatInterval => repeatInterval;
        public float InputGrace => inputGrace;

        public AudioMixer Mixer => mixer;
        public AudioMixerGroup UiOutput => uiOutput;
        public AudioClip MoveClip => moveClip;
        public AudioClip ConfirmClip => confirmClip;
        public AudioClip BackClip => backClip;
        public float UiVolume => uiVolume;

        public float MoveRumble => moveRumble;
        public float ConfirmRumble => confirmRumble;

        /// <summary>Eased 0..1. Falls back to SmoothStep if the curve has been emptied in the inspector.</summary>
        public float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return entranceCurve != null && entranceCurve.length > 1
                ? entranceCurve.Evaluate(t)
                : Mathf.SmoothStep(0f, 1f, t);
        }

        static Font Legacy => legacyFont != null
            ? legacyFont
            : legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
