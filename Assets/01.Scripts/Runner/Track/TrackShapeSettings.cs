using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Shape of the road itself, as opposed to what sits on it: how the base
    /// spline rises and falls (elevation), and later how it banks. One
    /// settings asset, drawn inline on the <see cref="TrackGenerator"/>, so
    /// the road's feel is tuned in one place; in play the generator runs on a
    /// runtime CLONE (the pause menu's debug sliders edit the clone, never
    /// this asset), and an edit-mode preview reads the asset as is.
    ///
    /// Elevation is a pitch walk: every knot the grade drifts by up to
    /// <see cref="maxGradeStepPerKnot"/>, leans back toward the baseline in
    /// proportion to how far off it the road already is
    /// (<see cref="baselinePull"/>), is forced home outside
    /// <see cref="elevationBand"/>, and never exceeds <see cref="maxGrade"/>.
    /// Grade changes are small per knot and knots are 300–420 m apart, which
    /// is what keeps the swells long and the road never bumpy at Light Speed.
    ///
    /// Turns are SWEEPS, not a per-knot wobble: a sweep holds one direction at
    /// one rate (<see cref="turnRateRange"/>) for as many knots as its arc
    /// (<see cref="turnArcRange"/>) needs, then the road runs straight for
    /// <see cref="minStraightKnots"/> or more. The generator's straightness
    /// knob is the chance a straight knot starts a sweep. Holding the turn
    /// over several knots is what lets the bank build into a wall.
    ///
    /// Banking rolls the road into its sweeps: the target bank at a knot is
    /// the heading change there × <see cref="bankPerDegreeOfTurn"/> (outer
    /// edge up — at the defaults a sweep is a near-vertical wall the ship
    /// rides like an oval's banking), capped at <see cref="maxBankAngle"/>,
    /// eased by at most <see cref="maxBankStepPerKnot"/> per knot — and it is
    /// zero wherever a feature is coming: a loop must stand upright, a tube
    /// curls from a flat pose, a ramp rides its rails. The generator levels
    /// the road <see cref="levelLeadDistance"/> plus however many knots the
    /// current bank needs to unwind before every feature spot, and never
    /// starts a sweep that could not finish and unwind in time.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_TrackShape", menuName = "FiniteRunner/Track Shape Settings")]
    public class TrackShapeSettings : ScriptableObject
    {
        [ToggleGroup("elevationEnabled", "Elevation")]
        [Tooltip("Let the road rise and fall. Off = the flat track (and no random draws, so a seed reproduces the flat layout exactly).")]
        public bool elevationEnabled = true;

        [ToggleGroup("elevationEnabled")]
        [Tooltip("How far above or below the start height the road may wander, metres. Inside it the pull below leans the grade home; outside it the next knot always heads back.")]
        [PropertyRange(0f, 300f), SuffixLabel("m", true)]
        public float elevationBand = 60f;

        [ToggleGroup("elevationEnabled")]
        [Tooltip("Steepest grade the road takes, degrees. Keep it well under vertical — the pose frame needs a tangent that is never straight up.")]
        [PropertyRange(0f, 20f), SuffixLabel("°", true)]
        public float maxGrade = 6f;

        [ToggleGroup("elevationEnabled")]
        [Tooltip("Most the grade may change from one knot to the next, degrees. Knots are 300–420 m apart, so 3° per knot is a swell kilometres long — raise it and the road starts to feel bumpy.")]
        [PropertyRange(0f, 10f), SuffixLabel("°", true)]
        public float maxGradeStepPerKnot = 3f;

        [ToggleGroup("elevationEnabled")]
        [Tooltip("How strongly the grade leans back toward the baseline as the road drifts off it: 0 = a free random walk inside the band, 1 = a full step's worth of pull at the band's edge.")]
        [PropertyRange(0f, 1f)]
        public float baselinePull = 0.5f;

        [TitleGroup("Turns")]
        [Tooltip("Heading change per knot during a sweep (min, max), degrees, rolled per sweep. Knots are 300–420 m apart: 30°/knot is a radius of roughly 700 m. The chance a straight knot STARTS a sweep is the generator's straightness knob (100% = never).")]
        [MinMaxSlider(2f, 60f, true), SuffixLabel("°/knot", true)]
        public Vector2 turnRateRange = new(15f, 35f);

        [TitleGroup("Turns")]
        [Tooltip("Total heading change of a sweep (min, max), degrees, rolled per sweep. A sweep lasts arc / rate knots.")]
        [MinMaxSlider(10f, 180f, true), SuffixLabel("°", true)]
        public Vector2 turnArcRange = new(45f, 120f);

        [TitleGroup("Turns")]
        [Tooltip("Straight knots kept after a sweep before the next may start.")]
        [PropertyRange(0, 6)]
        public int minStraightKnots = 1;

        [TitleGroup("Turns")]
        [Tooltip("Chance the next sweep goes the other way. Keeps the road from spiralling.")]
        [PropertyRange(0f, 1f)]
        public float alternateTurnChance = 0.7f;

        [TitleGroup("Turns")]
        [Tooltip("Once the heading has wandered this far from the start direction, the next sweep always heads back, so the road ahead never doubles over the road behind.")]
        [PropertyRange(0f, 180f), SuffixLabel("°", true)]
        public float maxHeadingDrift = 150f;

        [ToggleGroup("bankEnabled", "Banking")]
        [Tooltip("Roll the road into its sweeps. Off = the road stays level through every turn.")]
        public bool bankEnabled = true;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Steepest bank the road takes, degrees (outer edge up). 80 is a wall the ship rides like an oval's banking; keep it under 90.")]
        [PropertyRange(0f, 89f), SuffixLabel("°", true)]
        public float maxBankAngle = 80f;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Degrees of bank per degree of heading change at a knot. 4 with a 20°/knot sweep is the full 80° wall; a gentler sweep banks less.")]
        [PropertyRange(0f, 10f)]
        public float bankPerDegreeOfTurn = 4f;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Most the bank may change from one knot to the next, degrees. 45 rolls a flat road up to the wall in two knots and back down in two; it also sets how many knots the road needs to unwind before a feature.")]
        [PropertyRange(0f, 90f), SuffixLabel("°", true)]
        public float maxBankStepPerKnot = 45f;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Extra level road kept before every feature spot, metres, on top of the knots the current bank needs to unwind. Features (loops, tubes, ramps) always start on level road.")]
        [PropertyRange(0f, 3000f), SuffixLabel("m", true)]
        public float levelLeadDistance = 200f;

        // Bands unpacked for the generator, so it never reads .x/.y itself.
        public float TurnRateMin => Mathf.Max(0.1f, turnRateRange.x);
        public float TurnRateMax => Mathf.Max(TurnRateMin, turnRateRange.y);
        public float TurnArcMin => Mathf.Max(1f, turnArcRange.x);
        public float TurnArcMax => Mathf.Max(TurnArcMin, turnArcRange.y);
    }
}
