using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// A hand-placed vehicle under the city prefab's DefaultVehicles socket:
    /// part of the scenery until the player arrives. It parks itself in Awake
    /// (kinematic rigidbody — zero physics cost, an immovable obstacle to AI
    /// cars, exactly like the decoration props) and wakes when the player's
    /// car enters its padded box trigger, going dynamic so it can be rammed,
    /// shunted and wrecked. It never drives — the CarController is disabled,
    /// there is no driver — and it is never despawned: the traffic manager
    /// only culls cars it spawned itself, and the only way this car leaves
    /// the city is the player destroying it (a CarHealth is attached so the
    /// normal wreck path applies; the wreck lingers per that component's
    /// rules). Its cell reading as blocked for spawn queries is correct — a
    /// parked car IS occupying the road.
    /// </summary>
    public class DefaultVehicle : MonoBehaviour
    {
        [Tooltip("How far beyond the car's bounds the wake trigger reaches. Must exceed the distance the player covers in one physics step at top speed, or contact happens while still kinematic.")]
        [PropertyRange(2f, 30f), SuffixLabel("m", true)]
        public float wakeDistance = 6f;

        Rigidbody body;
        bool awakened;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            // Parked means parked: no driver ever, and the controller's wheel
            // sim has nothing to do until (and unless) physics takes over.
            var controller = GetComponent<CarController>();
            if (controller != null) controller.enabled = false;

            if (GetComponent<CarHealth>() == null) gameObject.AddComponent<CarHealth>();

            AddWakeTrigger();
        }

        /// <summary>
        /// Box trigger padded <see cref="wakeDistance"/> metres beyond the
        /// car's horizontal bounds — the same pre-wake contract as
        /// <see cref="Decoration.DecorationProp"/>: flip dynamic BEFORE the
        /// player makes contact, so the collision resolves by true mass ratio
        /// instead of against an infinite-mass kinematic body.
        /// </summary>
        void AddWakeTrigger()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

            Transform root = transform;
            Vector3 scale = root.lossyScale;
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = root.InverseTransformPoint(bounds.center);
            trigger.size = new Vector3(
                (bounds.size.x + 2f * wakeDistance) / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                (bounds.size.z + 2f * wakeDistance) / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        }

        void OnTriggerEnter(Collider other)
        {
            if (awakened || body == null) return;
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || rb.GetComponent<CarInput>() == null) return; // only the player wakes scenery

            awakened = true;
            body.isKinematic = false;
            // The player arrives at speed — speculative CCD so the shunted car
            // doesn't tunnel through a building front.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
    }
}
