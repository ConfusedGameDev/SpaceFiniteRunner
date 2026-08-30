using ConfusedGameDev.FiniteRunner.Rendering;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Game-facing dial for the fullscreen glitch post effect: the shader
    /// (Hidden/PoliceEscape/GlitchPost, run by the renderer's GlitchPost
    /// fullscreen feature) exposes one master _Intensity, and this component
    /// writes it every frame as base level + decaying pulse. Gameplay code
    /// calls the static Instance — <see cref="Pulse"/> for one-shot bursts
    /// (getting spotted, collisions, messages) and
    /// <see cref="SetBaseIntensity"/> for sustained states (being chased,
    /// hack in progress). The material is a shared asset used by every scene's
    /// renderer, so OnDisable always resets it to a clean feed — a scene
    /// without this controller must never inherit someone else's glitch.
    /// Runs on unscaled time so pulses decay through pause screens. Awake
    /// audits the ACTIVE pipeline asset for the GlitchPost feature
    /// (<see cref="RendererFeatureAudit"/>): a quality level pointing at a
    /// pipeline asset without it fails silently otherwise.
    /// </summary>
    public class GlitchController : MonoBehaviour
    {
        public static GlitchController Instance { get; private set; }

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Required, InlineEditor]
        [Tooltip("Material used by the GlitchPost fullscreen renderer feature — per-effect tuning (slice strength, RGB split…) lives on it.")]
        public Material glitchMaterial;

        [TitleGroup("Glitch")]
        [Tooltip("Steady glitch level pulses ride on top of. 0 = clean feed.")]
        [PropertyRange(0f, 1f)]
        public float baseIntensity;

        [TitleGroup("Glitch")]
        [Tooltip("Base level applied on scene start — set to 1 in scenes entered through a glitch transition, so they open fully corrupted and fade in.")]
        [PropertyRange(0f, 1f)]
        public float startIntensity;

        [TitleGroup("Glitch")]
        [Tooltip("How fast the base level drifts back to 0, per second. 0 = hold forever (accumulated damage stays); >0 fades a transition in, or heals damage over time.")]
        [PropertyRange(0f, 2f)]
        public float baseFadePerSecond;

        [TitleGroup("Glitch")]
        [Tooltip("How fast a pulse fades back to the base level, in intensity per second.")]
        [PropertyRange(0.1f, 10f)]
        public float pulseDecayPerSecond = 2f;

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float CurrentIntensity => Mathf.Clamp01(baseIntensity + pulse);

        float pulse;

        void Awake()
        {
            Instance = this;
            RendererFeatureAudit.WarnIfMissing(glitchMaterial, nameof(GlitchController), this);
        }

        void Start()
        {
            baseIntensity = Mathf.Max(baseIntensity, startIntensity);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (baseFadePerSecond > 0f)
                baseIntensity = Mathf.MoveTowards(baseIntensity, 0f, baseFadePerSecond * Time.unscaledDeltaTime);
            pulse = Mathf.MoveTowards(pulse, 0f, pulseDecayPerSecond * Time.unscaledDeltaTime);
            Apply(CurrentIntensity);
        }

        void OnDisable()
        {
            // Only the last controller standing cleans the shared material —
            // during an additive scene handoff the incoming scene's controller
            // has already claimed Instance, and zeroing here would blank the
            // transition for a frame.
            if (Instance == null || Instance == this) Apply(0f);
        }

        /// <summary>One-shot burst that decays back down to the base level. Strengths don't stack — the loudest active pulse wins.</summary>
        public void Pulse(float strength) => pulse = Mathf.Max(pulse, Mathf.Clamp01(strength));

        /// <summary>Sustained glitch level for ongoing states; call with 0 to return to a clean feed.</summary>
        public void SetBaseIntensity(float value) => baseIntensity = Mathf.Clamp01(value);

        void Apply(float value)
        {
            if (glitchMaterial != null) glitchMaterial.SetFloat(IntensityId, value);
        }
    }
}
