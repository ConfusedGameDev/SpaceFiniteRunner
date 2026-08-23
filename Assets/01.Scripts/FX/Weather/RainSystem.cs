using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The weather. Builds its particle systems from code (no prefab to keep
    /// in sync, same rule as the police cruiser and every HUD piece) and
    /// re-applies the whole <see cref="RainSettings"/> asset every frame, so
    /// the inline inspector and the debug page tune a live downpour.
    ///
    /// Three design rules matter here:
    /// <list type="bullet">
    /// <item>The rain is a small box that RIDES WITH THE CAMERA, biased along
    /// the view by <see cref="RainSettings.leadDistance"/> — a world-sized
    /// storm would cost thousands of particles nobody can see. Simulation is
    /// in world space regardless, so turning the camera does not drag the
    /// drops with it.</item>
    /// <item>Atmosphere touches GLOBAL render settings (fog, ambient). They
    /// are captured on the way in and restored on the way out, so a scene
    /// without rain can never inherit someone else's overcast — the same
    /// contract <see cref="GlitchController"/> keeps with its shared material.</item>
    /// <item>The two games move at wildly different speeds, so the drops carry
    /// a share of the camera's own motion (<see cref="RainSettings.followSpeed"/>)
    /// and the streak they draw is capped in metres. World-static rain is
    /// correct at a car's pace and simply gone between two frames at the
    /// runner's, which is what those two knobs reconcile.</item>
    /// </list>
    /// Thunder rides along: every so often it washes the screen white and
    /// fires <see cref="onThunderStrike"/> on the same frame, which is where
    /// the thunderclap sound hangs.
    ///
    /// Runs on scaled time, so the rain freezes with the pause menu, and is
    /// <c>[ExecuteAlways]</c>: with <c>preview</c> on it hand-steps the
    /// simulation in the editor, so the whole asset is tunable before pressing
    /// play. The particle objects it builds are flagged DontSaveInEditor (and
    /// re-adopted rather than duplicated on a recompile) so nothing it makes
    /// is ever written into the scene file — the only thing in the scene is
    /// this one component.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class RainSystem : MonoBehaviour
    {
        const string DropsName = "Rain_Drops";
        const string SplashName = "Rain_Splashes";
        const string FlashName = "Rain_Flash";

        // Above the HUD (10) and the story messages (15), below the pause menu
        // (20): lightning washes the world and its readouts, never a menu the
        // player is reading.
        const int FlashSortingOrder = 18;

        public static RainSystem Instance { get; private set; }

        [InlineEditor]
        [Tooltip("All weather tunables live on this asset — add new knobs there, not here. Empty = the shipped one from Resources.")]
        public RainSettings settings;

        [TitleGroup("Runtime")]
        [Tooltip("Gameplay's dial on top of the asset's intensity — drive it with SetIntensity to ramp a storm in and out.")]
        [PropertyRange(0f, 1f)]
        public float intensityScale = 1f;

        [TitleGroup("Runtime")]
        [Tooltip("Simulate the rain in the editor so it can be tuned without entering play mode. The particle objects it builds are flagged DontSaveInEditor and never land in the scene file; turning this off stops the editor repainting for them.")]
        public bool preview = true;

        [TitleGroup("Runtime"), ShowInInspector, ReadOnly]
        public float CurrentIntensity => settings != null ? Mathf.Clamp01(settings.intensity * intensityScale) : 0f;

        [TitleGroup("Events")]
        [Tooltip("Fired the instant lightning hits, before the flash is drawn — hang the thunderclap here. Wire it on the scene object; code that spawns later listens to the static ThunderStruck instead.")]
        public UnityEvent onThunderStrike;

        /// <summary>Static twin of <see cref="onThunderStrike"/>, for listeners that are not around when the scene object is wired.</summary>
        public static event System.Action ThunderStruck;

        ParticleSystem drops;
        ParticleSystem splash;
        ParticleSystemRenderer dropsRenderer;
        ParticleSystemRenderer splashRenderer;
        Material dropMaterial;
        Material splashMaterial;
        Texture2D generatedDropTexture;
        Texture2D generatedSplashTexture;
        Texture appliedDropTexture;
        Texture appliedSplashTexture;
        bool appliedAdditive;
        bool splashesBuilt;

        Canvas flashCanvas;
        Image flash;
        float strikeCountdown;
        float strikeTime;
        bool striking;

        // Captured global render settings, restored the moment atmosphere stops.
        bool atmosphereApplied;
        bool savedFog;
        Color savedFogColor;
        FogMode savedFogMode;
        float savedFogDensity;
        float savedAmbientIntensity;

        Vector3 lastCameraPosition;
        Vector3 cameraVelocity;
        bool cameraTracked;

#if UNITY_EDITOR
        double lastEditorTime;
        bool previewRunning;
#endif

        /// <summary>
        /// The call every scene owner makes on boot. A hand-placed RainSystem
        /// always wins — that is the object the designer tuned before pressing
        /// play, and it must never be shadowed by a second one — so this finds
        /// it (inactive included) and only creates one when the scene has none.
        /// Switching the weather off PARKS that object rather than ignoring it,
        /// or a scene-placed system would keep raining through a disabled flag.
        /// </summary>
        public static RainSystem Apply(bool enabled, RainSettings settings = null)
        {
            RainSystem system = Instance != null
                ? Instance
                : FindAnyObjectByType<RainSystem>(FindObjectsInactive.Include);

            if (!enabled)
            {
                if (system != null) system.gameObject.SetActive(false);
                return null;
            }
            if (system == null) system = new GameObject("RainSystem").AddComponent<RainSystem>();
            if (settings != null) system.settings = settings;
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            return system;
        }

        /// <summary>Gameplay's ramp: 0 stops the downpour without destroying anything, 1 restores the asset's intensity.</summary>
        public void SetIntensity(float scale) => intensityScale = Mathf.Clamp01(scale);

        void Awake()
        {
            if (settings == null) settings = RainSettings.Load();
            // In the editor the systems are built lazily by the preview, so a
            // scene simply being opened never litters it with objects.
            if (Application.isPlaying) Build();
        }

        // On enable rather than in Awake, so a system switched off and back on
        // during a run reclaims the singleton instead of leaving it null.
        void OnEnable()
        {
            Instance = this;
#if UNITY_EDITOR
            // Kick the first editor tick: LateUpdate keeps the loop turning
            // afterwards, but nothing would start it on a freshly opened scene.
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void OnDisable()
        {
            RestoreAtmosphere();
            // A system switched off mid-strike must not leave the screen white:
            // the flash canvas is a child and would stay up on its own.
            striking = false;
            ShowFlash(0f);
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            // Runtime materials and fallback textures are ours alone — nothing
            // else can be holding them, so they go with the system. Only in
            // play mode: editor teardown already runs inside Unity's own
            // destroy pass, and these are HideAndDontSave, so they go on the
            // next domain reload anyway.
            if (!Application.isPlaying) return;
            Destroy(dropMaterial);
            Destroy(splashMaterial);
            Destroy(generatedDropTexture);
            Destroy(generatedSplashTexture);
        }

        /// <summary>Destroy that works in both modes — edit mode has no end-of-frame to defer to.</summary>
        static void Discard(Object victim)
        {
            if (victim == null) return;
            if (Application.isPlaying) Destroy(victim);
            else DestroyImmediate(victim);
        }

        void LateUpdate()
        {
            if (settings == null) return;

            // One delta drives everything below: scaled time in play (so the
            // whole storm freezes with the pause menu), real time in the
            // editor preview, where no clock is running at all.
            float dt;
            if (Application.isPlaying)
            {
                dt = Time.deltaTime;
            }
            else
            {
#if UNITY_EDITOR
                if (!preview)
                {
                    if (previewRunning) StopPreview();
                    return;
                }
                previewRunning = true;
                if (drops == null) Build();
                dt = EditorDelta();
#else
                return;
#endif
            }
            if (drops == null) return;

            FollowCamera();   // measures the camera's speed, which the drift below rides on
            ApplySettings();
            UpdateThunder(dt);

            // Atmosphere writes GLOBAL RenderSettings, which in the editor
            // would dirty the scene's lighting every repaint — the preview
            // shows the drops, never the overcast.
            if (Application.isPlaying) ApplyAtmosphere();
#if UNITY_EDITOR
            else StepPreview(dt);
#endif
        }

#if UNITY_EDITOR
        /// <summary>Real seconds since the last preview tick — the editor has no game clock to read.</summary>
        float EditorDelta()
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float dt = lastEditorTime <= 0d ? 0.02f : Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.1f);
            lastEditorTime = now;
            return dt;
        }

        /// <summary>
        /// Hand-drives the simulation outside play mode. Nothing ticks a
        /// particle system in the editor, so the rain is stepped by real time
        /// and the loop is kept turning — which is what makes the whole asset
        /// tunable before pressing play.
        /// </summary>
        void StepPreview(float dt)
        {
            drops.Simulate(dt, true, false, false);
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }

        void StopPreview()
        {
            previewRunning = false;
            if (drops != null) drops.Clear(true);
            striking = false;
            ShowFlash(0f);
        }
#endif

        // --------------------------------------------------------------- build

        /// <summary>
        /// Builds (or re-adopts) the two particle objects. Re-adoption is what
        /// makes it safe to call again: a script recompile re-runs Awake on the
        /// components that survived, and a second set of emitters would double
        /// the downpour. Both objects are flagged DontSaveInEditor — the editor
        /// preview must never write particle systems into the scene file —
        /// nor the lightning canvas.
        /// </summary>
        [Button("Rebuild"), PropertyOrder(100)]
        void Build()
        {
            GameObject dropsGo = Adopt(transform, DropsName);
            drops = GetOrAdd<ParticleSystem>(dropsGo);
            dropsRenderer = GetOrAdd<ParticleSystemRenderer>(dropsGo);

            // The splash must be a CHILD of the drops for Unity to accept it as
            // a sub-emitter — that parenting is the API's contract, not a
            // hierarchy preference.
            GameObject splashGo = Adopt(dropsGo.transform, SplashName);
            splash = GetOrAdd<ParticleSystem>(splashGo);
            splashRenderer = GetOrAdd<ParticleSystemRenderer>(splashGo);

            // Lightning's full-screen wash. Its own overlay canvas, so it is
            // unaffected by where this object sits and cannot be occluded by
            // anything in the world — and switched off between strikes, which
            // is nearly all the time.
            GameObject flashGo = Adopt(transform, FlashName);
            flashCanvas = GetOrAdd<Canvas>(flashGo);
            flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            flashCanvas.sortingOrder = FlashSortingOrder;
            flashCanvas.enabled = false;
            flash = GetOrAdd<Image>(flashGo);
            flash.raycastTarget = false; // a flash must never eat a button press
            flash.color = new Color(1f, 1f, 1f, 0f);

            splashesBuilt = false; // the collision/sub-emitter wiring is re-made below
            ConfigureDrops();
            ConfigureSplash();

            // Components carry their own hide flags — stamping only the
            // GameObject would leave its ParticleSystem serialized into the
            // scene with nothing to belong to.
            HideFromScene(dropsGo);
            HideFromScene(splashGo);
            HideFromScene(flashGo);

            drops.Play();
        }

        static void HideFromScene(GameObject go)
        {
            go.hideFlags = HideFlags.DontSaveInEditor;
            foreach (var component in go.GetComponents<Component>())
                component.hideFlags = HideFlags.DontSaveInEditor;
        }

        /// <summary>Existing child by name, or a fresh one — kept out of the scene file either way.</summary>
        static GameObject Adopt(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null) go.transform.SetParent(parent, false);
            go.hideFlags = HideFlags.DontSaveInEditor;
            return go;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        void ConfigureDrops()
        {
            var main = drops.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f; // constant fall speed: a drop at terminal velocity already is
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            var shape = drops.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Tipped a quarter turn so the box's emit direction (+Z) points at
            // the ground; its local Y is therefore world Z, which is why the
            // scale below reads (width, depth, thickness).
            shape.rotation = new Vector3(90f, 0f, 0f);

            var velocity = drops.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            var noise = drops.noise;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = 0.6f;
            noise.damping = false;

            dropsRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            dropsRenderer.lengthScale = 1f;
            dropsRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dropsRenderer.receiveShadows = false;
            dropsRenderer.sortMode = ParticleSystemSortMode.None;
        }

        void ConfigureSplash()
        {
            var main = splash.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startSpeed = 0f;

            var emission = splash.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // every splash comes from the collision sub-emitter
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = splash.shape;
            shape.enabled = false;

            var size = splash.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f), new Keyframe(1f, 1f)));

            var color = splash.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(Fade(1f, 0f));

            // A ripple lies ON the ground — a view billboard would stand it up
            // like a coin on its edge.
            splashRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            splashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            splashRenderer.receiveShadows = false;
            splashRenderer.sortMode = ParticleSystemSortMode.None;
        }

        static Gradient Fade(float from, float to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(from, 0f), new GradientAlphaKey(to, 1f) });
            return gradient;
        }

        // ---------------------------------------------------------- live tuning

        void ApplySettings()
        {
            float intensity = CurrentIntensity;
            float fallMin = Mathf.Max(0.5f, Mathf.Min(settings.fallSpeed.x, settings.fallSpeed.y));
            float fallMax = Mathf.Max(fallMin, settings.fallSpeed.y);

            // Long enough to clear the camera and keep falling out of frame —
            // with splashes on, most drops die on the ground well before this.
            float lifetime = (settings.spawnHeight + settings.areaRadius) / fallMin;
            float rate = settings.dropsPerSecond * intensity;

            var main = drops.main;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(fallMin, fallMax);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Min(settings.dropSize.x, settings.dropSize.y), Mathf.Max(settings.dropSize.x, settings.dropSize.y));
            main.startColor = settings.dropColor;
            main.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * lifetime * 1.15f) + 64, 64, 60000);

            var emission = drops.emission;
            emission.rateOverTime = rate;
            emission.enabled = rate > 0f;

            var shape = drops.shape;
            shape.scale = new Vector3(settings.areaRadius * 2f, settings.areaRadius * 2f, 0.02f);
            shape.position = new Vector3(0f, settings.spawnHeight, 0f);

            // Horizontal only: letting the drops inherit the camera's vertical
            // motion would cancel the fall on a climb and leave them hanging.
            Vector3 drift = new Vector3(cameraVelocity.x, 0f, cameraVelocity.z) * settings.followSpeed;
            Vector3 wind = settings.WindVelocity;
            var velocity = drops.velocityOverLifetime;
            velocity.x = wind.x + drift.x;
            velocity.z = wind.z + drift.z;

            var noise = drops.noise;
            noise.enabled = settings.gustStrength > 0f;
            noise.strength = settings.gustStrength;
            noise.frequency = settings.gustFrequency;

            // Streak length is authored per m/s of travel, so a drifting drop at
            // ship speeds would be smeared across the whole screen — the cap is
            // what keeps the same value usable in both games.
            float streak = settings.streakLength;
            if (settings.maxStreakLength > 0f)
            {
                float dropSpeed = drift.magnitude + fallMax + wind.magnitude;
                streak = Mathf.Min(streak, settings.maxStreakLength / Mathf.Max(1f, dropSpeed));
            }
            dropsRenderer.velocityScale = streak;

            ApplyMaterials();
            ApplySplashSettings();
        }

        /// <summary>
        /// Splash collision is the expensive half of the system, so the toggle
        /// really does switch it off: no collision module, no sub-emitter, and
        /// the drops simply live out their fall.
        /// </summary>
        void ApplySplashSettings()
        {
            var collision = drops.collision;
            collision.enabled = settings.splashes;

            var subEmitters = drops.subEmitters;
            subEmitters.enabled = settings.splashes;

            if (!settings.splashes)
            {
                splash.gameObject.SetActive(false);
                return;
            }
            splash.gameObject.SetActive(true);

            if (!splashesBuilt)
            {
                splashesBuilt = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.quality = ParticleSystemCollisionQuality.Medium;
                collision.collidesWith = ~0;
                collision.bounce = 0f;
                collision.dampen = 1f;
                collision.lifetimeLoss = 1f; // landing kills the drop; the ripple takes over
                collision.sendCollisionMessages = false;
                collision.maxCollisionShapes = 256;
                // A re-adopted system still carries the sub-emitter it was
                // wired with before the recompile — clear before adding, or a
                // rebuild stacks a second splash on every landing drop.
                while (subEmitters.subEmittersCount > 0) subEmitters.RemoveSubEmitter(0);
                subEmitters.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision,
                                          ParticleSystemSubEmitterProperties.InheritNothing);
            }
            subEmitters.SetSubEmitterEmitProbability(0, settings.splashChance);

            var main = splash.main;
            main.startLifetime = settings.splashLifetime;
            main.startSize = settings.splashSize;
            main.startColor = settings.splashColor;
            // Headroom for the whole curtain landing at once.
            main.maxParticles = Mathf.Clamp(
                Mathf.CeilToInt(settings.dropsPerSecond * CurrentIntensity * settings.splashChance * settings.splashLifetime) + 32,
                32, 20000);
        }

        void ApplyMaterials()
        {
            Texture dropTexture = settings.dropTexture != null ? settings.dropTexture : GeneratedDropTexture();
            Texture splashTexture = settings.splashTexture != null ? settings.splashTexture : GeneratedSplashTexture();

            if (dropMaterial == null || appliedDropTexture != dropTexture || appliedAdditive != settings.additive)
            {
                Discard(dropMaterial);
                dropMaterial = BuildParticleMaterial("Rain_Drop", dropTexture, settings.additive);
                dropsRenderer.sharedMaterial = dropMaterial;
                appliedDropTexture = dropTexture;
                appliedAdditive = settings.additive;
            }
            if (splashMaterial == null || appliedSplashTexture != splashTexture)
            {
                Discard(splashMaterial);
                splashMaterial = BuildParticleMaterial("Rain_Splash", splashTexture, false);
                splashRenderer.sharedMaterial = splashMaterial;
                appliedSplashTexture = splashTexture;
            }
        }

        /// <summary>
        /// A transparent unlit particle material, set up for URP first and for
        /// the built-in particle shaders as a fallback — the raw blend/ZWrite
        /// ints are written either way, because only the URP shader reads the
        /// _Surface/_Blend pair that drives them.
        /// </summary>
        static Material BuildParticleMaterial(string name, Texture texture, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Particles/Standard Unlit")
                            ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };

            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);            // transparent
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 1f : 0f); // additive / alpha
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);                   // double sided
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)(additive
                    ? UnityEngine.Rendering.BlendMode.One
                    : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (texture != null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }

        // ------------------------------------------------------------ thunder

        /// <summary>
        /// Fires lightning right now: the event goes out FIRST (the thunderclap
        /// wants to start on the same frame as the flash, not after it) and the
        /// gap to the next strike is rolled here, so it counts from strike to
        /// strike rather than from the end of one wash to the start of the next
        /// — which is what keeps <c>thunderFrequency</c> meaning what it says
        /// even when the flash is long.
        /// Public so a story beat can call for thunder on cue.
        /// </summary>
        [Button("Test Strike"), PropertyOrder(101)]
        public void Strike()
        {
            striking = true;
            strikeTime = 0f;
            strikeCountdown = RollGap();

            // Never fire scene callbacks out of an editor preview — the button
            // above is for looking at the flash, not for running the game.
            if (!Application.isPlaying) return;
            onThunderStrike?.Invoke();
            ThunderStruck?.Invoke();
        }

        void UpdateThunder(float dt)
        {
            if (!settings.thunder || CurrentIntensity < settings.thunderMinIntensity)
            {
                // A storm that dies mid-strike takes the flash with it, and the
                // gap is dropped so the next one is rolled fresh — a countdown
                // banked during a dry spell would strike the instant it returns.
                striking = false;
                strikeCountdown = 0f;
                ShowFlash(0f);
                return;
            }

            if (striking)
            {
                strikeTime += dt;
                float phase = strikeTime / Mathf.Max(0.01f, settings.flashDuration);
                if (phase >= 1f)
                {
                    striking = false;
                    ShowFlash(0f);
                }
                else ShowFlash(FlashAlpha(phase));
                return;
            }

            if (strikeCountdown <= 0f) strikeCountdown = RollGap();
            strikeCountdown -= dt;
            if (strikeCountdown <= 0f) Strike();
        }

        /// <summary>
        /// Seconds until the next strike: a roll inside the authored band,
        /// divided by the frequency dial. Dividing the ROLL rather than the
        /// band's ends keeps the spread proportional — turning the storm up
        /// makes strikes closer together, not more evenly spaced.
        /// </summary>
        float RollGap()
        {
            float min = Mathf.Max(0.1f, Mathf.Min(settings.strikeInterval.x, settings.strikeInterval.y));
            float max = Mathf.Max(min, settings.strikeInterval.y);
            return Random.Range(min, max) / Mathf.Max(0.05f, settings.thunderFrequency);
        }

        /// <summary>
        /// The envelope of one strike: <c>flashFlickers</c> sharp pops riding a
        /// single falling curve. One smooth fade reads as a camera flash — it is
        /// the stutter that makes it lightning.
        /// </summary>
        float FlashAlpha(float phase)
        {
            int flickers = Mathf.Max(1, settings.flashFlickers);
            float pop = 1f - Mathf.Repeat(phase * flickers, 1f);
            return settings.flashPeak * (1f - phase) * pop * pop * pop;
        }

        /// <summary>Draws the wash — and switches the canvas off entirely between strikes, which is most of the time.</summary>
        void ShowFlash(float alpha)
        {
            if (flash == null || flashCanvas == null) return;

            bool visible = alpha > 0.002f;
            if (flashCanvas.enabled != visible) flashCanvas.enabled = visible;
            if (!visible) return;

            Color tint = settings.flashColor;
            tint.a = Mathf.Clamp01(alpha);
            flash.color = tint;
        }

        // ------------------------------------------------------- fallback art

        /// <summary>
        /// Horizontal soft streak — drops are stretched along their velocity,
        /// which maps the sprite's X axis to the direction of travel, so the
        /// generated art is drawn lying down.
        /// </summary>
        Texture2D GeneratedDropTexture()
        {
            if (generatedDropTexture != null) return generatedDropTexture;
            const int width = 64, height = 16;
            generatedDropTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Rain_Drop (generated)",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width * 2f - 1f;
                float v = (y + 0.5f) / height * 2f - 1f;
                float alpha = Mathf.Exp(-(u * u * 3.5f + v * v * 9f));
                generatedDropTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            generatedDropTexture.Apply();
            return generatedDropTexture;
        }

        /// <summary>Soft ring — the ripple a landing drop leaves.</summary>
        Texture2D GeneratedSplashTexture()
        {
            if (generatedSplashTexture != null) return generatedSplashTexture;
            const int size = 64;
            generatedSplashTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Rain_Splash (generated)",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float radius = Mathf.Sqrt(u * u + v * v);
                float ring = (radius - 0.72f) / 0.22f;
                generatedSplashTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Exp(-ring * ring)));
            }
            generatedSplashTexture.Apply();
            return generatedSplashTexture;
        }

        // ------------------------------------------------------------- camera

        /// <summary>
        /// Parks the volume on the camera, pushed along the flat view direction
        /// so the drops are where the player is heading. Height is left alone —
        /// the spawn box already sits above by <see cref="RainSettings.spawnHeight"/>.
        ///
        /// It moves the DROPS CHILD, never this transform: the child is flagged
        /// out of the scene file, so following the camera in the editor preview
        /// cannot leave the scene permanently dirty, and the object stays
        /// wherever the designer parked it in the hierarchy.
        /// </summary>
        void FollowCamera()
        {
            Transform cameraTransform = ResolveCamera();
            if (cameraTransform == null) return;

            Vector3 position = cameraTransform.position;
            float dt = Time.deltaTime;
            if (!cameraTracked || dt <= 0f)
            {
                cameraVelocity = Vector3.zero;
                cameraTracked = true;
            }
            else
            {
                Vector3 step = position - lastCameraPosition;
                // A respawn, a restart or a scene handoff moves the camera
                // further in one frame than anything can drive — that is a
                // teleport, not speed, and must not fling the whole curtain.
                cameraVelocity = step.magnitude > 200f ? Vector3.zero
                    : Vector3.Lerp(cameraVelocity, step / dt, 1f - Mathf.Exp(-8f * dt));
            }
            lastCameraPosition = position;

            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            drops.transform.position = position + forward * settings.leadDistance;
        }

        /// <summary>The viewpoint to rain around: the game camera, or the scene view while previewing.</summary>
        static Transform ResolveCamera()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var view = UnityEditor.SceneView.lastActiveSceneView;
                if (view != null && view.camera != null) return view.camera.transform;
            }
#endif
            var main = Camera.main;
            return main != null ? main.transform : null;
        }

        // --------------------------------------------------------- atmosphere

        void ApplyAtmosphere()
        {
            if (!settings.atmosphere)
            {
                RestoreAtmosphere();
                return;
            }
            if (!atmosphereApplied)
            {
                atmosphereApplied = true;
                savedFog = RenderSettings.fog;
                savedFogColor = RenderSettings.fogColor;
                savedFogMode = RenderSettings.fogMode;
                savedFogDensity = RenderSettings.fogDensity;
                savedAmbientIntensity = RenderSettings.ambientIntensity;
            }

            float intensity = CurrentIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(savedFogColor, settings.fogColor, intensity);
            RenderSettings.fogDensity = Mathf.Lerp(savedFogDensity, settings.fogDensity, intensity);
            RenderSettings.ambientIntensity = savedAmbientIntensity * (1f - settings.ambientDim * intensity);
        }

        void RestoreAtmosphere()
        {
            if (!atmosphereApplied) return;
            atmosphereApplied = false;
            RenderSettings.fog = savedFog;
            RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogMode = savedFogMode;
            RenderSettings.fogDensity = savedFogDensity;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
        }
    }
}
