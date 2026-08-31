using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Audio;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.UI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Puts the city chase's scene-lifetime systems into the scene BEFORE
    /// play, so the hierarchy the designer sees is the hierarchy that runs:
    /// the police and traffic managers, the minimap, speedometer and city
    /// map, the speed motion blur, the chase camera rig with its
    /// first-person sibling, the mission cinema system (which builds its
    /// video holders under itself at play) and the EventSystem. Every one of them already
    /// supported being hand-placed (they find the CityManager lazily and
    /// build their canvases / Cinemachine components in Awake or on first
    /// Update); <c>CityManager.Awake</c> kept spawning them only because
    /// nothing put them in the scene. Its "find one, else create" fallback
    /// stays, so an older scene still boots — it just never has to.
    /// What stays runtime-spawned is what is genuinely per run: the player
    /// car and every NPC (their spawn cells come from the live road graph),
    /// the MissionBriefScreen (a modal that destroys itself once accepted)
    /// and the EVP ground-effects rig (exists only under that physics
    /// backend, and its TireMarksRenderer builds its mesh in OnEnable —
    /// placing it in edit mode would bake runtime state into the scene).
    /// Idempotent: only what is missing is created (a DISABLED hand-placed
    /// system counts as present — disabling one is how it is switched off),
    /// each under the "===SYSTEMS===" header, and the play-mode headers
    /// (===PLAYER===, ===NPC=== with ==Police== / ==TrafficNPC==) are
    /// created too so the runtime spawners find them in place — all via
    /// <see cref="SceneHierarchy"/>, which is what keeps every header at
    /// the origin. Re-running on a hand-organised scene changes nothing. Both scene
    /// builders call it after wiring the CityManager; Tools → Police Escape
    /// → Place Scene Systems runs it on the open scene.
    /// </summary>
    public static class SceneSystemsPlacer
    {
        [MenuItem("Tools/Police Escape/Place Scene Systems")]
        public static void PlaceInOpenScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("SceneSystemsPlacer: place the systems in edit mode — in play mode CityManager has already spawned whatever was missing.");
                return;
            }
            var city = Object.FindAnyObjectByType<CityManager>(FindObjectsInactive.Include);
            if (city == null)
            {
                Debug.LogWarning("SceneSystemsPlacer: the open scene has no CityManager to read the systems' settings from.");
                return;
            }
            int placed = PlaceMissing(city);
            if (placed > 0) EditorSceneManager.MarkSceneDirty(city.gameObject.scene);
            Debug.Log($"SceneSystemsPlacer: {placed} object(s) placed in '{city.gameObject.scene.name}' — save the scene to keep them.");
        }

        /// <summary>
        /// Create every scene-lifetime system the manager is wired for and
        /// the scene lacks, wired from the manager's own fields. Returns how
        /// many objects were created.
        /// </summary>
        public static int PlaceMissing(CityManager city)
        {
            Scene scene = city.gameObject.scene;
            int placed = 0;
            foreach (string header in new[] { SceneHierarchy.SystemsName, SceneHierarchy.PlayerName, SceneHierarchy.NpcName })
                if (!HasRoot(scene, header)) placed++;
            Transform parent = SceneHierarchy.Systems(scene);
            SceneHierarchy.Player(scene);
            Transform npc = SceneHierarchy.Npc(scene);
            if (npc.Find(SceneHierarchy.PoliceName) == null) placed++;
            if (npc.Find(SceneHierarchy.TrafficName) == null) placed++;
            SceneHierarchy.Police(scene);
            SceneHierarchy.Traffic(scene);

            if (city.policeCarPrefab != null && city.pursuitSettings != null)
                placed += Place<PatrolManager>("PatrolManager", parent, m =>
                {
                    m.settings = city.pursuitSettings;
                    m.policeCarPrefab = city.policeCarPrefab;
                });
            if (city.trafficSettings != null)
                placed += Place<TrafficManager>("TrafficManager", parent, m => m.settings = city.trafficSettings);
            if (city.minimapSettings != null)
                placed += Place<Minimap>("Minimap", parent, m => m.settings = city.minimapSettings);
            if (city.speedometerSettings != null)
                placed += Place<Speedometer>("Speedometer", parent, s => s.settings = city.speedometerSettings);
            if (city.mapSettings != null)
                placed += Place<CityMapScreen>("CityMap", parent, m => m.settings = city.mapSettings);
            if (city.speedMotionBlur)
                placed += Place<SpeedMotionBlur>("SpeedMotionBlur", parent, null);

            if (city.orbitCameraSettings != null)
            {
                placed += Place<OrbitCameraRig>("OrbitCameraRig", parent, r => r.settings = city.orbitCameraSettings);
                // The first-person vcam must be the rig's SIBLING (see
                // OrbitCameraRig.Build); the rig adds its Cinemachine
                // components in Awake, so an empty object is all it needs.
                var rig = Object.FindAnyObjectByType<OrbitCameraRig>(FindObjectsInactive.Include);
                if (rig != null && rig.FindPrePlacedFirstPerson() == null)
                {
                    var fp = new GameObject(OrbitCameraRig.FirstPersonName);
                    fp.transform.SetParent(rig.transform.parent, false);
                    placed++;
                }
            }

            // The cinema player needs nothing from the CityManager — only its
            // format library, created here if the project has none yet.
            placed += Place<CinemaSystem>("CinemaSystem", parent, c => c.library = CinemaAssetBuilder.CreateOrLoad());

            // The car radio likewise stands alone: its playlist asset is created
            // (and the InGame songs fetched into it) if the project has none.
            placed += Place<RadioSystem>("Radio", parent, r => r.settings = RadioAssetBuilder.CreateOrLoad());

            // Menus poll the EventSystem for mouse input; MenuScreenFactory
            // creates one on demand, so pre-placing it is what keeps that
            // code path idle.
            if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject("EventSystem");
                go.transform.SetParent(parent, false);
                go.AddComponent<EventSystem>();
                go.AddComponent<InputSystemUIInputModule>(); // no actions asset: the module falls back to the default UI actions, as the runtime path did
                placed++;
            }

            return placed;
        }

        static int Place<T>(string name, Transform parent, System.Action<T> wire) where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null) return 0;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            T component = go.AddComponent<T>();
            wire?.Invoke(component);
            return 1;
        }

        static bool HasRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return true;
            return false;
        }
    }
}
