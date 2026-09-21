using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The standalone ship's hands: every tick it sweeps the hull's
    /// cross-section along the path the body actually flew — substep by
    /// substep, so the box follows a loop or a tube instead of cutting its
    /// chord — and hands itself to every <see cref="IShipPickup"/> the box
    /// touched. It is the world-space counterpart of the runner's analytic
    /// pickup sweep and tunnel-proof for the same reason: what is tested is
    /// the whole distance covered, not where the ship happens to stand at the
    /// end of the step. The box is as wide and tall as the hull collider, so
    /// "did I fly through it" means what it looks like; a jump clears the
    /// pickups on the ground under it because the path really is up there.
    ///
    /// Pickups are found by COLLIDER (any shape, trigger or not) on
    /// <see cref="ShipSettings.pickupLayers"/>, so a level makes something
    /// collectable by putting a component on it — no registry, no track.
    /// </summary>
    [DefaultExecutionOrder(5)] // after the ship's tick: the path swept is this tick's
    [RequireComponent(typeof(HoverShip))]
    public sealed class ShipPickupSweeper : MonoBehaviour
    {
        const float HalfDepth = 0.25f;

        static readonly RaycastHit[] Hits = new RaycastHit[32];
        static readonly Collider[] Overlaps = new Collider[32];

        readonly HashSet<IShipPickup> takenThisTick = new();
        HoverShip ship;
        Vector3 halfExtents = new(2.5f, 2.3f, HalfDepth);

        void Awake()
        {
            ship = GetComponent<HoverShip>();
            if (TryGetComponent(out BoxCollider hull))
                halfExtents = new Vector3(hull.size.x * 0.5f, hull.size.y * 0.5f, HalfDepth);
        }

        void FixedUpdate()
        {
            ShipSettings settings = ship.Settings;
            if (ship.Paused || settings == null || !settings.pickupsEnabled) return;
            ShipState state = ship.State;
            if (state == ShipState.OffTrack || state == ShipState.Respawning || state == ShipState.Falling) return; // out of play takes nothing

            IReadOnlyList<Pose> path = ship.Body.Path;
            if (path.Count == 0) return;
            takenThisTick.Clear();
            int mask = settings.pickupLayers;

            for (int i = 1; i < path.Count; i++)
            {
                Vector3 delta = path[i].position - path[i - 1].position;
                float distance = delta.magnitude;
                if (distance < 1e-4f) continue;
                int count = Physics.BoxCastNonAlloc(path[i - 1].position, halfExtents, delta / distance, Hits,
                                                    path[i].rotation, distance, mask, QueryTriggerInteraction.Collide);
                for (int h = 0; h < count; h++) Take(Hits[h].collider);
            }

            // A cast does not report what it starts inside of (a slow ship, a pickup that came back around it).
            Pose end = path[path.Count - 1];
            int overlapping = Physics.OverlapBoxNonAlloc(end.position, halfExtents, Overlaps, end.rotation, mask, QueryTriggerInteraction.Collide);
            for (int o = 0; o < overlapping; o++) Take(Overlaps[o]);
        }

        void Take(Collider collider)
        {
            var pickup = collider.GetComponentInParent<IShipPickup>();
            if (pickup == null || !pickup.Available || !takenThisTick.Add(pickup)) return;
            pickup.PickUp(ship);
        }
    }
}
