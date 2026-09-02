using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.UI;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.Cameras
{
    /// <summary>
    /// Cinemachine chase rig shared by both games: a CinemachineCamera with
    /// OrbitalFollow (sphere orbit, locked to the target's heading so axis 0
    /// = behind it) and a RotationComposer aim. The player pans around the
    /// vehicle with the mouse, the gamepad right stick or the arrow keys —
    /// polled straight off the Input System like the rest of the project's
    /// input — and after a moment of idle input the orbit swings back behind
    /// it. Holding the right stick button (R3) or Right Shift whips the orbit
    /// round to look back down the road; releasing eases it home.
    ///
    /// <b>It follows an anchor, not the vehicle.</b> The rig seats a
    /// <c>CameraAnchor</c> sibling on the target every LateUpdate (before the
    /// brain — see the execution order) and Cinemachine follows THAT. With
    /// <see cref="UpBinding.WorldUp"/> the anchor is the target's pose and
    /// the orbit keeps the world's horizon (the car). With
    /// <see cref="UpBinding.TargetUp"/> the orbit rolls with the target — a
    /// ship on a loop or hanging under a tube keeps "behind" behind — and the
    /// anchor's up vector trails the target's by
    /// <see cref="OrbitCameraSettings.rollLagSeconds"/>, so the horizon
    /// swings a beat after the ship instead of snapping 180° with it.
    ///
    /// <b>Three views, one button</b>: Tab / the gamepad's Back button
    /// (<see cref="MenuNavigator.CameraCyclePressed"/>) cycle Far → Close →
    /// First person. Far and Close are the SAME orbit camera at two framings
    /// (distance, look height, resting pitch), slid between over
    /// <see cref="OrbitCameraSettings.modeBlendSeconds"/> so the pan the
    /// player holds survives the switch; First person is a second vcam (a
    /// sibling object — see Build for why it cannot be a child) bolted
    /// to an eye point on the vehicle (hard lock + rotate-with-target, no
    /// deoccluder — it is inside the silhouette), and the brain blends the cut
    /// over the same time. Looking back in first person hands the picture
    /// to the orbit for the duration of the hold — a rear-view glance, the
    /// eye itself never turns. Built entirely from code by
    /// <see cref="CameraRigInstaller"/> (which also puts a CinemachineBrain on
    /// the main camera); retargeted on every spawn. All feel knobs live on the
    /// OrbitCameraSettings asset and are re-applied live every frame. The
    /// vehicle's say over the shared controls comes through
    /// <see cref="ICameraTarget"/> — the rig never references a vehicle type.
    /// </summary>
    [DefaultExecutionOrder(-100)] // LateUpdate before the CinemachineBrain's, so the anchor is seated this frame
    public class OrbitCameraRig : MonoBehaviour
    {
        [Required, InlineEditor]
        [Tooltip("All camera-feel tunables live on this asset — add new knobs there, not here.")]
        public OrbitCameraSettings settings;

        CinemachineCamera cinemachineCamera;
        CinemachineOrbitalFollow orbital;
        CinemachineRotationComposer composer;
        CinemachineDeoccluder deoccluder;
        CinemachineCamera firstPersonCamera;
        CinemachineHardLockToTarget firstPersonLock;
        CinemachineRotateWithFollowTarget firstPersonRotate;
        Transform anchor;
        Transform eye;
        ICameraTarget target;
        float idleTimer;
        bool built;
        float defaultFarClip;
        Vector3 laggedUp = Vector3.up;

        // Mode state. framingBlend is 0 at the far framing and 1 at the close
        // one; it slides rather than snaps so a switch reads as a dolly, and
        // the resting pitch it produces is what recentering aims at.
        CameraMode mode;
        float framingBlend;
        float appliedPitch;

        // Look-back state. The swing is driven by a 0..1 blend off a REMEMBERED
        // orbit rather than by nudging the live axis: the axis wraps at +/-180,
        // so accumulating towards "behind" would be ambiguous exactly where the
        // player is heading, and the release has to land back on the pan they
        // had before the glance, not on wherever the wrap left it.
        float lookBackBlend;
        float lookBackFromYaw;
        float lookBackFromPitch;

        /// <summary>Built-in tag the rig stamps on its target's colliders so the deoccluder ignores them.</summary>
        const string PlayerTag = "Player";
        const string EyeName = "CameraEye";
        const string AnchorName = "CameraAnchor";
        const int OrbitPriority = 10;

        /// <summary>Name of the first-person vcam object; SceneSystemsPlacer pre-places one under this name for Build to adopt.</summary>
        public const string FirstPersonName = "FirstPersonCamera";

        bool ownsFirstPersonObject; // created here (destroy with the rig) vs adopted from the scene (leave it)
        const int FirstPersonPriority = 20;

        public ICameraTarget Target => target;

        /// <summary>The view currently selected — what the next cycle press advances from.</summary>
        public CameraMode Mode => mode;

        public void SetTarget(ICameraTarget newTarget)
        {
            target = newTarget;
            if (!built) Build();
            Transform vehicle = target != null ? target.Transform : null;
            if (vehicle != null)
            {
                TagRecursively(vehicle, PlayerTag);
                laggedUp = vehicle.up;
                SeatAnchor(0f);
            }
            cinemachineCamera.Follow = vehicle != null ? anchor : null;
            cinemachineCamera.LookAt = vehicle != null ? anchor : null;
            eye = vehicle != null ? EnsureEye(vehicle) : null;
            firstPersonCamera.Follow = eye;
            firstPersonCamera.LookAt = eye;
            // Fresh vehicle: start resting behind it, in the default view, and
            // never mid-glance.
            lookBackBlend = 0f;
            SetMode(settings != null ? settings.defaultMode : CameraMode.Far, instant: true);
            orbital.HorizontalAxis.Value = 0f;
        }

        /// <summary>Advance to the next view: Far → Close → First person → Far.</summary>
        public void CycleMode()
        {
            SetMode((CameraMode)(((int)mode + 1) % 3), instant: false);
        }

        /// <summary>
        /// Select a view. Instant lands the framing at once (a fresh vehicle);
        /// otherwise the orbit slides and the first-person cut blends over
        /// <see cref="OrbitCameraSettings.modeBlendSeconds"/>.
        /// </summary>
        public void SetMode(CameraMode newMode, bool instant)
        {
            mode = newMode;
            if (!built || settings == null) return;
            if (instant)
            {
                framingBlend = mode == CameraMode.Close ? 1f : 0f;
                ApplyFraming(0f);
                orbital.VerticalAxis.Value = appliedPitch;
            }
            ApplyPriorities();
        }

        /// <summary>
        /// The target was teleported by <paramref name="delta"/>: move the
        /// anchor and the eye with it and tell Cinemachine, so the camera cuts
        /// along instead of damping across the gap.
        /// </summary>
        public void NotifyWarp(Vector3 delta)
        {
            if (!built) return;
            if (anchor != null)
            {
                anchor.position += delta;
                CinemachineCore.OnTargetObjectWarped(anchor, delta);
            }
            if (eye != null) CinemachineCore.OnTargetObjectWarped(eye, delta);
        }

        void Build()
        {
            built = true;
            cinemachineCamera = gameObject.AddComponent<CinemachineCamera>();
            cinemachineCamera.Priority = OrbitPriority;
            orbital = gameObject.AddComponent<CinemachineOrbitalFollow>();
            composer = gameObject.AddComponent<CinemachineRotationComposer>();
            Ensure<CinemachineCameraShake>(gameObject);

            // Ramps and overpass decks would otherwise put the orbit camera
            // under the road surface while the car climbs (8 m back on a
            // slope is several metres lower); the deoccluder pulls it forward
            // past anything solid between vehicle and camera. The vehicle
            // itself is excluded by layer (PlayerCar, when the glitch feature
            // created it) and by tag, so it can never push the camera off its
            // target. Switched off per settings asset (the runner has nothing
            // to look through).
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
            // Locked to the anchor's heading: horizontal axis 0 is always
            // directly behind the vehicle, which makes recentering trivial.
            // The binding (world up vs the anchor's own up) is a settings knob.
            orbital.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            orbital.HorizontalAxis = new InputAxis { Value = 0f, Range = new Vector2(-180f, 180f), Wrap = true, Center = 0f };
            orbital.VerticalAxis = new InputAxis { Value = 18f, Range = new Vector2(2f, 55f), Wrap = false, Center = 18f };

            // The follow anchor: a sibling, not a child — this object is moved
            // by its own vcam every frame, and a child would ride along.
            anchor = new GameObject(AnchorName).transform;
            anchor.SetParent(transform.parent, false);

            // First person: its own vcam so the two can be blended by
            // priority. It MUST be a sibling, never a child of this object:
            // Cinemachine treats any vcam whose parent transform carries a
            // vcam as a "private army" member and never enters it in the
            // priority queue, so a child first-person camera could hold
            // priority 20 forever and still never go live. Hard-locked to the
            // eye point and rotating with it, so the view is the vehicle's own
            // heading; the small damping is what keeps road bumps from being
            // the whole picture.
            // A scene-placed sibling of that name is adopted (the placer
            // leaves it empty: the components are added here either way).
            GameObject fp = FindPrePlacedFirstPerson();
            ownsFirstPersonObject = fp == null;
            if (fp == null)
            {
                fp = new GameObject(FirstPersonName);
                fp.transform.SetParent(transform.parent, false);
            }
            firstPersonCamera = Ensure<CinemachineCamera>(fp);
            firstPersonCamera.Priority = OrbitPriority - 1;
            firstPersonLock = Ensure<CinemachineHardLockToTarget>(fp);
            firstPersonRotate = Ensure<CinemachineRotateWithFollowTarget>(fp);
            Ensure<CinemachineCameraShake>(fp);
            firstPersonCamera.Lens.NearClipPlane = 0.05f;

            // The scene camera's far clip is the authored default; the lens
            // (which the brain pushes onto the camera every frame) starts there.
            defaultFarClip = Camera.main != null ? Camera.main.farClipPlane : cinemachineCamera.Lens.FarClipPlane;
            cinemachineCamera.Lens.FarClipPlane = defaultFarClip;
            firstPersonCamera.Lens.FarClipPlane = defaultFarClip;
            appliedPitch = settings != null ? settings.defaultPitch : 18f;
            ApplySettings();
        }

        void OnDestroy()
        {
            // The first-person vcam is a sibling, not a child, so it does not
            // die with the rig on its own. Unless it was the scene's to begin
            // with: that one stays, and a later rig adopts it again. The
            // anchor is always ours.
            if (ownsFirstPersonObject && firstPersonCamera != null) Destroy(firstPersonCamera.gameObject);
            if (anchor != null) Destroy(anchor.gameObject);
        }

        /// <summary>The scene-placed first-person object next to this rig (same parent, or a scene root when the rig is one), or null.</summary>
        public GameObject FindPrePlacedFirstPerson()
        {
            if (transform.parent != null)
            {
                Transform sibling = transform.parent.Find(FirstPersonName);
                return sibling != null ? sibling.gameObject : null;
            }
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                if (root != gameObject && root.name == FirstPersonName)
                    return root;
            return null;
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        void ApplySettings()
        {
            if (settings == null) return;
            orbital.TrackerSettings.PositionDamping = Vector3.one * settings.positionDamping;
            orbital.TrackerSettings.BindingMode = settings.upBinding == UpBinding.TargetUp
                ? BindingMode.LockToTarget
                : BindingMode.LockToTargetWithWorldUp;
            orbital.VerticalAxis.Range = new Vector2(settings.PitchMin, settings.PitchMax);
            deoccluder.enabled = settings.deoccluder;
            firstPersonLock.Damping = 0f;
            firstPersonRotate.Damping = settings.firstPersonDamping;
            if (eye != null) PlaceEye(eye, target);

            // Mode switches share one blend length: the brain's default blend
            // (the first-person cut) and the framing slide in ApplyFraming.
            var brain = CinemachineCore.FindPotentialTargetBrain(cinemachineCamera);
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, settings.modeBlendSeconds);
        }

        /// <summary>
        /// Slide the orbit between its far and close framings and push the
        /// result onto the orbital body. The pitch change is applied to the
        /// live axis as a delta, so the player sees the framing tilt with the
        /// dolly instead of waiting for auto-recenter to find the new rest.
        /// </summary>
        void ApplyFraming(float dt)
        {
            float goal = mode == CameraMode.Close ? 1f : 0f;
            float seconds = Mathf.Max(0.001f, settings.modeBlendSeconds);
            framingBlend = dt > 0f ? Mathf.MoveTowards(framingBlend, goal, dt / seconds) : goal;
            float eased = Mathf.SmoothStep(0f, 1f, framingBlend);

            float pitch = Mathf.Lerp(settings.defaultPitch, settings.closePitch, eased);
            float pitchDelta = pitch - appliedPitch;
            appliedPitch = pitch;
            if (dt > 0f && Mathf.Abs(pitchDelta) > 0.0001f)
                orbital.VerticalAxis.Value = Mathf.Clamp(orbital.VerticalAxis.Value + pitchDelta, settings.PitchMin, settings.PitchMax);

            float lookHeight = Mathf.Lerp(settings.lookHeight, settings.closeLookHeight, eased);
            orbital.Radius = Mathf.Lerp(settings.distance, settings.closeDistance, eased);
            orbital.TargetOffset = new Vector3(0f, lookHeight, 0f);
            orbital.VerticalAxis.Center = appliedPitch;
            composer.TargetOffset = new Vector3(0f, lookHeight, 0f);
        }

        /// <summary>
        /// First person wins by priority whenever it is the selected view —
        /// except while the look-back owns the orbit, which is how a glance
        /// over the shoulder works from inside the vehicle: the brain cuts to
        /// the orbit for the hold and back to the eye on release.
        /// </summary>
        void ApplyPriorities()
        {
            bool firstPerson = mode == CameraMode.FirstPerson && lookBackBlend <= 0f && eye != null;
            firstPersonCamera.Priority = firstPerson ? FirstPersonPriority : OrbitPriority - 1;
        }

        /// <summary>
        /// The first-person eye point: a child of the vehicle, so it rides
        /// every bump and lean the chassis takes. Found again on a retarget
        /// rather than duplicated (the same vehicle can be handed to the rig
        /// twice).
        /// </summary>
        static Transform EnsureEye(Transform vehicle)
        {
            Transform eye = vehicle.Find(EyeName);
            if (eye == null)
            {
                eye = new GameObject(EyeName).transform;
                eye.SetParent(vehicle, false);
            }
            return eye;
        }

        /// <summary>
        /// Seat the eye. Off the chassis box when the target has one and the
        /// settings ask for it — its top is the roofline the camera has to
        /// clear, its centre the axle-to-axle middle the forward knob is
        /// measured from — otherwise at the settings' authored offset (the
        /// ship, whose trigger box is a volume, not a hull). Re-run every
        /// frame so the sliders move it live.
        /// </summary>
        void PlaceEye(Transform eye, ICameraTarget vehicle)
        {
            if (settings.eyeFromChassis && vehicle != null && vehicle.TryGetChassisBox(out Vector3 centre, out float top))
                eye.localPosition = new Vector3(centre.x, top + settings.firstPersonHeight, centre.z + settings.firstPersonForward);
            else
                eye.localPosition = settings.firstPersonEyeOffset;
            eye.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Put the anchor on the target. Position and heading are the
        /// target's own; the up vector is the target's under WorldUp (unused
        /// by that binding anyway) and the lagged one under TargetUp, so a
        /// roll reaches the camera a beat after the vehicle.
        /// </summary>
        void SeatAnchor(float dt)
        {
            if (anchor == null || target == null) return;
            Transform vehicle = target.Transform;
            if (vehicle == null) return;

            Vector3 up = vehicle.up;
            bool lag = settings != null && settings.upBinding == UpBinding.TargetUp && settings.rollLagSeconds > 0f && dt > 0f;
            laggedUp = lag ? Vector3.Slerp(laggedUp, up, 1f - Mathf.Exp(-dt / settings.rollLagSeconds)) : up;

            Vector3 forward = vehicle.forward;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            anchor.SetPositionAndRotation(vehicle.position, Quaternion.LookRotation(forward, laggedUp));
        }

        void Update()
        {
            if (!built || settings == null) return;
            ApplySettings(); // live tuning off the inline settings asset

            if (target == null) return;
            float dt = Time.deltaTime;

            // Tab / Back cycle the view — but only over live gameplay: every
            // menu in the project reads the same chord for its own tabs, and
            // the vehicle may be holding the view (a menu open, a jump).
            if (MenuNavigator.CameraCyclePressed() && Time.timeScale > 0f && !target.BlockModeCycle)
                CycleMode();

            ApplyFraming(dt);

            if (!UpdateLookBack(dt))
            {
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
                        orbital.VerticalAxis.Value = Mathf.MoveTowards(orbital.VerticalAxis.Value, appliedPitch, step * settings.verticalScale);
                    }
                }
            }
            ApplyPriorities();

            // Speed FOV kick — on both views, so the cut never changes the lens.
            float fov = settings.baseFov + Mathf.Min(settings.maxFovBoost, target.SpeedKmh * settings.fovPerKmh);
            cinemachineCamera.Lens.FieldOfView = fov;
            firstPersonCamera.Lens.FieldOfView = fov;

            // Far clip follows the distance fog: past the solid fog there is
            // nothing to draw. Cinemachine pushes the lens clip planes onto the
            // camera every frame, so the clamp has to live on the lens, not on
            // Camera.main. Back to the authored default when the fog is off.
            float? fogFar = DistanceFog.Instance != null ? DistanceFog.Instance.FarClipPlane : null;
            cinemachineCamera.Lens.FarClipPlane = fogFar ?? defaultFarClip;
            firstPersonCamera.Lens.FarClipPlane = fogFar ?? defaultFarClip;
        }

        // After every vehicle's Update (the ship moves in Update, the car's
        // rigidbody interpolates) and before the brain's LateUpdate, by the
        // execution order on the class.
        void LateUpdate()
        {
            if (!built || target == null) return;
            SeatAnchor(Time.deltaTime);
        }

        /// <summary>
        /// Drives the over-the-shoulder glance and returns true while it owns
        /// the orbit — held, or still swinging back. Both ends of the swing are
        /// eased, and the idle timer is pinned at 0 throughout so auto-recenter
        /// cannot start fighting the return halfway home; it only begins
        /// counting once the camera is back where the player left it.
        /// </summary>
        bool UpdateLookBack(float dt)
        {
            bool held = settings.lookBack && LookBackHeld();
            if (held && lookBackBlend <= 0f)
            {
                // Remember the pan we are glancing away from, so the release
                // returns to the player's framing rather than to dead centre.
                lookBackFromYaw = orbital.HorizontalAxis.Value;
                lookBackFromPitch = orbital.VerticalAxis.Value;
            }

            float seconds = Mathf.Max(0.01f, held ? settings.lookBackInSeconds : settings.lookBackOutSeconds);
            lookBackBlend = Mathf.MoveTowards(lookBackBlend, held ? 1f : 0f, dt / seconds);
            if (lookBackBlend <= 0f && !held) return false;

            float eased = Mathf.SmoothStep(0f, 1f, lookBackBlend);
            orbital.HorizontalAxis.Value = Mathf.LerpAngle(lookBackFromYaw, lookBackFromYaw + settings.lookBackAngle, eased);
            orbital.VerticalAxis.Value = Mathf.Lerp(lookBackFromPitch,
                Mathf.Clamp(settings.lookBackPitch, settings.PitchMin, settings.PitchMax), eased);
            idleTimer = 0f;
            return true;
        }

        /// <summary>Look-back is a HOLD, on the same devices the pan is read from: right stick click (R3), or Right Shift.</summary>
        static bool LookBackHeld()
        {
            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.rightStickButton.isPressed) return true;

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.rightShiftKey.isPressed;
        }

        static void TagRecursively(Transform root, string tag)
        {
            root.gameObject.tag = tag;
            for (int i = 0; i < root.childCount; i++)
                TagRecursively(root.GetChild(i), tag);
        }

        /// <summary>
        /// Pan input in degrees this frame: mouse delta, right stick, arrow
        /// keys — first non-zero source wins. While the vehicle claims the
        /// stick and the arrows (<see cref="ICameraTarget.BlockPanInput"/> —
        /// a car's air control), only the mouse keeps panning; auto-recenter
        /// runs as usual. The arrows are a settings knob because the runner
        /// steers with them.
        /// </summary>
        Vector2 ReadPan(float dt)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0.01f)
                    return delta * settings.mouseSensitivity; // delta is already per-frame — no dt
            }

            if (target.BlockPanInput) return Vector2.zero;

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.01f)
                    return stick * (settings.stickSpeed * dt);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && settings.arrowKeysPan)
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
