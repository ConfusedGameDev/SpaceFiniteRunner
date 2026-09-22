using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Ship.UI;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Makes the ship RIG as prefabs, once: <c>ShipSpeedHud.prefab</c> (a
    /// screen-space canvas carrying the <see cref="ShipSpeedHud"/>) and
    /// <c>ShipSystem.prefab</c> — an empty root with the hand-made
    /// <c>HoverShip.prefab</c> and the HUD nested under it, the HUD wired to
    /// that ship. A level takes the whole rig or the ship alone; the HUD alone
    /// works too, finding the scene's ship by itself. Existing prefabs are
    /// never overwritten — they are the user's to edit after this; delete one
    /// to have it built again.
    /// </summary>
    public static class ShipSystemPrefabBuilder
    {
        const string Folder = "Assets/03.Prefabs/Ship";
        const string ShipPrefabPath = "Assets/03.Prefabs/Runner/HoverShip.prefab";
        const string HudPrefabPath = Folder + "/ShipSpeedHud.prefab";
        const string SystemPrefabPath = Folder + "/ShipSystem.prefab";

        [MenuItem("Tools/FiniteRunner/Ship/Build Ship System Prefabs")]
        public static void Build()
        {
            if (Application.isPlaying) return;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/03.Prefabs", "Ship");

            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            if (hudPrefab == null) hudPrefab = BuildHud();
            else Debug.Log($"ShipSystemPrefabBuilder: {HudPrefabPath} exists, kept.");

            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (shipPrefab == null)
            {
                Debug.LogError($"ShipSystemPrefabBuilder: {ShipPrefabPath} is missing — the rig nests the hand-made ship prefab.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(SystemPrefabPath) != null)
            {
                Debug.Log($"ShipSystemPrefabBuilder: {SystemPrefabPath} exists, kept.");
                return;
            }
            BuildSystem(shipPrefab, hudPrefab);
        }

        // A canvas of its own: overlay, scaled from 1920×1080, drawn over a level's other canvases. The HUD builds its
        // widgets at Start, so the prefab is the one object.
        static GameObject BuildHud()
        {
            var go = new GameObject("ShipSpeedHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<ShipSpeedHud>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, HudPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"ShipSystemPrefabBuilder: built {HudPrefabPath}.", prefab);
            return prefab;
        }

        // Nested prefab instances, so an edit to HoverShip.prefab or the HUD reaches the rig.
        static void BuildSystem(GameObject shipPrefab, GameObject hudPrefab)
        {
            var root = new GameObject("ShipSystem");
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
            ship.transform.SetParent(root.transform, false);
            var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab);
            hud.transform.SetParent(root.transform, false);

            var hudFields = new SerializedObject(hud.GetComponent<ShipSpeedHud>());
            hudFields.FindProperty("ship").objectReferenceValue = ship.GetComponent<HoverShip>();
            hudFields.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, SystemPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"ShipSystemPrefabBuilder: built {SystemPrefabPath} (HoverShip + ShipSpeedHud).", prefab);
        }
    }
}
