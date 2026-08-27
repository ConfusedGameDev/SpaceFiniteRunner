//------------------------------------------------------------------------------------------------
// Edy's Vehicle Physics
// (c) Angel Garcia "Edy" - Oviedo, Spain
// http://www.edy.es
//------------------------------------------------------------------------------------------------
// Project modification: bridge to the Unity Input System so EVP scripts can poll
// keyboard keys without the legacy Input Manager (the project runs Input System only).

using UnityEngine.InputSystem;

namespace EVP
{

/// <summary>
/// Polling helpers over <see cref="Keyboard.current"/> used by the EVP scripts that were
/// ported from the legacy Input class. All methods are null-safe (no keyboard attached)
/// and treat <see cref="Key.None"/> as "no binding".
/// </summary>
public static class InputCompat
	{
	static bool IsPollable (Key key)
		{
		return key != Key.None && key != Key.IMESelected && Keyboard.current != null;
		}


	/// <summary>True the frame the key went down (legacy Input.GetKeyDown).</summary>
	public static bool KeyDown (Key key)
		{
		return IsPollable(key) && Keyboard.current[key].wasPressedThisFrame;
		}


	/// <summary>True while the key is held (legacy Input.GetKey).</summary>
	public static bool KeyPressed (Key key)
		{
		return IsPollable(key) && Keyboard.current[key].isPressed;
		}


	/// <summary>True while either shift key is held.</summary>
	public static bool ShiftPressed ()
		{
		return Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
		}
	}

}
