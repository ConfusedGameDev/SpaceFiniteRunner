using UnityEngine;

using ConfusedGameDev.FiniteRunner.Cameras;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Gives a standalone ship the project's chase camera in a level that has
    /// no game manager to do it: on Start it hands the ship to
    /// <see cref="CameraRigInstaller.Attach"/> — the one camera entry point,
    /// scoped to the ship's own scene — with the camera asset set here. A game
    /// that attaches the camera itself (the runner's GameManager) simply
    /// leaves this component off the prefab variant.
    /// </summary>
    [RequireComponent(typeof(HoverShip))]
    public sealed class ShipCameraAttach : MonoBehaviour
    {
        [SerializeField] OrbitCameraSettings cameraSettings;

        void Start() => CameraRigInstaller.Attach(GetComponent<HoverShip>(), cameraSettings);
    }
}
