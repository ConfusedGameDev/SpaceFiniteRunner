using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A repair orb: a white cross inside a translucent green sphere floating
    /// on the flight line. Flying through it gives back
    /// <see cref="GameSettings.repairOrbHealFraction"/> of the ship's hull
    /// (<see cref="ShipHealth.HealFromRepairOrb"/>) and uses it up. At full
    /// hull it is <b>ignored</b> — not taken, it stays on the track. It is
    /// deliberately NOT a <see cref="SpeedPad"/> and NOT an <c>ITrackPickup</c>:
    /// it never enters the <c>PickupRegistry</c>, so the patrol neither seeks
    /// nor takes it, and it raises no speed impulse (no boost/brake haptics,
    /// shake or pad stats). Only the ship's collider sweep
    /// (<see cref="IShipPickup"/>) finds it, and only a ship with a
    /// <see cref="ShipHealth"/> can use it. Spawned by the
    /// <see cref="TrackGenerator"/>'s repair-orb stream, at the Green boost
    /// orb's rate.
    /// </summary>
    public class RepairOrb : MonoBehaviour, IShipPickup
    {
        bool taken;

        /// <summary>Raised when the player's ship takes a repair orb. Arguments: the orb, the ship, the hull points it restored. Static, like <see cref="SpeedPad.Collected"/>.</summary>
        public static event System.Action<RepairOrb, IShip, float> Collected;

        public bool Available => !taken;

        public void PickUp(IShip ship)
        {
            if (taken) return;
            var health = ShipHealth.For(ship);
            if (health == null) return; // no hull on it: never the patrol's

            float healed = health.HealFromRepairOrb();
            if (healed <= 0f) return; // full hull: left where it is

            taken = true;
            Collected?.Invoke(this, ship, healed);
            gameObject.SetActive(false);
        }
    }
}
