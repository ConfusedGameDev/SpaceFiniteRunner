using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// The Store's turntable: a hand-placed scene object with one slot per
    /// section (car / ship / character), each holding its model instanced
    /// in edit mode by the scene builder — the main camera looks straight at
    /// it and the UI is drawn over it with the centre left open. Only the
    /// active section's slot is visible. The active slot yaws under the
    /// player's right stick or mouse drag (<see cref="Nudge"/>) and idles
    /// back into a slow spin once the input stops. Knobs come off the
    /// <see cref="StoreSettings"/> asset so the viewer's feel is tuned in
    /// one place; the models' scale and seat come off their section's
    /// <see cref="StoreModel"/> (the Reframe button re-applies them).
    /// </summary>
    public class StoreStage : MonoBehaviour
    {
        [Tooltip("The store settings — viewer feel and the sections whose models sit in the slots.")]
        [SerializeField, Required] StoreSettings settings;

        [Title("Slots")]
        [SerializeField, Required] Transform carSlot;
        [SerializeField, Required] Transform shipSlot;
        [SerializeField, Required] Transform characterSlot;

        StoreSectionKind active;
        float yaw;
        float idleTimer;

        /// <summary>The section whose model is showing.</summary>
        public StoreSectionKind Active => active;

        StoreSettings Settings => settings != null ? settings : StoreSettings.Load();

        void Start()
        {
            Show(active);
        }

        /// <summary>Shows one section's model and hides the others. The yaw carries over so a tab change never snaps.</summary>
        public void Show(StoreSectionKind kind)
        {
            active = kind;
            for (int k = 0; k < 3; k++)
            {
                Transform slot = Slot((StoreSectionKind)k);
                if (slot != null) slot.gameObject.SetActive(k == (int)kind);
            }
            ApplyYaw();
        }

        /// <summary>Turns the active model by <paramref name="yawDegrees"/> and holds the idle spin off for a while.</summary>
        public void Nudge(float yawDegrees)
        {
            if (Mathf.Approximately(yawDegrees, 0f)) return;
            yaw += yawDegrees;
            idleTimer = 0f;
            ApplyYaw();
        }

        void Update()
        {
            StoreSettings s = Settings;
            if (s == null) return;
            float dt = Time.unscaledDeltaTime;
            idleTimer += dt;
            if (idleTimer < s.idleResumeSeconds) return;
            yaw += s.autoSpinDegreesPerSecond * dt;
            ApplyYaw();
        }

        void ApplyYaw()
        {
            yaw = Mathf.Repeat(yaw, 360f);
            Transform slot = Slot(active);
            if (slot != null) slot.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>The slot of a section.</summary>
        public Transform Slot(StoreSectionKind kind) => kind switch
        {
            StoreSectionKind.Car => carSlot,
            StoreSectionKind.Ship => shipSlot,
            StoreSectionKind.Character => characterSlot,
            _ => null
        };

        /// <summary>Wires the slots (the scene builder's entry).</summary>
        public void Configure(StoreSettings storeSettings, Transform car, Transform ship, Transform character)
        {
            settings = storeSettings;
            carSlot = car;
            shipSlot = ship;
            characterSlot = character;
        }

        /// <summary>
        /// Re-seats every slot's children from their section's model entry
        /// (scale, offset, LOD stripping) — after retuning a StoreModel, or
        /// after a "Revert All" on the Quadron instance brought its stacked
        /// LODs back.
        /// </summary>
        [Button("Reframe", ButtonSizes.Large)]
        public void Reframe()
        {
            StoreSettings s = Settings;
            if (s == null) return;
            for (int k = 0; k < 3; k++)
            {
                StoreSection section = s.Section((StoreSectionKind)k);
                Transform slot = Slot((StoreSectionKind)k);
                StoreModel model = section != null ? section.DefaultModel : null;
                if (slot == null || model == null) continue;
                for (int i = 0; i < slot.childCount; i++) PrepareInstance(slot.GetChild(i).gameObject, model);
            }
        }

        /// <summary>
        /// Seats a model instance in its slot: offset and uniform scale from
        /// the entry, identity rotation, and — for a LODGroup model — the
        /// group disabled with every LOD past the first hidden (disabling the
        /// group alone draws all its LODs stacked; leaving it on flips the
        /// car to its lowest LOD, since it never fills half the screen here).
        /// </summary>
        public static void PrepareInstance(GameObject instance, StoreModel model)
        {
            if (instance == null || model == null) return;
            Transform t = instance.transform;
            t.localPosition = model.previewOffset;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * Mathf.Max(0.001f, model.previewScale);

            LODGroup lods = instance.GetComponentInChildren<LODGroup>(true);
            if (lods == null) return;
            LOD[] levels = lods.GetLODs();
            for (int i = 0; i < levels.Length; i++)
            {
                bool keep = i == 0;
                foreach (Renderer r in levels[i].renderers)
                    if (r != null) r.gameObject.SetActive(keep);
            }
            lods.enabled = false;
        }
    }
}
