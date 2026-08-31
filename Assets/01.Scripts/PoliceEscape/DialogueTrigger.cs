using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Ship;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// A hand-placed volume that speaks one <see cref="RpgMessageSystem"/>
    /// line when the player drives (or flies) into it: portrait sprite,
    /// speaker name, text and hold duration are authored on the trigger
    /// itself. It recognises both games' players — the city car by its
    /// <see cref="Vehicles.CarInput"/>, the runner ship by its
    /// <see cref="ShipMotor"/> — so the same component works in every scene.
    /// With <see cref="oneShot"/> on the object destroys itself after firing;
    /// otherwise it stays and re-arms only once the player has LEFT the
    /// volume AND <see cref="cooldownSeconds"/> has passed since the line
    /// fired — re-entering during the cooldown is swallowed, not deferred,
    /// so a player circling the trigger can't stack lines. The collider is
    /// forced to a trigger so a misconfigured one can never block the car.
    /// While playing in the editor, <see cref="Debugging.DialogueTriggerVisualizer"/>
    /// fills the volume with a translucent orange box (DebugManager's
    /// dialogue-triggers channel); the gizmo below covers edit-mode placement.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DialogueTrigger : MonoBehaviour
    {
        [TitleGroup("Line")]
        [Tooltip("Portrait shown in the dialogue box. Empty falls back to the message system's default, then to the speaker's initial.")]
        [PreviewField(60)]
        [SerializeField] Sprite characterSprite;

        [Tooltip("Speaker name shown above the line.")]
        [Required]
        [SerializeField] string characterName = "PILOT";

        [Tooltip("The line the box types out.")]
        [MultiLineProperty(4)]
        [SerializeField] string text = "";

        [Tooltip("Seconds the finished line holds on screen before hiding.")]
        [PropertyRange(0.5f, 15f)]
        [SerializeField] float duration = 3f;

        [Tooltip("Tint for the speaker name and portrait frame.")]
        [SerializeField] Color accent = Color.white;

        [TitleGroup("Re-trigger")]
        [Tooltip("Destroy this trigger after it fires once.")]
        [SerializeField] bool oneShot;

        [Tooltip("After firing, the trigger stays dead for this long — on top of requiring the player to exit before it can fire again. 0 = exit alone re-arms it.")]
        [HideIf(nameof(oneShot))]
        [PropertyRange(0f, 120f)]
        [SerializeField] float cooldownSeconds = 5f;

        static readonly List<DialogueTrigger> active = new();

        float lastFireTime = float.NegativeInfinity;

        /// <summary>Every enabled trigger in the scene — what the debug overlay draws.</summary>
        public static IReadOnlyList<DialogueTrigger> Active => active;

        /// <summary>True when the cooldown has passed and the next entry will fire.</summary>
        public bool IsReady => Time.time - lastFireTime >= cooldownSeconds;

        void Awake()
        {
            foreach (var col in GetComponents<Collider>()) col.isTrigger = true;
        }

        void OnEnable()
        {
            active.Add(this);
            Debugging.DialogueTriggerVisualizer.EnsureInstalled();
        }

        void OnDisable() => active.Remove(this);

        void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;
            if (Time.time - lastFireTime < cooldownSeconds) return;

            lastFireTime = Time.time;
            RpgMessageSystem.Instance.ShowMessage(characterName, text, duration, accent, characterSprite);
            if (oneShot) Destroy(gameObject);
        }

        /// <summary>
        /// Is this collider the player? The two player markers: CarInput sits
        /// on the city car's rigidbody root, ShipMotor on the ship root above
        /// its trigger collider. AI cars, props and the patrol match neither.
        /// Shared with the other story-trigger volumes (the cinema trigger)
        /// so "the player" means the same thing everywhere.
        /// </summary>
        public static bool IsPlayer(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb != null && rb.GetComponent<Vehicles.CarInput>() != null) return true;
            return other.GetComponentInParent<ShipMotor>() != null;
        }

        void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.25f);
            if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (col is SphereCollider sphere)
            {
                Gizmos.DrawSphere(transform.TransformPoint(sphere.center), sphere.radius);
            }
        }
    }
}
