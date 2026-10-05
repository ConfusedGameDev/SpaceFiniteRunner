using ConfusedGameDev.FiniteRunner.Rendering;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Scene-side dial of the distance fog + far glitch. The renderer's
    /// DistanceFogFeature runs the shader (Hidden/PoliceEscape/DistanceFog)
    /// over a shared material asset; this component writes the
    /// <see cref="DistanceFogSettings"/> asset into that material every frame,
    /// so the inline inspector and the debug FOG page tune the live picture
    /// with no runtime clone — the same contract <see cref="GlitchController"/>
    /// keeps with the glitch material and <see cref="RainSystem"/> with its
    /// asset. Because the material is shared by every scene's renderer,
    /// OnDisable zeroes its intensity: a scene without this object can never
    /// inherit someone else's fog, and the feature skips its pass entirely at
    /// intensity 0. The far-clip clamp is only ADVERTISED here
    /// (<see cref="FarClipPlane"/>): Cinemachine pushes its lens clip planes
    /// onto the camera every frame, so the chase rig is what applies it.
    /// Legacy RenderSettings fog (RainSystem's overcast atmosphere) is
    /// independent and simply stacks — URP Lit applies it per object before
    /// this pass runs. [ExecuteAlways] with <see cref="preview"/> on shows the
    /// fog in the Scene view, so the asset is tunable before pressing play.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class DistanceFog : MonoBehaviour
    {
        public static DistanceFog Instance { get; private set; }

        static readonly System.Collections.Generic.List<DistanceFog> active = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            active.Clear();
            Instance = null;
        }

        /// <summary>
        /// The fog of <paramref name="scene"/>, or null when that scene has
        /// none. The chase camera clamps its far clip to its OWN scene's fog
        /// (refactor Step 10.3): during the additive city→runner handoff both
        /// scenes have a fog, and the global <see cref="Instance"/> could hand
        /// the runner's camera the city's 630 m clip, or the city's the
        /// runner's 1800 m.
        /// </summary>
        public static DistanceFog For(UnityEngine.SceneManagement.Scene scene)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                DistanceFog fog = active[i];
                if (fog != null && fog.gameObject.scene == scene) return fog;
            }
            return null;
        }

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int FogStartId = Shader.PropertyToID("_FogStart");
        static readonly int FogEndId = Shader.PropertyToID("_FogEnd");
        static readonly int FogDensityId = Shader.PropertyToID("_FogDensity");
        static readonly int FogColorNearId = Shader.PropertyToID("_FogColorNear");
        static readonly int FogColorFarId = Shader.PropertyToID("_FogColorFar");
        static readonly int SkyFogAmountId = Shader.PropertyToID("_SkyFogAmount");
        static readonly int HeightFalloffId = Shader.PropertyToID("_HeightFalloff");
        static readonly int HeightBaseId = Shader.PropertyToID("_HeightBase");
        static readonly int GlitchStartId = Shader.PropertyToID("_GlitchStart");
        static readonly int GlitchStrengthId = Shader.PropertyToID("_GlitchStrength");
        static readonly int GlitchRateId = Shader.PropertyToID("_GlitchRate");
        static readonly int SliceStrengthId = Shader.PropertyToID("_SliceStrength");
        static readonly int BlockAmountId = Shader.PropertyToID("_BlockAmount");
        static readonly int ColorSplitId = Shader.PropertyToID("_ColorSplit");
        static readonly int ScanlineStrengthId = Shader.PropertyToID("_ScanlineStrength");

        [Required, InlineEditor]
        [Tooltip("Material used by the renderer's DistanceFog feature — the same asset the feature holds. Created by Tools → Police Escape → Install Distance Fog Feature.")]
        public Material fogMaterial;

        [InlineEditor]
        [Tooltip("Every fog and glitch knob. Empty = the shipped Resources asset (FiniteRunner_DistanceFog), or an in-memory default.")]
        public DistanceFogSettings settings;

        [Tooltip("Gameplay's ramp on top of the asset's intensity — SetIntensity writes it. 1 = the asset as authored.")]
        [PropertyRange(0f, 1f)]
        public float intensityScale = 1f;

        [Tooltip("Apply the fog in edit mode too, so the Scene view shows what the asset does before play.")]
        public bool preview = true;

        /// <summary>
        /// Effective master intensity: asset × gameplay scale, 0 in edit mode
        /// with preview off. In play the gameplay scale goes through the
        /// post-processing manager, whose baseline is the asset's own value.
        /// </summary>
        public float CurrentIntensity
        {
            get
            {
                if (settings == null || (!Application.isPlaying && !preview)) return 0f;
                float live = settings.intensity * intensityScale;
                if (Application.isPlaying) live = PostProcessManager.Gate(this, PostEffect.DistanceFog, live, settings.intensity);
                return Mathf.Clamp01(live);
            }
        }

        /// <summary>
        /// Far clip plane the chase camera should use while the fog is on
        /// (fog end + margin), or null when there is nothing to clamp: fog
        /// off, or the clamp disabled on the asset.
        /// </summary>
        public float? FarClipPlane =>
            settings != null && settings.clampFarClip && CurrentIntensity > 0f
                ? settings.fogEnd + settings.farClipMargin
                : null;

        void OnEnable()
        {
            Instance = this;
            if (!active.Contains(this)) active.Add(this);
            Rendering.DistanceFogFeature.HasDriver = true;
            if (settings == null) settings = DistanceFogSettings.Load();
            if (Application.isPlaying) RendererFeatureAudit.WarnIfMissing(fogMaterial, nameof(DistanceFog), this);
            Apply();
        }

        void LateUpdate()
        {
            Apply();
        }

        void OnDisable()
        {
            active.Remove(this);
            // Only the last fog standing cleans the shared material — during
            // an additive scene handoff the incoming scene's fog has already
            // claimed Instance, and zeroing here would blink the picture.
            if (Instance == null || Instance == this)
            {
                if (fogMaterial != null) fogMaterial.SetFloat(IntensityId, 0f);
                Instance = null;
                Rendering.DistanceFogFeature.HasDriver = false;
            }
        }

        /// <summary>Gameplay's ramp on the asset's intensity (0..1) — fade the world out, or in.</summary>
        public void SetIntensity(float scale) => intensityScale = Mathf.Clamp01(scale);

        void Apply()
        {
            if (fogMaterial == null || settings == null) return;
            fogMaterial.SetFloat(IntensityId, CurrentIntensity);
            fogMaterial.SetFloat(FogStartId, Mathf.Min(settings.fogStart, settings.fogEnd - 1f));
            fogMaterial.SetFloat(FogEndId, settings.fogEnd);
            fogMaterial.SetFloat(FogDensityId, settings.fogDensity);
            fogMaterial.SetColor(FogColorNearId, settings.fogColorNear);
            fogMaterial.SetColor(FogColorFarId, settings.fogColorFar);
            fogMaterial.SetFloat(SkyFogAmountId, settings.skyFogAmount);
            fogMaterial.SetFloat(HeightFalloffId, settings.heightFog ? settings.heightFalloff : 0f);
            fogMaterial.SetFloat(HeightBaseId, settings.heightBase);
            fogMaterial.SetFloat(GlitchStartId, Mathf.Min(settings.glitchStart, settings.fogEnd - 1f));
            fogMaterial.SetFloat(GlitchStrengthId, settings.farGlitch ? settings.glitchStrength : 0f);
            fogMaterial.SetFloat(GlitchRateId, settings.glitchRate);
            fogMaterial.SetFloat(SliceStrengthId, settings.sliceStrength);
            fogMaterial.SetFloat(BlockAmountId, settings.blockAmount);
            fogMaterial.SetFloat(ColorSplitId, settings.colorSplit);
            fogMaterial.SetFloat(ScanlineStrengthId, settings.scanlineStrength);
        }
    }
}
