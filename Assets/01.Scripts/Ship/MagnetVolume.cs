using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// A region with its own say over the magnetic hover. A ship's settings
    /// choose the rule for the whole level (hold to any surface, or
    /// world-gravity: steep surfaces need speed, banks pull downhill); a
    /// volume overrides it for the ship inside — a loop that only holds at
    /// speed in an otherwise magnetic level, or a stretch of wall-riding in a
    /// world-gravity one. Like <see cref="KillVolume"/> it lives on the
    /// <see cref="ShipLayers.Volume"/> layer and is found by the ship's own
    /// query, never by a trigger callback.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class MagnetVolume : MonoBehaviour
    {
        [Tooltip("On = ships inside hold to any surface. Off = world-gravity rules inside.")]
        [SerializeField] bool magnetic;

        public bool Magnetic => magnetic;

        void Awake() => Prepare();
        void Reset() => Prepare();

        void Prepare()
        {
            gameObject.layer = ShipLayers.Volume;
            foreach (Collider collider in GetComponents<Collider>())
            {
                if (collider is MeshCollider mesh) mesh.convex = true;
                collider.isTrigger = true;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = magnetic ? new Color(0.2f, 0.7f, 1f, 0.2f) : new Color(1f, 0.8f, 0.2f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (TryGetComponent(out BoxCollider box)) Gizmos.DrawCube(box.center, box.size);
        }
    }
}
