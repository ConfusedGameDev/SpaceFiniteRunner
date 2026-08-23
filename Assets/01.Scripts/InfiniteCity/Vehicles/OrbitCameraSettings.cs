using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Every knob of the Cinemachine orbit camera in one designer-facing
    /// asset: framing, per-device orbit speeds (mouse, right stick, arrow
    /// keys), auto-recenter behavior and the speed-driven FOV kick. The
    /// OrbitCameraRig draws it inline and re-applies values live, same as
    /// every other settings asset in the project.
    /// </summary>
    [CreateAssetMenu(fileName = "OrbitCameraSettings", menuName = "PoliceEscape/Orbit Camera Settings")]
    public class OrbitCameraSettings : ScriptableObject
    {
        // ------------------------------------------------------------- framing
        [TitleGroup("Framing")]
        [Tooltip("Orbit radius — distance from the car.")]
        [PropertyRange(3f, 25f), SuffixLabel("m", true)]
        public float distance = 8f;

        [TitleGroup("Framing")]
        [Tooltip("Point on the car the camera orbits and aims at, above its pivot.")]
        [PropertyRange(0f, 3f), SuffixLabel("m", true)]
        public float lookHeight = 1.1f;

        [TitleGroup("Framing")]
        [Tooltip("Default pitch of the orbit — where the camera rests and recenters to vertically.")]
        [PropertyRange(0f, 60f), SuffixLabel("°", true)]
        public float defaultPitch = 18f;

        [TitleGroup("Framing")]
        [Tooltip("How far the pitch may be panned: min looks up from near road level, max looks down from above.")]
        [MinMaxSlider(-20f, 80f, true), SuffixLabel("°", true)]
        public Vector2 pitchRange = new(2f, 55f);

        [TitleGroup("Framing")]
        [Tooltip("Position damping of the follow — higher trails the car more loosely (more cinematic, less precise).")]
        [PropertyRange(0f, 3f)]
        public float positionDamping = 0.75f;

        public float PitchMin => pitchRange.x;
        public float PitchMax => pitchRange.y;

        // --------------------------------------------------------------- input
        [TitleGroup("Input")]
        [Tooltip("Degrees of orbit per pixel of mouse movement.")]
        [PropertyRange(0.01f, 1f), SuffixLabel("°/px", true)]
        public float mouseSensitivity = 0.12f;

        [TitleGroup("Input")]
        [Tooltip("Orbit speed at full right-stick deflection.")]
        [PropertyRange(30f, 360f), SuffixLabel("°/s", true)]
        public float stickSpeed = 180f;

        [TitleGroup("Input")]
        [Tooltip("Orbit speed while holding the arrow keys.")]
        [PropertyRange(30f, 360f), SuffixLabel("°/s", true)]
        public float keySpeed = 140f;

        [TitleGroup("Input")]
        [Tooltip("Vertical panning speed as a fraction of the horizontal one.")]
        [PropertyRange(0.1f, 1f)]
        public float verticalScale = 0.6f;

        [TitleGroup("Input")]
        [Tooltip("Invert vertical panning (push up = look down).")]
        public bool invertY;

        // ------------------------------------------------------------ recenter
        [TitleGroup("Recenter")]
        [Tooltip("Swing back behind the car after the input has been idle for the delay below. Off = the orbit stays where you left it.")]
        public bool autoRecenter = true;

        [TitleGroup("Recenter")]
        [Tooltip("Seconds of idle input before recentering starts.")]
        [PropertyRange(0.2f, 10f), SuffixLabel("s", true)]
        public float recenterDelay = 1.5f;

        [TitleGroup("Recenter")]
        [Tooltip("How fast the camera swings back behind the car.")]
        [PropertyRange(30f, 360f), SuffixLabel("°/s", true)]
        public float recenterSpeed = 120f;

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
