using ConfusedGameDev.FiniteRunner.Rendering;
using ConfusedGameDev.FiniteRunner.UI;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Scene-side dial of the PlayStation-1 look. The renderer's
    /// <see cref="PsxLookFeature"/> runs the shader (Hidden/FiniteRunner/PsxLook)
    /// over the finished picture through a shared material asset; this
    /// component writes its <see cref="PsxLookSettings"/> asset plus the live
    /// drive into that material every frame — the <see cref="DistanceFog"/> /
    /// <see cref="VhsTape"/> contract, including the "last one standing zeroes
    /// the material on disable" rule and the feature's HasDriver gate. The
    /// drive is the asset's <c>intensity</c> × gameplay's
    /// <see cref="SetIntensity"/> scale × the player's
    /// <see cref="UserSettings.PsxFilter"/> dial (the VIDEO settings page),
    /// nothing more: the console is either on or it is not. [ExecuteAlways] with <see cref="preview"/> on shows
    /// the look in the Scene view (off by default).
    /// **It is a hand-placed scene object, never spawned**: each scene carries
    /// one next to its DistanceFog, RainSystem, SpeedLines and VhsTape, with
    /// the material and settings asset wired so the look can be tuned before
    /// play; <see cref="Apply"/> only finds it (and errors when it is missing)
    /// and parks it when the owner's settings say off. Tools → FiniteRunner →
    /// Install PSX Look Feature places one in the open scene.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class PsxLook : MonoBehaviour
    {
        public static PsxLook Instance { get; private set; }

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int TargetHeightId = Shader.PropertyToID("_TargetHeight");
        static readonly int ColorBitsId = Shader.PropertyToID("_ColorBits");
        static readonly int DitherId = Shader.PropertyToID("_Dither");
        static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        static readonly int WobbleBlockId = Shader.PropertyToID("_WobbleBlock");
        static readonly int WobbleDepthFalloffId = Shader.PropertyToID("_WobbleDepthFalloff");
        static readonly int SwimId = Shader.PropertyToID("_Swim");
        static readonly int JitterRateId = Shader.PropertyToID("_JitterRate");

        [InlineEditor]
        [Tooltip("Material used by the renderer's PsxLook feature — the same asset the feature holds. Empty = the one on the settings asset (the installer writes it there).")]
        public Material lookMaterial;

        [InlineEditor]
        [Tooltip("Every PSX knob. Empty = the shipped Resources asset (FiniteRunner_PsxLook), or an in-memory default.")]
        public PsxLookSettings settings;

        [Tooltip("Gameplay's ramp on top of the asset's intensity — SetIntensity writes it. 1 = the asset as authored.")]
        [PropertyRange(0f, 1f)]
        public float intensityScale = 1f;

        [Tooltip("Show the look in edit mode too, so the asset can be tuned in the Scene view before play. Off by default: a pixelated Scene view is hard to work in.")]
        public bool preview;

        [ShowIf("preview"), PropertyRange(0f, 1f)]
        [Tooltip("Intensity scale the edit-mode preview shows at (on top of the asset's own intensity).")]
        public float previewIntensity = 1f;

        /// <summary>Effective master intensity written to the material this frame.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float CurrentIntensity { get; private set; }

        /// <summary>
        /// The owner's one call: finds the scene's hand-placed driver (its
        /// wiring and tuning are the designer's), pushes an override asset
        /// onto it when one is given, and parks it when the owner says off.
        /// NEVER creates one — systems live in the scene so they can be tuned
        /// before play; a missing driver is a scene-setup error (run Tools →
        /// FiniteRunner → Install PSX Look Feature). Returns null when off or
        /// missing.
        /// </summary>
        public static PsxLook Apply(bool enabled, PsxLookSettings settings = null)
        {
            PsxLook system = Instance != null
                ? Instance
                : FindAnyObjectByType<PsxLook>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null)
            {
                Debug.LogError($"{nameof(PsxLook)}: the scene has no PsxLook object — place one (Tools → FiniteRunner → Install PSX Look Feature adds it to the open scene). Systems are never spawned at play time.");
                return null;
            }
            if (settings != null) system.settings = settings;
            if (system.settings == null) system.settings = PsxLookSettings.Load();
            if (system.lookMaterial == null) system.lookMaterial = system.settings.material;
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        /// <summary>Gameplay's ramp on the asset's intensity (0..1).</summary>
        public void SetIntensity(float scale) => intensityScale = Mathf.Clamp01(scale);

        void OnEnable()
        {
            Instance = this;
            PsxLookFeature.HasDriver = true;
            if (settings == null) settings = PsxLookSettings.Load();
            if (lookMaterial == null) lookMaterial = settings.material;
            if (Application.isPlaying)
            {
                if (lookMaterial == null)
                    Debug.LogWarning($"{nameof(PsxLook)}: no material to drive — run Tools → FiniteRunner → Install PSX Look Feature (it creates the material and writes it onto the settings asset).", this);
                else
                    RendererFeatureAudit.WarnIfMissing(lookMaterial, nameof(PsxLook), this);
            }
            Write();
        }

        void LateUpdate()
        {
            if (settings == null) return;
            // The player's VIDEO dial is read every frame (no event), so a
            // slider drag in the pause menu shows through the menu live.
            float scale = Application.isPlaying
                ? intensityScale * UserSettings.PsxFilter
                : (preview ? previewIntensity : 0f);
            CurrentIntensity = Mathf.Clamp01(settings.intensity * scale);
            Write();
        }

        void OnDisable()
        {
            // Only the last driver standing cleans the shared material — during
            // an additive scene handoff the incoming scene's driver has already
            // claimed Instance, and zeroing here would blink the picture.
            if (Instance == null || Instance == this)
            {
                if (lookMaterial != null) lookMaterial.SetFloat(IntensityId, 0f);
                Instance = null;
                PsxLookFeature.HasDriver = false;
            }
        }

        void Write()
        {
            if (lookMaterial == null || settings == null) return;
            lookMaterial.SetFloat(IntensityId, CurrentIntensity);
            lookMaterial.SetFloat(TargetHeightId, settings.targetHeight);
            lookMaterial.SetFloat(ColorBitsId, settings.colorBits);
            lookMaterial.SetFloat(DitherId, settings.dither);
            lookMaterial.SetFloat(WobbleId, settings.wobble);
            lookMaterial.SetFloat(WobbleBlockId, settings.wobbleBlock);
            lookMaterial.SetFloat(WobbleDepthFalloffId, settings.wobbleDepthFalloff);
            lookMaterial.SetFloat(SwimId, settings.swim);
            lookMaterial.SetFloat(JitterRateId, settings.jitterRate);
        }
    }
}
