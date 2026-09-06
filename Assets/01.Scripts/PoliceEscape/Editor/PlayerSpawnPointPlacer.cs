using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// GameObject → Police Escape → Player Spawn Point: drops a
    /// <see cref="PlayerSpawnPoint"/> at the scene view's pivot with a unique
    /// id. Under a selected object it parents there; with nothing selected it
    /// goes straight under the city's AdditionalItems socket (the rebake-proof
    /// home — in the scene or in the open prefab stage), and warns when no
    /// socket could be found so a point at the scene root is never mistaken
    /// for a persistent one.
    /// </summary>
    public static class PlayerSpawnPointPlacer
    {
        [MenuItem("GameObject/Police Escape/Player Spawn Point", false, 10)]
        static void Create(MenuCommand command)
        {
            var go = new GameObject("PlayerSpawnPoint");
            var parent = command.context as GameObject;
            var view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;

            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(go, parent);
            }
            else
            {
                Transform socket = FindAdditionalItemsSocket();
                if (socket != null) go.transform.SetParent(socket, false);
                else Debug.LogWarning("PlayerSpawnPoint placed at the scene root — move it under the city's AdditionalItems socket or the next bake will not keep it.", go);
                go.transform.position = pivot;
                go.transform.rotation = Quaternion.identity;
            }

            var point = go.AddComponent<PlayerSpawnPoint>();
            var so = new SerializedObject(point);
            so.FindProperty("id").stringValue = UniqueId(point);
            so.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(go, "Create Player Spawn Point");
            Selection.activeGameObject = go;
        }

        // The prefab stage's root when City.prefab is open in isolation, else the scene's CityRoot.
        static Transform FindAdditionalItemsSocket()
        {
            CityRoot root = null;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                root = stage.prefabContentsRoot.GetComponentInChildren<CityRoot>(true);
            if (root == null)
                root = Object.FindAnyObjectByType<CityRoot>(FindObjectsInactive.Include);
            return root != null ? root.additionalItems : null;
        }

        // spawn, spawn_2, spawn_3 … against every point already in the scene or stage.
        static string UniqueId(PlayerSpawnPoint created)
        {
            var taken = new HashSet<string>();
            foreach (var point in Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (point != created && !string.IsNullOrWhiteSpace(point.Id)) taken.Add(point.Id.Trim());
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                foreach (var point in stage.prefabContentsRoot.GetComponentsInChildren<PlayerSpawnPoint>(true))
                    if (point != created && !string.IsNullOrWhiteSpace(point.Id)) taken.Add(point.Id.Trim());

            const string stem = "spawn";
            if (!taken.Contains(stem)) return stem;
            for (int n = 2; ; n++)
            {
                string candidate = $"{stem}_{n}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
