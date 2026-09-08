using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A stretch of TRACK DISTANCE that owns its own pose function instead of
    /// the spline's. A section covers <see cref="SplineExtent"/> metres of
    /// spline and <see cref="Length"/> metres of track: an <b>overlay</b> (a
    /// tube) covers as much spline as track and only reshapes the pose across
    /// it, an <b>insert</b> (a loop) is longer than the spline under it — the
    /// difference (<see cref="InsertedLength"/>) is track distance the spline
    /// does not have, and every distance past it shifts by that much. A loop's
    /// spline extent is the BRIDGE the generator lays between its entry and
    /// exit knots (zero when the exit is the entry): spline the ship never
    /// rides, because the section routes every distance inside it. The
    /// <see cref="TrackManager"/> registers sections and hands each one its
    /// local distance, and because distance stays the one authoritative
    /// coordinate, the pads, the patrol, the decorator's road stamps and the
    /// streamer all ride a section with no changes of their own: the
    /// decorator even stamps a loop's road, chord by chord, for free. A
    /// section may also reshape the steering lane (<see cref="GetLateralBand"/>
    /// — a tube's band is an arc around the pipe). Sections are registered
    /// the moment the generator DECIDES a feature's spot (before any pad or
    /// road is placed beyond it), never later — an insert under
    /// already-placed objects would shift their distances.
    /// </summary>
    public abstract class TrackSection
    {
        /// <summary>Track distance where the section begins.</summary>
        public float StartDistance { get; }

        /// <summary>Metres of track distance the section covers (inserted, or overlaid on the spline).</summary>
        public abstract float Length { get; }

        /// <summary>Metres of SPLINE under the section: all of it for an overlay (a tube), the bridge between entry and exit knots for a loop.</summary>
        public float SplineExtent { get; protected set; }

        /// <summary>Track distance the section adds that the spline lacks.</summary>
        public float InsertedLength => Mathf.Max(0f, Length - SplineExtent);

        /// <summary>True when the section adds distance the spline lacks (a loop); false for a pure overlay (a tube).</summary>
        public bool InsertsDistance => InsertedLength > 0.01f;

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
    /// A vertical loop standing on the track in the plane of the entry pose:
    /// <see cref="Turns"/> full circles of <see cref="Radius"/> that, over the
    /// ride, may DRIFT sideways along the entry's right (a corkscrew whose
    /// exit is <see cref="LateralDrift"/> metres over), CARRY forward
    /// (<see cref="ForwardCarry"/>, an elongated loop that exits ahead of the
    /// entry) and YAW the exit heading (<see cref="ExitYawDegrees"/> about the
    /// entry's up). All three follow a smoothstep of the ride fraction, so
    /// the tangent at the mouth is exactly the entry forward and at the exit
    /// exactly <see cref="ExitForward"/> — no kink against the road either
    /// side. With none of them the loop is today's circle, entered and left
    /// at the same point. The ride is parameterised by arc length through a
    /// table built once, so the ship's speed along it is its track speed; at
    /// each top the pose is inverted (forward reversed, up pointing down),
    /// which is what the target-up camera binding and the visual's roll
    /// follow. Lateral runs along the (yawing) right, so steering inside a
    /// loop is the same lateral steering as on the road. A failed loop drops
    /// from the top of the FIRST turn (<see cref="FirstTopLocal"/>) onto the
    /// exit (<see cref="GetExitPose"/>).
    /// </summary>
    public class LoopSection : TrackSection
    {
        const int Samples = 256;

        public float Radius { get; }
        public int Turns { get; }
        /// <summary>Sideways displacement of the exit along the entry's right, metres (signed).</summary>
        public float LateralDrift { get; }
        /// <summary>Forward displacement of the exit along the entry's forward, metres.</summary>
        public float ForwardCarry { get; }
        /// <summary>Heading change of the exit about the entry's up, degrees (signed).</summary>
        public float ExitYawDegrees { get; }

        readonly Vector3 origin;   // entry point on the flight line
        readonly Vector3 forward;  // entry heading
        readonly Vector3 up;       // entry up
        readonly Vector3 right;
        readonly float[] arc = new float[Samples + 1]; // cumulative ride length at u = i / Samples
        readonly float length;

        public override float Length => length;

        /// <summary>Highest point of the first turn, on the centre line.</summary>
        public Vector3 Top => PointAt(0.5f / Turns, 0f);

        /// <summary>Local distance of the top of the first turn — where a failed loop lets go.</summary>
        public float FirstTopLocal => length / (2f * Turns);

        /// <summary>Heading the track continues with after the exit.</summary>
        public Vector3 ExitForward => Fwd(1f);

        public LoopSection(float startDistance, float radius, Vector3 origin, Quaternion entryRotation,
                           float lateralDrift = 0f, float forwardCarry = 0f, float exitYawDegrees = 0f, int turns = 1)
            : base(startDistance)
        {
            Radius = Mathf.Max(1f, radius);
            Turns = Mathf.Max(1, turns);
            LateralDrift = lateralDrift;
            ForwardCarry = Mathf.Max(0f, forwardCarry);
            ExitYawDegrees = exitYawDegrees;
            this.origin = origin;
            forward = entryRotation * Vector3.forward;
            up = entryRotation * Vector3.up;
            right = entryRotation * Vector3.right;

            // Arc length by chords: the ride is only ever sampled through
            // this table, so it is as exact as it needs to be (2.5 m steps on
            // a 630 m loop, against a ship covering 36 m per physics step).
            Vector3 previous = PointAt(0f, 0f);
            arc[0] = 0f;
            for (int i = 1; i <= Samples; i++)
            {
                Vector3 point = PointAt(i / (float)Samples, 0f);
                arc[i] = arc[i - 1] + Vector3.Distance(previous, point);
                previous = point;
            }
            length = Mathf.Max(arc[Samples], 1f);
        }

        /// <summary>The generator's measure of the bridge it laid between the entry and exit knots.</summary>
        public void SetSplineExtent(float metres) => SplineExtent = Mathf.Max(0f, metres);

        // Drift, carry and yaw ease in and out with the ride fraction, so the
        // ends are tangent to the road (a linear drift would kink both mouths).
        static float Ease(float u) => u * u * (3f - 2f * u);

        Quaternion Yaw(float u) => Quaternion.AngleAxis(ExitYawDegrees * Ease(u), up);
        Vector3 Fwd(float u) => Yaw(u) * forward;
        Vector3 Right(float u) => Yaw(u) * right;

        Vector3 Centre(float u)
        {
            float e = Ease(u);
            return origin + Fwd(u) * (ForwardCarry * e) + up * Radius + Right(u) * (LateralDrift * e);
        }

        Vector3 PointAt(float u, float lateral)
        {
            float theta = 2f * Mathf.PI * Turns * u;
            return Centre(u) + (Fwd(u) * Mathf.Sin(theta) - up * Mathf.Cos(theta)) * Radius + Right(u) * lateral;
        }

        /// <summary>Ride fraction (0 at the mouth, 1 at the exit) for a local distance, off the arc table.</summary>
        public float UAt(float local)
        {
            float d = Mathf.Clamp(local, 0f, length);
            int i = Mathf.Clamp((int)(d / length * Samples), 0, Samples - 1);
            while (i > 0 && arc[i] > d) i--;
            while (i < Samples - 1 && arc[i + 1] < d) i++;
            float span = arc[i + 1] - arc[i];
            float t = span > 1e-5f ? (d - arc[i]) / span : 0f;
            return (i + t) / Samples;
        }

        public override void GetPose(TrackManager track, float splineStart, float local, float lateral,
                                     out Vector3 position, out Quaternion rotation)
        {
            float u = UAt(local);
            position = PointAt(u, lateral);

            const float du = 1e-3f;
            Vector3 tangent = PointAt(Mathf.Min(u + du, 1f), lateral) - PointAt(Mathf.Max(u - du, 0f), lateral);
            if (tangent.sqrMagnitude < 1e-8f) tangent = Fwd(u);
            // Toward the ring's centre: up at the mouth, down at the top.
            Vector3 normal = Centre(u) - PointAt(u, 0f);
            if (normal.sqrMagnitude < 1e-8f) normal = up;
            rotation = Quaternion.LookRotation(tangent.normalized, normal.normalized);
        }

        /// <summary>Pose on the flight line at the exit — where the track continues and where a failed loop's fall lands.</summary>
        public void GetExitPose(float lateral, out Vector3 position, out Quaternion rotation)
        {
            position = PointAt(1f, lateral);
            rotation = Quaternion.LookRotation(Fwd(1f), up);
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

        public TubeSection(float startDistance, float length, float radius, float bandDegrees, float centreDegrees,
                           float curlLength, float returnLength, float steeringFactor = 1f)
            : base(startDistance)
        {
            this.length = Mathf.Max(1f, length);
            SplineExtent = this.length; // an overlay: as much spline as track
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
