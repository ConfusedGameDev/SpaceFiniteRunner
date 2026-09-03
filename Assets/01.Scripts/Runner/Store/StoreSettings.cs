using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// The Store's one settings asset: the three sections, where START
    /// MISSION goes, and the feel knobs of the model viewer. It lives in a
    /// Resources folder because gameplay resolves upgrade multipliers off the
    /// same sections with no scene reference (<see cref="StoreUpgrades"/>),
    /// the way every other Resources-loaded settings asset works. Cached
    /// statically and reset with the domain-reload-off rule.
    /// </summary>
    [CreateAssetMenu(fileName = "StoreSettings", menuName = "FiniteRunner/Store/Store Settings")]
    public class StoreSettings : ScriptableObject
    {
        /// <summary>Scene name of the store — what the main menu's START loads.</summary>
        public const string SceneName = "Store";

        /// <summary>Resources path of the asset.</summary>
        public const string ResourcePath = "Store/StoreSettings";

        [Title("Flow")]
        [Tooltip("Scene START MISSION loads. One fixed mission for now — a mission list is a later feature.")]
        public string nextMissionScene = "CarTest";

        [Title("Sections")]
        [Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public StoreSection car;
        [Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public StoreSection ship;
        [Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public StoreSection character;

        [Title("Model viewer")]
        [Tooltip("Idle spin of the model, degrees per second.")]
        [PropertyRange(0f, 90f), SuffixLabel("deg/s", true)]
        public float autoSpinDegreesPerSecond = 20f;

        [Tooltip("Seconds without input before the idle spin resumes.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float idleResumeSeconds = 2f;

        [Tooltip("Mouse drag: degrees of yaw per pixel.")]
        [PropertyRange(0.01f, 2f), SuffixLabel("deg/px", true)]
        public float dragDegreesPerPixel = 0.25f;

        [Tooltip("Right stick: degrees of yaw per second at full deflection.")]
        [PropertyRange(10f, 720f), SuffixLabel("deg/s", true)]
        public float stickDegreesPerSecond = 120f;

        [Title("Wallet")]
        [Tooltip("Scale punch on the money counter when a purchase lands.")]
        [PropertyRange(1f, 2f)]
        public float walletPunchScale = 1.25f;

        static StoreSettings cached;
        static bool warned;

        /// <summary>The section of a kind, or null when the asset has none wired.</summary>
        public StoreSection Section(StoreSectionKind kind) => kind switch
        {
            StoreSectionKind.Car => car,
            StoreSectionKind.Ship => ship,
            StoreSectionKind.Character => character,
            _ => null
        };

        /// <summary>The asset from Resources, or null (warned once) when it has not been created yet.</summary>
        public static StoreSettings Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<StoreSettings>(ResourcePath);
            if (cached == null && !warned)
            {
                warned = true;
                Debug.LogWarning($"No {nameof(StoreSettings)} at Resources/{ResourcePath} — run Tools → FiniteRunner → Create Store Scene. Upgrades read as stock until then.");
            }
            return cached;
        }

        // Domain reload is off in this project: statics survive between plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cached = null;
            warned = false;
        }
    }
}
