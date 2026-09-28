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
    /// glance in play mode. The rest of the set is the same one the runner
    /// scene uses, so both games' scenes read alike: ===ENV=== for the baked
    /// city and the world, ===CAMERAS=== for the chase rig and its
    /// by-name siblings, ===UI=== for the HUD screens, ===LIGHTING=== for the
    /// lights and volume with the full-screen filter drivers under its
    /// <c>Filters</c> child. Every header is forced back to the origin
    /// (identity rotation, unit scale) each time it is fetched: a header
    /// someone nudged in the inspector would otherwise offset every spawn
    /// pose, wrap teleport and road-graph lookup under it, silently, since
    /// the spawners set WORLD poses and then parent. Find-or-create, so an
    /// older scene without the headers grows them at play; SceneSystemsPlacer
    /// creates the same ones in edit mode so they are there before play, as
    /// PF_ prefab instances keeping these names (CarTestHierarchyTidier is
    /// what authored them) — which is why nothing may look a header up by
    /// anything but its name.
    /// </summary>
    public static class SceneHierarchy
    {
        public const string SystemsName = "===SYSTEMS===";
        public const string PlayerName = "===PLAYER===";
        public const string NpcName = "===NPC===";
        public const string PoliceName = "==Police==";
        public const string TrafficName = "==TrafficNPC==";
        public const string EnvName = "===ENV===";
        public const string CamerasName = "===CAMERAS===";
        public const string UiName = "===UI===";
        public const string LightingName = "===LIGHTING===";
        public const string FiltersName = "Filters";

        public static Transform Systems(Scene scene) => Root(scene, SystemsName);
        public static Transform Player(Scene scene) => Root(scene, PlayerName);
        public static Transform Npc(Scene scene) => Root(scene, NpcName);
        public static Transform Police(Scene scene) => Child(Npc(scene), PoliceName);
        public static Transform Traffic(Scene scene) => Child(Npc(scene), TrafficName);
        public static Transform Env(Scene scene) => Root(scene, EnvName);
        public static Transform Cameras(Scene scene) => Root(scene, CamerasName);
        public static Transform Ui(Scene scene) => Root(scene, UiName);
        public static Transform Lighting(Scene scene) => Root(scene, LightingName);

        /// <summary>
        /// The full-screen filter drivers' folder, under ===LIGHTING=== — the
        /// runner keeps its SpeedLines/CrtScreen/PsxLook/DistanceFog there and
        /// the city does the same, so a driver is never a loose scene root.
        /// </summary>
        public static Transform Filters(Scene scene) => Child(Lighting(scene), FiltersName);

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

        /// <summary>A named folder under <paramref name="parent"/>, created when missing, at the origin.</summary>
        public static Transform Child(Transform parent, string name)
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
