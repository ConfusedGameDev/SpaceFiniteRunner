using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FiniteRunner
{
    /// <summary>
    /// Owns the track spline. Converts (spline t, lateral offset) into world
    /// poses for the ship, pads and markers. Distance along the track is the
    /// authoritative coordinate — the spline grows during the run (endless
    /// streaming), which shifts normalized t, so consumers map distance to t
    /// through <see cref="DistanceToT"/> every frame.
    /// </summary>
    public class TrackManager : MonoBehaviour
    {
        [SerializeField] SplineContainer spline;

        [Tooltip("How far the ship can steer to each side of the spline center.")]
        [SerializeField, Min(0.5f)] float halfWidth = 6f;

        public SplineContainer Spline => spline;
        public float HalfWidth => halfWidth;
        public float Length { get; private set; }

        /// <summary>
        /// Sets the playable lane from a full track width in meters. Driven by
        /// the TrackGenerator's Core Settings so the gameplay clamp, the pad
        /// placement bounds and the road meshes all share one width knob.
        /// </summary>
        public void SetWidth(float fullWidth) => halfWidth = Mathf.Max(0.5f, fullWidth * 0.5f);

        void Awake() => Recalculate();
        void OnValidate() => Recalculate();

        public void Recalculate() => Length = spline != null ? spline.CalculateLength() : 0f;

        /// <summary>
        /// Maps a world-space distance from the track start to normalized t,
        /// using the spline's cached arc-length tables. Clamped to the
        /// currently generated track.
        /// </summary>
        public float DistanceToT(float distance)
        {
            if (spline == null) return 0f;
            if (Length <= 0f) Recalculate();
            if (Length <= 0f) return 0f;

            float t = SplineUtility.GetNormalizedInterpolation(
                spline.Spline, math.clamp(distance, 0f, Length), PathIndexUnit.Distance);
            return math.clamp(t, 0f, 1f);
        }

        /// <summary>World pose on the track at normalized t, shifted laterally across the width.</summary>
        public void GetPose(float t, float lateral, out Vector3 position, out Quaternion rotation)
        {
            spline.Evaluate(t, out float3 pos, out float3 tangent, out float3 up);
            float3 fwd = math.normalizesafe(tangent, new float3(0f, 0f, 1f));
            float3 upDir = math.normalizesafe(up, new float3(0f, 1f, 0f));
            float3 right = math.normalize(math.cross(upDir, fwd));

            position = (Vector3)(pos + right * lateral);
            rotation = Quaternion.LookRotation(fwd, upDir);
        }

        /// <summary>Pose at a world-space distance from the track start.</summary>
        public void GetPoseAtDistance(float distance, float lateral, out Vector3 position, out Quaternion rotation)
            => GetPose(DistanceToT(distance), lateral, out position, out rotation);
    }
}
