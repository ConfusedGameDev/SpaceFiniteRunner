using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Every knob of the chase camera in one designer-facing asset — framing,
    /// smoothing, velocity look-ahead and the speed-driven FOV kick. The
    /// ChaseCamera component draws it inline so camera feel is tuned live in
    /// play mode, same as the car's handling config.
    /// </summary>
    [CreateAssetMenu(fileName = "ChaseCameraSettings", menuName = "PoliceEscape/Chase Camera Settings")]
    public class ChaseCameraSettings : ScriptableObject
    {
        // ------------------------------------------------------------- framing
        [TitleGroup("Framing")]
        [Tooltip("Distance behind the car.")]
        [PropertyRange(3f, 20f), SuffixLabel("m", true)]
        public float followDistance = 7f;

        [TitleGroup("Framing")]
        [Tooltip("Height above the car.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("m", true)]
        public float followHeight = 3f;

        [TitleGroup("Framing")]
        [Tooltip("Point on the car the camera aims at, above its pivot.")]
        [PropertyRange(0f, 3f), SuffixLabel("m", true)]
        public float lookHeight = 1.2f;

        // ----------------------------------------------------------- smoothing
        [TitleGroup("Smoothing")]
        [Tooltip("SmoothDamp time for the camera position. Lower = tighter, higher = floatier.")]
        [PropertyRange(0.02f, 0.6f), SuffixLabel("s", true)]
        public float positionSmoothTime = 0.15f;

        [TitleGroup("Smoothing")]
        [Tooltip("How quickly the aim point catches up to the car. Higher = stiffer aim.")]
        [PropertyRange(1f, 30f)]
        public float lookSharpness = 10f;

        // ---------------------------------------------------------- look-ahead
        [TitleGroup("Look-ahead")]
        [Tooltip("How much the camera hangs toward the travel direction instead of the car's facing — makes drifts read on screen. 0 = always dead behind the nose.")]
        [PropertyRange(0f, 1f)]
        public float velocityAlignment = 0.6f;

        [TitleGroup("Look-ahead")]
        [Tooltip("Seconds of velocity added to the aim point — the camera looks where the car is going, not where it is.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("s", true)]
        public float lookAhead = 0.4f;

        // ----------------------------------------------------------------- fov
        [TitleGroup("Speed FOV")]
        [Tooltip("Field of view at standstill.")]
        [PropertyRange(40f, 90f), SuffixLabel("°", true)]
        public float baseFov = 60f;

        [TitleGroup("Speed FOV")]
        [Tooltip("Extra FOV per km/h — the cheap speed-sensation dial.")]
        [PropertyRange(0f, 0.3f), SuffixLabel("° per km/h", true)]
        public float fovPerKmh = 0.08f;

        [TitleGroup("Speed FOV")]
        [Tooltip("Cap on the speed FOV kick.")]
        [PropertyRange(0f, 40f), SuffixLabel("°", true)]
        public float maxFovBoost = 15f;
    }
}
