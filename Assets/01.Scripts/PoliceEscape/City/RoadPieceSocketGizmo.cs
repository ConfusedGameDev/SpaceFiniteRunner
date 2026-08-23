using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Showcase helper: draws a road piece's declared sockets as arrows on
    /// top of the instantiated model, one per footprint cell, plus a label.
    /// Attached by the Road Kit Showcase editor tool so a wrong connectionMask
    /// or rotationOffset is visible at a glance — an arrow must point along a
    /// visible road. Reads the settings asset live (and re-applies the
    /// rotationOffset to the model underneath) so fixes show up without
    /// rebuilding the scene. Ramps show their measured start/end heights and
    /// uphill side instead. Purely diagnostic; never spawned by the generator.
    /// </summary>
    public class RoadPieceSocketGizmo : MonoBehaviour
    {
        [Tooltip("Settings asset whose roadPieces entry this object previews (live).")]
        public CityGenerationSettings settings;
        public int pieceIndex = -1;
        [Tooltip("Fallback definition when no settings/index is wired.")]
        public RoadPieceDefinition definition;
        public float scale = 1f;

        RoadPieceDefinition Definition =>
            settings != null && settings.roadPieces != null && pieceIndex >= 0 && pieceIndex < settings.roadPieces.Count
                ? settings.roadPieces[pieceIndex]
                : definition;

        void OnDrawGizmos()
        {
            RoadPieceDefinition def = Definition;
            if (def == null) return;

            // Keep the model under us in sync with the asset's rotationOffset.
            if (transform.childCount > 0)
                transform.GetChild(0).localRotation = Quaternion.Euler(0f, def.rotationOffset, 0f);

            int w = Mathf.Max(1, def.footprintInCells.x);
            int h = Mathf.Max(1, def.footprintInCells.y);
            Vector3 center = transform.position;
            Vector3 minCorner = center - new Vector3(w * 0.5f, 0f, h * 0.5f) * scale;

            if (def.role == RoadPieceRole.Fork)
            {
                // Expected convention: stem in from the West at the centre, two exits East half a cell either side.
                Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                Gizmos.DrawWireCube(center, new Vector3(w * scale, 0.01f, h * scale));
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawLine(center + Vector3.left * (0.5f * w * scale), center);
                Gizmos.DrawSphere(center + Vector3.left * (0.5f * w * scale), 0.05f * scale);
                Gizmos.color = Color.cyan;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 exit = center + new Vector3(0.5f * w * scale, 0f, s * 0.5f * scale);
                    Gizmos.DrawLine(center, exit);
                    Gizmos.DrawSphere(exit, 0.04f * scale);
                }
#if UNITY_EDITOR
                UnityEditor.Handles.Label(center + Vector3.up * (0.8f * scale),
                    $"{(def.prefab != null ? def.prefab.name : name)}\nFORK: orange = entrance (West), cyan = exits (East)" +
                    (Mathf.Approximately(def.rotationOffset, 0f) ? "" : $"\noffset {def.rotationOffset:0}°"));
#endif
                return;
            }

            for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
            {
                EdgeMask mask = def.IsMultiCell ? def.CellMask(u, v) : def.connectionMask;
                Vector3 cellCenter = minCorner + new Vector3((u + 0.5f) * scale, 0.05f * scale, (v + 0.5f) * scale);
                Gizmos.color = mask == EdgeMask.None ? new Color(0.5f, 0.5f, 0.5f, 0.4f) : new Color(1f, 1f, 1f, 0.25f);
                Gizmos.DrawWireCube(cellCenter, new Vector3(scale, 0.01f, scale));
                if (def.role == RoadPieceRole.Pillar) continue;

                for (int dir = 0; dir < 4; dir++)
                {
                    if ((mask & EdgeMaskUtility.DirectionBit(dir)) == 0) continue;
                    Vector2Int o = EdgeMaskUtility.Offset(dir);
                    var direction = new Vector3(o.x, 0f, o.y);
                    Vector3 tip = cellCenter + direction * (0.5f * scale);
                    if (def.role == RoadPieceRole.Deck) tip.y = cellCenter.y + def.deckHeight * scale;
                    Gizmos.color = def.role == RoadPieceRole.Ramp && dir == 0 ? new Color(1f, 0.5f, 0f) : Color.cyan;
                    Gizmos.DrawLine(cellCenter + (tip - cellCenter) * 0.1f, tip);
                    Gizmos.DrawSphere(tip, 0.04f * scale);
                }
            }

#if UNITY_EDITOR
            string text = def.prefab != null ? def.prefab.name : name;
            text += def.role switch
            {
                RoadPieceRole.Ramp => $"\nRAMP {def.rampStartHeight:0.##} → {def.rampEndHeight:0.##} (orange = uphill)",
                RoadPieceRole.Deck => $"\nDECK surface at {def.deckHeight:0.##}",
                RoadPieceRole.Pillar => "\nPILLAR",
                RoadPieceRole.HalfStraight => $"\nHALF STRAIGHT, mask {def.connectionMask}",
                _ => def.IsMultiCell ? $"\n{w}×{h} template, chance {def.placeChance:0.##}" : $"\nmask {def.connectionMask}",
            };
            if (!Mathf.Approximately(def.rotationOffset, 0f)) text += $"\noffset {def.rotationOffset:0}°";
            UnityEditor.Handles.Label(center + Vector3.up * (0.8f * scale), text);
#endif
        }
    }
}
