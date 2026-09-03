using ConfusedGameDev.FiniteRunner.UI;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Collectibles
{
    /// <summary>
    /// A pickup the player drives or flies through, shared by both games: an
    /// <see cref="Id"/> (what it is — "floppy", "KeyCard"; several
    /// collectibles share one id), a <see cref="Kind"/> that decides what
    /// collecting it DOES (<see cref="CollectibleKind.Item"/> is just counted,
    /// <see cref="CollectibleKind.Money"/> carries a <see cref="Value"/> in
    /// dollars rolled once from <c>valueRange</c> unless a spawner called
    /// <see cref="SetValue"/>), a trigger volume and a mesh slot whose object
    /// spins around a chosen local axis (Y by default) and hovers up and down
    /// over its authored position. The player is any collider whose rigidbody
    /// or parent chain carries an <see cref="ICollector"/> — the ship's motor
    /// and the city car's input both do — so the same prefab works on the
    /// track and on the street. Collecting plays the optional
    /// <c>pickupClip</c>, raises the static <see cref="Collected"/> event and
    /// destroys the object; it records NOTHING itself — the hand-placed
    /// <see cref="CollectibleManager"/> is the one recorder and runs the
    /// per-kind logic, which is why a scene without one is an error, not a
    /// silent pickup. Every collider on it is forced to a trigger, and one is
    /// added when there is none, so a bare object with a mesh child works.
    /// Hand-place it as a root object or under the city prefab's
    /// <c>AdditionalItems</c> socket (which survives rebakes); GameObject →
    /// Police Escape → Collectible drops a ready one; the runner's
    /// TrackGenerator streams money ones between the orbs.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        /// <summary>The mesh's local axis the spin turns around. Order is the save format — append only.</summary>
        public enum SpinAxis { X = 0, Y = 1, Z = 2 }

        /// <summary>Raised the moment the player collects one, before it is destroyed.</summary>
        public static event System.Action<Collectible> Collected;

        [Tooltip("What this is — the id a COLLECT OBJECTS objective names and the LOG counts by. Many collectibles share one id.")]
        [Required]
        [SerializeField] string id = "floppy";

        [TitleGroup("Kind")]
        [Tooltip("What collecting it does: Item = counted only (objectives), Money = its value is banked.")]
        [EnumToggleButtons]
        [SerializeField] CollectibleKind kind = CollectibleKind.Item;

        [TitleGroup("Kind")]
        [Tooltip("Money only: the dollars this one is worth, rolled once when it wakes. A spawner may set the exact value instead.")]
        [ShowIf("kind", CollectibleKind.Money)]
        [MinMaxSlider(1, 100, true), SuffixLabel("$", true)]
        [SerializeField] Vector2Int valueRange = new(1, 5);

        [Tooltip("The visual that spins and hovers. Empty = the first child with a renderer (or this object).")]
        [SerializeField] Transform mesh;

        [TitleGroup("Motion")]
        [Tooltip("The mesh's local axis it spins around. Y for an upright object; X or Z for something lying flat (a disc, a floppy).")]
        [EnumToggleButtons]
        [SerializeField] SpinAxis spinAxis = SpinAxis.Y;

        [TitleGroup("Motion")]
        [Tooltip("Spin speed around the chosen axis, degrees per second.")]
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
        int value = -1; // rolled in Awake for Money unless SetValue came first

        public string Id => string.IsNullOrEmpty(id) ? "" : id.Trim();
        public CollectibleKind Kind => kind;

        /// <summary>Dollars for a Money pickup (0 for an Item).</summary>
        public int Value => value < 0 ? 0 : value;

        /// <summary>Fixes the value a spawner rolled (overrides the range; safe before or after Awake).</summary>
        public void SetValue(int amount) => value = Mathf.Max(0, amount);

        /// <summary>
        /// Configure a code-built pickup after AddComponent (Awake has run by
        /// then, so the mesh is re-resolved and its base position re-cached).
        /// </summary>
        public void Configure(string newId, CollectibleKind newKind, SpinAxis axis, Transform visual = null)
        {
            id = newId;
            kind = newKind;
            spinAxis = axis;
            if (visual != null) mesh = visual;
            ResolveMesh();
            if (kind == CollectibleKind.Money && value < 0) RollValue();
        }

        void Awake()
        {
            ResolveMesh();
            phase = Random.value * 10f; // desync neighbours so a row never bobs in unison

            var colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                var sphere = gameObject.AddComponent<SphereCollider>();
                sphere.radius = 1.5f;
                colliders = new Collider[] { sphere };
            }
            foreach (var collider in colliders) collider.isTrigger = true;

            if (kind == CollectibleKind.Money && value < 0) RollValue();

            if (string.IsNullOrEmpty(Id))
                Debug.LogWarning($"{nameof(Collectible)} '{name}' has no id — it will be counted as nothing.", this);
        }

        void ResolveMesh()
        {
            if (mesh == null)
            {
                var renderer = GetComponentInChildren<Renderer>();
                mesh = renderer != null ? renderer.transform : transform;
            }
            meshBase = mesh.localPosition;
        }

        // Random.Range(int, int) is max-exclusive.
        void RollValue() => value = Random.Range(Mathf.Max(1, valueRange.x), Mathf.Max(valueRange.x, valueRange.y) + 1);

        void Update()
        {
            if (mesh == null) return;
            float bob = Mathf.Sin((Time.time + phase) * hoverFrequency * 2f * Mathf.PI) * hoverAmplitude;
            mesh.localPosition = meshBase + Vector3.up * bob;
            mesh.Rotate(Axis(spinAxis), spinDegreesPerSecond * Time.deltaTime, Space.Self);
        }

        static Vector3 Axis(SpinAxis axis) => axis switch
        {
            SpinAxis.X => Vector3.right,
            SpinAxis.Z => Vector3.forward,
            _ => Vector3.up
        };

        void OnTriggerEnter(Collider other)
        {
            if (collected || !IsCollector(other)) return;
            collected = true;

            // Loud when the scene has no manager: nothing would record this.
            _ = CollectibleManager.Instance;

            if (pickupClip != null) PlayOneShot(pickupClip, transform.position, pickupVolume);
            Collected?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        /// Is this collider the player? The marker sits on the city car's
        /// rigidbody root (wheel colliders resolve through attachedRigidbody)
        /// and on the ship root above its trigger box. AI cars, props and the
        /// patrol carry none.
        /// </summary>
        static bool IsCollector(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb != null && rb.GetComponent<ICollector>() != null) return true;
            return other.GetComponentInParent<ICollector>() != null;
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
            Gizmos.color = kind == CollectibleKind.Money ? new Color(1f, 0.85f, 0.3f, 0.8f) : new Color(0.6f, 1f, 0.9f, 0.8f);
            // The spawner may have enlarged the trigger — draw what is actually there.
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
            else
            {
                var sphere = GetComponent<SphereCollider>();
                Gizmos.DrawWireSphere(transform.position, sphere != null ? sphere.radius * transform.lossyScale.x : 1.5f);
            }
#if UNITY_EDITOR
            string label = kind == CollectibleKind.Money ? $"[{id}] ${(value < 0 ? valueRange.x : value)}" : $"[{id}]";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, label);
#endif
        }
    }
}
