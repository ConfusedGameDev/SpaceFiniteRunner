using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Reads steering input from the New Input System and forwards it to VehicleController.
    /// Isolated from VehicleController so the controller never imports from UnityEngine.InputSystem.
    ///
    /// SETUP REQUIRED (one-time, in the Unity Editor):
    ///   1. Select Assets/04.Data/InputSystem_Actions.inputactions in the Project window.
    ///   2. In the Inspector, tick "Generate C# Class".
    ///   3. Set Namespace to "HoverRacer", then click Apply.
    ///   Unity will generate InputSystem_Actions.cs alongside the asset.
    ///
    /// Only the horizontal (X) axis of the "Move" action is used.
    /// Y axis (vertical) is intentionally ignored — the player has no throttle control.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleInputHandler : MonoBehaviour
    {
        VehicleController  vehicle;
        InputSystem_Actions inputActions;

        void Awake()
        {
            vehicle      = GetComponent<VehicleController>();
            inputActions = new InputSystem_Actions();
        }

        void OnEnable()
        {
            inputActions.Player.Enable();
        }

        void OnDisable()
        {
            inputActions.Player.Disable();
        }

        void Update()
        {
            if (vehicle.IsStopped) return;

            // X axis = left (-1) / right (+1) steering.
            float horizontal = inputActions.Player.Move.ReadValue<UnityEngine.Vector2>().x;
            vehicle.SetSteerInput(horizontal);

            // Boost: triggered once per press (Jump action = Space / gamepad South button).
            if (inputActions.Player.Jump.WasPressedThisFrame())
                vehicle.ActivateBoost();
        }
    }
}
