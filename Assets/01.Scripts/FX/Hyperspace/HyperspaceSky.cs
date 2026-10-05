using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The jump to hyperspace: while the owner holds it engaged (the runner —
    /// flying at Light Speed), the sky crossfades from the scene's own skybox
    /// into a tunnel of cyan / green streaks rushing out of a vanishing point
    /// that follows the focus's heading; released (the speed dropped below it)
    /// it fades back to the skybox. The look is <see cref="HyperspaceSkySettings"/>
    /// (Skybox/FiniteRunner/Hyperspace), re-read every frame.
    /// **The scene's skybox is never edited**: the first frame the sky
    /// engages, the driver takes whatever <see cref="RenderSettings.skybox"/>
    /// is up — lazily, because during the additive city→runner handoff the
    /// runner scene only becomes the active one after its Awake — copies its
    /// panorama (texture, tint, exposure, rotation) onto a runtime COPY of the
    /// asset's material and swaps that in; it puts the original back the
    /// moment the blend is back at 0 (faded out, or <see cref="ResetForRun"/>)
    /// and on disable — the RainSystem's capture-and-restore contract with
    /// RenderSettings. A source sky that is not a lat-long panorama is kept
    /// only as far as the shader can read it (no texture = black behind the
    /// fade). Play mode only: nothing is swapped in the editor.
    /// Generic like <see cref="SpeedLines"/> — FX cannot see the ship, so the
    /// owner hands it a focus <see cref="Transform"/>, a km/h reader and the
    /// reference speed the scroll is scaled against (<see cref="SetTarget"/>),
    /// then drives <see cref="SetEngaged"/>. Runs on scaled time, so the tunnel
    /// freezes with the pause menu. **A hand-placed scene object, never
    /// spawned**: <see cref="Apply"/> only finds it and parks it when off.
    /// </summary>
    [DisallowMultipleComponent]
    public class HyperspaceSky : MonoBehaviour
    {
        public static HyperspaceSky Instance { get; private set; }

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        static readonly int RotationId = Shader.PropertyToID("_Rotation");
        static readonly int BlendId = Shader.PropertyToID("_Blend");
        static readonly int TunnelDirId = Shader.PropertyToID("_TunnelDir");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        static readonly int ColorCId = Shader.PropertyToID("_ColorC");
        static readonly int GreenShareId = Shader.PropertyToID("_GreenShare");
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        static readonly int ColumnsId = Shader.PropertyToID("_Columns");
        static readonly int DensityId = Shader.PropertyToID("_Density");
        static readonly int CellLengthId = Shader.PropertyToID("_CellLength");
        static readonly int StreakLengthId = Shader.PropertyToID("_StreakLength");
        static readonly int StreakWidthId = Shader.PropertyToID("_StreakWidth");
        static readonly int CoreRadiusId = Shader.PropertyToID("_CoreRadius");

        [InlineEditor]
        [Tooltip("Every hyperspace knob. Empty = the shipped Resources asset (FiniteRunner_HyperspaceSky).")]
        public HyperspaceSkySettings settings;

        [Tooltip("Force the sky in for tuning while playing, whatever the owner says.")]
        public bool forceEngaged;

        /// <summary>Crossfade this frame: 0 the scene's sky, 1 full hyperspace.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float Blend { get; private set; }

        /// <summary>What the owner last asked for through <see cref="SetEngaged"/>.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public bool Engaged { get; private set; }

        Transform focus;
        System.Func<float> speedKmh;
        float referenceSpeedKmh = 1f;
        Material runtimeSky;
        Material sourceSky;
        bool swapped;
        Vector3 axis = Vector3.forward;
        float scroll;
        float flash;
        float shownBlend; // Blend as the post-processing manager lets it show

        /// <summary>
        /// The owner's one call: finds the scene's hand-placed driver and parks
        /// it when the owner says off. NEVER creates one — a missing driver is
        /// a scene-setup error. Returns null when off or missing.
        /// </summary>
        public static HyperspaceSky Apply(bool enabled)
        {
            HyperspaceSky system = Instance != null
                ? Instance
                : FindAnyObjectByType<HyperspaceSky>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null)
            {
                Debug.LogError($"{nameof(HyperspaceSky)}: the scene has no HyperspaceSky object — place PF_HyperspaceSky under ===LIGHTING===/Filters. Systems are never spawned at play time.");
                return null;
            }
            if (system.settings == null) system.settings = HyperspaceSkySettings.Load();
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        /// <summary>What the tunnel follows: the focus's heading is its axis, and the scroll is scaled by speed ÷ <paramref name="referenceSpeedKmh"/>.</summary>
        public void SetTarget(Transform focus, System.Func<float> speedKmh, float referenceSpeedKmh)
        {
            this.focus = focus;
            this.speedKmh = speedKmh;
            this.referenceSpeedKmh = Mathf.Max(1f, referenceSpeedKmh);
        }

        /// <summary>
        /// On: the sky fades into hyperspace (with a flash when it starts from
        /// the plain sky). Off: it fades back to the scene's skybox. Call it
        /// every frame with the owner's state — only a change does anything.
        /// </summary>
        public void SetEngaged(bool on)
        {
            if (on == Engaged) return;
            Engaged = on;
            if (!on) return;
            if (settings != null && Blend <= 0f) flash = settings.flashStrength;
            if (focus != null && Blend <= 0f) axis = focus.forward; // start centred on the heading, no swing in
        }

        /// <summary>A retry: back to the scene's sky at once, no fade.</summary>
        public void ResetForRun()
        {
            Engaged = false;
            Blend = 0f;
            flash = 0f;
            scroll = 0f;
            Restore();
        }

        void OnEnable()
        {
            Instance = this;
            if (settings == null) settings = HyperspaceSkySettings.Load();
        }

        void LateUpdate()
        {
            if (settings == null) return;
            float dt = Time.deltaTime; // scaled on purpose: the tunnel freezes with the pause menu

            bool wanted = Engaged || forceEngaged;
            float seconds = wanted ? settings.fadeInSeconds : settings.fadeOutSeconds;
            float rate = seconds > 0f ? dt / seconds : 1f;
            Blend = Mathf.MoveTowards(Blend, wanted ? 1f : 0f, rate);
            // The post-processing manager's switch and band rule what is shown; the scene's sky is the baseline.
            shownBlend = PostProcessManager.Gate(this, PostEffect.HyperspaceSky, Blend, 0f);
            if (shownBlend <= 0f)
            {
                Restore();
                return;
            }
            if (!Swap()) return;

            if (focus != null)
                axis = Vector3.Slerp(axis, focus.forward, 1f - Mathf.Exp(-settings.axisResponse * dt)).normalized;

            float factor = speedKmh != null ? speedKmh() / referenceSpeedKmh : 1f;
            factor = Mathf.Clamp(factor, settings.speedFactorBand.x, Mathf.Max(settings.speedFactorBand.x, settings.speedFactorBand.y));
            scroll += settings.scrollSpeed * settings.cellLength * factor * dt;
            if (scroll > 10000f) scroll -= 10000f; // keep float precision; the pattern jumps once, unseen at speed
            flash = Mathf.MoveTowards(flash, 0f, settings.flashStrength / Mathf.Max(0.05f, settings.flashSeconds) * dt);

            Write();
        }

        void OnDisable()
        {
            Restore();
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            if (runtimeSky != null) Destroy(runtimeSky);
        }

        // Takes the scene's sky and swaps the hyperspace copy in. False when there is nothing to play.
        bool Swap()
        {
            if (swapped) return true;
            if (!Application.isPlaying || settings.material == null) return false;

            if (runtimeSky == null)
            {
                runtimeSky = new Material(settings.material) { name = settings.material.name + " (runtime)" };
                runtimeSky.hideFlags = HideFlags.DontSave;
            }
            sourceSky = RenderSettings.skybox;
            if (sourceSky != null)
            {
                if (sourceSky.HasProperty(MainTexId)) runtimeSky.SetTexture(MainTexId, sourceSky.GetTexture(MainTexId));
                if (sourceSky.HasProperty(TintId)) runtimeSky.SetColor(TintId, sourceSky.GetColor(TintId));
                if (sourceSky.HasProperty(ExposureId)) runtimeSky.SetFloat(ExposureId, sourceSky.GetFloat(ExposureId));
                if (sourceSky.HasProperty(RotationId)) runtimeSky.SetFloat(RotationId, sourceSky.GetFloat(RotationId));
            }
            RenderSettings.skybox = runtimeSky;
            swapped = true;
            return true;
        }

        // Puts the scene's own sky back — but only if nobody replaced ours since.
        void Restore()
        {
            if (!swapped) return;
            if (RenderSettings.skybox == runtimeSky) RenderSettings.skybox = sourceSky;
            sourceSky = null;
            swapped = false;
        }

        void Write()
        {
            Material m = runtimeSky;
            m.SetFloat(BlendId, shownBlend);
            m.SetVector(TunnelDirId, axis);
            m.SetFloat(ScrollId, scroll);
            m.SetFloat(FlashId, flash);
            m.SetColor(BackgroundColorId, settings.backgroundColor);
            m.SetColor(CoreColorId, settings.coreColor);
            m.SetColor(ColorAId, settings.streakCyan);
            m.SetColor(ColorBId, settings.streakAqua);
            m.SetColor(ColorCId, settings.streakGreen);
            m.SetFloat(GreenShareId, settings.greenShare);
            m.SetFloat(BrightnessId, settings.brightness);
            m.SetFloat(ColumnsId, settings.columns);
            m.SetFloat(DensityId, settings.density);
            m.SetFloat(CellLengthId, settings.cellLength);
            m.SetFloat(StreakLengthId, settings.streakLength);
            m.SetFloat(StreakWidthId, settings.streakWidth);
            m.SetFloat(CoreRadiusId, settings.coreRadius);
        }
    }
}
