using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Cameras
{
    /// <summary>The three views the chase camera cycles through: Far and Close are the same orbit at two framings, FirstPerson rides the vehicle.</summary>
    public enum CameraMode { Far, Close, FirstPerson }

    /// <summary>
    /// Which "up" the orbit keeps. WorldUp holds the horizon level however
    /// the vehicle leans (a car). TargetUp rolls the orbit with the vehicle
    /// so "behind" stays behind through a loop or under a tube (the ship).
    /// </summary>
    public enum UpBinding { WorldUp, TargetUp }

    /// <summary>
    /// Every knob of the Cinemachine orbit camera in one designer-facing
    /// asset: framing, per-device orbit speeds (mouse, right stick, arrow
    /// keys), auto-recenter behavior, the speed-driven FOV kick and the
    /// three camera modes (far / close orbit framings, first person). One
    /// asset per vehicle — the city car and the runner ship each have their
    /// own. The OrbitCameraRig draws it inline and re-applies values live,
    /// same as every other settings asset in the project.
    /// </summary>
    [CreateAssetMenu(fileName = "OrbitCameraSettings", menuName = "FiniteRunner/Orbit Camera Settings")]
    public class OrbitCameraSettings : ScriptableObject
    {
        // ------------------------------------------------------------- framing
        [TitleGroup("Framing")]
        [Tooltip("Orbit radius — distance from the car.")]
        [PropertyRange(3f, 60f), SuffixLabel("m", true)]
        public float distance = 8f;

        [TitleGroup("Framing")]
        [Tooltip("Point on the car the camera orbits and aims at, above its pivot.")]
        [PropertyRange(0f, 12f), SuffixLabel("m", true)]
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

        [TitleGroup("Framing")]
        [Tooltip("Which up the orbit keeps. World Up holds the horizon level however the vehicle leans (the car). Target Up rolls the orbit with the vehicle, so behind stays behind through a loop or under a tube (the ship).")]
        public UpBinding upBinding = UpBinding.WorldUp;

        [TitleGroup("Framing")]
        [Tooltip("Target Up only: seconds the camera's roll trails the vehicle's. 0 is bolted to the hull; a short lag lets the horizon swing a beat after the ship instead of snapping with it.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        public float rollLagSeconds = 0.15f;

        [TitleGroup("Framing")]
        [Tooltip("Pull the camera forward past anything solid between it and the vehicle (the city's ramps and decks). Off where there is nothing to look through.")]
        public bool deoccluder = true;

        public float PitchMin => pitchRange.x;
        public float PitchMax => pitchRange.y;

        // --------------------------------------------------------------- modes
        [TitleGroup("Camera modes")]
        [Tooltip("The view a fresh car starts in. Tab / the gamepad's Back button cycle Far → Close → First person → Far.")]
        public CameraMode defaultMode = CameraMode.Far;

        [TitleGroup("Camera modes")]
        [Tooltip("Seconds a mode switch takes: the orbit slides between its two framings, and the first-person cut is a Cinemachine blend of the same length.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("s", true)]
        public float modeBlendSeconds = 0.35f;

        [TitleGroup("Camera modes")]
        [Tooltip("Orbit radius of the CLOSE view. The Framing distance above is the FAR view.")]
        [PropertyRange(1.5f, 30f), SuffixLabel("m", true)]
        public float closeDistance = 3.2f;

        [TitleGroup("Camera modes")]
        [Tooltip("Look height of the CLOSE view — lower than the far one keeps the roofline in frame.")]
        [PropertyRange(0f, 12f), SuffixLabel("m", true)]
        public float closeLookHeight = 0.7f;

        [TitleGroup("Camera modes")]
        [Tooltip("Resting pitch of the CLOSE view. Flatter than the far view reads as riding the bumper rather than a drone.")]
        [PropertyRange(0f, 60f), SuffixLabel("°", true)]
        public float closePitch = 12f;

        [TitleGroup("Camera modes")]
        [Tooltip("Seat the first-person eye off the vehicle's chassis box (the two knobs below). Off, the eye sits at the authored offset instead — for a vehicle whose box is a trigger volume rather than a hull.")]
        public bool eyeFromChassis = true;

        [TitleGroup("Camera modes")]
        [Tooltip("First-person eye point in the vehicle's local space, used when the eye is not seated off the chassis box.")]
        [HideIf("eyeFromChassis")]
        public Vector3 firstPersonEyeOffset = new(0f, 1.2f, 0.5f);

        [TitleGroup("Camera modes")]
        [Tooltip("First-person eye point, forward of the chassis centre. Negative sits the eye back over the cabin, positive pushes it toward the bonnet.")]
        [ShowIf("eyeFromChassis")]
        [PropertyRange(-2.5f, 2.5f), SuffixLabel("m", true)]
        public float firstPersonForward = 0.2f;

        [TitleGroup("Camera modes")]
        [Tooltip("First-person eye height above the top of the chassis box. Keep it above zero or the roof clips the view.")]
        [ShowIf("eyeFromChassis")]
        [PropertyRange(-0.5f, 1.5f), SuffixLabel("m", true)]
        public float firstPersonHeight = 0.12f;

        [TitleGroup("Camera modes")]
        [Tooltip("Rotation damping of the first-person view: 0 is bolted to the car (every bump is in the picture), higher soaks up the jitter but lags the steering.")]
        [PropertyRange(0f, 0.5f), SuffixLabel("s", true)]
        public float firstPersonDamping = 0.06f;

        [TitleGroup("Camera modes")]
        [Tooltip("Near clip plane of the first-person lens. The eye sits inside the vehicle's silhouette, so it has to be small enough not to cut the dashboard — but too small and the depth buffer loses precision on the far city.")]
        [PropertyRange(0.01f, 1f), SuffixLabel("m", true)]
        public float firstPersonNearClip = 0.05f;

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
        [Tooltip("Pan with the keyboard's camera keys (the arrows by default — rebindable on the CONTROLS screen). Off for a vehicle that wants those keys for itself (the ship).")]
        public bool arrowKeysPan = true;

        [TitleGroup("Input")]
        [Tooltip("Orbit speed while holding the camera keys.")]
        [PropertyRange(30f, 360f), SuffixLabel("°/s", true), ShowIf("arrowKeysPan")]
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

        // ----------------------------------------------------------- look back
        [ToggleGroup("lookBack", "Look back")]
        [Tooltip("Hold the right stick button (R3) or Right Shift to whip the camera round and watch the road behind; releasing swings it back to where it was.")]
        public bool lookBack = true;

        [ToggleGroup("lookBack")]
        [Tooltip("Orbit yaw of the rear view, measured from straight behind the car — an absolute pose, not an offset from the current pan. 180 aims the camera dead along the car's axis at the road behind; less keeps the car's flank in frame.")]
        [PropertyRange(90f, 180f), SuffixLabel("°", true)]
        public float lookBackAngle = 180f;

        [ToggleGroup("lookBack")]
        [Tooltip("Seconds to swing round. Short is a snap over the shoulder, long is cinematic — and too slow to be useful at speed.")]
        [PropertyRange(0.02f, 1.5f), SuffixLabel("s", true)]
        public float lookBackInSeconds = 0.18f;

        [ToggleGroup("lookBack")]
        [Tooltip("Seconds to swing back on release. Usually slower than the way in — the return should not yank the view off the road.")]
        [PropertyRange(0.02f, 2f), SuffixLabel("s", true)]
        public float lookBackOutSeconds = 0.32f;

        [ToggleGroup("lookBack")]
        [Tooltip("Pitch held while looking back, clamped to the range above. Flatter than the default reads as a glance over the shoulder rather than a drone shot.")]
        [PropertyRange(-20f, 80f), SuffixLabel("°", true)]
        public float lookBackPitch = 12f;

        [ToggleGroup("lookBack")]
        [Tooltip("Orbit radius of the rear view, whatever view the player is in — the third fixed component of the pose beside its yaw and pitch.")]
        [PropertyRange(3f, 60f), SuffixLabel("m", true)]
        public float lookBackDistance = 8f;

        [ToggleGroup("lookBack")]
        [Tooltip("Position damping while looking back. The follow's lag trails the car, and with the camera IN FRONT of it that trail pulls the camera into the bonnet by more the faster you go — 0 bolts the rear view to its authored distance at any speed.")]
        [PropertyRange(0f, 3f)]
        public float lookBackDamping = 0f;

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
