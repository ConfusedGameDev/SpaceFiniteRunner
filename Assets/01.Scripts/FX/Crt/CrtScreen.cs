using ConfusedGameDev.FiniteRunner.Rendering;
using ConfusedGameDev.FiniteRunner.UI;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Scene-side dial of the CRT screen. The renderer's
    /// <see cref="CrtScreenFeature"/> runs the shader (Hidden/FiniteRunner/CrtScreen)
    /// over the finished picture through a shared material asset; this
    /// component writes its <see cref="CrtScreenSettings"/> asset plus the
    /// live drive into that material every frame — the <see cref="DistanceFog"/>
    /// / <see cref="VhsTape"/> / <see cref="PsxLook"/> contract, including the
    /// "last one standing zeroes the material on disable" rule and the
    /// feature's HasDriver gate. The drive is the asset's <c>intensity</c> ×
    /// gameplay's <see cref="SetIntensity"/> scale × the player's
    /// <see cref="UserSettings.CrtFilter"/> dial from the VIDEO settings page,
    /// nothing more: the tube is either on or it is not. [ExecuteAlways] with
    /// <see cref="preview"/> on shows the tube in the Scene view (off by
    /// default).
    /// **It is a hand-placed scene object, never spawned**: each scene carries
    /// one next to its DistanceFog, RainSystem, SpeedLines, VhsTape and
    /// PsxLook, with the material and settings asset wired so the look can be
    /// tuned before play; <see cref="Apply"/> only finds it (and errors when
    /// it is missing) and parks it when the owner's settings say off. Tools →
    /// FiniteRunner → Install CRT Screen Feature places one in the open scene.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class CrtScreen : MonoBehaviour
    {
        public static CrtScreen Instance { get; private set; }

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int CurvatureId = Shader.PropertyToID("_Curvature");
        static readonly int CornerRadiusId = Shader.PropertyToID("_CornerRadius");
        static readonly int ScanlinesId = Shader.PropertyToID("_Scanlines");
        static readonly int ScanlineCountId = Shader.PropertyToID("_ScanlineCount");
        static readonly int BleedId = Shader.PropertyToID("_Bleed");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int MaskId = Shader.PropertyToID("_Mask");
        static readonly int MaskScaleId = Shader.PropertyToID("_MaskScale");
        static readonly int ConvergenceId = Shader.PropertyToID("_Convergence");
        static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        static readonly int RefreshRateId = Shader.PropertyToID("_RefreshRate");
        static readonly int VignetteId = Shader.PropertyToID("_Vignette");

        [InlineEditor]
        [Tooltip("Material used by the renderer's CrtScreen feature — the same asset the feature holds. Empty = the one on the settings asset (the installer writes it there).")]
        public Material screenMaterial;

        [InlineEditor]
        [Tooltip("Every CRT knob. Empty = the shipped Resources asset (FiniteRunner_CrtScreen), or an in-memory default.")]
        public CrtScreenSettings settings;

        [Tooltip("Gameplay's ramp on top of the asset's intensity — SetIntensity writes it. 1 = the asset as authored. The player's VIDEO dial multiplies on top of this.")]
        [PropertyRange(0f, 1f)]
        public float intensityScale = 1f;

        [Tooltip("Show the tube in edit mode too, so the asset can be tuned in the Scene view before play. Off by default: a curved Scene view is hard to work in.")]
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
        /// FiniteRunner → Install CRT Screen Feature). Returns null when off
        /// or missing.
        /// </summary>
        public static CrtScreen Apply(bool enabled, CrtScreenSettings settings = null)
        {
            CrtScreen system = Instance != null
                ? Instance
                : FindAnyObjectByType<CrtScreen>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null)
            {
                Debug.LogError($"{nameof(CrtScreen)}: the scene has no CrtScreen object — place one (Tools → FiniteRunner → Install CRT Screen Feature adds it to the open scene). Systems are never spawned at play time.");
                return null;
            }
            if (settings != null) system.settings = settings;
            if (system.settings == null) system.settings = CrtScreenSettings.Load();
            if (system.screenMaterial == null) system.screenMaterial = system.settings.material;
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        /// <summary>Gameplay's ramp on the asset's intensity (0..1).</summary>
        public void SetIntensity(float scale) => intensityScale = Mathf.Clamp01(scale);

        void OnEnable()
        {
            Instance = this;
            CrtScreenFeature.HasDriver = true;
            if (settings == null) settings = CrtScreenSettings.Load();
            if (screenMaterial == null) screenMaterial = settings.material;
            if (Application.isPlaying)
            {
                if (screenMaterial == null)
                    Debug.LogWarning($"{nameof(CrtScreen)}: no material to drive — run Tools → FiniteRunner → Install CRT Screen Feature (it creates the material and writes it onto the settings asset).", this);
                else
                    RendererFeatureAudit.WarnIfMissing(screenMaterial, nameof(CrtScreen), this);
            }
            Write();
        }

        void LateUpdate()
        {
            if (settings == null) return;
            // The player's VIDEO dial is read every frame (no event), so a
            // slider drag in the pause menu shows through the menu live.
            float scale = Application.isPlaying
                ? intensityScale * UserSettings.CrtFilter
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
                if (screenMaterial != null) screenMaterial.SetFloat(IntensityId, 0f);
                Instance = null;
                CrtScreenFeature.HasDriver = false;
            }
        }

        void Write()
        {
            if (screenMaterial == null || settings == null) return;
            screenMaterial.SetFloat(IntensityId, CurrentIntensity);
            screenMaterial.SetFloat(CurvatureId, settings.curvature);
            screenMaterial.SetFloat(CornerRadiusId, settings.cornerRadius);
            screenMaterial.SetFloat(ScanlinesId, settings.scanlines);
            screenMaterial.SetFloat(ScanlineCountId, settings.scanlineCount);
            screenMaterial.SetFloat(BleedId, settings.bleed);
            screenMaterial.SetFloat(GlowId, settings.glow);
            screenMaterial.SetFloat(MaskId, settings.mask);
            screenMaterial.SetFloat(MaskScaleId, settings.maskScale);
            screenMaterial.SetFloat(ConvergenceId, settings.convergence);
            screenMaterial.SetFloat(FlickerId, settings.flicker);
            screenMaterial.SetFloat(RefreshRateId, settings.refreshRate);
            screenMaterial.SetFloat(VignetteId, settings.vignette);
        }
    }
}
