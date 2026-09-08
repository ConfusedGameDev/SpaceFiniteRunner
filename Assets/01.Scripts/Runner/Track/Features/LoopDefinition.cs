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
    /// A loop is only ever PLACED when the ship, as it is right then, would
    /// arrive fast enough (<see cref="gateHeadroom"/> above the demand after
    /// the bleed over the gap) — the generator redraws another feature
    /// otherwise, so a red gate can only come from speed lost after the
    /// loop was decided, never from a demand that was out of reach.
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

        [TitleGroup("Loop"), Title("Variation (rolled per loop)")]
        [Tooltip("How far sideways the exit lands from the entry (min, max), metres — a corkscrew. Side is random. 120 m is one track width.")]
        [MinMaxSlider(0f, 600f, true), SuffixLabel("m", true)]
        public Vector2 lateralDriftRange = new(0f, 240f);

        [TitleGroup("Loop")]
        [Tooltip("How far AHEAD of the entry the exit lands (min, max), metres — an elongated loop that carries the ship forward.")]
        [MinMaxSlider(0f, 1000f, true), SuffixLabel("m", true)]
        public Vector2 forwardCarryRange = new(0f, 300f);

        [TitleGroup("Loop")]
        [Tooltip("Heading change between entry and exit (min, max), degrees, about the entry's up. Side is random.")]
        [MinMaxSlider(0f, 60f, true), SuffixLabel("°", true)]
        public Vector2 exitYawRange = new(0f, 30f);

        [TitleGroup("Loop")]
        [Tooltip("Full turns per loop (min, max). Every turn is another circumference of track; a failed loop still drops from the top of the first.")]
        [MinMaxSlider(1, 3, true)]
        public Vector2Int turnsRange = new(1, 1);

        // Bands unpacked so nothing else reads .x/.y.
        public float DriftMin => Mathf.Max(0f, lateralDriftRange.x);
        public float DriftMax => Mathf.Max(DriftMin, lateralDriftRange.y);
        public float CarryMin => Mathf.Max(0f, forwardCarryRange.x);
        public float CarryMax => Mathf.Max(CarryMin, forwardCarryRange.y);
        public float YawMin => Mathf.Max(0f, exitYawRange.x);
        public float YawMax => Mathf.Max(YawMin, exitYawRange.y);
        public int TurnsMin => Mathf.Max(1, turnsRange.x);
        public int TurnsMax => Mathf.Max(TurnsMin, turnsRange.y);

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

        [TitleGroup("Gate")]
        [Tooltip("Height of the required-speed number above the flight line at the mouth, metres. The code-built gate lifts it above its crossbar when that is taller.")]
        [PropertyRange(5f, 150f), SuffixLabel("m", true)]
        public float labelHeight = 30f;

        [TitleGroup("Gate")]
        [Tooltip("Character size of the required-speed number (a world-space text mesh, so this is roughly metres per character).")]
        [PropertyRange(0.5f, 20f)]
        public float labelSize = 5f;

        [TitleGroup("Gate")]
        [Tooltip("How far ahead of the loop its required-speed number is shown, metres. At 1000+ m/s the ship crosses 400 m in a third of a second, so this sits beyond the fog end (1500 m) — the number is up before the gate itself clears the haze.")]
        [PropertyRange(50f, 4000f), SuffixLabel("m", true)]
        public float labelLeadMeters = 1800f;

        [TitleGroup("Gate")]
        [Tooltip("Placement rule: a loop is only placed when the ship's predicted speed at the gate (current speed minus the passive bleed over the gap) clears the required speed by this fraction. 0.1 = 10% above the demand. A refused spot draws another feature instead.")]
        [PropertyRange(0f, 0.5f)]
        public float gateHeadroom = 0.1f;

        public float Circumference => 2f * Mathf.PI * radius;

        public override float FootprintLength => Circumference;
        public override float ExclusionAhead => exitClearance;

        public override TrackSection CreateSection(TrackManager track, float startDistance, ref Unity.Mathematics.Random rng)
        {
            track.GetPoseAtDistance(startDistance, 0f, out Vector3 origin, out Quaternion rotation);
            // Fixed draw order, so a seed reproduces the loop: drift, its
            // side, carry, yaw, its side, turns.
            float drift = rng.NextFloat(DriftMin, DriftMax);
            if (rng.NextFloat(0f, 1f) < 0.5f) drift = -drift;
            float carry = rng.NextFloat(CarryMin, CarryMax);
            float yaw = rng.NextFloat(YawMin, YawMax);
            if (rng.NextFloat(0f, 1f) < 0.5f) yaw = -yaw;
            int turns = rng.NextInt(TurnsMin, TurnsMax + 1);
            return new LoopSection(startDistance, radius, origin, rotation, drift, carry, yaw, turns);
        }
    }
}
