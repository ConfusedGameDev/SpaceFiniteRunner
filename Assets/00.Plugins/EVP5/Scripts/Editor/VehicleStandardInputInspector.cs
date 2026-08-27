//------------------------------------------------------------------------------------------------
// Edy's Vehicle Physics
// (c) Angel Garcia "Edy" - Oviedo, Spain
// http://www.edy.es
//------------------------------------------------------------------------------------------------

using UnityEngine;
using UnityEditor;

namespace EVP
{

[CustomEditor(typeof(VehicleStandardInput))]
public class VehicleStandardInputInspector : Editor
	{
	public override void OnInspectorGUI ()
		{
		InspectorTools.BeginContent();
		serializedObject.Update();

		EditorGUILayout.PropertyField(serializedObject.FindProperty("target"));

		InspectorTools.SetMinLabelWidth(210);
		EditorGUILayout.PropertyField(serializedObject.FindProperty("continuousForwardAndReverse"));
		InspectorTools.SetMinLabelWidth(160);

		SerializedProperty propThrottleAndBrakeInput = serializedObject.FindProperty("throttleAndBrakeInput");
		EditorGUILayout.PropertyField(propThrottleAndBrakeInput);
		EditorGUILayout.PropertyField(serializedObject.FindProperty("steerAction"));

		VehicleStandardInput.ThrottleAndBrakeInput throttleAndBrakeInput = (VehicleStandardInput.ThrottleAndBrakeInput)propThrottleAndBrakeInput.enumValueIndex;

		if (throttleAndBrakeInput == VehicleStandardInput.ThrottleAndBrakeInput.SeparateAxes)
			{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("throttleAction"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("brakeAction"));
			}
		else
			{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("throttleAndBrakeAction"));
			}

		EditorGUILayout.PropertyField(serializedObject.FindProperty("handbrakeAction"));
		EditorGUILayout.PropertyField(serializedObject.FindProperty("reverseModifierAction"));
		EditorGUILayout.PropertyField(serializedObject.FindProperty("resetVehicleAction"));

		serializedObject.ApplyModifiedProperties();
		InspectorTools.EndContent();
		}

	}
}
