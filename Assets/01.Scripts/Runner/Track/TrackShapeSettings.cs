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
    /// Banking leans the road into its turns like a velodrome: the target
    /// bank at a knot is the heading change there × <see cref="bankPerDegreeOfTurn"/>
    /// (outer edge up), capped at <see cref="maxBankAngle"/>, eased by at
    /// most <see cref="maxBankStepPerKnot"/> per knot — and it is zero
    /// wherever a feature is coming: a loop must stand upright, a tube curls
    /// from a flat pose, a ramp rides its rails. The generator levels the
    /// road <see cref="levelLeadDistance"/> plus however many knots the
    /// current bank needs to unwind before every feature spot.
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

        [ToggleGroup("bankEnabled", "Banking")]
        [Tooltip("Lean the road into its turns. Off = the road stays level through every turn. Turns themselves come from the generator's straightness knob.")]
        public bool bankEnabled = true;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Steepest bank the road takes, degrees (outer edge up).")]
        [PropertyRange(0f, 60f), SuffixLabel("°", true)]
        public float maxBankAngle = 25f;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Degrees of bank per degree of heading change at a knot. 1.5 with a 24° turn is the full 25° cap (well, 36° before the cap).")]
        [PropertyRange(0f, 5f)]
        public float bankPerDegreeOfTurn = 1.5f;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Most the bank may change from one knot to the next, degrees. Also sets how many knots the road needs to unwind before a feature.")]
        [PropertyRange(0f, 30f), SuffixLabel("°", true)]
        public float maxBankStepPerKnot = 8f;

        [ToggleGroup("bankEnabled")]
        [Tooltip("Extra level road kept before every feature spot, metres, on top of the knots the current bank needs to unwind. Features (loops, tubes, ramps) always start on level road.")]
        [PropertyRange(0f, 3000f), SuffixLabel("m", true)]
        public float levelLeadDistance = 300f;
    }
}
