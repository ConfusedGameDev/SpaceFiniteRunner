using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Names the standalone ship's four physics layers in their FIXED slots
    /// (<see cref="ShipLayers"/>, 6–9) and switches off the collision pairs
    /// that mean nothing: pickup and rule volumes are only ever found by the
    /// ship's swept queries, so they collide with no layer at all. Explicit
    /// slots on purpose — the glitch installer's "first free slot from 31
    /// down" would walk into the unnamed 28 / 29 the runner scene already
    /// uses for its tidy parents. Refuses to overwrite a slot that carries
    /// another name, touches no other layer, and is idempotent.
    /// </summary>
    public static class ShipLayersInstaller
    {
        [MenuItem("Tools/FiniteRunner/Ship/Install Ship Layers")]
        public static void Install()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            bool ok = Name(layers, ShipLayers.Ship, ShipLayers.ShipName)
                    & Name(layers, ShipLayers.Ground, ShipLayers.GroundName)
                    & Name(layers, ShipLayers.Pickup, ShipLayers.PickupName)
                    & Name(layers, ShipLayers.Volume, ShipLayers.VolumeName);
            tagManager.ApplyModifiedProperties();
            if (!ok) return;

            // Volumes are query-only: no contact pair is ever wanted with them.
            for (int other = 0; other < 32; other++)
            {
                Physics.IgnoreLayerCollision(ShipLayers.Pickup, other, true);
                Physics.IgnoreLayerCollision(ShipLayers.Volume, other, true);
            }

            // Only the two settings files: a blanket SaveAssets would also flush
            // whatever unrelated asset happens to be dirty in the editor.
            AssetDatabase.SaveAssetIfDirty(tagManager.targetObject);
            foreach (Object physics in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset"))
                AssetDatabase.SaveAssetIfDirty(physics);
            Debug.Log($"ShipLayersInstaller: layers {ShipLayers.Ship}–{ShipLayers.Volume} = " +
                      $"{ShipLayers.ShipName} / {ShipLayers.GroundName} / {ShipLayers.PickupName} / {ShipLayers.VolumeName}.");
        }

        static bool Name(SerializedProperty layers, int slot, string name)
        {
            SerializedProperty entry = layers.GetArrayElementAtIndex(slot);
            if (entry.stringValue == name) return true;
            if (!string.IsNullOrEmpty(entry.stringValue))
            {
                Debug.LogError($"ShipLayersInstaller: layer slot {slot} is already '{entry.stringValue}' — the ship's slots are fixed, free it in Project Settings → Tags and Layers and re-run.");
                return false;
            }
            entry.stringValue = name;
            return true;
        }
    }
}
