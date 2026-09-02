using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// Tunables of a cylinder section — a stretch where the road curls into
    /// a pipe the ship runs round the OUTSIDE of, its top being the flight
    /// line (see <see cref="TubeSection"/>). Steering becomes arc around the
    /// pipe inside ±<see cref="bandDegrees"/> of <see cref="centreDegrees"/>;
    /// one asset per band kind (a ±90° tube, a ±180° full tube) so which one
    /// the player is on is a fact they can read, not a roll. Orbs and brake
    /// pads spawn anywhere in the band at the usual density — a purple orb
    /// hanging under the pipe is the reason to go under — and the road is
    /// the ordinary road prefab stamped in strips round the band.
    /// </summary>
    [CreateAssetMenu(fileName = "Tube_Definition", menuName = "FiniteRunner/Tube Definition")]
    public class TubeDefinition : TrackFeatureDefinition
    {
        [TitleGroup("Tube")]
        [Tooltip("Radius of the pipe, metres. 60 m is a 377 m circumference — three track widths of lateral travel for a full wrap.")]
        [PropertyRange(20f, 150f), SuffixLabel("m", true)]
        public float radius = 60f;

        [TitleGroup("Tube")]
        [Tooltip("Half the steering band, degrees round the pipe. 90 = hang off either side, never invert; 180 = a FULL tube: no clamp once curled, the ship can go round and round, and the return before the exit unwinds it to the nearest top.")]
        [PropertyRange(15f, 180f), SuffixLabel("°", true)]
        public float bandDegrees = 90f;

        [TitleGroup("Tube")]
        [Tooltip("Where the band is centred, degrees from the top. 0 keeps it on top; 180 runs the whole stretch underneath.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float centreDegrees;

        [TitleGroup("Tube")]
        [Tooltip("Steering authority on the pipe as a multiple of the road's — a full wrap is three track widths of arc, so the ship needs to move faster to use it.")]
        [PropertyRange(0.5f, 6f), SuffixLabel("x", true)]
        public float steeringFactor = 3f;

        [TitleGroup("Length")]
        [Tooltip("Section length rolled per instance, metres of track.")]
        [MinMaxSlider(200f, 12000f, true), SuffixLabel("m", true)]
        public Vector2 lengthRange = new(3000f, 7000f);

        [TitleGroup("Length")]
        [Tooltip("Metres at each end over which the flat road curls into the pipe and back out — position, up vector and steering band ease together.")]
        [PropertyRange(20f, 500f), SuffixLabel("m", true)]
        public float curlLength = 150f;

        [TitleGroup("Length")]
        [Tooltip("Metres before the curl-out over which the ship is steered back to the band's centre for the player — steering and dash are locked, the lateral eases home — so the road never unrolls under a ship hanging off its side.")]
        [PropertyRange(100f, 1500f), SuffixLabel("m", true)]
        public float returnLength = 400f;

        [TitleGroup("Length")]
        [Tooltip("Metres of plain track kept clear after the exit before the next feature may start.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float exitClearance = 150f;

        public override float FootprintLength => lengthRange.y;
        public override float ExclusionAhead => exitClearance;
        public override bool ClaimsFootprint => false;

        public override TrackSection CreateSection(TrackManager track, float startDistance, float roll01)
        {
            float length = Mathf.Lerp(lengthRange.x, lengthRange.y, Mathf.Clamp01(roll01));
            return new TubeSection(startDistance, length, radius, bandDegrees, centreDegrees, curlLength, returnLength, steeringFactor);
        }
    }
}
