using ConfusedGameDev.FiniteRunner.Rendering;
using Sirenix.OdinInspector;
using System.Collections.Generic;
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
    /// hack in progress), and <see cref="Hold"/> / <see cref="Release"/> for
    /// a sequence that must keep the picture corrupted (an ending's ramp, a
    /// death screen, a scene handoff): while any owner holds, the base shows
    /// at least the held level and the fade is suspended; releasing leaves
    /// the level where it was held and the fade takes it from there. Owners
    /// never touch the fade rate, so nobody has to remember and restore it.
    /// The material is a shared asset used by every scene's
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
        public float CurrentIntensity => Mathf.Clamp01(Mathf.Max(baseIntensity, Mathf.Max(HeldLevel, FloorLevel)) + pulse);

        /// <summary>True while any owner holds the picture.</summary>
        public bool IsHeld => holds.Count > 0;

        float pulse;
        readonly Dictionary<object, float> holds = new();
        readonly Dictionary<object, float> floors = new();
        readonly List<object> deadHolders = new();

        float HeldLevel
        {
            get
            {
                float level = 0f;
                foreach (var hold in holds.Values) level = Mathf.Max(level, hold);
                return level;
            }
        }

        float FloorLevel
        {
            get
            {
                float level = 0f;
                foreach (var floor in floors.Values) level = Mathf.Max(level, floor);
                return level;
            }
        }

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
            DropDeadHolders();
            if (holds.Count == 0 && baseFadePerSecond > 0f)
                baseIntensity = Mathf.MoveTowards(baseIntensity, 0f, baseFadePerSecond * Time.unscaledDeltaTime);
            pulse = Mathf.MoveTowards(pulse, 0f, pulseDecayPerSecond * Time.unscaledDeltaTime);
            // The post-processing manager's switch and band rule what is shown; the clean feed is the baseline.
            Apply(PostProcessManager.Gate(this, PostEffect.Glitch, CurrentIntensity, 0f));
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

        /// <summary>
        /// Keeps the picture at least at <paramref name="level"/> for
        /// <paramref name="owner"/>, with the fade suspended, until it calls
        /// <see cref="Release"/>. Call again to move the level (a ramp).
        /// </summary>
        public void Hold(object owner, float level)
        {
            if (owner == null) return;
            holds[owner] = Mathf.Clamp01(level);
        }

        /// <summary>
        /// Shows a level some system OWNS (the city's damage meter) without
        /// taking the picture over: unlike <see cref="Hold"/> it leaves the
        /// base fade running and never writes the base, so the value stays the
        /// owner's and the glitch only displays it. Call again to move it.
        /// </summary>
        public void SetFloor(object owner, float level)
        {
            if (owner == null) return;
            floors[owner] = Mathf.Clamp01(level);
        }

        /// <summary>Removes <paramref name="owner"/>'s floor. Idempotent.</summary>
        public void ClearFloor(object owner)
        {
            if (owner != null) floors.Remove(owner);
        }

        /// <summary>Ends <paramref name="owner"/>'s hold: the base keeps the held level and, once nobody holds, the fade takes it from there. Idempotent.</summary>
        public void Release(object owner)
        {
            if (owner == null || !holds.TryGetValue(owner, out float level)) return;
            holds.Remove(owner);
            baseIntensity = Mathf.Max(baseIntensity, level);
        }

        // A holder destroyed without releasing (its scene unloaded) must not
        // freeze the picture forever.
        void DropDeadHolders()
        {
            if (holds.Count == 0 && floors.Count == 0) return;
            deadHolders.Clear();
            foreach (var owner in holds.Keys)
                if (owner is Object unityObject && unityObject == null) deadHolders.Add(owner);
            foreach (var owner in floors.Keys)
                if (owner is Object unityObject && unityObject == null) deadHolders.Add(owner);
            foreach (var owner in deadHolders) { holds.Remove(owner); floors.Remove(owner); }
        }

        void Apply(float value)
        {
            if (glitchMaterial != null) glitchMaterial.SetFloat(IntensityId, value);
        }
    }
}
