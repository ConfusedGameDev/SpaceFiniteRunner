using Sirenix.OdinInspector;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Cinemachine chase rig: a CinemachineCamera with OrbitalFollow (sphere
    /// orbit, locked to the car's heading so axis 0 = behind the car) and a
    /// RotationComposer aim. The player pans around the car with the mouse,
    /// the gamepad right stick or the arrow keys — polled straight off the
    /// Input System like the rest of the project's input (WASD stays on
    /// driving) — and after a moment of idle input the orbit swings back
    /// behind the car. Built entirely from code by CarFactory (which also
    /// puts a CinemachineBrain on the main camera); retargeted on every car
    /// spawn. All feel knobs live on the OrbitCameraSettings asset and are
    /// re-applied live every frame.
    /// </summary>
    public class OrbitCameraRig : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("All camera-feel tunables live on this asset — add new knobs there, not here.")]
        public OrbitCameraSettings settings;

        CinemachineCamera cinemachineCamera;
        CinemachineOrbitalFollow orbital;
        CinemachineRotationComposer composer;
        CinemachineDeoccluder deoccluder;
        CarController target;
        float idleTimer;
        bool built;

        /// <summary>Built-in tag the rig stamps on its target's colliders so the deoccluder ignores them.</summary>
        const string PlayerTag = "Player";

        public CarController Target => target;

        public void SetTarget(CarController car)
        {
            target = car;
            if (!built) Build();
            Transform follow = car != null ? car.transform : null;
            if (car != null) TagRecursively(car.transform, PlayerTag);
            cinemachineCamera.Follow = follow;
            cinemachineCamera.LookAt = follow;
            // Fresh car: start resting behind it.
            orbital.HorizontalAxis.Value = 0f;
            orbital.VerticalAxis.Value = settings != null ? settings.defaultPitch : 18f;
        }

        void Build()
        {
            built = true;
            cinemachineCamera = gameObject.AddComponent<CinemachineCamera>();
            orbital = gameObject.AddComponent<CinemachineOrbitalFollow>();
            composer = gameObject.AddComponent<CinemachineRotationComposer>();

            // Ramps and overpass decks would otherwise put the orbit camera
            // under the road surface while the car climbs (8 m back on a
            // slope is several metres lower); the deoccluder pulls it forward
            // past anything solid between car and camera. The car itself is
            // excluded by layer (PlayerCar, when the glitch feature created
            // it) and by tag, so it can never push the camera off its target.
            deoccluder = gameObject.AddComponent<CinemachineDeoccluder>();
            int playerLayer = LayerMask.NameToLayer("PlayerCar");
            deoccluder.CollideAgainst = playerLayer >= 0 ? ~(1 << playerLayer) : ~0;
            deoccluder.IgnoreTag = PlayerTag;
            deoccluder.MinimumDistanceFromTarget = 1.5f;
            deoccluder.AvoidObstacles.Enabled = true;
            deoccluder.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            deoccluder.AvoidObstacles.DistanceLimit = 0f;
            deoccluder.AvoidObstacles.MinimumOcclusionTime = 0f;
            deoccluder.AvoidObstacles.CameraRadius = 0.4f;

            orbital.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            // Locked to the target's heading: horizontal axis 0 is always
            // directly behind the car, which makes recentering trivial.
            orbital.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            orbital.HorizontalAxis = new InputAxis { Value = 0f, Range = new Vector2(-180f, 180f), Wrap = true, Center = 0f };
            orbital.VerticalAxis = new InputAxis { Value = 18f, Range = new Vector2(2f, 55f), Wrap = false, Center = 18f };
            ApplySettings();
        }

        void ApplySettings()
        {
            if (settings == null) return;
            orbital.Radius = settings.distance;
            orbital.TargetOffset = new Vector3(0f, settings.lookHeight, 0f);
            orbital.TrackerSettings.PositionDamping = Vector3.one * settings.positionDamping;
            orbital.VerticalAxis.Range = new Vector2(settings.PitchMin, settings.PitchMax);
            orbital.VerticalAxis.Center = settings.defaultPitch;
            composer.TargetOffset = new Vector3(0f, settings.lookHeight, 0f);
        }

        void Update()
        {
            if (!built || settings == null) return;
            ApplySettings(); // live tuning off the inline settings asset

            if (target == null) return;
            float dt = Time.deltaTime;

            Vector2 pan = ReadPan(dt);
            if (pan.sqrMagnitude > 0.0001f)
            {
                idleTimer = 0f;
                orbital.HorizontalAxis.Value += pan.x;
                float sign = settings.invertY ? -1f : 1f;
                orbital.VerticalAxis.Value = Mathf.Clamp(
                    orbital.VerticalAxis.Value + pan.y * sign * settings.verticalScale,
                    settings.PitchMin, settings.PitchMax);
            }
            else if (settings.autoRecenter)
            {
                idleTimer += dt;
                if (idleTimer >= settings.recenterDelay)
                {
                    float step = settings.recenterSpeed * dt;
                    orbital.HorizontalAxis.Value = Mathf.MoveTowardsAngle(orbital.HorizontalAxis.Value, 0f, step);
                    orbital.VerticalAxis.Value = Mathf.MoveTowards(orbital.VerticalAxis.Value, settings.defaultPitch, step * settings.verticalScale);
                }
            }

            // Speed FOV kick.
            cinemachineCamera.Lens.FieldOfView = settings.baseFov
                + Mathf.Min(settings.maxFovBoost, target.SpeedKmh * settings.fovPerKmh);
        }

        static void TagRecursively(Transform root, string tag)
        {
            root.gameObject.tag = tag;
            for (int i = 0; i < root.childCount; i++)
                TagRecursively(root.GetChild(i), tag);
        }

        /// <summary>Pan input in degrees this frame: mouse delta, right stick, arrow keys — first non-zero source wins.</summary>
        Vector2 ReadPan(float dt)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0.01f)
                    return delta * settings.mouseSensitivity; // delta is already per-frame — no dt
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.01f)
                    return stick * (settings.stickSpeed * dt);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Vector2 keys = Vector2.zero;
                if (keyboard.leftArrowKey.isPressed) keys.x -= 1f;
                if (keyboard.rightArrowKey.isPressed) keys.x += 1f;
                if (keyboard.upArrowKey.isPressed) keys.y += 1f;
                if (keyboard.downArrowKey.isPressed) keys.y -= 1f;
                if (keys.sqrMagnitude > 0f)
                    return keys * (settings.keySpeed * dt);
            }
            return Vector2.zero;
        }
    }
}
