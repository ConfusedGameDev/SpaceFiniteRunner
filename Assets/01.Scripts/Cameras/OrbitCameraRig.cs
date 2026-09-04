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
    ///
    /// <b>Editor Setup</b>: the same components can be pre-built into the
    /// scene with the inspector's Setup button (edit mode only), so a
    /// hand-placed rig shows its Cinemachine components and the
    /// first-person sibling before play instead of growing them on the first
    /// SetTarget. Build finds-or-adds, so a set-up rig is configured in place
    /// at play, never duplicated. The button is live only while it has
    /// something to do: never pressed, the settings asset swapped since, or a
    /// set-up component gone missing (<see cref="SetupPending"/>).
    /// </summary>
    [DefaultExecutionOrder(-100)] // LateUpdate before the CinemachineBrain's, so the anchor is seated this frame
    public class OrbitCameraRig : MonoBehaviour
    {
        [Required, InlineEditor]
        [InfoBox("$SetupStatus", InfoMessageType.None)]
        [Tooltip("All camera-feel tunables live on this asset — add new knobs there, not here.")]
        public OrbitCameraSettings settings;

        // Editor Setup bookkeeping: whether the button has run on this rig and
        // which settings asset it was last configured against. Serialized so
        // the button stays disabled across editor sessions until either changes.
        [SerializeField, HideInInspector] OrbitCameraSettings setupSettings;
        [SerializeField, HideInInspector] bool setupDone;

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

        // The camera this rig drives (set by the installer, which found it in
        // the target's own scene) — never Camera.main, which answers the OTHER
        // scene's camera during the city → runner additive handoff.
        Camera outputCamera;

        /// <summary>The camera whose brain renders this rig; read for the authored far clip.</summary>
        public void SetOutputCamera(Camera camera) => outputCamera = camera;

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
            EnsureComponents();
            ConfigureComponents();

            // The follow anchor: a sibling, not a child — this object is moved
            // by its own vcam every frame, and a child would ride along. A
            // per-run object: never pre-built by Setup.
            anchor = new GameObject(AnchorName).transform;
            anchor.SetParent(transform.parent, false);

            // The scene camera's far clip is the authored default; the lens
            // (which the brain pushes onto the camera every frame) starts there.
            Camera authored = outputCamera != null ? outputCamera : Camera.main;
            defaultFarClip = authored != null ? authored.farClipPlane : cinemachineCamera.Lens.FarClipPlane;
            cinemachineCamera.Lens.FarClipPlane = defaultFarClip;
            firstPersonCamera.Lens.FarClipPlane = defaultFarClip;
            appliedPitch = settings != null ? settings.defaultPitch : 18f;
            ApplySettings();
        }

        /// <summary>
        /// Find-or-add every Cinemachine component the rig runs on: the orbit
        /// vcam's pipeline on this object and the first-person vcam on its
        /// sibling (created here when the scene has none). Shared by the play
        /// mode Build and the editor Setup, so a pre-built rig is adopted, not
        /// doubled — in edit mode the additions go through Undo.
        /// </summary>
        void EnsureComponents()
        {
            cinemachineCamera = Ensure<CinemachineCamera>(gameObject);
            orbital = Ensure<CinemachineOrbitalFollow>(gameObject);
            composer = Ensure<CinemachineRotationComposer>(gameObject);
            deoccluder = Ensure<CinemachineDeoccluder>(gameObject);
            Ensure<CinemachineCameraShake>(gameObject);

            // First person: its own vcam so the two can be blended by
            // priority. It MUST be a sibling, never a child of this object:
            // Cinemachine treats any vcam whose parent transform carries a
            // vcam as a "private army" member and never enters it in the
            // priority queue, so a child first-person camera could hold
            // priority 20 forever and still never go live. Hard-locked to the
            // eye point and rotating with it, so the view is the vehicle's own
            // heading; the small damping is what keeps road bumps from being
            // the whole picture.
            // A scene-placed sibling of that name is adopted (the placer and
            // Setup leave it there; the components are ensured either way).
            GameObject fp = FindPrePlacedFirstPerson();
            ownsFirstPersonObject = fp == null;
            if (fp == null)
            {
                fp = new GameObject(FirstPersonName);
                fp.transform.SetParent(transform.parent, false);
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(fp, SetupUndoName);
#endif
            }
            firstPersonCamera = Ensure<CinemachineCamera>(fp);
            firstPersonLock = Ensure<CinemachineHardLockToTarget>(fp);
            firstPersonRotate = Ensure<CinemachineRotateWithFollowTarget>(fp);
            Ensure<CinemachineCameraShake>(fp);
        }

        /// <summary>
        /// The fixed part of the rig's configuration — everything that does
        /// not come off the settings asset (priorities, orbit style and axes,
        /// the deoccluder's collision rules). Re-run on
        /// every Build, so the serialized values a Setup left behind are
        /// always brought back in line.
        /// </summary>
        void ConfigureComponents()
        {
            cinemachineCamera.Priority = OrbitPriority;

            // Ramps and overpass decks would otherwise put the orbit camera
            // under the road surface while the car climbs (8 m back on a
            // slope is several metres lower); the deoccluder pulls it forward
            // past anything solid between vehicle and camera. The vehicle
            // itself is excluded by layer (PlayerCar, when the glitch feature
            // created it) and by tag, so it can never push the camera off its
            // target. Switched off per settings asset (the runner has nothing
            // to look through).
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

            firstPersonCamera.Priority = OrbitPriority - 1;
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
            if (!gameObject.scene.IsValid()) return null; // a prefab asset has no scene roots to search
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

        /// <summary>Get-or-add; in edit mode the add is an Undo step, so a Setup can be undone in one go.</summary>
        static T Ensure<T>(GameObject go) where T : Component
        {
            if (go.TryGetComponent(out T component)) return component;
#if UNITY_EDITOR
            if (!Application.isPlaying) return UnityEditor.Undo.AddComponent<T>(go);
#endif
            return go.AddComponent<T>();
        }

        /// <summary>The set-up components are all present — on this object and on the first-person sibling.</summary>
        bool HasSetupComponents()
        {
            if (!TryGetComponent<CinemachineCamera>(out _) || !TryGetComponent<CinemachineOrbitalFollow>(out _)
                || !TryGetComponent<CinemachineRotationComposer>(out _) || !TryGetComponent<CinemachineDeoccluder>(out _)
                || !TryGetComponent<CinemachineCameraShake>(out _))
                return false;
            GameObject fp = FindPrePlacedFirstPerson();
            return fp != null
                && fp.TryGetComponent<CinemachineCamera>(out _)
                && fp.TryGetComponent<CinemachineHardLockToTarget>(out _)
                && fp.TryGetComponent<CinemachineRotateWithFollowTarget>(out _)
                && fp.TryGetComponent<CinemachineCameraShake>(out _);
        }

#if UNITY_EDITOR
        const string SetupUndoName = "Setup Orbit Camera Rig";

        /// <summary>
        /// Editor only: the Setup button has something to do — never run on
        /// this rig, the settings asset swapped since it ran, or one of the
        /// components it added removed by hand. Otherwise it stays greyed out,
        /// so a set-up rig is never rebuilt by accident.
        /// </summary>
        bool SetupPending => !Application.isPlaying && (!setupDone || setupSettings != settings || !HasSetupComponents());

        /// <summary>The line above the settings asset saying why the Setup button is live or not.</summary>
        string SetupStatus
        {
            get
            {
                if (Application.isPlaying) return "Setup runs in edit mode only — the components are built at play.";
                if (!setupDone) return "Not set up: Setup pre-builds the Cinemachine components on this object and the first-person camera beside it.";
                if (setupSettings != settings) return "The settings asset changed since Setup ran — press Setup to reconfigure against it.";
                if (!HasSetupComponents()) return "A set-up component is missing — press Setup to restore it.";
                return "Set up against " + (settings != null ? settings.name : "no settings") + ".";
            }
        }

        /// <summary>
        /// Pre-build the rig in the scene: find-or-add its Cinemachine
        /// components, create the first-person sibling if the scene has
        /// none, push the fixed configuration and the settings asset's
        /// values (default framing, pitch, FOV — what Build will apply again
        /// at play), and remember the asset so the button goes quiet until
        /// it changes. One Undo step. The follow anchor and the eye are
        /// per-run objects and are left to play mode.
        /// </summary>
        [Button("Setup", ButtonSizes.Medium), PropertyOrder(-1)]
        [DisableInPlayMode, EnableIf(nameof(SetupPending))]
        [PropertyTooltip("Edit mode only. Adds the Cinemachine components this rig builds at play (orbit vcam, orbital follow, composer, deoccluder, shake) and creates the FirstPersonCamera sibling with its own, then configures them from the settings asset. Enabled until it has run, and again whenever the settings asset changes.")]
        void Setup()
        {
            if (Application.isPlaying) return;
            EnsureComponents();
            UnityEditor.Undo.RecordObjects(new Object[]
            {
                this, cinemachineCamera, orbital, composer, deoccluder,
                firstPersonCamera, firstPersonLock, firstPersonRotate,
            }, SetupUndoName);
            ConfigureComponents();
            if (settings != null)
            {
                mode = settings.defaultMode;
                framingBlend = mode == CameraMode.Close ? 1f : 0f;
                appliedPitch = settings.defaultPitch;
                ApplySettings();
                ApplyFraming(0f);
                orbital.VerticalAxis.Value = appliedPitch;
                cinemachineCamera.Lens.FieldOfView = settings.baseFov;
                firstPersonCamera.Lens.FieldOfView = settings.baseFov;
            }
            setupSettings = settings;
            setupDone = true;
            UnityEditor.EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

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
            firstPersonCamera.Lens.NearClipPlane = settings.firstPersonNearClip;
            if (eye != null) PlaceEye(eye, target);

            // Mode switches share one blend length: the brain's default blend
            // (the first-person cut) and the framing slide in ApplyFraming.
            // Play only: the editor Setup must not dirty a scene brain
            // outside its Undo step, and the value is pushed every frame anyway.
            CinemachineBrain brain = null;
            if (Application.isPlaying)
                brain = outputCamera != null ? outputCamera.GetComponent<CinemachineBrain>()
                                             : CinemachineCore.FindPotentialTargetBrain(cinemachineCamera);
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
        /// the orbit — held, or still swinging back. The glance swings to a
        /// fixed pose behind the vehicle's axis and back to where the player
        /// left the orbit. Both ends of the swing are
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

            // The rear view is an ABSOLUTE pose in the vehicle's frame (yaw,
            // pitch and radius all authored), never an offset from the pan the
            // player happened to hold: the glance must show the same road
            // behind whether the camera was centred or swung out to the side.
            // Only the way back is relative — it lands on the remembered pan.
            float eased = Mathf.SmoothStep(0f, 1f, lookBackBlend);
            orbital.HorizontalAxis.Value = Mathf.LerpAngle(lookBackFromYaw, settings.lookBackAngle, eased);
            orbital.VerticalAxis.Value = Mathf.Lerp(lookBackFromPitch,
                Mathf.Clamp(settings.lookBackPitch, settings.PitchMin, settings.PitchMax), eased);
            // The radius rides the same blend: ApplyFraming has already written
            // this frame's Far/Close radius, so the return lands on the live
            // framing even if the view was cycled during the glance.
            orbital.Radius = Mathf.Lerp(orbital.Radius, settings.lookBackDistance, eased);
            // Damping too — ApplySettings wrote the normal value at the top of
            // the frame, and the follow's lag in front of a fast car is the
            // rear view creeping into the bonnet. Blended, so the press never
            // snaps the lagged camera onto the rigid pose.
            orbital.TrackerSettings.PositionDamping =
                Vector3.one * Mathf.Lerp(settings.positionDamping, settings.lookBackDamping, eased);
            idleTimer = 0f;
            return true;
        }

        /// <summary>Look-back is a HOLD of the bound <see cref="GameAction.CameraLookBack"/> control — right stick click (R3) or Right Shift by default.</summary>
        static bool LookBackHeld() => ControlBindings.IsPressed(GameAction.CameraLookBack);

        static void TagRecursively(Transform root, string tag)
        {
            root.gameObject.tag = tag;
            for (int i = 0; i < root.childCount; i++)
                TagRecursively(root.GetChild(i), tag);
        }

        /// <summary>
        /// Pan input in degrees this frame: mouse delta, then the bound pad
        /// pan controls (right stick by default), then the bound pan keys
        /// (arrows by default) — first non-zero source wins; the mouse is
        /// never bound. While the vehicle claims the stick and the keys
        /// (<see cref="ICameraTarget.BlockPanInput"/> — a car's air control),
        /// only the mouse keeps panning; auto-recenter runs as usual. The
        /// keys are a settings knob (<c>arrowKeysPan</c>) because a vehicle
        /// may want them for itself.
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

            var stick = new Vector2(
                ControlBindings.PadAxis(GameAction.CameraPanLeft, GameAction.CameraPanRight, 0.1f),
                ControlBindings.PadAxis(GameAction.CameraPanDown, GameAction.CameraPanUp, 0.1f));
            if (stick.sqrMagnitude > 0.01f)
                return stick * (settings.stickSpeed * dt);

            if (settings.arrowKeysPan)
            {
                var keys = new Vector2(
                    ControlBindings.KeyboardAxis(GameAction.CameraPanLeft, GameAction.CameraPanRight),
                    ControlBindings.KeyboardAxis(GameAction.CameraPanDown, GameAction.CameraPanUp));
                if (keys.sqrMagnitude > 0f)
                    return keys * (settings.keySpeed * dt);
            }
            return Vector2.zero;
        }
    }
}
