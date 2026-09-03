using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A stretch of TRACK DISTANCE that owns its own pose function instead of
    /// the spline's. Two kinds: an <b>insert</b> (a loop) adds track distance
    /// the spline does not have — every distance past it shifts by
    /// <see cref="Length"/> — and an <b>overlay</b> (a tube) rides a stretch
    /// of spline and only reshapes the pose across it, inserting nothing
    /// (<see cref="InsertsDistance"/> false). The <see cref="TrackManager"/>
    /// registers sections and hands each one its local distance, and because
    /// distance stays the one authoritative coordinate, the pads, the
    /// patrol, the decorator's road stamps and the streamer all ride a
    /// section with no changes of their own: the decorator even stamps a
    /// loop's road, chord by chord, for free. A section may also reshape the
    /// steering lane (<see cref="GetLateralBand"/> — a tube's band is an arc
    /// around the pipe). Sections are registered the moment the generator
    /// DECIDES a feature's spot (before any pad or road is placed beyond it),
    /// never later — an insert under already-placed objects would shift
    /// their distances.
    /// </summary>
    public abstract class TrackSection
    {
        /// <summary>Track distance where the section begins.</summary>
        public float StartDistance { get; }

        /// <summary>Metres of track distance the section covers (inserted, or overlaid on the spline).</summary>
        public abstract float Length { get; }

        /// <summary>True when the section adds distance the spline lacks (a loop); false when it overlays spline (a tube).</summary>
        public virtual bool InsertsDistance => true;

        public float EndDistance => StartDistance + Length;

        protected TrackSection(float startDistance) => StartDistance = startDistance;

        public bool Contains(float distance) => distance >= StartDistance && distance < EndDistance;

        /// <summary>
        /// World pose at <paramref name="local"/> metres into the section,
        /// shifted <paramref name="lateral"/> across. <paramref name="splineStart"/>
        /// is the spline distance under the section's start, for overlays
        /// that shape the spline's own pose.
        /// </summary>
        public abstract void GetPose(TrackManager track, float splineStart, float local, float lateral,
                                     out Vector3 position, out Quaternion rotation);

        /// <summary>The steering lane at a local distance; the plain track's ±half width unless the section reshapes it.</summary>
        public virtual void GetLateralBand(float local, float trackHalfWidth, out float min, out float max)
        {
            min = -trackHalfWidth;
            max = trackHalfWidth;
        }
    }

    /// <summary>
    /// A vertical loop: a full circle of <see cref="Radius"/> standing on the
    /// track in the plane of the entry pose, entered and left at the same
    /// point. The circle is parameterised by arc length, so the ship's speed
    /// along it is its track speed; at the top the pose is inverted (forward
    /// reversed, up pointing down), which is what the target-up camera
    /// binding and the visual's roll follow. Lateral runs along the entry
    /// pose's right, constant round the loop, so steering inside a loop is
    /// the same lateral steering as on the road.
    /// </summary>
    public class LoopSection : TrackSection
    {
        public float Radius { get; }
        readonly Vector3 origin;   // entry point on the flight line
        readonly Vector3 forward;  // entry heading
        readonly Vector3 up;       // entry up
        readonly Vector3 right;

        public override float Length => 2f * Mathf.PI * Radius;

        /// <summary>Highest point of the loop, on the centre line.</summary>
        public Vector3 Top => origin + up * (2f * Radius);

        public LoopSection(float startDistance, float radius, Vector3 origin, Quaternion entryRotation) : base(startDistance)
        {
            Radius = Mathf.Max(1f, radius);
            this.origin = origin;
            forward = entryRotation * Vector3.forward;
            up = entryRotation * Vector3.up;
            right = entryRotation * Vector3.right;
        }

        /// <summary>Angle round the loop (radians) at a local distance: 0 at the entry, π at the top.</summary>
        public float AngleAt(float local) => Mathf.Clamp(local, 0f, Length) / Radius;

        public override void GetPose(TrackManager track, float splineStart, float local, float lateral,
                                     out Vector3 position, out Quaternion rotation)
        {
            float theta = AngleAt(local);
            float sin = Mathf.Sin(theta);
            float cos = Mathf.Cos(theta);
            position = origin + forward * (Radius * sin) + up * (Radius * (1f - cos)) + right * lateral;
            Vector3 tangent = forward * cos + up * sin;
            Vector3 normal = up * cos - forward * sin;
            rotation = Quaternion.LookRotation(tangent, normal);
        }

        /// <summary>Pose on the flight line directly under the top — the exit, where a failed loop's fall lands.</summary>
        public void GetExitPose(float lateral, out Vector3 position, out Quaternion rotation)
        {
            position = origin + right * lateral;
            rotation = Quaternion.LookRotation(forward, up);
        }
    }

    /// <summary>
    /// A cylinder section: over a stretch of spline the road curls into a
    /// pipe of <see cref="Radius"/> whose top is the flight line (the axis
    /// runs one radius below it). Lateral becomes ARC — an angle of
    /// lateral / radius around the pipe — so the ship can run round and
    /// under the track, and the steering lane is the band
    /// ±<see cref="BandRadians"/> around <see cref="CentreRadians"/> (0 = the
    /// top). A full tube (band ≥ 180°) is <see cref="Unbounded"/>: once fully
    /// curled the ship may keep going round — the lateral just grows by a
    /// circumference per turn — and the return before the curl-out unwinds
    /// it to the nearest top. At each end
    /// a <see cref="CurlLength"/> eases the flat pose into the tube pose —
    /// position, up vector and band together — so the road visibly rolls up
    /// into the pipe and unrolls out of it. Before the curl-out a
    /// <see cref="ReturnLength"/> stretch hands the ship back to the top:
    /// <see cref="ReturnProgress"/> is what the motor eases the lateral home
    /// with, steering locked, so the road never unrolls under a ship hanging
    /// off its side. Overlay: it inserts no distance.
    /// </summary>
    public class TubeSection : TrackSection
    {
        public float Radius { get; }
        public float BandRadians { get; }
        public float CentreRadians { get; }
        public float CurlLength { get; }
        public float ReturnLength { get; }

        /// <summary>Steering authority on the pipe as a multiple of the road's (the arc is long, so the ship moves faster across it).</summary>
        public float SteeringFactor { get; }

        /// <summary>True for a full tube: no lateral clamp while fully curled, the ship can go round and round.</summary>
        public bool Unbounded { get; }

        public float Circumference => 2f * Mathf.PI * Radius;
        readonly float length;

        public override float Length => length;
        public override bool InsertsDistance => false;

        public TubeSection(float startDistance, float length, float radius, float bandDegrees, float centreDegrees,
                           float curlLength, float returnLength, float steeringFactor = 1f)
            : base(startDistance)
        {
            this.length = Mathf.Max(1f, length);
            SteeringFactor = Mathf.Max(0.1f, steeringFactor);
            Radius = Mathf.Max(1f, radius);
            BandRadians = Mathf.Clamp(bandDegrees, 1f, 180f) * Mathf.Deg2Rad;
            Unbounded = bandDegrees >= 180f;
            CentreRadians = centreDegrees * Mathf.Deg2Rad;
            CurlLength = Mathf.Clamp(curlLength, 0f, this.length * 0.5f);
            ReturnLength = Mathf.Clamp(returnLength, 0f, Mathf.Max(0f, this.length - 2f * CurlLength));
        }

        /// <summary>True while fully curled on a full tube: the motor applies no lateral clamp there.</summary>
        public bool IsUnboundedAt(float local) => Unbounded && Curl(local) >= 0.999f;

        /// <summary>0 while the player steers freely, easing to 1 at the start of the curl-out: how far home the assisted return has to be.</summary>
        public float ReturnProgress(float local)
        {
            if (ReturnLength <= 0f) return 0f;
            float end = length - CurlLength;
            float start = end - ReturnLength;
            if (local <= start) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((local - start) / ReturnLength));
        }

        /// <summary>0 on flat road, 1 fully curled, easing over the curl at each end.</summary>
        public float Curl(float local)
        {
            if (CurlLength <= 0f) return 1f;
            float fromStart = Mathf.Clamp01(local / CurlLength);
            float fromEnd = Mathf.Clamp01((length - local) / CurlLength);
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(fromStart, fromEnd));
        }

        public override void GetPose(TrackManager track, float splineStart, float local, float lateral,
                                     out Vector3 position, out Quaternion rotation)
        {
            track.GetSplinePoseAtDistance(splineStart + local, 0f, out Vector3 centre, out Quaternion flat);
            Vector3 forward = flat * Vector3.forward;
            Vector3 up = flat * Vector3.up;
            Vector3 right = flat * Vector3.right;

            float e = Curl(local);
            float phi = e * lateral / Radius;
            Vector3 radial = up * Mathf.Cos(phi) + right * Mathf.Sin(phi);
            Vector3 tubePosition = centre - up * Radius + radial * Radius;
            Vector3 flatPosition = centre + right * lateral;

            position = Vector3.Lerp(flatPosition, tubePosition, e);
            Vector3 upDir = Vector3.Slerp(up, radial, e);
            rotation = Quaternion.LookRotation(forward, upDir);
        }

        public override void GetLateralBand(float local, float trackHalfWidth, out float min, out float max)
        {
            float e = Curl(local);
            float half = Mathf.Lerp(trackHalfWidth, BandRadians * Radius, e);
            float centre = Mathf.Lerp(0f, CentreRadians * Radius, e);
            min = centre - half;
            max = centre + half;
        }
    }
}
