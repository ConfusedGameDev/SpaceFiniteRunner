using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.UI;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Driver abstraction: CarController only ever reads this, so the same
    /// vehicle physics serves the player (this component) and the patrol AI
    /// (a path-following implementation, M5) — "same physics, different
    /// driver".
    /// </summary>
    public interface ICarInput
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        float Steer { get; }

        /// <summary>+1 accelerate .. -1 brake/reverse. Opposing the current travel direction brakes first, then reverses.</summary>
        float Throttle { get; }

        /// <summary>Held handbrake — rear brake plus loosened rear grip for drift turns.</summary>
        bool Handbrake { get; }

        /// <summary>True only on the frame a manual respawn was requested.</summary>
        bool RespawnPressed { get; }
    }

    /// <summary>
    /// Player driver: the Car actions of <see cref="ControlBindings"/> — by
    /// default WASD, Space handbrake, R respawn on the keyboard and left
    /// stick steer, triggers accelerate/brake, South handbrake, North respawn
    /// on the pad, every one rebindable on the CONTROLS screen. Keyboard wins
    /// over the pad on an axis (the runner's SteeringInput rule), and the
    /// arrow keys are the camera's by default rather than a driving alias.
    /// </summary>
    public class CarInput : MonoBehaviour, ICarInput, ICollector
    {
        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake { get; private set; }
        public bool RespawnPressed { get; private set; }

        void Update()
        {
            Steer = ControlBindings.Axis(GameAction.CarSteerLeft, GameAction.CarSteerRight, 0.1f);
            Throttle = ControlBindings.Axis(GameAction.CarBrake, GameAction.CarAccelerate, 0.02f);
            Handbrake = ControlBindings.IsPressed(GameAction.CarHandbrake);
            RespawnPressed = ControlBindings.WasPressedThisFrame(GameAction.CarRespawn);
        }
    }
}
