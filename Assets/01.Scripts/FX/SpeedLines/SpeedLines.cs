using ConfusedGameDev.FiniteRunner.Rendering;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Scene-side dial of the manga speed lines. The renderer's
    /// <see cref="SpeedLinesFeature"/> blends the shader
    /// (Hidden/FiniteRunner/SpeedLines) over the picture through a shared
    /// material asset; this component writes its <see cref="SpeedLinesSettings"/>
    /// asset plus the live drive into that material every frame — the
    /// <see cref="DistanceFog"/> contract, including the "last one standing
    /// zeroes the material on disable" rule and the feature's HasDriver gate.
    /// It is deliberately generic: the FX assembly cannot see the camera rig
    /// or the vehicles (Cameras references FX), so the owner hands it a focus
    /// <see cref="Transform"/>, a km/h reader and the reference speed the
    /// asset's band is a fraction of (<see cref="SetTarget"/>), and pushes the
    /// camera-mode index (<see cref="SetCameraMode"/>: 0 Far, 1 Close, 2 First
    /// person) each frame. Intensity = the smoothed speed term + a max-wins
    /// <see cref="Pulse"/> (a boost orb), × the asset's per-mode multiplier ×
    /// gameplay's <see cref="SetIntensity"/> scale. The convergence point is
    /// the focus's viewport position, smoothed; screen centre when the focus is
    /// behind the camera or in first person (the ship is not on screen). Runs
    /// on scaled time so the lines freeze with the pause menu. [ExecuteAlways]
    /// with <see cref="preview"/> on shows the pattern in the Scene view (off
    /// by default — lines over the Scene view are a nuisance).
    /// **It is a hand-placed scene object, never spawned**: the runner scene
    /// carries one next to its DistanceFog and RainSystem, with the material
    /// and settings asset wired so the effect can be tuned before play;
    /// <see cref="Apply"/> only finds it (and errors when it is missing) and
    /// parks it when the owner's settings say off. Tools → FiniteRunner →
    /// Install Speed Lines Feature places one in the open scene.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class SpeedLines : MonoBehaviour
    {
        public static SpeedLines Instance { get; private set; }

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FocusId = Shader.PropertyToID("_Focus");
        static readonly int LineCountId = Shader.PropertyToID("_LineCount");
        static readonly int FineLineCountId = Shader.PropertyToID("_FineLineCount");
        static readonly int DensityId = Shader.PropertyToID("_Density");
        static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        static readonly int TaperLengthId = Shader.PropertyToID("_TaperLength");
        static readonly int InnerRadiusId = Shader.PropertyToID("_InnerRadius");
        static readonly int InnerJitterId = Shader.PropertyToID("_InnerJitter");
        static readonly int FlickerRateId = Shader.PropertyToID("_FlickerRate");
        static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

        const int FirstPersonMode = 2;

        [InlineEditor]
        [Tooltip("Material used by the renderer's SpeedLines feature — the same asset the feature holds. Empty = the one on the settings asset (the installer writes it there).")]
        public Material linesMaterial;

        [InlineEditor]
        [Tooltip("Every speed-lines knob. Empty = the shipped Resources asset (FiniteRunner_SpeedLines), or an in-memory default.")]
        public SpeedLinesSettings settings;

        [Tooltip("Gameplay's ramp on top of the asset's intensity — SetIntensity writes it. 1 = the asset as authored.")]
        [PropertyRange(0f, 1f)]
        public float intensityScale = 1f;

        [Tooltip("Draw the lines in edit mode too, so the asset's look can be tuned in the Scene view before play. Off by default: a Scene view full of lines is hard to work in.")]
        public bool preview;

        [ShowIf("preview"), PropertyRange(0f, 1f)]
        [Tooltip("Intensity the edit-mode preview draws at (the speed term does not exist outside play).")]
        public float previewIntensity = 1f;

        /// <summary>Effective master intensity written to the material this frame.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float CurrentIntensity { get; private set; }

        Transform focus;
        System.Func<float> speedKmh;
        float referenceSpeedKmh = 1f;
        int cameraMode;
        float speedIntensity;
        float pulse;
        float pulseDecayPerSecond;
        Vector2 focusViewport = new(0.5f, 0.5f);

        /// <summary>
        /// The owner's one call: finds the scene's hand-placed driver (its
        /// wiring and tuning are the designer's), pushes an override asset
        /// onto it when one is given, and parks it when the owner says off.
        /// NEVER creates one — systems live in the scene so they can be tuned
        /// before play; a missing driver is a scene-setup error (run Tools →
        /// FiniteRunner → Install Speed Lines Feature). Returns null when off
        /// or missing.
        /// </summary>
        public static SpeedLines Apply(bool enabled, SpeedLinesSettings settings = null)
        {
            SpeedLines system = Instance != null
                ? Instance
                : FindAnyObjectByType<SpeedLines>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null)
            {
                Debug.LogError($"{nameof(SpeedLines)}: the scene has no SpeedLines object — place one (Tools → FiniteRunner → Install Speed Lines Feature adds it to the open scene). Systems are never spawned at play time.");
                return null;
            }
            if (settings != null) system.settings = settings;
            if (system.settings == null) system.settings = SpeedLinesSettings.Load();
            if (system.linesMaterial == null) system.linesMaterial = system.settings.material;
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        /// <summary>
        /// What the lines converge on and what drives them: the focus's screen
        /// position is the convergence point, and the asset's speed band is a
        /// fraction of <paramref name="referenceSpeedKmh"/>.
        /// </summary>
        public void SetTarget(Transform focus, System.Func<float> speedKmh, float referenceSpeedKmh)
        {
            this.focus = focus;
            this.speedKmh = speedKmh;
            this.referenceSpeedKmh = Mathf.Max(1f, referenceSpeedKmh);
        }

        /// <summary>Camera-mode index the owner pushes while the rig's cinematic shot holds the picture — the asset's multiplier for it ships at 0, so the lines are off for the shot.</summary>
        public const int CinematicMode = 3;

        /// <summary>Camera-mode index for the asset's multipliers: 0 Far, 1 Close, 2 First person, 3 the cinematic shot (<see cref="CinematicMode"/>).</summary>
        public void SetCameraMode(int mode) => cameraMode = mode;

        /// <summary>
        /// A burst on top of the speed term (a boost orb): strength 0..1
        /// fading to 0 over <paramref name="seconds"/>. Max-wins — a weaker
        /// pulse inside a stronger one is ignored rather than stacked.
        /// </summary>
        public void Pulse(float strength, float seconds)
        {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f || strength < pulse) return;
            pulse = strength;
            pulseDecayPerSecond = strength / Mathf.Max(0.05f, seconds);
        }

        /// <summary>Drops any burst in flight — a restart teleport must not carry the last orb's flash into the new run.</summary>
        public void ClearPulse() => pulse = 0f;

        /// <summary>Gameplay's ramp on the asset's intensity (0..1).</summary>
        public void SetIntensity(float scale) => intensityScale = Mathf.Clamp01(scale);

        void OnEnable()
        {
            Instance = this;
            SpeedLinesFeature.HasDriver = true;
            if (settings == null) settings = SpeedLinesSettings.Load();
            if (linesMaterial == null) linesMaterial = settings.material;
            if (Application.isPlaying)
            {
                if (linesMaterial == null)
                    Debug.LogWarning($"{nameof(SpeedLines)}: no material to drive — run Tools → FiniteRunner → Install Speed Lines Feature (it creates the material and writes it onto the settings asset).", this);
                else
                    RendererFeatureAudit.WarnIfMissing(linesMaterial, nameof(SpeedLines), this);
            }
            Write();
        }

        void LateUpdate()
        {
            if (settings == null) return;
            float dt = Time.deltaTime; // scaled on purpose: the lines freeze with the pause menu

            // Speed term: the band's fraction of the reference speed, smoothed.
            float target = 0f;
            if (Application.isPlaying && speedKmh != null)
            {
                float fraction = speedKmh() / referenceSpeedKmh;
                float start = settings.speedBand.x;
                float full = Mathf.Max(settings.speedBand.y, start + 0.01f);
                target = Mathf.Clamp01((fraction - start) / (full - start));
            }
            speedIntensity = Mathf.Lerp(speedIntensity, target, 1f - Mathf.Exp(-settings.responseSharpness * dt));
            if (target <= 0f && speedIntensity < 0.005f) speedIntensity = 0f; // the feature gates on exactly 0
            pulse = Mathf.MoveTowards(pulse, 0f, pulseDecayPerSecond * dt);

            float value = Application.isPlaying
                ? Mathf.Clamp01(speedIntensity + pulse) * settings.ModeMultiplier(cameraMode) * intensityScale
                : (preview ? previewIntensity : 0f);
            CurrentIntensity = Mathf.Clamp01(value * settings.intensity);

            // Convergence point: the focus on screen, or the centre when it is
            // behind the camera or the view is first person (nothing to aim at).
            Vector2 wanted = new(0.5f, 0.5f);
            Camera camera = Camera.main;
            if (Application.isPlaying && cameraMode != FirstPersonMode && focus != null && camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(focus.position);
                if (viewport.z > 0f)
                    wanted = new Vector2(Mathf.Clamp(viewport.x, -0.5f, 1.5f), Mathf.Clamp(viewport.y, -0.5f, 1.5f));
            }
            focusViewport = Vector2.Lerp(focusViewport, wanted, 1f - Mathf.Exp(-settings.focusResponse * dt));

            Write();
        }

        void OnDisable()
        {
            // Only the last driver standing cleans the shared material — during
            // an additive scene handoff the incoming scene's driver has already
            // claimed Instance, and zeroing here would blink the picture.
            if (Instance == null || Instance == this)
            {
                if (linesMaterial != null) linesMaterial.SetFloat(IntensityId, 0f);
                Instance = null;
                SpeedLinesFeature.HasDriver = false;
            }
        }

        void Write()
        {
            if (linesMaterial == null || settings == null) return;
            linesMaterial.SetFloat(IntensityId, CurrentIntensity);
            linesMaterial.SetColor(ColorId, settings.color);
            linesMaterial.SetVector(FocusId, new Vector4(focusViewport.x, focusViewport.y, 0f, 0f));
            linesMaterial.SetFloat(LineCountId, settings.lineCount);
            linesMaterial.SetFloat(FineLineCountId, settings.fineLineCount);
            linesMaterial.SetFloat(DensityId, settings.density);
            linesMaterial.SetFloat(LineWidthId, settings.lineWidth);
            linesMaterial.SetFloat(TaperLengthId, settings.taperLength);
            linesMaterial.SetVector(InnerRadiusId, new Vector4(settings.innerRadius.x, settings.innerRadius.y, 0f, 0f));
            linesMaterial.SetFloat(InnerJitterId, settings.innerJitter);
            linesMaterial.SetFloat(FlickerRateId, settings.flickerRate);
            linesMaterial.SetFloat(EdgeSoftnessId, settings.edgeSoftness);
        }
    }
}
