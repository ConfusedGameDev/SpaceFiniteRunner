using System.Collections;
using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Abstract base for all in-track objects that modify vehicle speed.
    /// Handles the trigger detection, single-use logic, and respawn timer.
    /// Subclasses only need to implement ApplyTo() with a single line.
    ///
    /// GameObject setup:
    ///   - Add a Collider with isTrigger = true (sized to cover the track object's area).
    ///   - Assign a TrackObjectConfigSO in the Inspector.
    ///   - Layer should be set to TrackObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class SpeedModifierBase : MonoBehaviour
    {
        [SerializeField] protected TrackObjectConfigSO config;

        bool isActive = true;

        void OnTriggerEnter(Collider other)
        {
            if (!isActive) return;
            if (!other.TryGetComponent<VehicleController>(out VehicleController vehicle)) return;

            ApplyTo(vehicle);

            if (config.singleUse)
                StartCoroutine(RespawnRoutine());
        }

        /// <summary>
        /// Apply the speed modification to the vehicle.
        /// Call vehicle.ModifySpeed(config.speedDelta) with the appropriate sign.
        /// </summary>
        public abstract void ApplyTo(VehicleController vehicle);

        IEnumerator RespawnRoutine()
        {
            isActive = false;
            SetVisualActive(false);

            yield return new WaitForSeconds(config.respawnDelay);

            isActive = true;
            SetVisualActive(true);
        }

        /// <summary>
        /// Override in subclasses to show/hide a visual indicator when the object
        /// deactivates and respawns. Default implementation toggles child renderers.
        /// </summary>
        protected virtual void SetVisualActive(bool active)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.enabled = active;
        }
    }
}
