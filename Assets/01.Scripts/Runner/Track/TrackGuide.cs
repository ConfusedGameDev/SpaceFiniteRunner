using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The runner's track, spoken as a ship guide. The standalone ship lives
    /// in world space and the runner thinks in track coordinates (distance,
    /// lateral, height — the streamer, the loop gates, the patrol's gap, the
    /// pads); this is the one place the two meet: it turns a world position
    /// into track coordinates, and hands the ship the road's frame, curvature,
    /// lane, open edges and grip-tested stretches it needs to be helped along.
    ///
    /// <b>The track had no world→track function, and needs no inverse built
    /// for it.</b> <see cref="TrackManager.GetPoseAtDistance"/> already routes
    /// every distance through whatever section covers it — a loop's helix, a
    /// tube's curl — so the projection is a few Newton steps on that forward
    /// function, in (distance, lateral) at once: look the pose up, measure how
    /// far the point lies along its forward and its right, move by that. It
    /// stays on the right pass of a loop because it starts from the last
    /// answer; with no last answer it scans the live stretch of track first.
    /// Round a curled tube a Newton step from the top of the pipe would stall
    /// on the far side, so there the lateral starts from the angle round the
    /// pipe's axis instead.
    /// </summary>
    public sealed class TrackGuide : MonoBehaviour, IShipGuide
    {
        [SerializeField, Required] TrackManager track;

        [Tooltip("How strongly the runner helps: 1 = the heading is the track's and the stick strafes — the track-space ship's feel.")]
        [PropertyRange(0f, 1f)]
        [SerializeField] float assist = 1f;

        [Tooltip("How far from the flight line a ship is still on this track. Generous: round a tube the ship is a pipe's diameter away from the line it follows.")]
        [PropertyRange(20f, 2000f), SuffixLabel("m", true)]
        [SerializeField] float captureRange = 400f;

        [Tooltip("With no last answer to start from, the projection scans this much of the newest track.")]
        [PropertyRange(200f, 20000f), SuffixLabel("m", true)]
        [SerializeField] float acquireScanMeters = 4000f;

        const int Iterations = 8;
        const float Converged = 0.01f;
        const float MaxStep = 60f;

        public float Length => track != null ? track.Length : 0f;
        public float CaptureRange => captureRange;
        public float Assist => assist;
        public Scene Scene => gameObject.scene;

        /// <summary>For a guide added from code.</summary>
        public void Bind(TrackManager track) => this.track = track;

        void OnEnable() => ShipGuideRegistry.Register(this);
        void OnDisable() => ShipGuideRegistry.Unregister(this);

        public bool TryProject(Vector3 worldPosition, ref float hint, out GuideSample sample)
        {
            sample = default;
            if (track == null || track.Length <= 1f) return false;

            float distance = float.IsNaN(hint) ? Acquire(worldPosition) : hint;
            distance = Mathf.Clamp(distance, 0f, track.Length);
            float lateral = SeedLateral(worldPosition, distance);

            // Every pose lookup is a spline evaluation — the whole cost of a projection — so the
            // loop ends the moment a lookup lands on the point, and that last lookup is reused for
            // everything below. A ship asking every few metres, with last time's answer as the
            // hint, gets out in two.
            Vector3 onRoad = default;
            Quaternion there = Quaternion.identity;
            float along = float.MaxValue;
            for (int i = 0; i < Iterations; i++)
            {
                track.GetPoseAtDistance(distance, lateral, out onRoad, out there);
                Vector3 error = worldPosition - onRoad;
                along = Vector3.Dot(error, there * Vector3.forward);
                float across = Vector3.Dot(error, there * Vector3.right);
                if (Mathf.Abs(along) < Converged && Mathf.Abs(across) < Converged) break;
                distance = Mathf.Clamp(distance + Mathf.Clamp(along, -MaxStep, MaxStep), 0f, track.Length);
                lateral += across;
            }
            // Still metres along the road from the point: it lies past the end of what exists (the newest stretch is still being laid).
            if (Mathf.Abs(along) > 5f) return false;

            Fill(distance, lateral, onRoad, there, out sample);
            sample.lateral = lateral;
            sample.height = Vector3.Dot(worldPosition - onRoad, there * Vector3.up);
            hint = distance;
            return true;
        }

        public void SampleAt(float distance, out GuideSample sample)
        {
            distance = Mathf.Clamp(distance, 0f, Length);
            track.GetPoseAtDistance(distance, 0f, out Vector3 position, out Quaternion rotation);
            Fill(distance, 0f, position, rotation, out sample);
        }

        /// <summary>
        /// The first plain stretch at or PAST a distance — no section, flat
        /// sweep or ramp inside it or starting within the clearance ahead —
        /// so time is lost, never distance, and never back into what threw the
        /// ship. The track-space motor's rule, verbatim.
        /// </summary>
        public float FindRespawn(float from, float clearance)
        {
            if (track == null) return from;
            float d = from;
            for (int guard = 0; guard < 64; guard++)
            {
                float before = d;
                foreach (TrackSection section in track.Sections)
                    if (section.EndDistance > d && section.StartDistance - d < clearance) d = section.EndDistance;

                TrackManager.FlatSweep sweep = track.FlatSweepWithin(d, clearance);
                if (sweep != null) d = sweep.End;

                // The end ramps are not ground to clear: past them is the void.
                foreach (JumpRamp ramp in JumpRamp.Active)
                    if (ramp != null && !ramp.IsEndRamp && ramp.EndDistance > d && ramp.StartDistance - d < clearance) d = ramp.EndDistance;

                if (Mathf.Approximately(d, before)) break;
            }
            d = Mathf.Min(d, Mathf.Max(from, track.Length - 1f));
            // A finite track's final run-up is the last place to come back on: never further down than its start.
            if (track.EndZoneStart >= 0f) d = Mathf.Min(d, Mathf.Max(from, track.EndZoneStart));
            return d;
        }

        /// <summary>
        /// The line's frame and the road's data at a distance, from a pose
        /// already looked up at (distance, lateral). Off a tube the centre
        /// line's frame IS that pose's, moved back across by the lateral — no
        /// second spline evaluation; round a curled tube the frame turns with
        /// the lateral, so the centre is looked up.
        /// </summary>
        void Fill(float distance, float lateral, Vector3 posePosition, Quaternion poseRotation, out GuideSample sample)
        {
            Vector3 position = posePosition;
            Quaternion rotation = poseRotation;
            if (!Mathf.Approximately(lateral, 0f))
            {
                if (track.SectionAt(distance) is TubeSection) track.GetPoseAtDistance(distance, 0f, out position, out rotation);
                else position -= rotation * Vector3.right * lateral;
            }
            Vector3 forward = rotation * Vector3.forward, up = rotation * Vector3.up, right = rotation * Vector3.right;
            // The fence is the whole ROAD, lane plus banked shoulders, not the
            // steering lane: the bank is run-off the ship may ride, and the
            // wall it is turned back by stands on the shoulder's outer lip.
            // Pads, orbs, ramps and the patrol still read the lane.
            track.GetRoadBand(distance, out float min, out float max);
            bool unbounded = track.SectionAt(distance) is TubeSection pipe && pipe.IsUnboundedAt(distance - pipe.StartDistance);
            sample = new GuideSample
            {
                distance = distance,
                position = position,
                forward = forward,
                up = up,
                right = right,
                curvature = CurvatureAt(distance),
                bandMin = min,
                bandMax = max,
                // Round a full tube there is no edge at all: the lane wraps, and nothing may fence the seam under the pipe.
                openLeft = track.IsEdgeOpen(distance, -1) || unbounded,
                openRight = track.IsEdgeOpen(distance, 1) || unbounded,
                gripTested = track.FlatSweepAt(distance) != null,
                fenced = true, // the loops' surface-only ring: the lane is all that holds the ship on it
            };
        }

        // Curvature costs two more spline evaluations and only changes over tens of metres, so it is
        // looked up once per 5 m of track and remembered. Only on settled road: the trailing curves
        // are still being reshaped by AutoSmooth as knots land, and a rebuilt track starts over.
        const float CurvatureCell = 5f;
        const float UnsettledTail = 1000f;
        readonly System.Collections.Generic.Dictionary<int, float> curvatureCache = new();
        float cachedForLength;

        float CurvatureAt(float distance)
        {
            if (track.Length < cachedForLength) curvatureCache.Clear(); // the track was rebuilt
            cachedForLength = track.Length;
            if (distance > track.Length - UnsettledTail) return track.GetCurvatureAtDistance(distance);

            int cell = Mathf.FloorToInt(distance / CurvatureCell);
            if (curvatureCache.TryGetValue(cell, out float cached)) return cached;
            if (curvatureCache.Count > 4096) curvatureCache.Clear(); // an endless run: the cells behind are never asked for again
            float curvature = track.GetCurvatureAtDistance((cell + 0.5f) * CurvatureCell);
            curvatureCache[cell] = curvature;
            return curvature;
        }

        // Round a curled tube the lateral is arc round the pipe: start from the angle about its axis, not from 0.
        float SeedLateral(Vector3 worldPosition, float distance)
        {
            if (track.SectionAt(distance) is not TubeSection tube) return 0f;
            float curl = tube.Curl(distance - tube.StartDistance);
            if (curl < 0.5f) return 0f;
            track.GetFrameAtDistance(distance, out Vector3 top, out _, out Vector3 up, out Vector3 right);
            Vector3 fromAxis = worldPosition - (top - up * tube.Radius);
            float angle = Mathf.Atan2(Vector3.Dot(fromAxis, right), Vector3.Dot(fromAxis, up));
            return angle * tube.Radius / curl;
        }

        // No last answer: the nearest point of the newest stretch of track, coarsely — Newton does the rest.
        float Acquire(Vector3 worldPosition)
        {
            const float Stride = 20f;
            float from = Mathf.Max(0f, track.Length - acquireScanMeters);
            float best = from, bestSqr = float.MaxValue;
            for (float d = from; d <= track.Length; d += Stride)
            {
                track.GetPoseAtDistance(d, 0f, out Vector3 position, out _);
                float sqr = (position - worldPosition).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = d;
            }
            return best;
        }
    }
}
