using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Livery;
using ConfusedGameDev.FiniteRunner.Store;
namespace ConfusedGameDev.FiniteRunner.Customize
{
    /// <summary>
    /// The customize scene's turntable: a hand-placed object right of the
    /// screen centre that spawns one vehicle at a time from the
    /// <see cref="CustomizeSettings"/> list, seated by its Store model entry
    /// (<see cref="StoreStage.PrepareInstance"/>) and made paintable
    /// (<see cref="VehiclePaintTarget"/>). It yaws under <see cref="Nudge"/>
    /// and idles back into a slow spin; <see cref="Zoom"/> dollies the camera
    /// along the line from its authored pose to the vehicle, so the vehicle
    /// stays on the same spot of the screen at every distance.
    /// </summary>
    public class CustomizeStage : MonoBehaviour
    {
        const float PopSeconds = 0.25f;
        const float PopFrom = 0.85f;

        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)] CustomizeSettings settings;
        [Tooltip("The camera the zoom dollies. Empty = Camera.main.")]
        [SerializeField] Camera viewCamera;
        [Tooltip("Height above the stage the camera dollies toward (the model's seat height).")]
        [SerializeField, PropertyRange(0f, 3f)] float focusHeight = 0.9f;

        Transform slot;
        GameObject instance;
        float yaw;
        float idleTimer;
        float distance, targetDistance, defaultDistance, zoomVelocity;
        Vector3 dollyDirection;
        bool cameraReady;
        float pop = 1f;

        /// <summary>The spawned vehicle's paint slots, or null before the first <see cref="Show"/>.</summary>
        public VehiclePaintTarget Current { get; private set; }

        /// <summary>The index of the vehicle showing.</summary>
        public int Index { get; private set; } = -1;

        public CustomizeSettings Settings => settings;
        public int Count => settings != null ? settings.vehicles.Count : 0;

        /// <summary>The entry of the vehicle showing, or null.</summary>
        public CustomizeVehicleEntry Entry => settings != null && Index >= 0 && Index < settings.vehicles.Count ? settings.vehicles[Index] : null;

        Vector3 Focus => transform.position + Vector3.up * focusHeight;

        /// <summary>Wires the settings and camera (the scene builder's entry).</summary>
        public void Configure(CustomizeSettings customizeSettings, Camera cam)
        {
            settings = customizeSettings;
            viewCamera = cam;
        }

        void Awake()
        {
            slot = new GameObject("VehicleSlot").transform;
            slot.SetParent(transform, false);
            if (settings != null) yaw = settings.defaultYaw;
        }

        void EnsureCamera()
        {
            if (cameraReady) return;
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;
            Vector3 toCamera = viewCamera.transform.position - Focus;
            defaultDistance = Mathf.Max(0.5f, toCamera.magnitude);
            dollyDirection = toCamera / defaultDistance;
            distance = targetDistance = defaultDistance;
            cameraReady = true;
        }

        /// <summary>
        /// Spawns vehicle <paramref name="index"/> (wrapped) in place of the
        /// current one and returns its paint slots, painted by
        /// <paramref name="rules"/>' hue-to-colour floors (the ones every
        /// <see cref="VehicleLivery"/> uses, so the preview matches play).
        /// </summary>
        public VehiclePaintTarget Show(int index, VehicleColorProfile rules)
        {
            if (Count == 0) return null;
            Index = (index % Count + Count) % Count;
            if (instance != null) Destroy(instance);
            Current = null;

            CustomizeVehicleEntry entry = settings.vehicles[Index];
            if (entry == null || entry.model == null || entry.model.prefab == null)
            {
                Debug.LogWarning($"{nameof(CustomizeStage)}: vehicle {Index} has no prefab.", this);
                return null;
            }

            instance = Instantiate(entry.model.prefab, slot);
            StoreStage.PrepareInstance(instance, entry.model);
            Current = instance.AddComponent<VehiclePaintTarget>();
            Current.Initialize(entry.slotOverrides, rules);
            pop = 0f;
            ApplyPose();
            return Current;
        }

        /// <summary>Turns the vehicle and holds the idle spin off for a while.</summary>
        public void Nudge(float yawDegrees)
        {
            if (Mathf.Approximately(yawDegrees, 0f)) return;
            yaw += yawDegrees;
            idleTimer = 0f;
        }

        /// <summary>Moves the camera closer (negative) or farther (positive), in metres, within the zoom band.</summary>
        public void Zoom(float metres)
        {
            if (settings == null || Mathf.Approximately(metres, 0f)) return;
            EnsureCamera();
            targetDistance = Mathf.Clamp(targetDistance + metres, settings.ZoomMin, settings.ZoomMax);
            idleTimer = 0f;
        }

        /// <summary>Back to the default yaw and the authored camera distance.</summary>
        public void ResetView()
        {
            EnsureCamera();
            if (settings != null) yaw = settings.defaultYaw;
            targetDistance = defaultDistance;
            idleTimer = 0f;
        }

        void Update()
        {
            if (settings == null) return;
            EnsureCamera();
            float dt = Time.unscaledDeltaTime;

            idleTimer += dt;
            if (idleTimer >= settings.idleResumeSeconds) yaw += settings.autoSpinDegreesPerSecond * dt;
            pop = Mathf.Min(1f, pop + dt / PopSeconds);
            ApplyPose();

            if (cameraReady)
            {
                distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, settings.zoomSmoothing,
                                            Mathf.Infinity, dt);
                viewCamera.transform.position = Focus + dollyDirection * distance;
            }
        }

        void ApplyPose()
        {
            yaw = Mathf.Repeat(yaw, 360f);
            slot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float eased = 1f - (1f - pop) * (1f - pop);
            slot.localScale = Vector3.one * Mathf.Lerp(PopFrom, 1f, eased);
        }
    }
}
