using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// A volume that takes a ship out of play: the pit under a bridge, the
    /// void around a floating track, a hazard. Put it on any trigger collider;
    /// it moves itself to the <see cref="ShipLayers.Volume"/> layer, where
    /// nothing collides with it and only the ship's swept query finds it —
    /// which is why it works at Light Speed (a trigger callback would be
    /// tunnelled by a body covering 36 m a step, the sweep cannot be).
    /// The ship's <see cref="ShipRecovery"/> does the rest.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class KillVolume : MonoBehaviour
    {
        [Tooltip("Go straight to the respawn wait, without the tumbling fall — for a hazard that should read as a hit, not a drop.")]
        [SerializeField] bool skipFall;

        public bool SkipFall => skipFall;

        void Awake() => Prepare();
        void Reset() => Prepare();

        void Prepare()
        {
            gameObject.layer = ShipLayers.Volume;
            foreach (Collider collider in GetComponents<Collider>())
            {
                if (collider is MeshCollider mesh) mesh.convex = true; // a non-convex mesh cannot be a trigger
                collider.isTrigger = true;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (TryGetComponent(out BoxCollider box)) Gizmos.DrawCube(box.center, box.size);
        }
    }
}
