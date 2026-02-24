using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Placed at the end of the track. Detects when the vehicle crosses the line,
    /// then waits for the vehicle to stop and measures the signed stop distance.
    ///
    /// Signed distance convention:
    ///   Negative → vehicle stopped BEFORE the line (undershot)
    ///   Zero     → vehicle stopped exactly ON the line (perfect)
    ///   Positive → vehicle stopped PAST the line (overshot)
    ///
    /// GameObject setup:
    ///   - Add a BoxCollider sized to span the full track width and a tall enough height.
    ///   - Set isTrigger = true on the collider.
    ///   - Orient the GameObject so its local forward (Z+) points along the track direction.
    ///   - Layer: FinishLine.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FinishLine : MonoBehaviour
    {
        bool vehicleCrossed;
        VehicleController trackedVehicle;

        void OnTriggerEnter(Collider other)
        {
            if (vehicleCrossed) return;
            if (!other.TryGetComponent<VehicleController>(out VehicleController vehicle)) return;

            vehicleCrossed  = true;
            trackedVehicle  = vehicle;

            // Subscribe to the stop event so we measure position at the exact stop moment.
            RaceEvents.OnVehicleStopped += HandleVehicleStopped;
        }

        void HandleVehicleStopped()
        {
            RaceEvents.OnVehicleStopped -= HandleVehicleStopped;

            float signedDistance = CalculateSignedDistance(trackedVehicle.WorldPosition);
            RaceEvents.RaiseFinishLineCrossed(signedDistance);
        }

        /// <summary>
        /// Projects the vehicle's stop position onto the finish line's local forward axis (Z+).
        /// Positive result = vehicle is past the line; negative = before it.
        /// </summary>
        float CalculateSignedDistance(Vector3 vehicleWorldPosition)
        {
            Vector3 toVehicle = vehicleWorldPosition - transform.position;
            return Vector3.Dot(toVehicle, transform.forward);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;
            // Draw a thin slab to visualise the finish line plane
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(10f, 3f, 0.1f));
        }
    }
}
