using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Audio;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Stats;
using ConfusedGameDev.FiniteRunner.PoliceEscape.UI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using ConfusedGameDev.FiniteRunner.Screens;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Tools → Police Escape → Tidy City Scene Hierarchy: puts the open city
    /// scene into the shape the runner scene already has — the seven
    /// <see cref="SceneHierarchy"/> headers at the scene root, every object
    /// directly under one of them its own nested <c>PF_</c> prefab in
    /// 03.Prefabs/PoliceEscape, and the header instances keeping their
    /// <c>===…===</c> names (headers and the camera rig's siblings are found BY
    /// NAME, so the prefab ASSET is what carries the PF_ name, never the
    /// instance).
    /// <para>
    /// It also does the cleanup the shape depends on: the baked
    /// <c>MinimapCanvas</c>/<c>SpeedometerCanvas</c> trees go (they are rebuilt
    /// on play, and a prefab instance may not DestroyImmediate children that came
    /// from its asset, which would break their Rebuild Preview buttons), every
    /// nudged header is put back on the origin WITHOUT moving what hangs off it,
    /// the logic-only managers lose the compensating offsets that came with them,
    /// the loose full-screen filter drivers move under ===LIGHTING===/Filters,
    /// and the empty ===Audio=== / ==Managers== / ==Graphics== folders are
    /// dropped.
    /// </para>
    /// <para>
    /// Two things are deliberately NOT nested. The baked City prefab instance
    /// stays a SCENE-level child of the ===ENV=== instance, because its material
    /// overrides and its hand-placed <c>AdditionalItems</c> content are scene
    /// modifications and belong nowhere near PF_Env.prefab (and CityBaker's
    /// persistence mechanism reads them off City.prefab). And
    /// <see cref="CityManager.cityRoot"/> is re-asserted afterwards as an
    /// instance override, since a prefab asset cannot store a scene reference —
    /// left alone it nulls, and CityManager's FindAnyObjectByType fallback then
    /// covers for the lost wiring silently.
    /// </para>
    /// Idempotent: an object that is already an instance of its target prefab is
    /// left alone, so re-running after hand-edits only picks up what drifted.
    /// The scene is marked dirty, never saved — look at it first.
    /// </summary>
    public static class CarTestHierarchyTidier
    {
        const string CityPrefabFolder = "Assets/03.Prefabs/PoliceEscape";
        const string RunnerPrefabFolder = "Assets/03.Prefabs/FiniteRunner";

        static int reparented;
        static int deleted;
        static int saved;

        [MenuItem("Tools/Police Escape/Tidy City Scene Hierarchy")]
        public static void TidyOpenScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("CarTestHierarchyTidier: tidy in edit mode — in play mode the runtime has already spawned per-run objects under the headers.");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (Object.FindAnyObjectByType<CityManager>(FindObjectsInactive.Include) == null)
            {
                Debug.LogWarning($"CarTestHierarchyTidier: '{scene.name}' has no CityManager — this is the city chase's tool, run it on CarTest.");
                return;
            }
            reparented = deleted = saved = 0;

            // Renames FIRST: everything below finds headers by name, and the old
            // scene spelled the lighting header with lower case.
            RenameRoot(scene, "===Lighting===", SceneHierarchy.LightingName);
            RenameRoot(scene, "===Cameras===", SceneHierarchy.CamerasName);
            RenameRoot(scene, "===Ui===", SceneHierarchy.UiName);
            RenameRoot(scene, "===Env===", SceneHierarchy.EnvName);

            PutHeadersBackOnTheOrigin(scene);
            StripBakedHud();
            DropDeadLeftovers(scene);
            TidyCityInstanceContents(scene);

            Transform systems = SceneHierarchy.Systems(scene);
            Transform player = SceneHierarchy.Player(scene);
            Transform npc = SceneHierarchy.Npc(scene);
            SceneHierarchy.Police(scene);
            SceneHierarchy.Traffic(scene);
            Transform env = SceneHierarchy.Env(scene);
            Transform cameras = SceneHierarchy.Cameras(scene);
            Transform ui = SceneHierarchy.Ui(scene);
            Transform lighting = SceneHierarchy.Lighting(scene);
            Transform filters = SceneHierarchy.Filters(scene);

            // The City instance is parked LAST: it must not be inside ===ENV===
            // while PF_Env is saved, or its overrides land in the asset.
            GameObject city = FindCityInstance();
            if (city != null) city.transform.SetParent(null, true);

            Park<CityManager>(systems);
            Park<LevelManager>(systems);
            Park<PlayerCarSpawner>(systems, keepPose: true);
            Park<PatrolManager>(systems);
            Park<TrafficManager>(systems);
            Park<CinemaSystem>(systems);
            Park<RadioSystem>(systems);
            Park<CityStatsRecorder>(systems);
            Park<CollectibleManager>(systems);
            Park<EventSystem>(systems);

            Park<Minimap>(ui);
            Park<Speedometer>(ui);
            Park<CityMapScreen>(ui);
            Park<PauseMenu>(ui);
            Park<MoneyHud>(ui);

            ParkByName(scene, "Ground", env, keepPose: true);
            Park<RainSystem>(env);

            ParkMainCamera(cameras);
            Park<OrbitCameraRig>(cameras);
            EnsureRigSibling(scene, cameras, OrbitCameraRig.FirstPersonName);
            EnsureRigSibling(scene, cameras, OrbitCameraRig.CinematicName);

            ParkByName(scene, "Directional Light", lighting, keepPose: true);
            ParkByName(scene, "GlobalVolume", lighting);
            ParkByName(scene, "Global Volume", lighting);

            Park<GlitchController>(filters);
            Park<DistanceFog>(filters);
            Park<SpeedMotionBlur>(filters);
            Park<PsxLook>(filters);
            Park<VhsTape>(filters);
            Park<CrtScreen>(filters);

            DropEmptyFolder(scene, "===Audio===");
            DropEmptyFolder(scene, "==Managers==");
            DropEmptyFolder(scene, "==Graphics==");

            OrderRoots(systems, player, npc, env, cameras, ui, lighting);

            // --- prefabs, bottom-up: leaves, then Filters, then the headers ---
            SaveLeaf<CityManager>("CityManager");
            SaveLeaf<LevelManager>("LevelManager");
            SaveLeaf<PlayerCarSpawner>("PlayerCarSpawner");
            SaveLeaf<PatrolManager>("PatrolManager");
            SaveLeaf<TrafficManager>("TrafficManager");
            SaveLeaf<CinemaSystem>("CinemaSystem");
            SaveLeaf<RadioSystem>("Radio");
            SaveLeaf<CityStatsRecorder>("StatsRecorder");
            // Config-free and identical to the runner's: link those, don't fork them.
            ReuseRunnerPrefab<CollectibleManager>("PF_CollectibleManager", systems);
            ReuseRunnerPrefab<EventSystem>("PF_EventSystem", systems);
            ReuseRunnerPrefab<MoneyHud>("PF_MoneyHud", ui);

            SaveLeaf<Minimap>("Minimap");
            SaveLeaf<Speedometer>("Speedometer");
            SaveLeaf<CityMapScreen>("CityMap");
            SaveLeaf<PauseMenu>("PauseMenu");

            SaveLeafByName(scene, "Main Camera", "MainCamera");
            SaveLeaf<OrbitCameraRig>("OrbitCameraRig");
            SaveLeafByName(scene, OrbitCameraRig.FirstPersonName, "FirstPersonCamera");
            SaveLeafByName(scene, OrbitCameraRig.CinematicName, "CinematicCamera");

            SaveLeafByName(scene, "Ground", "Ground");
            SaveLeaf<RainSystem>("RainSystem");

            SaveLeafByName(scene, "Directional Light", "DirectionalLight");
            SaveLeafByName(scene, "GlobalVolume", "GlobalVolume");

            SaveLeaf<GlitchController>("GlitchController");
            SaveLeaf<DistanceFog>("DistanceFog");
            SaveLeaf<SpeedMotionBlur>("SpeedMotionBlur");
            SaveLeaf<PsxLook>("PsxLook");
            SaveLeaf<VhsTape>("VhsTape");
            SaveLeaf<CrtScreen>("CrtScreen");

            SaveHeader(filters, "Filters");

            SaveHeader(systems, "Systems");
            SaveHeader(player, "Player");
            SaveHeader(npc, "Npc");
            SaveHeader(env, "Env");
            SaveHeader(cameras, "Cameras");
            SaveHeader(ui, "UI");
            SaveHeader(lighting, "Lighting");

            // The City goes back under the (now prefab-instance) ===ENV===, as an
            // added scene object, and CityManager is pointed at it again.
            if (city != null)
            {
                SceneHierarchy.Adopt(city, SceneHierarchy.Env(scene));
                city.transform.SetSiblingIndex(0);
                RewireCityRoot(city);
            }

            OrderRoots(systems, player, npc, env, cameras, ui, lighting);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"CarTestHierarchyTidier: '{scene.name}' tidied — {reparented} object(s) re-parented, {deleted} deleted, {saved} prefab(s) written. SAVE THE SCENE to keep it.");
        }

        // ------------------------------------------------------------ cleanup

        /// <summary>
        /// Every <c>==…==</c> root back to identity, moving nothing under it: the
        /// children are detached and re-adopted keeping their WORLD pose, so a
        /// header someone dragged (and the compensating offsets its children
        /// grew) resolves instead of teleporting the scene.
        /// </summary>
        static void PutHeadersBackOnTheOrigin(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name.StartsWith("==")) ZeroKeepingChildren(root.transform);
        }

        static void ZeroKeepingChildren(Transform folder)
        {
            if (folder == null) return;
            if (folder.localPosition == Vector3.zero && folder.localRotation == Quaternion.identity && folder.localScale == Vector3.one)
                return;

            var children = new List<Transform>(folder.childCount);
            for (int i = 0; i < folder.childCount; i++) children.Add(folder.GetChild(i));
            Transform parent = folder.parent;
            foreach (Transform child in children) child.SetParent(parent, true);
            folder.localPosition = Vector3.zero;
            folder.localRotation = Quaternion.identity;
            folder.localScale = Vector3.one;
            foreach (Transform child in children) child.SetParent(folder, true);
        }

        /// <summary>
        /// The baked HUD trees under Minimap and Speedometer. Both are built at
        /// runtime (and torn down first), so serializing them only bloats the
        /// scene — and once their owner is a prefab instance, asset-owned
        /// children could no longer be DestroyImmediate'd by Rebuild Preview.
        /// </summary>
        static void StripBakedHud()
        {
            StripChildren<Minimap>();
            StripChildren<Speedometer>();
            WarnIfChildren<CityMapScreen>();
            WarnIfChildren<MoneyHud>();
            WarnIfChildren<PauseMenu>();
        }

        static void StripChildren<T>() where T : Component
        {
            var owner = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (owner == null) return;
            for (int i = owner.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = owner.transform.GetChild(i).gameObject;
                // On a re-run the owner is already a prefab instance: a child that
                // came from the ASSET cannot be destroyed here, and the asset was
                // written without one anyway.
                if (PrefabUtility.IsPartOfPrefabInstance(child) && !PrefabUtility.IsAddedGameObjectOverride(child)) continue;
                Object.DestroyImmediate(child);
                deleted++;
            }
        }

        static void WarnIfChildren<T>() where T : Component
        {
            var owner = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (owner != null && owner.transform.childCount > 0)
                Debug.LogWarning($"CarTestHierarchyTidier: '{owner.name}' has {owner.transform.childCount} baked child object(s) that it rebuilds at play — kept, but they are scene bloat.", owner);
        }

        /// <summary>
        /// The leftovers: the two raw showroom .fbx cars parked on top of each
        /// other, the disabled CinemaTest trigger, and the bare KeyCard model
        /// duplicating what Collectible_KeyCard.prefab already carries.
        /// </summary>
        static void DropDeadLeftovers(Scene scene)
        {
            Delete(FindByName(scene, "CP_Quadron"));
            Delete(FindByName(scene, "CP_Minivan"));
            Delete(FindByName(scene, "CinemaTest"));

            Transform objective = FindByName(scene, "testObjective") ?? FindByName(scene, "TestObjective");
            if (objective != null) Delete(objective.Find("KeyCard"));
        }

        static void TidyCityInstanceContents(Scene scene)
        {
            // A folder that got dragged kilometres away from the trigger inside it.
            ZeroKeepingChildren(FindByName(scene, "Optional Objectives Trigger"));

            Transform objective = FindByName(scene, "testObjective");
            if (objective != null) objective.name = "TestObjective";
        }

        // ----------------------------------------------------------- parking

        static T Park<T>(Transform header, bool keepPose = false) where T : Component
        {
            var component = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (component == null) return null;
            ParkTransform(component.transform, header, keepPose);
            return component;
        }

        static void ParkByName(Scene scene, string name, Transform header, bool keepPose = false)
        {
            Transform found = FindByName(scene, name);
            if (found != null) ParkTransform(found, header, keepPose);
        }

        /// <summary>
        /// Main Camera keeps its overhead edit-mode vantage — the rig takes the
        /// picture over at play, but that pose is how the scene opens.
        /// </summary>
        static void ParkMainCamera(Transform cameras)
        {
            Camera camera = Camera.main;
            if (camera != null) ParkTransform(camera.transform, cameras, keepPose: true);
        }

        static void ParkTransform(Transform target, Transform header, bool keepPose)
        {
            if (target == null || header == null) return;
            if (target.parent != header)
            {
                target.SetParent(header, true);
                reparented++;
            }
            // Logic-only objects: drop whatever offset they inherited from a
            // nudged header. A pose-carrying one (the spawn anchor, the camera,
            // the ground slab) keeps every component of its transform.
            if (keepPose) return;
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        /// <summary>
        /// The rig's first-person and cinematic vcams are found BY NAME under the
        /// rig's own parent (OrbitCameraRig.FindPrePlacedSibling), so they must be
        /// its siblings — an empty object is all the rig needs, and pre-placing
        /// the cinematic one is what stops it adding a child to the ===CAMERAS===
        /// prefab instance on every play.
        /// </summary>
        static void EnsureRigSibling(Scene scene, Transform cameras, string name)
        {
            if (cameras == null) return;
            if (cameras.Find(name) != null) return;
            Transform stray = FindByName(scene, name);
            if (stray != null) { ParkTransform(stray, cameras, keepPose: false); return; }
            var go = new GameObject(name);
            go.transform.SetParent(cameras, false);
            reparented++;
        }

        static void DropEmptyFolder(Scene scene, string name)
        {
            Transform folder = FindByName(scene, name);
            if (folder == null) return;
            if (folder.childCount > 0)
            {
                Debug.LogWarning($"CarTestHierarchyTidier: '{name}' still has {folder.childCount} child object(s) — kept, move them yourself and re-run.", folder);
                return;
            }
            Delete(folder);
        }

        static void OrderRoots(params Transform[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
                if (headers[i] != null) headers[i].SetSiblingIndex(i);
        }

        // ----------------------------------------------------------- prefabs

        static void SaveLeaf<T>(string assetName) where T : Component
        {
            var component = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (component != null) SaveAsPrefab(component.gameObject, assetName);
        }

        static void SaveLeafByName(Scene scene, string name, string assetName)
        {
            Transform found = FindByName(scene, name);
            if (found != null) SaveAsPrefab(found.gameObject, assetName);
        }

        static void SaveHeader(Transform header, string assetName)
        {
            if (header != null) SaveAsPrefab(header.gameObject, assetName);
        }

        /// <summary>
        /// Save one object as <c>PF_&lt;assetName&gt;</c> and reconnect it, then put
        /// its scene name back: the ASSET root carries the PF_ name, the instance
        /// keeps the name the scene (and every by-name lookup) knows it by.
        /// Children that are already prefab instances stay nested rather than
        /// being inlined, which is why this runs bottom-up.
        /// </summary>
        static void SaveAsPrefab(GameObject go, string assetName)
        {
            string path = $"{CityPrefabFolder}/PF_{assetName}.prefab";
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == path)
                return;

            string sceneName = go.name;
            go.name = "PF_" + assetName;
            GameObject asset = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
            go.name = sceneName;
            if (asset == null)
            {
                Debug.LogError($"CarTestHierarchyTidier: could not write {path}.", go);
                return;
            }
            saved++;
        }

        /// <summary>
        /// Swap a config-free object for an instance of the runner's existing
        /// prefab — there is nothing on these to diverge, so the city links the
        /// same asset instead of forking a second copy of it.
        /// </summary>
        static void ReuseRunnerPrefab<T>(string assetName, Transform header) where T : Component
        {
            var component = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (component == null || header == null) return;
            string path = $"{RunnerPrefabFolder}/{assetName}.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"CarTestHierarchyTidier: {path} is missing — '{component.name}' left as a plain object.", component);
                return;
            }
            GameObject old = component.gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(old) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old) == path)
                return;

            string sceneName = old.name;
            int index = old.transform.GetSiblingIndex();
            Object.DestroyImmediate(old);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, header.gameObject.scene);
            instance.name = sceneName;
            instance.transform.SetParent(header, false);
            instance.transform.SetSiblingIndex(index);
            saved++;
        }

        // -------------------------------------------------------------- city

        /// <summary>The baked city in the scene: the prefab-instance root that owns the CityRoot.</summary>
        static GameObject FindCityInstance()
        {
            var root = Object.FindAnyObjectByType<CityRoot>(FindObjectsInactive.Include);
            return root == null ? null : root.gameObject;
        }

        /// <summary>
        /// CityManager now lives inside PF_Systems.prefab, which cannot store a
        /// reference to a scene object — so the link to the City instance is
        /// written back as an instance override. Without this the field nulls and
        /// CityManager's lazy FindAnyObjectByType fallback hides the loss.
        /// </summary>
        static void RewireCityRoot(GameObject city)
        {
            var manager = Object.FindAnyObjectByType<CityManager>(FindObjectsInactive.Include);
            var root = city.GetComponent<CityRoot>();
            if (manager == null || root == null) return;
            if (manager.cityRoot == root) return;
            manager.cityRoot = root;
            EditorUtility.SetDirty(manager);
        }

        // ----------------------------------------------------------- helpers

        static void RenameRoot(Scene scene, string from, string to)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == from) root.name = to;
        }

        static void Delete(Transform target)
        {
            if (target == null) return;
            Object.DestroyImmediate(target.gameObject);
            deleted++;
        }

        /// <summary>
        /// First transform in the scene with that exact name, inactive ones
        /// included — breadth-first, and it never descends into a
        /// <see cref="CityBlock"/>. The baked city is hundreds of thousands of
        /// transforms; walking it once per lookup would hang the editor, and
        /// nothing this tool moves lives inside a block (the hand-placed content
        /// hangs off the City root's own AdditionalItems socket).
        /// </summary>
        static Transform FindByName(Scene scene, string name)
        {
            var queue = new Queue<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects()) queue.Enqueue(root.transform);
            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                if (current.name == name) return current;
                if (current.GetComponent<CityBlock>() != null) continue;
                for (int i = 0; i < current.childCount; i++) queue.Enqueue(current.GetChild(i));
            }
            return null;
        }
    }
}
