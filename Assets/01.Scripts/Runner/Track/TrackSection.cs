using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A stretch of TRACK DISTANCE that owns its own pose function instead of
    /// the spline's — a loop today, a tube section later. The spline stays
    /// flat and untouched; the <see cref="TrackManager"/> inserts the section
    /// at <see cref="StartDistance"/>, every distance past it shifts by
    /// <see cref="Length"/>, and <c>GetPoseAtDistance</c> hands the section
    /// its local distance. Because distance stays the one authoritative
    /// coordinate, the pads, the patrol, the decorator's road stamps and the
    /// streamer all ride a section with no changes of their own: the
    /// decorator even stamps the loop's road, chord by chord, for free.
    /// Sections are registered the moment the generator DECIDES a feature's
    /// spot (before any pad or road is placed beyond it), never later — an
    /// insert under already-placed objects would shift their distances.
    /// </summary>
    public abstract class TrackSection
    {
        /// <summary>Track distance where the section begins (the spline distance is the same number here).</summary>
        public float StartDistance { get; }

        /// <summary>Metres of track distance the section inserts.</summary>
        public abstract float Length { get; }

        public float EndDistance => StartDistance + Length;

        protected TrackSection(float startDistance) => StartDistance = startDistance;

        public bool Contains(float distance) => distance >= StartDistance && distance < EndDistance;

        /// <summary>World pose at <paramref name="local"/> metres into the section, shifted <paramref name="lateral"/> across.</summary>
        public abstract void GetPose(float local, float lateral, out Vector3 position, out Quaternion rotation);
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

        public override void GetPose(float local, float lateral, out Vector3 position, out Quaternion rotation)
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
}
