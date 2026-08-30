using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// The city chase's hierarchy headers, and the one rule about them: they
    /// are FOLDERS, never frames. Everything the game spawns at play is
    /// parented under a header — ===SYSTEMS=== for scene-lifetime objects
    /// that only exist at runtime (the mission brief, the EVP ground
    /// effects), ===PLAYER=== for the car, ===NPC=== with ==Police== /
    /// ==TrafficNPC== under it for the fleets — so the hierarchy reads at a
    /// glance in play mode. Every header is forced back to the origin
    /// (identity rotation, unit scale) each time it is fetched: a header
    /// someone nudged in the inspector would otherwise offset every spawn
    /// pose, wrap teleport and road-graph lookup under it, silently, since
    /// the spawners set WORLD poses and then parent. Find-or-create, so an
    /// older scene without the headers grows them at play; SceneSystemsPlacer
    /// creates the same ones in edit mode so they are there before play.
    /// </summary>
    public static class SceneHierarchy
    {
        public const string SystemsName = "===SYSTEMS===";
        public const string PlayerName = "===PLAYER===";
        public const string NpcName = "===NPC===";
        public const string PoliceName = "==Police==";
        public const string TrafficName = "==TrafficNPC==";

        public static Transform Systems(Scene scene) => Root(scene, SystemsName);
        public static Transform Player(Scene scene) => Root(scene, PlayerName);
        public static Transform Npc(Scene scene) => Root(scene, NpcName);
        public static Transform Police(Scene scene) => Child(Npc(scene), PoliceName);
        public static Transform Traffic(Scene scene) => Child(Npc(scene), TrafficName);

        /// <summary>
        /// Parent a spawned object under a header. The world pose is kept by
        /// default (the header sits at the origin, so local equals world
        /// anyway — but a rigidbody's pose must never be re-derived); pass
        /// false for UI roots, which have no world pose worth keeping.
        /// </summary>
        public static void Adopt(GameObject go, Transform header, bool worldPositionStays = true)
        {
            if (go == null || header == null) return;
            go.transform.SetParent(header, worldPositionStays);
        }

        /// <summary>A root header of the scene — created in that scene when missing — at the origin.</summary>
        public static Transform Root(Scene scene, string name)
        {
            if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return AtOrigin(root.transform);

            var header = new GameObject(name);
            if (header.scene != scene) SceneManager.MoveGameObjectToScene(header, scene);
            return AtOrigin(header.transform);
        }

        static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                child = new GameObject(name).transform;
                child.SetParent(parent, false);
            }
            return AtOrigin(child);
        }

        /// <summary>Folders, not frames: a header found off the origin is put back, with a warning naming it.</summary>
        public static Transform AtOrigin(Transform header)
        {
            if (header.localPosition != Vector3.zero || header.localRotation != Quaternion.identity || header.localScale != Vector3.one)
            {
                Debug.LogWarning($"SceneHierarchy: header '{header.name}' was off the origin — reset, so nothing under it inherits an offset.", header);
                header.localPosition = Vector3.zero;
                header.localRotation = Quaternion.identity;
                header.localScale = Vector3.one;
            }
            return header;
        }
    }
}
