using ConfusedGameDev.FiniteRunner.Rendering;
using ConfusedGameDev.FiniteRunner.UI;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Scene-side dial of the VHS tape look. The renderer's
    /// <see cref="VhsTapeFeature"/> runs the shader (Hidden/FiniteRunner/VhsTape)
    /// over the finished picture through a shared material asset; this
    /// component writes its <see cref="VhsTapeSettings"/> asset plus the live
    /// drive into that material every frame — the <see cref="DistanceFog"/> /
    /// <see cref="SpeedLines"/> contract, including the "last one standing
    /// zeroes the material on disable" rule and the feature's HasDriver gate.
    /// The drive is deliberately small: the asset's <c>intensity</c> ×
    /// gameplay's <see cref="SetIntensity"/> scale × the player's
    /// <see cref="UserSettings.VhsFilter"/> dial (the VIDEO settings page) is
    /// the master, and
    /// <see cref="TrackingPulse"/> is a max-wins burst on the tracking band
    /// (a hit, a story beat) that decays on scaled time so it freezes with
    /// the pause menu. [ExecuteAlways] with <see cref="preview"/> on plays
    /// the tape in the Scene view (off by default).
    /// **It is a hand-placed scene object, never spawned**: each scene carries
    /// one next to its DistanceFog, RainSystem and SpeedLines, with the
    /// material and settings asset wired so the look can be tuned before
    /// play; <see cref="Apply"/> only finds it (and errors when it is missing)
    /// and parks it when the owner's settings say off. Tools → FiniteRunner →
    /// Install VHS Tape Feature places one in the open scene.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class VhsTape : MonoBehaviour
    {
        public static VhsTape Instance { get; private set; }

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ChromaBleedId = Shader.PropertyToID("_ChromaBleed");
        static readonly int ChromaLagId = Shader.PropertyToID("_ChromaLag");
        static readonly int LumaSoftnessId = Shader.PropertyToID("_LumaSoftness");
        static readonly int JitterId = Shader.PropertyToID("_Jitter");
        static readonly int TrackingId = Shader.PropertyToID("_Tracking");
        static readonly int TrackingSpeedId = Shader.PropertyToID("_TrackingSpeed");
        static readonly int TrackingHeightId = Shader.PropertyToID("_TrackingHeight");
        static readonly int HeadSwitchId = Shader.PropertyToID("_HeadSwitch");
        static readonly int HeadSwitchHeightId = Shader.PropertyToID("_HeadSwitchHeight");
        static readonly int NoiseId = Shader.PropertyToID("_Noise");
        static readonly int ScanlinesId = Shader.PropertyToID("_Scanlines");
        static readonly int ScanlineCountId = Shader.PropertyToID("_ScanlineCount");
        static readonly int WashId = Shader.PropertyToID("_Wash");
        static readonly int VignetteId = Shader.PropertyToID("_Vignette");
        static readonly int FrameRateId = Shader.PropertyToID("_FrameRate");

        [InlineEditor]
        [Tooltip("Material used by the renderer's VhsTape feature — the same asset the feature holds. Empty = the one on the settings asset (the installer writes it there).")]
        public Material tapeMaterial;

        [InlineEditor]
        [Tooltip("Every VHS knob. Empty = the shipped Resources asset (FiniteRunner_VhsTape), or an in-memory default.")]
        public VhsTapeSettings settings;

        [Tooltip("Gameplay's ramp on top of the asset's intensity — SetIntensity writes it. 1 = the asset as authored.")]
        [PropertyRange(0f, 1f)]
        public float intensityScale = 1f;

        [Tooltip("Play the tape in edit mode too, so the asset's look can be tuned in the Scene view before play. Off by default: a Scene view through a worn tape is hard to work in.")]
        public bool preview;

        [ShowIf("preview"), PropertyRange(0f, 1f)]
        [Tooltip("Intensity scale the edit-mode preview plays at (on top of the asset's own intensity).")]
        public float previewIntensity = 1f;

        /// <summary>Effective master intensity written to the material this frame.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float CurrentIntensity { get; private set; }

        /// <summary>Extra tracking-band strength from the burst in flight, 0..1.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float CurrentTrackingPulse => trackingPulse;

        float trackingPulse;
        float trackingPulseDecayPerSecond;

        /// <summary>
        /// The owner's one call: finds the scene's hand-placed driver (its
        /// wiring and tuning are the designer's), pushes an override asset
        /// onto it when one is given, and parks it when the owner says off.
        /// NEVER creates one — systems live in the scene so they can be tuned
        /// before play; a missing driver is a scene-setup error (run Tools →
        /// FiniteRunner → Install VHS Tape Feature). Returns null when off or
        /// missing.
        /// </summary>
        public static VhsTape Apply(bool enabled, VhsTapeSettings settings = null)
        {
            VhsTape system = Instance != null
                ? Instance
                : FindAnyObjectByType<VhsTape>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null)
            {
                Debug.LogError($"{nameof(VhsTape)}: the scene has no VhsTape object — place one (Tools → FiniteRunner → Install VHS Tape Feature adds it to the open scene). Systems are never spawned at play time.");
                return null;
            }
            if (settings != null) system.settings = settings;
            if (system.settings == null) system.settings = VhsTapeSettings.Load();
            if (system.tapeMaterial == null) system.tapeMaterial = system.settings.material;
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        /// <summary>Gameplay's ramp on the asset's intensity (0..1).</summary>
        public void SetIntensity(float scale) => intensityScale = Mathf.Clamp01(scale);

        /// <summary>
        /// A tracking error on cue (a hit, a story beat): strength 0..1 added
        /// to the asset's tracking band, fading to 0 over
        /// <paramref name="seconds"/>. Max-wins — a weaker burst inside a
        /// stronger one is ignored rather than stacked.
        /// </summary>
        public void TrackingPulse(float strength, float seconds)
        {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f || strength < trackingPulse) return;
            trackingPulse = strength;
            trackingPulseDecayPerSecond = strength / Mathf.Max(0.05f, seconds);
        }

        /// <summary>Drops any burst in flight — a restart must not carry the last hit's tracking error into the new run.</summary>
        public void ClearPulse() => trackingPulse = 0f;

        void OnEnable()
        {
            Instance = this;
            VhsTapeFeature.HasDriver = true;
            if (settings == null) settings = VhsTapeSettings.Load();
            if (tapeMaterial == null) tapeMaterial = settings.material;
            if (Application.isPlaying)
            {
                if (tapeMaterial == null)
                    Debug.LogWarning($"{nameof(VhsTape)}: no material to drive — run Tools → FiniteRunner → Install VHS Tape Feature (it creates the material and writes it onto the settings asset).", this);
                else
                    RendererFeatureAudit.WarnIfMissing(tapeMaterial, nameof(VhsTape), this);
            }
            Write();
        }

        void LateUpdate()
        {
            if (settings == null) return;
            float dt = Time.deltaTime; // scaled on purpose: the burst freezes with the pause menu
            trackingPulse = Mathf.MoveTowards(trackingPulse, 0f, trackingPulseDecayPerSecond * dt);

            // The player's VIDEO dial is read every frame (no event), so a
            // slider drag in the pause menu shows through the menu live.
            float scale = Application.isPlaying
                ? intensityScale * UserSettings.VhsFilter
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
                if (tapeMaterial != null) tapeMaterial.SetFloat(IntensityId, 0f);
                Instance = null;
                VhsTapeFeature.HasDriver = false;
            }
        }

        void Write()
        {
            if (tapeMaterial == null || settings == null) return;
            tapeMaterial.SetFloat(IntensityId, CurrentIntensity);
            tapeMaterial.SetFloat(ChromaBleedId, settings.chromaBleed);
            tapeMaterial.SetFloat(ChromaLagId, settings.chromaLag);
            tapeMaterial.SetFloat(LumaSoftnessId, settings.lumaSoftness);
            tapeMaterial.SetFloat(JitterId, settings.jitter);
            tapeMaterial.SetFloat(TrackingId, Mathf.Clamp01(settings.tracking + trackingPulse));
            tapeMaterial.SetFloat(TrackingSpeedId, settings.trackingSpeed);
            tapeMaterial.SetFloat(TrackingHeightId, settings.trackingHeight);
            tapeMaterial.SetFloat(HeadSwitchId, settings.headSwitch);
            tapeMaterial.SetFloat(HeadSwitchHeightId, settings.headSwitchHeight);
            tapeMaterial.SetFloat(NoiseId, settings.noise);
            tapeMaterial.SetFloat(ScanlinesId, settings.scanlines);
            tapeMaterial.SetFloat(ScanlineCountId, settings.scanlineCount);
            tapeMaterial.SetFloat(WashId, settings.wash);
            tapeMaterial.SetFloat(VignetteId, settings.vignette);
            tapeMaterial.SetFloat(FrameRateId, settings.frameRate);
        }
    }
}
