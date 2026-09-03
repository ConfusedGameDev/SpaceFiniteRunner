using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Cameras
{
    /// <summary>
    /// What the chase camera needs from whatever it follows — the city's
    /// <c>CarController</c> and the runner's <c>ShipMotor</c> both implement
    /// it, so one <see cref="OrbitCameraRig"/> serves both games and the rig
    /// never references a vehicle type. The two input gates are the vehicle's
    /// say over the camera's shared controls: a car mid-jump owns the stick
    /// and the arrows (air control), a menu owns Tab / Back, and later the
    /// runner's airborne ship locks the view cycle so a forced framing can't
    /// be undone mid-arc.
    /// </summary>
    public interface ICameraTarget
    {
        /// <summary>The transform the orbit follows; its forward is "ahead", its up is the roll the target-up binding tracks.</summary>
        Transform Transform { get; }

        /// <summary>Speed for the FOV kick, in km/h.</summary>
        float SpeedKmh { get; }

        /// <summary>
        /// The chassis box the first-person eye is seated off (local centre
        /// and the height of its top). False when the target has no usable
        /// box — the rig then places the eye from the settings' authored offset.
        /// </summary>
        bool TryGetChassisBox(out Vector3 localCentre, out float localTop);

        /// <summary>True while the stick / arrow-key pan belongs to the vehicle (the mouse keeps panning).</summary>
        bool BlockPanInput { get; }

        /// <summary>True while the view cycle (Tab / Back) must be ignored — a menu is open, or the view is being held.</summary>
        bool BlockModeCycle { get; }
    }
}
