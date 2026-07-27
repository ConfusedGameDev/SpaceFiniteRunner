using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FiniteRunner
{
    /// <summary>
    /// Owns the track spline. Converts (spline t, lateral offset) into world
    /// poses for the ship, pads and markers, and advances t by travelled
    /// distance in an arc-length-correct way.
    /// </summary>
    public class TrackManager : MonoBehaviour
    {
        [SerializeField] SplineContainer spline;

        [Tooltip("How far the ship can steer to each side of the spline center.")]
        [SerializeField, Min(0.5f)] float halfWidth = 6f;

        public SplineContainer Spline => spline;
        public float HalfWidth => halfWidth;
        public float Length { get; private set; }

        void Awake() => Recalculate();
        void OnValidate() => Recalculate();

        public void Recalculate() => Length = spline != null ? spline.CalculateLength() : 0f;

        /// <summary>Advances normalized position <paramref name="t"/> by a world-space distance.</summary>
        public float AdvanceT(float t, float distance)
        {
            if (spline == null || distance <= 0f) return t;
            SplineUtility.GetPointAtLinearDistance(spline.Spline, t, distance, out float newT);
            return math.min(newT, 1f);
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

        /// <summary>Pose at a world-space distance from the track start. Editor/placement helper.</summary>
        public void GetPoseAtDistance(float distance, float lateral, out Vector3 position, out Quaternion rotation)
        {
            Recalculate();
            SplineUtility.GetPointAtLinearDistance(spline.Spline, 0f, distance, out float t);
            GetPose(math.min(t, 1f), lateral, out position, out rotation);
        }
    }
}
