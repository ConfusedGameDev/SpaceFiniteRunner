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

        // The container re-acquired off this object when the serialized
        // reference goes stale (a stale editor session): the spline is always
        // the component beside this one, never something to destroy or replace.
        SplineContainer Container
        {
            get
            {
                if (spline == null) spline = GetComponent<SplineContainer>();
                return spline;
            }
        }

        [Tooltip("How far the ship can steer to each side of the spline center.")]
        [SerializeField, Min(0.5f)] float halfWidth = 6f;

        readonly List<TrackSection> sections = new(); // sorted by StartDistance

        public SplineContainer Spline => Container;

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

        /// <summary>True on an authored circuit: the spline is closed and every distance wraps modulo <see cref="Length"/>.</summary>
        public bool Closed { get; private set; }

        /// <summary>
        /// Closes (or opens) the spline into a circuit. Unity re-smooths the
        /// two seam knots on close, so the join is as smooth as any other
        /// AutoSmooth knot; the closing curve counts in <see cref="Length"/>.
        /// </summary>
        public void SetClosed(bool closed)
        {
            Closed = closed;
            if (Container != null) Container.Spline.Closed = closed;
            Recalculate();
        }

        /// <summary>
        /// The track distance nearest a world point, and the point's lateral
        /// there — by sampling <see cref="GetPoseAtDistance"/> along the whole
        /// track (coarse, then refined), so loops and tubes count too. For the
        /// editor's layout tool: ~1000 lookups per call on a 20 km circuit.
        /// </summary>
        public float NearestDistance(Vector3 point, out float lateral, float coarseStep = 20f)
        {
            lateral = 0f;
            float length = Length;
            if (length <= 0f) return 0f;

            float best = 0f;
            float bestSq = float.MaxValue;
            for (float d = 0f; d < length; d += Mathf.Max(coarseStep, 1f))
            {
                GetPoseAtDistance(d, 0f, out Vector3 p, out _);
                float sq = (p - point).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = d; }
            }
            float span = Mathf.Max(coarseStep, 1f);
            for (int pass = 0; pass < 4; pass++)
            {
                float lo = best - span, hi = best + span, step = span / 5f;
                for (float d = lo; d <= hi; d += step)
                {
                    GetPoseAtDistance(d, 0f, out Vector3 p, out _);
                    float sq = (p - point).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = d; }
                }
                span = step;
            }

            GetPoseAtDistance(best, 0f, out Vector3 centre, out Quaternion rotation);
            lateral = Vector3.Dot(point - centre, rotation * Vector3.right);
            return Closed ? Wrap(best) : Mathf.Clamp(best, 0f, length);
        }

        /// <summary>
        /// A track distance brought into the lap: modulo <see cref="Length"/>
        /// on a closed track (negative distances land at the end of the lap,
        /// which IS the road behind the start line), unchanged on an open one.
        /// The ship's own distance never wraps — laps are counted off it — so
        /// every lookup here wraps instead.
        /// </summary>
        public float Wrap(float distance)
        {
            if (!Closed || Length <= 0f) return distance;
            return distance - Mathf.Floor(distance / Length) * Length;
        }

        void Awake() => Recalculate();
        void OnValidate() => Recalculate();

        public void Recalculate()
        {
            SplineLength = Container != null ? Container.CalculateLength() : 0f;
            float inserted = 0f;
            foreach (var s in sections) inserted += s.InsertedLength;
            Length = SplineLength + inserted;
        }

        /// <summary>Drops every knot and every section. Call <see cref="Recalculate"/> after the rebuild.</summary>
        public void ClearKnots()
        {
            if (Container != null)
            {
                Container.Spline.Clear();
                Container.Spline.Closed = false;
            }
            Closed = false;
            sections.Clear();
        }

        /// <summary>Appends an auto-smoothed knot at a world position (knots are never removed during a run).</summary>
        public void AppendKnot(float3 position)
        {
            if (Container != null) Container.Spline.Add(new BezierKnot(position), TangentMode.AutoSmooth);
        }

        /// <summary>
        /// Appends an auto-smoothed knot carrying an authored rotation. AutoSmooth
        /// recomputes the tangents from the neighbours but keeps the rotation's
        /// UP (projected onto the tangent plane) — that up is how the road's
        /// grade and bank reach every pose evaluated between the knots.
        /// </summary>
        public void AppendKnot(float3 position, quaternion rotation)
        {
            if (Container != null)
                Container.Spline.Add(new BezierKnot(position, float3.zero, float3.zero, rotation), TangentMode.AutoSmooth);
        }

        /// <summary>
        /// Appends a knot with an EXPLICIT tangent (in = −tangent, out =
        /// tangent, Continuous): unlike AutoSmooth it is never reshaped by
        /// the knots that land after it, so the pose at this knot is fixed
        /// the moment it is laid — what a feature's entry and a loop's exit
        /// need. A uniform-parameter tangent is the segment direction ×
        /// chord / 3. <paramref name="tangent"/> is in WORLD space; a
        /// BezierKnot stores its tangents in the knot's local frame, so it is
        /// brought into that frame here (a world tangent handed over as-is
        /// would be rotated twice — a kink at every feature knot).
        /// </summary>
        public void AppendKnot(float3 position, quaternion rotation, float3 tangent)
        {
            if (Container == null) return;
            float3 local = math.mul(math.inverse(rotation), tangent);
            Container.Spline.Add(new BezierKnot(position, -local, local, rotation), TangentMode.Continuous);
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
            distance = Wrap(distance);
            foreach (var s in sections)
            {
                if (distance < s.StartDistance) return null;
                if (distance < s.EndDistance) return s;
            }
            return null;
        }

        /// <summary>Spline distance for a track distance (inside an insert: the insert's start; past one: shifted by what it inserted, so a loop's exit maps to its exit knot).</summary>
        public float SplineDistanceOf(float distance)
        {
            distance = Wrap(distance);
            float offset = 0f;
            foreach (var s in sections)
            {
                if (distance < s.StartDistance) break;
                if (distance < s.EndDistance) return s.InsertsDistance ? s.StartDistance - offset : distance - offset;
                offset += s.InsertedLength;
            }
            return distance - offset;
        }

        /// <summary>The steering lane at a track distance: ±half width on the road, a section's own band inside one.</summary>
        public void GetLateralBand(float distance, out float min, out float max)
        {
            distance = Wrap(distance);
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
            if (Container == null) return 0f;
            if (SplineLength <= 0f) Recalculate();
            if (SplineLength <= 0f) return 0f;

            // Closed: modulo the lap (SplineContainer.Evaluate clamps t, so the
            // wrap has to happen here); open: clamped to the generated track.
            float d = Closed
                ? splineDistance - Mathf.Floor(splineDistance / SplineLength) * SplineLength
                : math.clamp(splineDistance, 0f, SplineLength);
            float t = SplineUtility.GetNormalizedInterpolation(Container.Spline, d, PathIndexUnit.Distance);
            return math.clamp(t, 0f, 1f);
        }

        /// <summary>World pose on the spline at normalized t, shifted laterally across the width.</summary>
        public void GetPose(float t, float lateral, out Vector3 position, out Quaternion rotation)
        {
            Container.Evaluate(t, out float3 pos, out float3 tangent, out float3 up);
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
            distance = Wrap(distance);
            float offset = 0f;
            foreach (var s in sections)
            {
                if (distance < s.StartDistance) break;
                if (distance < s.EndDistance)
                {
                    s.GetPose(this, s.StartDistance - offset, distance - s.StartDistance, lateral, out position, out rotation);
                    return;
                }
                offset += s.InsertedLength;
            }
            GetPose(DistanceToT(distance - offset), lateral, out position, out rotation);
        }
    }
}
