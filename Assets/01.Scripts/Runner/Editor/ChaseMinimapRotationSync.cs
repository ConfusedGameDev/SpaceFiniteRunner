using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.HUD;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Keeps the minimap's sprite rotations two-way: the style asset's
    /// <c>shipSpriteRotation</c> / <c>policeSpriteRotation</c> sliders drive
    /// the icons (via <see cref="ChaseMinimap.RebuildUI"/>), and this hook
    /// writes a hand-edited Ship / Police Z rotation (inspector or rotate tool)
    /// back into the asset, then rebuilds every other minimap on it. Only while
    /// that icon has a sprite — the spriteless diamond's 45° is not a setting.
    /// </summary>
    [InitializeOnLoad]
    static class ChaseMinimapRotationSync
    {
        static readonly HashSet<Transform> pending = new();

        static ChaseMinimapRotationSync()
        {
            Undo.postprocessModifications -= OnModifications;
            Undo.postprocessModifications += OnModifications;
        }

        static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            foreach (var mod in modifications)
            {
                var target = mod.currentValue?.target as Transform;
                if (target == null || !mod.currentValue.propertyPath.StartsWith("m_LocalRotation")
                    && !mod.currentValue.propertyPath.StartsWith("m_LocalEulerAnglesHint")) continue;
                if (pending.Count == 0) EditorApplication.delayCall += Flush;
                pending.Add(target);
            }
            return modifications;
        }

        // Deferred so the transform already holds the new rotation.
        static void Flush()
        {
            foreach (var icon in pending)
            {
                if (icon == null) continue;
                var map = icon.GetComponentInParent<ChaseMinimap>(true);
                var style = map != null ? map.StyleAsset : null;
                if (style == null) continue;

                float angle = Mathf.DeltaAngle(0f, icon.localEulerAngles.z);
                if (icon == map.ShipIcon && style.shipSprite != null && !Mathf.Approximately(style.shipSpriteRotation, angle))
                {
                    Undo.RecordObject(style, "Ship Sprite Rotation");
                    style.shipSpriteRotation = angle;
                }
                else if (icon == map.PoliceIcon && style.policeSprite != null && !Mathf.Approximately(style.policeSpriteRotation, angle))
                {
                    Undo.RecordObject(style, "Police Sprite Rotation");
                    style.policeSpriteRotation = angle;
                }
                else continue;

                EditorUtility.SetDirty(style);
                ChaseMinimap.RebuildAllUsing(style);
            }
            pending.Clear();
        }
    }
}
