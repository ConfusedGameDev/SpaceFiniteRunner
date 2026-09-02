using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// Tunables of a vertical loop. A loop is mandatory — the track IS the
    /// loop, a full circle standing on the road across its whole width — and
    /// it is a <see cref="LoopSection"/> inserted into the track's distance
    /// (the spline stays flat), so the pads, the patrol, the road stamps and
    /// the streamer ride it unchanged. Its entry speed rule lives on
    /// <c>GameSettings</c> (a floor plus a ramp with distance, capped), and is
    /// decided at the gate: enter fast enough and the loop is yours whatever
    /// happens inside; too slow and the ship rides up to the top, drops off
    /// it under <see cref="fallGravity"/> straight down onto the exit, loses
    /// <see cref="fallSpeedLoss"/> of its speed, and the patrol never slows.
    /// </summary>
    [CreateAssetMenu(fileName = "Loop_Definition", menuName = "FiniteRunner/Loop Definition")]
    public class LoopDefinition : TrackFeatureDefinition
    {
        [TitleGroup("Loop")]
        [Tooltip("Radius of the loop in metres. At 100 m the loop is 628 m of track: 2.5 s at launch speed, 0.6 s at 1000 m/s.")]
        [PropertyRange(40f, 250f), SuffixLabel("m", true)]
        public float radius = 100f;

        [TitleGroup("Loop")]
        [Tooltip("Metres of plain track kept clear after the exit before the next feature may start.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float exitClearance = 200f;

        [TitleGroup("Fall")]
        [Tooltip("Fake gravity of the drop from the top of a failed loop, m/s². Higher is a shorter fall (a 100 m loop is a 200 m drop).")]
        [PropertyRange(20f, 400f), SuffixLabel("m/s²", true)]
        public float fallGravity = 120f;

        [TitleGroup("Fall")]
        [Tooltip("Fraction of the current speed lost on dropping off the top.")]
        [PropertyRange(0f, 1f)]
        public float fallSpeedLoss = 0.4f;

        [TitleGroup("Gate")]
        [Tooltip("Gate colour while the ship is fast enough for the loop.")]
        public Color passColor = new(0.2f, 1f, 0.45f);

        [TitleGroup("Gate")]
        [Tooltip("Gate colour while the ship is too slow.")]
        public Color failColor = new(1f, 0.2f, 0.15f);

        public float Circumference => 2f * Mathf.PI * radius;

        public override float FootprintLength => Circumference;
        public override float ExclusionAhead => exitClearance;
        public override float InsertLength => Circumference;

        public override TrackSection CreateSection(TrackManager track, float startDistance)
        {
            track.GetPoseAtDistance(startDistance, 0f, out Vector3 origin, out Quaternion rotation);
            return new LoopSection(startDistance, radius, origin, rotation);
        }
    }
}
