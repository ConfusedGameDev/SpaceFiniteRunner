using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// A boost orb or a brake pad for ANY level: put it on an object with a
    /// collider and a ship that flies through it gains (or loses) speed
    /// through <see cref="IShip.AddSpeedImpulse"/> — so the ship's weight
    /// scales it, the body blends it in, and the "+N" / shake / rumble that
    /// hang on the ship's <c>PadImpulse</c> come for free. It needs no track
    /// and no manager; the runner's own <c>SpeedPad</c> keeps its tiers,
    /// story hooks and track placement and simply speaks the same
    /// <see cref="IShipPickup"/>.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ShipBoostPickup : MonoBehaviour, IShipPickup
    {
        [Tooltip("Speed change handed to the ship, before its weight: positive boosts, negative brakes.")]
        [SuffixLabel("m/s", true)]
        [SerializeField] float speedDelta = 42.6f;

        [Tooltip("On = taken once and gone (an orb). Off = stays where it is and bites every ship once per pass (a pad painted on the road).")]
        [SerializeField] bool consumed = true;

        [Tooltip("Seconds until a consumed pickup comes back. 0 = never.")]
        [PropertyRange(0f, 120f), SuffixLabel("s", true), ShowIf(nameof(consumed))]
        [SerializeField] float respawnSeconds;

        [Tooltip("Seconds before a pickup that is NOT consumed can bite again — one bite per pass, not one per tick spent inside it.")]
        [PropertyRange(0.1f, 10f), SuffixLabel("s", true), HideIf(nameof(consumed))]
        [SerializeField] float rearmSeconds = 1f;

        float readyAt;
        Renderer[] renderers;

        /// <summary>Any boost pickup was taken — static, so a HUD or a stat counter needs no per-pickup wiring.</summary>
        public static event Action<ShipBoostPickup, IShip> Taken;

        public float SpeedDelta => speedDelta;
        public bool Available => isActiveAndEnabled && Time.time >= readyAt;

        /// <summary>For a pickup built from code (a generated level).</summary>
        public void Configure(float speedDelta, bool consumed, float respawnSeconds = 0f)
        {
            this.speedDelta = speedDelta;
            this.consumed = consumed;
            this.respawnSeconds = respawnSeconds;
        }

        void Awake() => renderers = GetComponentsInChildren<Renderer>();

        public void PickUp(IShip ship)
        {
            if (!Available || ship == null) return;
            ship.AddSpeedImpulse(speedDelta);
            Taken?.Invoke(this, ship);

            if (!consumed) { readyAt = Time.time + rearmSeconds; return; }
            if (respawnSeconds <= 0f) { gameObject.SetActive(false); return; }
            // Coming back later: it stays in the scene, unseen and unavailable, and shows itself again when ready.
            readyAt = Time.time + respawnSeconds;
            SetVisible(false);
        }

        void Update()
        {
            if (consumed && respawnSeconds > 0f && Time.time >= readyAt) SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer renderer in renderers)
                if (renderer != null && renderer.enabled != visible) renderer.enabled = visible;
        }
    }
}
