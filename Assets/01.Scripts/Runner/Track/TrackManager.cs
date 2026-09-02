using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Owns the track spline. Converts (track distance, lateral offset) into
    /// world poses for the ship, pads, patrol and road stamps. Distance along
    /// the track is the authoritative coordinate — the spline grows during
    /// the run (endless streaming), which shifts normalized t, so consumers
    /// map distance to t through <see cref="DistanceToT"/> every frame. It is
    /// also the only object that touches the SplineContainer: the generator
    /// grows the track through <see cref="AppendKnot"/> / <see cref="ClearKnots"/>,
    /// so a future multi-spline route layer has one seam to replace.
    ///
    /// <b>Sections</b> (<see cref="TrackSection"/>) are stretches of track
    /// distance with their own pose function — a loop is a vertical circle
    /// standing on the road (an insert: it adds track distance), a tube
    /// curls a stretch of spline into a pipe (an overlay: it adds none).
    /// Track distance = spline distance + the lengths of every INSERT before
    /// it, so <see cref="Length"/> is the whole track including inserts and
    /// <see cref="GetPoseAtDistance"/> routes a distance inside a section to
    /// it. Everything that thinks in distance rides a section unchanged; the
    /// steering lane is asked per distance (<see cref="GetLateralBand"/>)
    /// because a tube's lane is an arc.
    /// </summary>
    public class TrackManager : MonoBehaviour
    {
        [SerializeField] SplineContainer spline;

        [Tooltip("How far the ship can steer to each side of the spline center.")]
        [SerializeField, Min(0.5f)] float halfWidth = 6f;

        readonly List<TrackSection> sections = new(); // sorted by StartDistance

        public SplineContainer Spline => spline;

        /// <summary>The plain road's half width; ask <see cref="GetLateralBand"/> for the lane at a distance.</summary>
        public float HalfWidth => halfWidth;

        /// <summary>Whole track length in track distance: the spline plus every inserted section.</summary>
        public float Length { get; private set; }

        /// <summary>The spline's own arc length.</summary>
        public float SplineLength { get; private set; }

        public IReadOnlyList<TrackSection> Sections => sections;

        /// <summary>
        /// Sets the playable lane from a full track width in meters. Driven by
        /// the TrackGenerator's Core Settings so the gameplay clamp, the pad
        /// placement bounds and the road meshes all share one width knob.
        /// </summary>
        public void SetWidth(float fullWidth) => halfWidth = Mathf.Max(0.5f, fullWidth * 0.5f);

        void Awake() => Recalculate();
        void OnValidate() => Recalculate();

        public void Recalculate()
        {
            SplineLength = spline != null ? spline.CalculateLength() : 0f;
            float inserted = 0f;
            foreach (var s in sections)
                if (s.InsertsDistance) inserted += s.Length;
            Length = SplineLength + inserted;
        }

        /// <summary>Drops every knot and every section. Call <see cref="Recalculate"/> after the rebuild.</summary>
        public void ClearKnots()
        {
            if (spline != null) spline.Spline.Clear();
            sections.Clear();
        }

        /// <summary>Appends an auto-smoothed knot at a world position (knots are never removed during a run).</summary>
        public void AppendKnot(float3 position)
        {
            if (spline != null) spline.Spline.Add(new BezierKnot(position), TangentMode.AutoSmooth);
        }

        /// <summary>
        /// Registers a section at its start distance. Must happen before
        /// anything is placed beyond that distance — an insert shifts every
        /// distance past it by its length.
        /// </summary>
        public void AddSection(TrackSection section)
        {
            if (section == null) return;
            int i = 0;
            while (i < sections.Count && sections[i].StartDistance <= section.StartDistance) i++;
            sections.Insert(i, section);
            Recalculate();
        }

        /// <summary>The section covering a track distance, or null on plain spline.</summary>
        public TrackSection SectionAt(float distance)
        {
            foreach (var s in sections)
            {
                if (distance < s.StartDistance) return null;
                if (distance < s.EndDistance) return s;
            }
            return null;
        }

        /// <summary>Spline distance for a track distance (inside an insert: the insert's start).</summary>
        public float SplineDistanceOf(float distance)
        {
            float offset = 0f;
            foreach (var s in sections)
            {
                if (distance < s.StartDistance) break;
                if (!s.InsertsDistance) continue;
                if (distance < s.EndDistance) return s.StartDistance - offset;
                offset += s.Length;
            }
            return distance - offset;
        }

        /// <summary>The steering lane at a track distance: ±half width on the road, a section's own band inside one.</summary>
        public void GetLateralBand(float distance, out float min, out float max)
        {
            TrackSection s = SectionAt(distance);
            if (s != null) s.GetLateralBand(distance - s.StartDistance, halfWidth, out min, out max);
            else
            {
                min = -halfWidth;
                max = halfWidth;
            }
        }

        /// <summary>
        /// Maps a SPLINE distance from the track start to normalized t, using
        /// the spline's cached arc-length tables. Clamped to the currently
        /// generated track.
        /// </summary>
        public float DistanceToT(float splineDistance)
        {
            if (spline == null) return 0f;
            if (SplineLength <= 0f) Recalculate();
            if (SplineLength <= 0f) return 0f;

            float t = SplineUtility.GetNormalizedInterpolation(
                spline.Spline, math.clamp(splineDistance, 0f, SplineLength), PathIndexUnit.Distance);
            return math.clamp(t, 0f, 1f);
        }

        /// <summary>World pose on the spline at normalized t, shifted laterally across the width.</summary>
        public void GetPose(float t, float lateral, out Vector3 position, out Quaternion rotation)
        {
            spline.Evaluate(t, out float3 pos, out float3 tangent, out float3 up);
            float3 fwd = math.normalizesafe(tangent, new float3(0f, 0f, 1f));
            float3 upDir = math.normalizesafe(up, new float3(0f, 1f, 0f));
            float3 right = math.normalizesafe(math.cross(upDir, fwd), new float3(1f, 0f, 0f));

            position = (Vector3)(pos + right * lateral);
            rotation = Quaternion.LookRotation(fwd, upDir);
        }

        /// <summary>The spline's own pose at a SPLINE distance — what an overlay section shapes.</summary>
        public void GetSplinePoseAtDistance(float splineDistance, float lateral, out Vector3 position, out Quaternion rotation)
            => GetPose(DistanceToT(splineDistance), lateral, out position, out rotation);

        /// <summary>Pose at a TRACK distance from the start: a section's own pose inside one, the spline's elsewhere.</summary>
        public void GetPoseAtDistance(float distance, float lateral, out Vector3 position, out Quaternion rotation)
        {
            float offset = 0f;
            foreach (var s in sections)
            {
                if (distance < s.StartDistance) break;
                if (distance < s.EndDistance)
                {
                    s.GetPose(this, s.StartDistance - offset, distance - s.StartDistance, lateral, out position, out rotation);
                    return;
                }
                if (s.InsertsDistance) offset += s.Length;
            }
            GetPose(DistanceToT(distance - offset), lateral, out position, out rotation);
        }
    }
}
