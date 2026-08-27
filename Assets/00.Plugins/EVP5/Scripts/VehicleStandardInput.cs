//------------------------------------------------------------------------------------------------
// Edy's Vehicle Physics
// (c) Angel Garcia "Edy" - Oviedo, Spain
// http://www.edy.es
//------------------------------------------------------------------------------------------------
// Project modification: legacy Input Manager axes replaced with Unity Input System
// InputActions. Each action gets sensible default bindings (keyboard + gamepad) in code
// when none are authored, so the component keeps working with zero inspector wiring.

using UnityEngine;
using UnityEngine.InputSystem;

namespace EVP
{

public class VehicleStandardInput : MonoBehaviour
	{
	public VehicleController target;

	public bool continuousForwardAndReverse = true;

	public enum ThrottleAndBrakeInput { SingleAxis, SeparateAxes };
	public ThrottleAndBrakeInput throttleAndBrakeInput = ThrottleAndBrakeInput.SingleAxis;

	public InputAction steerAction = new InputAction("Steer", InputActionType.Value, expectedControlType: "Axis");
	public InputAction throttleAndBrakeAction = new InputAction("Throttle And Brake", InputActionType.Value, expectedControlType: "Axis");
	public InputAction throttleAction = new InputAction("Throttle", InputActionType.Value, expectedControlType: "Axis");
	public InputAction brakeAction = new InputAction("Brake", InputActionType.Value, expectedControlType: "Axis");
	public InputAction handbrakeAction = new InputAction("Handbrake", InputActionType.Value, expectedControlType: "Axis");
	public InputAction reverseModifierAction = new InputAction("Reverse Modifier", InputActionType.Button);
	public InputAction resetVehicleAction = new InputAction("Reset Vehicle", InputActionType.Button);

	bool m_doReset = false;


	InputAction[] AllActions ()
		{
		return new[]
			{
			steerAction, throttleAndBrakeAction, throttleAction, brakeAction,
			handbrakeAction, reverseModifierAction, resetVehicleAction
			};
		}


	// Editor-time: fill the default bindings when the component is first added,
	// so they are serialized and can be edited in the inspector.

	void Reset ()
		{
		SetupDefaultBindings();
		}


	void OnEnable ()
		{
		// Cache vehicle

		if (target == null)
			target = GetComponent<VehicleController>();

		// Components that predate the Input System port (or were added from code)
		// may carry actions with no bindings. Give those the defaults.

		SetupDefaultBindings();

		foreach (InputAction action in AllActions())
			action.Enable();
		}


	void OnDisable ()
		{
		foreach (InputAction action in AllActions())
			action.Disable();
		}


	void SetupDefaultBindings ()
		{
		if (steerAction.bindings.Count == 0)
			{
			steerAction.AddCompositeBinding("1DAxis")
				.With("Negative", "<Keyboard>/a")
				.With("Positive", "<Keyboard>/d");
			steerAction.AddCompositeBinding("1DAxis")
				.With("Negative", "<Keyboard>/leftArrow")
				.With("Positive", "<Keyboard>/rightArrow");
			steerAction.AddBinding("<Gamepad>/leftStick/x");
			}

		if (throttleAndBrakeAction.bindings.Count == 0)
			{
			throttleAndBrakeAction.AddCompositeBinding("1DAxis")
				.With("Negative", "<Keyboard>/s")
				.With("Positive", "<Keyboard>/w");
			throttleAndBrakeAction.AddCompositeBinding("1DAxis")
				.With("Negative", "<Keyboard>/downArrow")
				.With("Positive", "<Keyboard>/upArrow");
			throttleAndBrakeAction.AddCompositeBinding("1DAxis")
				.With("Negative", "<Gamepad>/leftTrigger")
				.With("Positive", "<Gamepad>/rightTrigger");
			throttleAndBrakeAction.AddBinding("<Gamepad>/leftStick/y");
			}

		if (throttleAction.bindings.Count == 0)
			{
			throttleAction.AddBinding("<Keyboard>/w");
			throttleAction.AddBinding("<Keyboard>/upArrow");
			throttleAction.AddBinding("<Gamepad>/rightTrigger");
			}

		if (brakeAction.bindings.Count == 0)
			{
			brakeAction.AddBinding("<Keyboard>/s");
			brakeAction.AddBinding("<Keyboard>/downArrow");
			brakeAction.AddBinding("<Gamepad>/leftTrigger");
			}

		if (handbrakeAction.bindings.Count == 0)
			{
			handbrakeAction.AddBinding("<Keyboard>/space");
			handbrakeAction.AddBinding("<Gamepad>/buttonSouth");
			}

		if (reverseModifierAction.bindings.Count == 0)
			{
			reverseModifierAction.AddBinding("<Keyboard>/leftCtrl");
			reverseModifierAction.AddBinding("<Keyboard>/rightCtrl");
			reverseModifierAction.AddBinding("<Gamepad>/leftStickPress");
			}

		if (resetVehicleAction.bindings.Count == 0)
			{
			resetVehicleAction.AddBinding("<Keyboard>/enter");
			resetVehicleAction.AddBinding("<Keyboard>/numpadEnter");
			resetVehicleAction.AddBinding("<Gamepad>/select");
			}
		}


	void Update ()
		{
		if (target == null) return;

		if (resetVehicleAction.WasPressedThisFrame()) m_doReset = true;
		}


	void FixedUpdate ()
		{
		if (target == null) return;

		// Read the user input

		float steerInput = Mathf.Clamp(steerAction.ReadValue<float>(), -1.0f, 1.0f);
		float handbrakeInput = Mathf.Clamp01(handbrakeAction.ReadValue<float>());

		float forwardInput = 0.0f;
		float reverseInput = 0.0f;

		if (throttleAndBrakeInput == ThrottleAndBrakeInput.SeparateAxes)
			{
			forwardInput = Mathf.Clamp01(throttleAction.ReadValue<float>());
			reverseInput = Mathf.Clamp01(brakeAction.ReadValue<float>());
			}
		else
			{
			float axis = throttleAndBrakeAction.ReadValue<float>();
			forwardInput = Mathf.Clamp01(axis);
			reverseInput = Mathf.Clamp01(-axis);
			}

		// Translate forward/reverse to vehicle input

		float throttleInput = 0.0f;
		float brakeInput = 0.0f;

		if (continuousForwardAndReverse)
			{
			float minSpeed = 0.1f;
			float minInput = 0.1f;

			if (target.speed > minSpeed)
				{
				throttleInput = forwardInput;
				brakeInput = reverseInput;
				}
			else
				{
				if (reverseInput > minInput)
					{
					throttleInput = -reverseInput;
					brakeInput = 0.0f;
					}
				else if (forwardInput > minInput)
					{
					if (target.speed < -minSpeed)
						{
						throttleInput = 0.0f;
						brakeInput = forwardInput;
						}
					else
						{
						throttleInput = forwardInput;
						brakeInput = 0;
						}
					}
				}
			}
		else
			{
			bool reverse = reverseModifierAction.IsPressed();

			if (!reverse)
				{
				throttleInput = forwardInput;
				brakeInput = reverseInput;
				}
			else
				{
				throttleInput = -reverseInput;
				brakeInput = 0;
				}
			}

		// Apply input to vehicle

		target.steerInput = steerInput;
		target.throttleInput = throttleInput;
		target.brakeInput = brakeInput;
		target.handbrakeInput = handbrakeInput;

		// Do a vehicle reset

		if (m_doReset)
			{
			target.ResetVehicle();
			m_doReset = false;
			}
		}
	}
}
