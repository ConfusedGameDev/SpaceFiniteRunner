using ConfusedGameDev.FiniteRunner.Collectibles;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// GameObject → Police Escape → Collectible: drops a ready-made pickup at
    /// the scene view's pivot — a root object carrying the
    /// <see cref="Collectible"/>, a trigger sphere, and a placeholder cube
    /// child in the "Mesh" slot to swap for real art. Under a selected
    /// object it parents there instead (the city prefab's AdditionalItems
    /// socket is the rebake-proof home).
    /// </summary>
    public static class CollectiblePlacer
    {
        [MenuItem("GameObject/Police Escape/Collectible", false, 10)]
        static void Create(MenuCommand command)
        {
            var go = new GameObject("Collectible");
            var parent = command.context as GameObject;
            if (parent != null) GameObjectUtility.SetParentAndAlign(go, parent);
            else
            {
                var view = SceneView.lastActiveSceneView;
                go.transform.position = view != null ? view.pivot : Vector3.zero;
            }

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.5f;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            Object.DestroyImmediate(cube.GetComponent<Collider>()); // the trigger is on the root
            cube.transform.SetParent(go.transform, false);
            cube.transform.localPosition = Vector3.up * 1f;
            cube.transform.localScale = Vector3.one * 0.6f;

            go.AddComponent<Collectible>();
            var so = new SerializedObject(go.GetComponent<Collectible>());
            so.FindProperty("mesh").objectReferenceValue = cube.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(go, "Create Collectible");
            Selection.activeGameObject = go;
        }
    }
}
