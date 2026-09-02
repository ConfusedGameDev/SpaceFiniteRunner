using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// Tunables of a jump ramp. A ramp is a slope narrower than the track at
    /// a random lateral: steer onto it and the ship rides up and launches by
    /// itself (no button), steer past it and nothing happens. The arc is
    /// authored in <b>track distance, not time</b>: the ship leaves the lip
    /// tangent to the ramp and follows a parabola that lands exactly
    /// <c>clamp(takeoffSpeed × airDistancePerSpeed, airDistanceRange)</c>
    /// metres further down the track, higher and longer the faster it was
    /// going. That is what lets the generator space features safely — the
    /// longest possible arc is a knob, not a function of an unbounded speed —
    /// and it keeps a 1000 m/s ship's hop short and readable instead of a
    /// ten-second flight. The ship's own trigger box is a volume 5 m wide, so
    /// <see cref="entryMargin"/> is how far inside the ramp's edge the ship's
    /// centre must be to count as on it; anything closer to the edge hits the
    /// side rail instead.
    /// </summary>
    [CreateAssetMenu(fileName = "Jump_Definition", menuName = "FiniteRunner/Jump Definition")]
    public class JumpDefinition : TrackFeatureDefinition
    {
        [TitleGroup("Ramp")]
        [Tooltip("Ramp width as a fraction of the track width, so it keeps reading the same when the width knob moves.")]
        [PropertyRange(0.05f, 1f)]
        public float widthFraction = 0.25f;

        [TitleGroup("Ramp")]
        [Tooltip("Run-up length in metres — the slope the ship rides before the lip.")]
        [PropertyRange(10f, 200f), SuffixLabel("m", true)]
        public float length = 60f;

        [TitleGroup("Ramp")]
        [Tooltip("Slope of the ramp. The lip height is length × tan(angle), and the ship leaves the lip at this angle.")]
        [PropertyRange(5f, 45f), SuffixLabel("°", true)]
        public float rampAngle = 20f;

        [TitleGroup("Ramp")]
        [Tooltip("How far inside the ramp's edge the ship's centre must be to enter it (half the ship's width). Closer to the edge is a side hit.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        public float entryMargin = 2.5f;

        [TitleGroup("Arc")]
        [Tooltip("Metres of flight per m/s of takeoff speed. 0.6 throws a 250 m/s launch 150 m and a 1000 m/s ship 600 m (before the cap).")]
        [PropertyRange(0.05f, 3f), SuffixLabel("m per m/s", true)]
        public float airDistancePerSpeed = 0.6f;

        [TitleGroup("Arc")]
        [Tooltip("Shortest and longest arc, in metres of track. The upper end is the exclusion the generator keeps clear ahead of every ramp.")]
        [MinMaxSlider(20f, 2000f, true), SuffixLabel("m", true)]
        public Vector2 airDistanceRange = new(80f, 600f);

        [TitleGroup("Air")]
        [Tooltip("Steering and dash authority while airborne, as a fraction of grounded.")]
        [PropertyRange(0f, 1f)]
        public float airControlFactor = 0.5f;

        [TitleGroup("Side hit")]
        [Tooltip("Fraction of the current speed lost when the ship hits the ramp from the side (a fraction, because speed grows all run).")]
        [PropertyRange(0f, 1f)]
        public float sideHitSpeedLoss = 0.15f;

        /// <summary>Height of the lip above the flight line.</summary>
        public float LipHeight => length * Mathf.Tan(rampAngle * Mathf.Deg2Rad);

        /// <summary>Slope of the ramp (rise per metre of track).</summary>
        public float Slope => Mathf.Tan(rampAngle * Mathf.Deg2Rad);

        /// <summary>Arc length in metres of track for a takeoff at <paramref name="speed"/> m/s.</summary>
        public float AirDistanceFor(float speed) =>
            Mathf.Clamp(speed * airDistancePerSpeed, airDistanceRange.x, airDistanceRange.y);

        public override float FootprintLength => length;
        public override float ExclusionAhead => airDistanceRange.y;
    }
}
