using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Rear brake lights for every city car — player, police and traffic
    /// alike. The Cyberpunk Megapolis cars carry one material each whose
    /// emission MAP paints the tail lights (headlights and badge too), so
    /// braking is shown by driving that material's HDR emission INTENSITY:
    /// <see cref="CarConfig.brakeLightIdleIntensity"/> (EV, the colour
    /// picker's Intensity — -10 is dark) while rolling and
    /// <see cref="CarConfig.brakeLightBrakingIntensity"/> while
    /// <see cref="CarController.RearLightsOn"/> (braking,
    /// handbrake or reverse), faded over
    /// <see cref="CarConfig.brakeLightFadeSeconds"/>.
    ///
    /// <see cref="CarController"/> adds one to itself in Start, so all three
    /// construction paths (player prefab, police prefab, runtime traffic rigs)
    /// are covered with no spawn-site changes, and both physics backends feed
    /// the flag it reads. Only materials with the <c>_EMISSION</c> keyword
    /// AND an emission map qualify — the code-built police light bar and the
    /// Kenney toys have neither, so they are left alone. The emission colour
    /// is split into its LDR chroma (the map's tint, kept) and the exposure
    /// (replaced), matching how Unity's HDR picker composes
    /// <c>_EmissionColor = chroma × 2^intensity</c>.
    ///
    /// Materials are instanced through <c>Renderer.materials</c> — the fleet
    /// shares the source assets and must keep glowing independently — and
    /// those are the same instances <see cref="CarHealth"/> chars on a kill,
    /// which is why they are never destroyed here: the wreck keeps rendering
    /// after CarHealth strips this component, and Unity frees them with the
    /// scene like every other runtime material instance. Per-instance
    /// materials keep SRP batching (same shader variant), which a
    /// MaterialPropertyBlock would not.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [DisallowMultipleComponent]
    public class BrakeLights : MonoBehaviour
    {
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        const string EmissionKeyword = "_EMISSION";

        CarController car;
        readonly List<Material> lights = new();
        readonly List<Color> chroma = new(); // the source colour's LDR part, per instance
        float blend;                          // 0 idle .. 1 braking
        float appliedIntensity = float.NaN;

        /// <summary>Add the component to a car that has none yet — the CarController.Start hook.</summary>
        public static BrakeLights Ensure(CarController car) =>
            car.GetComponent<BrakeLights>() ?? car.gameObject.AddComponent<BrakeLights>();

        void Awake()
        {
            car = GetComponent<CarController>();
            Collect();
        }

        /// <summary>
        /// Instance every emissive-mapped material under the car once. Active
        /// renderers only: the kit's retired wheel LOD children are hidden
        /// before this runs and must not be instanced on their way out; the
        /// body's LOD1/LOD2 renderers are active objects (the LODGroup culls
        /// them, it does not deactivate them) and get their own instances so
        /// the lights read at every LOD.
        /// </summary>
        void Collect()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

                bool any = false;
                foreach (Material shared in renderer.sharedMaterials)
                    if (Qualifies(shared)) { any = true; break; }
                if (!any) continue;

                foreach (Material instance in renderer.materials) // instantiates once per renderer
                {
                    if (!Qualifies(instance)) continue;
                    Color source = instance.GetColor(EmissionColorId);
                    float exposure = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
                    lights.Add(instance);
                    chroma.Add(exposure > 0f ? new Color(source.r / exposure, source.g / exposure, source.b / exposure, 1f) : Color.white);
                }
            }
        }

        static bool Qualifies(Material material) =>
            material != null
            && material.HasProperty(EmissionColorId)
            && material.HasProperty(EmissionMapId)
            && material.GetTexture(EmissionMapId) != null
            && material.IsKeywordEnabled(EmissionKeyword);

        void OnEnable()
        {
            blend = 0f;
            appliedIntensity = float.NaN;
        }

        void Update()
        {
            if (lights.Count == 0 || car == null || car.config == null) return;
            CarConfig config = car.config;

            float target = car.RearLightsOn ? 1f : 0f;
            blend = config.brakeLightFadeSeconds > 0f
                ? Mathf.MoveTowards(blend, target, Time.deltaTime / config.brakeLightFadeSeconds)
                : target;

            Apply(Mathf.Lerp(config.brakeLightIdleIntensity, config.brakeLightBrakingIntensity, blend));
        }

        /// <summary>A stripped car (CarHealth's wreck pass) or a parked one goes dark rather than freezing mid-brake.</summary>
        void OnDisable()
        {
            if (lights.Count == 0 || car == null || car.config == null) return;
            Apply(car.config.brakeLightIdleIntensity);
        }

        void Apply(float intensity)
        {
            if (Mathf.Approximately(intensity, appliedIntensity)) return;
            appliedIntensity = intensity;
            float exposure = Mathf.Pow(2f, intensity);
            for (int i = 0; i < lights.Count; i++)
            {
                Material material = lights[i];
                if (material == null) continue;
                Color c = chroma[i];
                material.SetColor(EmissionColorId, new Color(c.r * exposure, c.g * exposure, c.b * exposure, 1f));
            }
        }
    }
}
