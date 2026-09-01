using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.UI;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Collectibles
{
    /// <summary>
    /// A pickup the player drives through: an <see cref="Id"/> (what it is —
    /// "floppy", "chip"; several collectibles share one id), a trigger volume
    /// and a mesh slot whose object spins on Y and hovers up and down over its
    /// authored position. Driving into it counts it into the saved profile
    /// (<see cref="PlayerStats.RecordCollectible"/> — the LOG lists each id),
    /// raises the static <see cref="Collected"/> event the LevelManager's
    /// COLLECT OBJECTS objectives tally, and destroys the object. Every
    /// collider on it is forced to a trigger, and one is added when there is
    /// none, so a bare object with a mesh child works. The player is
    /// recognised by <see cref="DialogueTrigger.IsPlayer"/>, the one rule
    /// every volume in the city uses. Hand-place it as a root object or under
    /// the city prefab's <c>AdditionalItems</c> socket (which survives
    /// rebakes); GameObject → Police Escape → Collectible drops a ready one.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        /// <summary>Raised the moment the player collects one, before it is destroyed.</summary>
        public static event System.Action<Collectible> Collected;

        [Tooltip("What this is — the id a COLLECT OBJECTS objective names and the LOG counts by. Many collectibles share one id.")]
        [Required]
        [SerializeField] string id = "floppy";

        [Tooltip("The visual that spins and hovers. Empty = the first child with a renderer (or this object).")]
        [SerializeField] Transform mesh;

        [TitleGroup("Motion")]
        [Tooltip("Spin around the vertical axis, degrees per second.")]
        [PropertyRange(0f, 360f), SuffixLabel("°/s", true)]
        [SerializeField] float spinDegreesPerSecond = 90f;

        [TitleGroup("Motion")]
        [Tooltip("How far above and below its authored position the mesh hovers.")]
        [PropertyRange(0f, 2f), SuffixLabel("m", true)]
        [SerializeField] float hoverAmplitude = 0.35f;

        [TitleGroup("Motion")]
        [Tooltip("Hover cycles per second.")]
        [PropertyRange(0.05f, 3f), SuffixLabel("Hz", true)]
        [SerializeField] float hoverFrequency = 0.8f;

        [TitleGroup("Pickup")]
        [Tooltip("Optional one-shot played at the pickup (FX bus). Empty = silent.")]
        [SerializeField] AudioClip pickupClip;

        [TitleGroup("Pickup")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float pickupVolume = 1f;

        Vector3 meshBase;
        float phase;
        bool collected;

        public string Id => string.IsNullOrEmpty(id) ? "" : id.Trim();

        void Awake()
        {
            if (mesh == null)
            {
                var renderer = GetComponentInChildren<Renderer>();
                mesh = renderer != null ? renderer.transform : transform;
            }
            meshBase = mesh.localPosition;
            phase = Random.value * 10f; // desync neighbours so a row never bobs in unison

            var colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                var sphere = gameObject.AddComponent<SphereCollider>();
                sphere.radius = 1.5f;
                colliders = new Collider[] { sphere };
            }
            foreach (var collider in colliders) collider.isTrigger = true;

            if (string.IsNullOrEmpty(Id))
                Debug.LogWarning($"{nameof(Collectible)} '{name}' has no id — it will be counted as nothing.", this);
        }

        void Update()
        {
            if (mesh == null) return;
            float bob = Mathf.Sin((Time.time + phase) * hoverFrequency * 2f * Mathf.PI) * hoverAmplitude;
            mesh.localPosition = meshBase + Vector3.up * bob;
            mesh.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
        }

        void OnTriggerEnter(Collider other)
        {
            if (collected || !DialogueTrigger.IsPlayer(other)) return;
            collected = true;

            PlayerStats.RecordCollectible(Id);
            if (pickupClip != null) PlayOneShot(pickupClip, transform.position, pickupVolume);
            Collected?.Invoke(this);
            Destroy(gameObject);
        }

        // A throwaway source routed through the FX bus — PlayClipAtPoint cannot
        // take a mixer group, and the pickup outlives this object.
        static void PlayOneShot(AudioClip clip, Vector3 position, float volume)
        {
            var go = new GameObject("CollectiblePickup");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.outputAudioMixerGroup = GameAudio.Fx;
            source.Play();
            Destroy(go, clip.length + 0.1f);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 1f, 0.9f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 1.5f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, $"[{id}]");
#endif
        }
    }
}
