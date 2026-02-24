using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Spring-damper hover simulation. Attach alongside VehicleController.
    /// Casts a ray downward from each configured offset point and applies an upward
    /// spring-damper force at that world position. Multi-point application gives
    /// natural pitch/roll response to terrain without locking rotation axes.
    ///
    /// Rigidbody requirements:
    ///   - Linear Drag  = 0  (drag is handled explicitly in VehicleController)
    ///   - Angular Drag = 0  (steering damping is handled in VehicleController)
    ///   - Freeze Rotation X, Z  (prevents the ship from tumbling end-over-end)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HoverPhysics : MonoBehaviour
    {
        [SerializeField] VehicleStatsSO stats;
        [SerializeField] LayerMask groundLayer;

        Rigidbody rb;

        /// <summary>True if at least one hover ray is currently detecting ground.</summary>
        public bool IsGrounded { get; private set; }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            IsGrounded = false;

            foreach (Vector3 localOffset in stats.raycastOffsets)
            {
                Vector3 worldOrigin = transform.TransformPoint(localOffset);
                ApplyHoverForceAtPoint(worldOrigin);
            }
        }

        void ApplyHoverForceAtPoint(Vector3 worldOrigin)
        {
            Ray ray = new Ray(worldOrigin, -transform.up);

            if (!Physics.Raycast(ray, out RaycastHit hit, stats.raycastRange, groundLayer))
                return;

            IsGrounded = true;

            // Spring: push up proportional to how far below target height we are.
            float compression = stats.hoverHeight - hit.distance;
            float springForce = stats.springStrength * compression;

            // Damper: oppose vertical velocity at this specific point (accounts for angular velocity).
            // dot with transform.up gives the component of point velocity along the spring axis.
            float pointVelocityUp = Vector3.Dot(rb.GetPointVelocity(worldOrigin), transform.up);
            float damperForce     = stats.springDamping * pointVelocityUp;

            Vector3 force = transform.up * (springForce - damperForce);
            rb.AddForceAtPosition(force, worldOrigin, ForceMode.Force);
        }

        void OnDrawGizmosSelected()
        {
            if (stats == null) return;

            foreach (Vector3 localOffset in stats.raycastOffsets)
            {
                Vector3 worldOrigin = transform.TransformPoint(localOffset);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(worldOrigin, worldOrigin - transform.up * stats.raycastRange);
                Gizmos.DrawWireSphere(worldOrigin, 0.05f);
            }
        }
    }
}
