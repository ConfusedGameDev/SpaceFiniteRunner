using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema
{
    /// <summary>Edge a cinema holder slides in from (and back out through). Order is the save format — append only.</summary>
    public enum SlideDirection { None = 0, Left = 1, Right = 2, Top = 3, Bottom = 4 }

    /// <summary>
    /// One way of putting a cinema on screen: WHERE the video sits (a
    /// normalized viewport rect the holder is anchored to), how it arrives
    /// (slide edge + seconds), how much the world dims behind it, and the
    /// panel's shape (an optional fixed aspect fitted inside the viewport —
    /// 1 makes the squared panel square whatever the screen — plus a frame).
    /// Pure data: the <see cref="CinemaSystem"/> builds one holder per entry
    /// and never reads anything else, which is what makes a new format a new
    /// list row rather than new code.
    /// </summary>
    [System.Serializable]
    public class CinemaFormat
    {
        [Tooltip("Name a LevelObjective picks this format by. Keep it unique.")]
        public string id = "FullScreen";

        [Tooltip("Screen area the holder is anchored to, normalized (0,0 bottom-left, 1,1 top-right): x, y, width, height.")]
        public Rect viewport = new(0f, 0f, 1f, 1f);

        [Tooltip("Edge the holder slides in from; None pops it in place.")]
        [EnumToggleButtons]
        public SlideDirection slideFrom = SlideDirection.None;

        [Tooltip("Length of the slide in (and out).")]
        [PropertyRange(0f, 1.5f), SuffixLabel("s", true)]
        public float slideSeconds = 0.35f;

        [Tooltip("How dark the world goes behind the holder: 1 blacks it out, 0 leaves it fully visible.")]
        [PropertyRange(0f, 1f)]
        public float backdropAlpha = 0.35f;

        [Tooltip("Panel aspect (width ÷ height) fitted inside the viewport; 0 fills the viewport as is. 1 = square.")]
        [PropertyRange(0f, 4f)]
        public float fixedAspect;

        [Tooltip("Letterbox the clip inside the panel instead of stretching it.")]
        public bool keepClipAspect = true;

        [Tooltip("Frame drawn behind the video, visible as a border of Frame Padding pixels.")]
        public Color frameColor = new(0.05f, 0.07f, 0.09f, 0.95f);

        [Tooltip("Border between the panel edge and the video, in reference pixels (1920×1080).")]
        [PropertyRange(0f, 24f)]
        public float framePadding = 6f;
    }

    /// <summary>
    /// The catalogue of cinema display formats, one list so adding a layout
    /// is adding a row. Loaded from Resources the way the rain / fog settings
    /// are (an in-memory default keeps cinemas working with no asset), and
    /// the objective inspector's format dropdown reads <see cref="Ids"/> off
    /// it — through a cached load, never a throwaway instance, because that
    /// getter runs on every inspector repaint. The skip hold length lives
    /// here too: it is a property of the gesture, not of one level.
    /// </summary>
    [CreateAssetMenu(fileName = "PoliceEscape_CinemaFormats", menuName = "PoliceEscape/Cinema Format Library")]
    public class CinemaFormatLibrary : ScriptableObject
    {
        public const string ResourcePath = "PoliceEscape_CinemaFormats";
        public const string FullScreenId = "FullScreen";

        /// <summary>The ids the defaults ship with — the dropdown's answer when no asset exists yet.</summary>
        static readonly string[] DefaultIds = { FullScreenId, "RearMirror", "SquareLeft", "BannerRight" };

        static CinemaFormatLibrary cachedForIds;

        [TitleGroup("Formats")]
        [Tooltip("Every way a cinema can be shown. Objectives pick one by id.")]
        [ListDrawerSettings(DraggableItems = true, ListElementLabelName = nameof(CinemaFormat.id))]
        public List<CinemaFormat> formats = new();

        [TitleGroup("Skip")]
        [Tooltip("How long Enter / A must be held to skip a cinema — the ring fills over this time.")]
        [PropertyRange(0.3f, 3f), SuffixLabel("s", true)]
        public float skipHoldSeconds = 1f;

        /// <summary>The format under this id, or false when the library has no such row.</summary>
        public bool TryGet(string id, out CinemaFormat format)
        {
            if (formats != null)
                foreach (CinemaFormat entry in formats)
                    if (entry != null && entry.id == id)
                    {
                        format = entry;
                        return true;
                    }
            format = null;
            return false;
        }

        /// <summary>The shipped asset from Resources, or an in-memory default so a project with no asset still plays cinemas.</summary>
        public static CinemaFormatLibrary Load()
        {
            var asset = Resources.Load<CinemaFormatLibrary>(ResourcePath);
            return asset != null ? asset : CreateDefault();
        }

        /// <summary>A throwaway instance on the four default formats — never written to disk.</summary>
        public static CinemaFormatLibrary CreateDefault()
        {
            var library = CreateInstance<CinemaFormatLibrary>();
            library.name = "CinemaFormatLibrary (default)";
            SeedDefaults(library);
            return library;
        }

        /// <summary>
        /// Fills an empty list with the four authored layouts — full screen,
        /// the rear-mirror band at the top, a square panel sliding in from the
        /// left and a wide banner sliding in from the right. Leaves an
        /// authored list alone; shared by <see cref="CreateDefault"/> and the
        /// editor asset builder.
        /// </summary>
        public static void SeedDefaults(CinemaFormatLibrary library)
        {
            if (library.formats == null) library.formats = new List<CinemaFormat>();
            if (library.formats.Count > 0) return;
            library.formats.Add(new CinemaFormat
            {
                id = FullScreenId, viewport = new Rect(0f, 0f, 1f, 1f), slideFrom = SlideDirection.None,
                slideSeconds = 0.25f, backdropAlpha = 1f, fixedAspect = 0f, keepClipAspect = true, framePadding = 0f
            });
            library.formats.Add(new CinemaFormat
            {
                id = "RearMirror", viewport = new Rect(0.3f, 0.78f, 0.4f, 0.2f), slideFrom = SlideDirection.Top,
                slideSeconds = 0.35f, backdropAlpha = 0.35f, fixedAspect = 0f, keepClipAspect = true, framePadding = 6f
            });
            library.formats.Add(new CinemaFormat
            {
                id = "SquareLeft", viewport = new Rect(0.04f, 0.3f, 0.3f, 0.4f), slideFrom = SlideDirection.Left,
                slideSeconds = 0.35f, backdropAlpha = 0.35f, fixedAspect = 1f, keepClipAspect = true, framePadding = 6f
            });
            library.formats.Add(new CinemaFormat
            {
                id = "BannerRight", viewport = new Rect(0.35f, 0.06f, 0.6f, 0.2f), slideFrom = SlideDirection.Right,
                slideSeconds = 0.35f, backdropAlpha = 0.35f, fixedAspect = 0f, keepClipAspect = true, framePadding = 6f
            });
        }

        /// <summary>
        /// Format ids for the objective inspector's dropdown. Reads the
        /// Resources asset through a cache (an inspector getter must not
        /// allocate) and falls back to the default ids when there is none.
        /// </summary>
        public static IEnumerable<string> Ids()
        {
            if (cachedForIds == null) cachedForIds = Resources.Load<CinemaFormatLibrary>(ResourcePath);
            if (cachedForIds == null || cachedForIds.formats == null || cachedForIds.formats.Count == 0)
                return DefaultIds;

            var ids = new List<string>(cachedForIds.formats.Count);
            foreach (CinemaFormat entry in cachedForIds.formats)
                if (entry != null && !string.IsNullOrEmpty(entry.id)) ids.Add(entry.id);
            return ids.Count > 0 ? ids : DefaultIds;
        }
    }
}
