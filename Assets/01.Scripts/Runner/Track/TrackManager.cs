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
        readonly List<FlatSweep> flatSweeps = new(); // in the order they were laid, so sorted by Start
        readonly List<OpenStretch> openStretches = new(); // likewise

        // Half the step the curvature is measured over: long enough to read
        // through AutoSmooth's knot-to-knot ripple, short next to any sweep.
        const float CurvatureHalfStep = 5f;

        /// <summary>
        /// A run of STRAIGHT, level road the generator authored with no wall
        /// on either side: steer (or dash) too close to the edge and the body
        /// drops off. Grip is not an issue on a straight, so unlike a
        /// <see cref="FlatSweep"/> it only answers <see cref="IsEdgeOpen"/>.
        /// The generator grows <see cref="End"/> segment by segment.
        /// </summary>
        public sealed class OpenStretch
        {
            public float Start { get; }
            public float End { get; set; }

            public OpenStretch(float start, float end)
            {
                Start = start;
                End = end;
            }

            public bool Contains(float distance) => distance >= Start && distance < End;
        }

        /// <summary>
        /// A sweep the generator authored FLAT (unbanked): the one kind of road
        /// where a body's grip is tested and the OUTER edge has no wall. One
        /// object answers both <see cref="FlatSweepAt"/> (the physics) and
        /// <see cref="IsEdgeOpen"/> (the physics AND the decorator's missing
        /// barrier), so what the player sees is what lets a ship slide off.
        /// The generator grows <see cref="End"/> knot by knot while the sweep
        /// is still being laid — always ahead of anything stamped or ridden.
        /// </summary>
        public sealed class FlatSweep
        {
            public float Start { get; }
            public float End { get; set; }
            /// <summary>The outside of the turn: −1 left (a right-hand sweep), +1 right.</summary>
            public int OuterSide { get; }

            public FlatSweep(float start, float end, int outerSide)
            {
                Start = start;
                End = end;
                OuterSide = outerSide < 0 ? -1 : 1;
            }

            public bool Contains(float distance) => distance >= Start && distance < End;
        }

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
            foreach (var s in sections) inserted += s.InsertedLength;
            Length = SplineLength + inserted;
        }

        /// <summary>Drops every knot and every section. Call <see cref="Recalculate"/> after the rebuild.</summary>
        public void ClearKnots()
        {
            if (spline != null) spline.Spline.Clear();
            sections.Clear();
            flatSweeps.Clear();
            openStretches.Clear();
        }

        /// <summary>Appends an auto-smoothed knot at a world position (knots are never removed during a run).</summary>
        public void AppendKnot(float3 position)
        {
            if (spline != null) spline.Spline.Add(new BezierKnot(position), TangentMode.AutoSmooth);
        }

        /// <summary>
        /// Appends an auto-smoothed knot carrying an authored rotation. AutoSmooth
        /// recomputes the tangents from the neighbours but keeps the rotation's
        /// UP (projected onto the tangent plane) — that up is how the road's
        /// grade and bank reach every pose evaluated between the knots.
        /// </summary>
        public void AppendKnot(float3 position, quaternion rotation)
        {
            if (spline != null)
                spline.Spline.Add(new BezierKnot(position, float3.zero, float3.zero, rotation), TangentMode.AutoSmooth);
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
            if (spline == null) return;
            float3 local = math.mul(math.inverse(rotation), tangent);
            spline.Spline.Add(new BezierKnot(position, -local, local, rotation), TangentMode.Continuous);
        }

        /// <summary>
        /// A WORLD pose brought into the space the knots are laid in — the
        /// spline container's own. The builder walks in that space from its
        /// origin, but anything it reads back off a pose query (a loop's exit)
        /// is world: appended as it stands, the rest of the track was shifted
        /// by the container's position at every loop (22 m in the test scene —
        /// a pop nobody saw in track space, a missing road for a physical ship).
        /// </summary>
        public void WorldToKnotSpace(ref Vector3 position, ref Quaternion rotation)
        {
            if (spline == null) return;
            Transform space = spline.transform;
            position = space.InverseTransformPoint(position);
            rotation = Quaternion.Inverse(space.rotation) * rotation;
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

        /// <summary>Spline distance for a track distance (inside an insert: the insert's start; past one: shifted by what it inserted, so a loop's exit maps to its exit knot).</summary>
        public float SplineDistanceOf(float distance)
        {
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
                offset += s.InsertedLength;
            }
            GetPose(DistanceToT(distance - offset), lateral, out position, out rotation);
        }

        // ------------------------------------------------------ body queries
        // What a track-space physics body (TrackBody) asks of the track. All
        // of them go through GetPoseAtDistance, so they read a section's own
        // shape inside one like everything else that thinks in distance.

        /// <summary>The centre-line frame at a track distance: position and the forward / up / right axes.</summary>
        public void GetFrameAtDistance(float distance, out Vector3 position, out Vector3 forward, out Vector3 up, out Vector3 right)
        {
            GetPoseAtDistance(distance, 0f, out position, out Quaternion rotation);
            forward = rotation * Vector3.forward;
            up = rotation * Vector3.up;
            right = rotation * Vector3.right;
        }

        /// <summary>
        /// Signed turn rate of the centre line in 1/m, positive to the RIGHT:
        /// the change of the forward vector over a short step, projected on
        /// the track's right — so a loop's pitch and the road's grade read as
        /// zero and only the turn a ship must corner through counts. The
        /// centripetal demand at speed v is v² × this.
        /// </summary>
        public float GetCurvatureAtDistance(float distance)
        {
            float from = Mathf.Max(0f, distance - CurvatureHalfStep);
            float to = Mathf.Min(Length, distance + CurvatureHalfStep);
            if (to - from < 0.01f) return 0f;

            GetPoseAtDistance(from, 0f, out _, out Quaternion before);
            GetPoseAtDistance(to, 0f, out _, out Quaternion after);
            GetPoseAtDistance(distance, 0f, out _, out Quaternion here);
            Vector3 turn = after * Vector3.forward - before * Vector3.forward;
            return Vector3.Dot(turn, here * Vector3.right) / (to - from);
        }

        /// <summary>
        /// Roll of the road about its forward axis against the level, in
        /// degrees, RIGHT EDGE UP positive — the generator's own bank
        /// convention, so a right-hand sweep banks negative. 0 where the
        /// forward is vertical (inside a loop) and level has no meaning.
        /// </summary>
        public float GetBankAtDistance(float distance)
        {
            GetFrameAtDistance(distance, out _, out Vector3 forward, out _, out Vector3 right);
            Vector3 levelRight = Vector3.Cross(Vector3.up, forward);
            if (levelRight.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(levelRight.normalized, right, forward);
        }

        /// <summary>
        /// Registers a flat sweep (see <see cref="FlatSweep"/>) the moment the
        /// generator starts laying it; the returned object's End is pushed out
        /// as the sweep's knots land.
        /// </summary>
        public FlatSweep AddFlatSweep(float startDistance, float endDistance, int outerSide)
        {
            var sweep = new FlatSweep(startDistance, Mathf.Max(startDistance, endDistance), outerSide);
            flatSweeps.Add(sweep);
            return sweep;
        }

        /// <summary>The flat sweep covering a distance, or null — everywhere else the road holds a body whatever its speed. Never inside a section (a loop, a tube).</summary>
        public FlatSweep FlatSweepAt(float distance)
        {
            if (flatSweeps.Count == 0) return null;
            // Newest first: the ship rides near the end of the list far more often than its start.
            for (int i = flatSweeps.Count - 1; i >= 0; i--)
            {
                var sweep = flatSweeps[i];
                if (distance >= sweep.End) return null; // sorted: every earlier one ends sooner still
                if (sweep.Contains(distance)) return SectionAt(distance) == null ? sweep : null;
            }
            return null;
        }

        /// <summary>The first flat sweep that covers <paramref name="distance"/> or starts within <paramref name="ahead"/> metres of it, or null — what a respawn spot must keep clear of.</summary>
        public FlatSweep FlatSweepWithin(float distance, float ahead)
        {
            foreach (var sweep in flatSweeps)
                if (sweep.End > distance && sweep.Start - distance < ahead) return sweep;
            return null;
        }

        /// <summary>Registers an open straight (see <see cref="OpenStretch"/>); the generator pushes its End out while the run lasts.</summary>
        public OpenStretch AddOpenStretch(float startDistance, float endDistance)
        {
            var stretch = new OpenStretch(startDistance, Mathf.Max(startDistance, endDistance));
            openStretches.Add(stretch);
            return stretch;
        }

        /// <summary>
        /// True where the road has no wall on that side (−1 left, +1 right):
        /// the OUTER side of a flat sweep, and BOTH sides of an open straight.
        /// The inside of a curve, banked sweeps, ramp corridors and landing
        /// zones, tubes and loops are always walled.
        /// </summary>
        public bool IsEdgeOpen(float distance, int side)
        {
            FlatSweep sweep = FlatSweepAt(distance);
            if (sweep != null && sweep.OuterSide == (side < 0 ? -1 : 1)) return true;

            // Newest first, sorted: see FlatSweepAt.
            for (int i = openStretches.Count - 1; i >= 0; i--)
            {
                var stretch = openStretches[i];
                if (distance >= stretch.End) return false;
                if (stretch.Contains(distance)) return SectionAt(distance) == null;
            }
            return false;
        }
    }
}
