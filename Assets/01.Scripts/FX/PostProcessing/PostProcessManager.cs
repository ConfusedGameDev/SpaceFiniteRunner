using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// The one door every runtime change to the full-screen picture goes
    /// through. Systems never write the Volume: they ask — <see cref="Set"/>
    /// an absolute value for a <see cref="PostEffect"/>, <see cref="Clear"/>
    /// it when done — and the manager decides. A request is applied only
    /// while <see cref="PostProcessSettings"/> allows it (the master switch
    /// and the effect's own), clamped to that effect's band; otherwise the
    /// effect sits at its BASELINE. Several owners on one effect: the value
    /// farthest from the baseline wins, so a kick never dips under a held
    /// level. The full-screen drivers (glitch, fog, speed lines, the retro
    /// filters, the hyperspace sky, the rain's atmosphere) keep writing their
    /// own materials but pass their intensity through <see cref="Gate"/>
    /// first, so the same switches and bands rule them.
    /// The baseline of a Volume effect is whatever the Volume's runtime
    /// profile holds while nobody overrides it: the manager writes a
    /// parameter only while a request is applied and puts the baseline back
    /// when it ends, so the Volume inspector stays where the look is tuned,
    /// in play mode too. <see cref="SaveCurrentStateAsBaseline"/> copies that
    /// tuned look onto the VolumeProfile asset.
    /// Hand-placed under <c>===SYSTEMS===</c> (<c>PF_PostProcessing</c>),
    /// never spawned; it drives the global Volume of its OWN scene, because
    /// the additive city→runner handoff has two alive. Without a manager a
    /// request is dropped and a gate passes the driver's value through, so
    /// every consumer still runs alone.
    /// </summary>
    [DisallowMultipleComponent]
    public class PostProcessManager : MonoBehaviour
    {
        static readonly List<PostProcessManager> active = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active.Clear();

        /// <summary>
        /// The manager a system in <paramref name="scene"/> talks to: its own
        /// scene's, else the newest one alive, else null.
        /// </summary>
        public static PostProcessManager For(Scene scene)
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i] != null && active[i].gameObject.scene == scene) return active[i];
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i] != null) return active[i];
            return null;
        }

        static PostProcessManager For(Component owner) => owner != null ? For(owner.gameObject.scene) : null;

        /// <summary>
        /// <paramref name="owner"/> asks for <paramref name="effect"/> at an
        /// absolute <paramref name="value"/>. Held until <see cref="Clear"/>;
        /// call again to move it. Dropped when the scene has no manager.
        /// </summary>
        public static void Set(Component owner, PostEffect effect, float value)
        {
            PostProcessManager manager = For(owner);
            if (manager != null) manager.channels[(int)effect].requests[owner] = value;
        }

        /// <summary>Ends <paramref name="owner"/>'s request for <paramref name="effect"/>. Idempotent.</summary>
        public static void Clear(Component owner, PostEffect effect)
        {
            // Every manager: the owner may have asked before its own scene's manager came up.
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null) active[i].channels[(int)effect].requests.Remove(owner);
        }

        /// <summary>
        /// The value <paramref name="effect"/> rests at for <paramref name="owner"/>'s
        /// scene — what a request should rise from. <paramref name="fallback"/>
        /// without a manager.
        /// </summary>
        public static float Baseline(Component owner, PostEffect effect, float fallback = 0f)
        {
            PostProcessManager manager = For(owner);
            return manager != null ? manager.channels[(int)effect].baseline : fallback;
        }

        /// <summary>
        /// A full-screen driver's door: it hands in the intensity gameplay
        /// drove it to (<paramref name="live"/>) and the one its asset authors
        /// (<paramref name="baseline"/>), and writes what comes back — the
        /// live value clamped to the effect's band, or the baseline when
        /// runtime adjustments are off. <paramref name="live"/> unchanged
        /// without a manager.
        /// </summary>
        public static float Gate(Component owner, PostEffect effect, float live, float baseline)
        {
            PostProcessManager manager = For(owner);
            if (manager == null) return live;

            Channel channel = manager.channels[(int)effect];
            channel.baseline = baseline;
            channel.live = live;
            channel.driven = true;
            bool adjusted = !Mathf.Approximately(live, baseline);
            channel.resolved = adjusted && manager.Allowed(effect)
                ? manager.settings.Clamp(effect, live)
                : baseline;
            return channel.resolved;
        }

        sealed class Channel
        {
            public readonly Dictionary<Component, float> requests = new();
            public VolumeParameter<float> parameter; // Volume channels: the runtime profile's parameter, once found
            public float baseline;
            public float live;      // the winning request, or a driver's value
            public float resolved;  // what is shown
            public bool overriding; // Volume channels: the manager is writing the parameter
            public bool driven;     // driver channels: a driver came through the gate
        }

        [InlineEditor]
        [Tooltip("The switches and bands. Empty = the shipped Resources asset (FiniteRunner_PostProcessing), or an in-memory default that allows everything.")]
        public PostProcessSettings settings;

        [Tooltip("The Volume whose overrides are managed. Left empty, the global Volume of this object's own scene is used; with none, Volume requests are dropped.")]
        public Volume volume;

        readonly Channel[] channels = NewChannels();
        readonly List<Component> deadOwners = new();

        static Channel[] NewChannels()
        {
            var created = new Channel[PostEffects.Count];
            for (int i = 0; i < created.Length; i++)
                created[i] = new Channel { baseline = PostEffects.Neutral((PostEffect)i) };
            return created;
        }

        bool Allowed(PostEffect effect) => settings == null || settings.Allowed(effect);

        void OnEnable()
        {
            if (settings == null) settings = PostProcessSettings.Load();
            if (volume == null) volume = FindSceneVolume();
            if (!active.Contains(this)) active.Add(this);
            // Baselines are readable from the first frame: a consumer's Update runs before our LateUpdate.
            for (int i = 0; i < channels.Length; i++)
                if (PostEffects.IsVolume((PostEffect)i)) ReadBaseline((PostEffect)i, channels[i]);
        }

        void OnDisable()
        {
            active.Remove(this);
            foreach (Channel channel in channels)
            {
                if (channel.overriding && channel.parameter != null) channel.parameter.value = channel.baseline;
                channel.overriding = false;
                channel.driven = false;
                channel.parameter = null;
                channel.requests.Clear();
            }
        }

        // The global Volume of this scene — never another scene's: during the
        // additive handoff the city's is about to unload under the runner.
        Volume FindSceneVolume()
        {
            Volume found = null;
            foreach (Volume candidate in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!candidate.isGlobal || candidate.gameObject.scene != gameObject.scene) continue;
                if (found == null || candidate.priority > found.priority) found = candidate;
            }
            return found;
        }

        void LateUpdate()
        {
            for (int i = 0; i < channels.Length; i++)
            {
                var effect = (PostEffect)i;
                Channel channel = channels[i];
                DropDeadOwners(channel);
                if (PostEffects.IsVolume(effect)) UpdateVolumeChannel(effect, channel);
            }
        }

        void UpdateVolumeChannel(PostEffect effect, Channel channel)
        {
            bool apply = channel.requests.Count > 0 && Allowed(effect) && volume != null;
            // Taking a channel over: its override is added, or switched on at the neutral value, first.
            if (apply && !channel.overriding)
            {
                ReadBaseline(effect, channel);
                channel.parameter = FindParameter(volume.profile, effect, true);
            }
            if (!apply || channel.parameter == null)
            {
                if (channel.overriding && channel.parameter != null) channel.parameter.value = channel.baseline;
                channel.overriding = false;
                ReadBaseline(effect, channel);
                channel.live = channel.requests.Count > 0 ? Winner(channel) : channel.baseline;
                channel.resolved = channel.baseline;
                return;
            }

            channel.overriding = true;
            channel.live = Winner(channel);
            channel.resolved = settings != null ? settings.Clamp(effect, channel.live) : channel.live;
            channel.parameter.value = channel.resolved;
        }

        // While nobody overrides it, the runtime profile IS the baseline — so
        // a value changed in the Volume inspector during play is picked up.
        void ReadBaseline(PostEffect effect, Channel channel)
        {
            if (volume == null) return;
            if (channel.parameter == null) channel.parameter = FindParameter(volume.profile, effect, false);
            channel.baseline = channel.parameter != null && Shown(volume.profile, effect, channel.parameter)
                ? channel.parameter.value
                : PostEffects.Neutral(effect);
        }

        // True when the parameter's value is what the picture shows: its
        // override is on and its component is not switched off in the profile.
        static bool Shown(VolumeProfile profile, PostEffect effect, VolumeParameter<float> parameter) =>
            parameter.overrideState && profile.TryGet(ComponentType(effect), out VolumeComponent component) && component.active;

        static System.Type ComponentType(PostEffect effect) => effect switch
        {
            PostEffect.LensDistortion => typeof(LensDistortion),
            PostEffect.MotionBlur => typeof(MotionBlur),
            PostEffect.MotionBlurClamp => typeof(MotionBlur),
            PostEffect.Bloom => typeof(Bloom),
            PostEffect.Vignette => typeof(Vignette),
            PostEffect.ChromaticAberration => typeof(ChromaticAberration),
            _ => typeof(FilmGrain),
        };

        // The request farthest from the baseline.
        static float Winner(Channel channel)
        {
            float winner = channel.baseline;
            float reach = -1f;
            foreach (float value in channel.requests.Values)
            {
                float distance = Mathf.Abs(value - channel.baseline);
                if (distance <= reach) continue;
                reach = distance;
                winner = value;
            }
            return winner;
        }

        // An owner destroyed without clearing (its scene unloaded) must not hold the picture.
        void DropDeadOwners(Channel channel)
        {
            if (channel.requests.Count == 0) return;
            deadOwners.Clear();
            foreach (Component owner in channel.requests.Keys)
                if (owner == null) deadOwners.Add(owner);
            foreach (Component owner in deadOwners) channel.requests.Remove(owner);
        }

        /// <summary>
        /// The parameter of <paramref name="profile"/> a Volume channel moves.
        /// With <paramref name="add"/>, a missing override is added and a
        /// switched-off one (the parameter, or its whole component) switched
        /// on, both at the neutral value — the picture does not change until
        /// a request moves it.
        /// </summary>
        static VolumeParameter<float> FindParameter(VolumeProfile profile, PostEffect effect, bool add)
        {
            if (profile == null) return null;
            switch (effect)
            {
                case PostEffect.LensDistortion:
                    return Ready(Override<LensDistortion>(profile, add), c => c.intensity, effect, add);
                case PostEffect.MotionBlur:
                    return Ready(Override<MotionBlur>(profile, add), c => c.intensity, effect, add);
                case PostEffect.MotionBlurClamp:
                    return Ready(Override<MotionBlur>(profile, add), c => c.clamp, effect, add);
                case PostEffect.Bloom:
                    return Ready(Override<Bloom>(profile, add), c => c.intensity, effect, add);
                case PostEffect.Vignette:
                    return Ready(Override<Vignette>(profile, add), c => c.intensity, effect, add);
                case PostEffect.ChromaticAberration:
                    return Ready(Override<ChromaticAberration>(profile, add), c => c.intensity, effect, add);
                case PostEffect.FilmGrain:
                    return Ready(Override<FilmGrain>(profile, add), c => c.intensity, effect, add);
                default:
                    return null;
            }
        }

        static T Override<T>(VolumeProfile profile, bool add) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            return add ? profile.Add<T>() : null;
        }

        static VolumeParameter<float> Ready<T>(T component, System.Func<T, VolumeParameter<float>> pick,
                                              PostEffect effect, bool add) where T : VolumeComponent
        {
            if (component == null) return null;
            VolumeParameter<float> parameter = pick(component);
            if (add && (!component.active || !parameter.overrideState))
            {
                // An override the profile has off (or a whole component it has
                // off) shows nothing, so it comes on showing nothing.
                parameter.value = PostEffects.Neutral(effect);
                parameter.overrideState = true;
                component.active = true;
            }
            return parameter;
        }

        /// <summary>
        /// Writes the look showing right now onto the Volume's profile ASSET,
        /// so it is what every effect rests at from here on: every parameter
        /// of the runtime profile as tuned in the Volume inspector, with the
        /// baseline — not the momentary value — for any effect a request is
        /// moving at this instant. Editor play mode only; the runner and the
        /// city share one profile asset, so both get it.
        /// </summary>
        [TitleGroup("Baseline")]
        [Button("Save Current State As Baseline", ButtonSizes.Large), EnableIf("@UnityEngine.Application.isPlaying")]
        public void SaveCurrentStateAsBaseline()
        {
#if UNITY_EDITOR
            if (volume == null || volume.sharedProfile == null)
            {
                Debug.LogWarning($"{nameof(PostProcessManager)}: no Volume with a profile asset to save to.", this);
                return;
            }
            VolumeProfile asset = volume.sharedProfile;
            VolumeProfile runtime = volume.profile;
            if (runtime == asset || !UnityEditor.EditorUtility.IsPersistent(asset))
            {
                Debug.LogWarning($"{nameof(PostProcessManager)}: the Volume's profile is not an asset — nothing to save to.", this);
                return;
            }

            foreach (VolumeComponent source in runtime.components)
            {
                if (source == null) continue;
                if (!asset.TryGet(source.GetType(), out VolumeComponent target))
                {
                    target = asset.Add(source.GetType());
                    target.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                    UnityEditor.AssetDatabase.AddObjectToAsset(target, asset);
                }
                target.active = source.active;
                int count = Mathf.Min(source.parameters.Count, target.parameters.Count);
                for (int i = 0; i < count; i++)
                {
                    target.parameters[i].SetValue(source.parameters[i]);
                    target.parameters[i].overrideState = source.parameters[i].overrideState;
                }
                UnityEditor.EditorUtility.SetDirty(target);
            }

            // An effect a request is moving right now is saved at its baseline.
            for (int i = 0; i < channels.Length; i++)
            {
                if (!channels[i].overriding) continue;
                VolumeParameter<float> parameter = FindParameter(asset, (PostEffect)i, false);
                if (parameter != null) parameter.value = channels[i].baseline;
            }

            UnityEditor.EditorUtility.SetDirty(asset);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log($"{nameof(PostProcessManager)}: saved the current look as the baseline in '{asset.name}'.", asset);
#endif
        }

        /// <summary>Every channel this frame: its baseline, what gameplay asks for, what is shown.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly, MultiLineProperty(PostEffects.Count + 1), HideLabel]
        string State
        {
            get
            {
                if (!Application.isPlaying) return "(play mode only)";
                var text = new StringBuilder();
                text.Append(volume != null ? $"Volume: {volume.name}" : "Volume: none in this scene");
                for (int i = 0; i < channels.Length; i++)
                {
                    var effect = (PostEffect)i;
                    Channel channel = channels[i];
                    bool asked = PostEffects.IsVolume(effect) ? channel.requests.Count > 0 : channel.driven;
                    text.Append('\n').Append(effect).Append(": base ").Append(channel.baseline.ToString("0.###"));
                    if (!asked) continue;
                    text.Append(", asked ").Append(channel.live.ToString("0.###"))
                        .Append(", shown ").Append(channel.resolved.ToString("0.###"));
                    if (!Allowed(effect)) text.Append("  [runtime off]");
                }
                return text.ToString();
            }
        }
    }
}
