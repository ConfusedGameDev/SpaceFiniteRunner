using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Track;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Lights the ship up with electricity when it takes a speed-up pad or
    /// orb (any <see cref="SpeedPad"/> with a positive delta): every tier the
    /// same, a burst of <see cref="MeshSurfaceSparks"/> over the model that
    /// fades out on its own (another pad mid-fade puts it back to full). Sits on <c>PF_ShipBoostSparks</c>, nested in <c>PF_Ship</c>
    /// under the ROOT — never under the model, whose meshes the dash ghosts
    /// copy — and finds its ship upward. Only the player's own pickups count
    /// (the patrol and rivals raise the same event), and only speed-ups: a
    /// repair orb is not a <see cref="SpeedPad"/>, a brake pad's delta is negative.
    /// </summary>
    [RequireComponent(typeof(MeshSurfaceSparks))]
    public class BoostOrbSparks : MonoBehaviour
    {
        MeshSurfaceSparks sparks;
        ShipMotor motor;

        void Awake()
        {
            sparks = GetComponent<MeshSurfaceSparks>();
            motor = GetComponentInParent<ShipMotor>();
        }

        // Subscribed here rather than in a static initializer: domain reload is
        // off, so a static subscription would survive play sessions and stack up.
        void OnEnable() => SpeedPad.Collected += OnPadCollected;
        void OnDisable() => SpeedPad.Collected -= OnPadCollected;

        void OnPadCollected(SpeedPad pad, IShip collector)
        {
            if (sparks == null || motor == null || pad == null) return;
            // Any pad that speeds the ship up — not IsBoostOrb: the shipped
            // BoostPad_Definition has floatingOrb off, so that is false for every pad.
            if (pad.SpeedDelta <= 0f || !motor.Is(collector)) return;
            sparks.Play();
        }
    }
}
