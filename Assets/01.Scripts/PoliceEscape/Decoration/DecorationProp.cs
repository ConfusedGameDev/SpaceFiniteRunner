using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// Runtime behaviour of a street prop: a rigidbody that only the PLAYER
    /// can push. It spawns kinematic — an immovable obstacle to police and
    /// traffic — with a padded wake trigger around it. When the player's car
    /// (the one with a <see cref="Vehicles.CarInput"/>) enters the trigger, a
    /// prop LIGHTER than the car flips dynamic before contact, so the actual
    /// collision resolves by true mass ratio and a cone never brakes the car;
    /// a prop heavier than the car stands its kinematic ground and the car
    /// halts against it, exactly the "heavier wins" rule. The first real
    /// player contact adds the set's impact impulse for juice. The per-prop
    /// mass is what differentiates the feel: cones fly, light posts keel over
    /// slowly, barriers shrug the hit off — and a prop flagged explosive gets
    /// an <see cref="ExplosiveBarrel"/> on top, which answers a hard enough
    /// contact with a blast instead. City clearance checks
    /// (<see cref="City.CityManager.IsCellClear"/>) look for this component to
    /// ignore props, so a cone on the sidewalk never blocks a car spawn.
    /// </summary>
    public class DecorationProp : MonoBehaviour
    {
        Rigidbody body;
        float impactMomentum;
        float maxLaunchFactor;
        float upBias;
        bool dynamicBody;
        bool impulseDone;

        /// <summary>
        /// Fit a freshly instantiated prop for duty: convex mesh colliders on
        /// every child mesh (skipped when the prefab ships its own — convex,
        /// because dynamic rigidbodies require it), a kinematic rigidbody with
        /// the definition's physics feel, the padded wake trigger, and this
        /// component to run the wake-up.
        /// </summary>
        public static void Configure(GameObject instance, DecorationDefinition definition, DecorationSet set)
        {
            if (instance.GetComponentInChildren<Collider>() == null)
            {
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = filter.sharedMesh;
                    meshCollider.convex = true;
                }
            }

            var body = instance.AddComponent<Rigidbody>();
            body.mass = definition.mass;
            body.angularDamping = definition.angularDamping;
            body.isKinematic = true;

            var prop = instance.AddComponent<DecorationProp>();
            prop.body = body;
            prop.impactMomentum = set.impactMomentum;
            prop.maxLaunchFactor = set.maxLaunchFactor;
            prop.upBias = set.impactUpBias;

            AddWakeTrigger(instance, set.wakeDistance);

            // Explosive props keep everything above — they are ordinary street
            // furniture until something hits them hard enough.
            if (definition.explosive) ExplosiveBarrel.Configure(instance, set);
        }

        /// <summary>
        /// A box trigger padded <paramref name="wakeDistance"/> metres beyond
        /// the prop's horizontal bounds. The padding must exceed the distance
        /// the car covers in one physics step at top speed, or a fast car
        /// reaches the mesh while the prop is still kinematic and eats the
        /// infinite-mass halt this trigger exists to prevent.
        /// </summary>
        static void AddWakeTrigger(GameObject instance, float wakeDistance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

            // World AABB mapped back into the (uniformly scaled, yawed) root —
            // the yaw makes this approximate, which the padding absorbs.
            Transform root = instance.transform;
            Vector3 scale = root.lossyScale;
            var trigger = instance.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = root.InverseTransformPoint(bounds.center);
            trigger.size = new Vector3(
                (bounds.size.x + 2f * wakeDistance) / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                (bounds.size.z + 2f * wakeDistance) / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        }

        void OnTriggerEnter(Collider other)
        {
            if (dynamicBody || body == null) return;

            // Pre-wake for props the approaching player car out-masses: going
            // dynamic before contact means the collision itself is resolved by
            // mass ratio, so light props can't brake the car. Heavier props
            // skip this and stay kinematic — the car halting against them is
            // the intended outcome.
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || rb.mass <= body.mass) return;
            if (rb.GetComponent<Vehicles.CarInput>() == null) return;
            MakeDynamic();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (impulseDone || body == null) return;

            // Only the player's car moves a prop — AI cars just bump into a
            // static obstacle. CarInput sits on the car root beside its
            // rigidbody, which is exactly what PatrolManager.FindPlayerCar
            // keys on too.
            Rigidbody other = collision.rigidbody;
            if (other == null || other.GetComponent<Vehicles.CarInput>() == null) return;

            MakeDynamic();
            impulseDone = true;

            // One-shot juice on top of the physical response: the set's impact
            // momentum along the car's incoming velocity (relativeVelocity is
            // self minus other — negate it), capped so a featherweight can't
            // launch at absurd speed. For heavy props this is also the only
            // kick — their first contact happened against the kinematic body.
            Vector3 velocity = -collision.relativeVelocity;
            velocity += Vector3.up * (velocity.magnitude * upBias);
            float effectiveMass = Mathf.Min(impactMomentum, body.mass * maxLaunchFactor);
            Vector3 contact = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
            body.AddForceAtPosition(velocity * effectiveMass, contact, ForceMode.Impulse);
        }

        void MakeDynamic()
        {
            if (dynamicBody) return;
            dynamicBody = true;
            body.isKinematic = false;
            // Light props leave at car speed and beyond — speculative CCD so
            // they don't tunnel through buildings on the way out.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
    }
}
