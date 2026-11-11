using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ConfusedGameDev.FiniteRunner.Livery;

namespace ConfusedGameDev.FiniteRunner.EditorTools.UnrealExport
{
    /// <summary>
    /// The reference half of the Unreal port's asset gallery (UnrealPortPRD M1). For every model the
    /// manifest marks <c>Port</c>, finds the materials the runner actually renders it with (the first
    /// prefab under <c>Assets/03.Prefabs</c> or <c>Assets/04.Data</c> that uses one of its meshes; the
    /// model's own import materials otherwise, or an entry of <see cref="materialOverrides"/> for pieces
    /// the runner dresses in code; a <see cref="VehicleLivery"/>'s slot overrides and shipped paint win, as
    /// at runtime), renders it from a fixed three-quarter view and writes into the Unreal
    /// repo's <c>Import</c> folder:
    /// <list type="bullet">
    /// <item><b>Captures/&lt;Model&gt;.png</b> — the Unity render.</item>
    /// <item><b>Gallery.json</b> — per model: the FBX path, the capture, the view direction and, per
    /// renderer, the Unity material paths, which the Unreal gallery script maps to its instances.</item>
    /// </list>
    /// Nothing in the project is changed (models are instantiated in a throwaway scene).
    /// Menu: Tools ▸ Unreal Export ▸ Capture Gallery. Batch (needs graphics, so no <c>-nographics</c>):
    /// <c>-executeMethod ConfusedGameDev.FiniteRunner.EditorTools.UnrealExport.UnrealGalleryCapture.CaptureAll</c>.
    /// </summary>
    public static class UnrealGalleryCapture
    {
        const string DefaultOutput = "../SpaceFiniteRunnerUE/SpaceFiniteRunnerUE/Import";
        const int Size = 512;
        static readonly string[] prefabRoots = { "Assets/03.Prefabs", "Assets/04.Data" };

        /// <summary>View direction from the model's centre towards the camera, in Unity axes (front-right, above).</summary>
        static readonly Vector3 viewDirection = new Vector3(0.9f, 0.55f, 1.2f).normalized;

        /// <summary>Models the runner dresses in code (TrackDecorator), keyed by FBX file name.</summary>
        static readonly Dictionary<string, string[]> materialOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["road-straight_v1.fbx"] = new[] { "Assets/02.Art/02.Materials/FiniteRunner/NeonRoad_Mat.mat" },
            ["road-straight-barrier.fbx"] = new[] { "Assets/02.Art/02.Materials/FiniteRunner/NeonBarrier_Mat.mat" },
            ["road-straight.fbx"] = new[] { "Assets/02.Art/02.Materials/FiniteRunner/NeonTube_Mat.mat" },
        };
        // PF_Track's roadMaterialOverride / barrierMaterialOverride / tubeMaterialOverride, applied by TrackDecorator
        // to RoadSlab (road-straight_v1), barrierPrefab and tubeRoadPrefab respectively.

        [MenuItem("Tools/Unreal Export/Capture Gallery")]
        public static void CaptureAll()
        {
            string output = ResolveOutput();
            string captures = Path.Combine(output, "Captures");
            Directory.CreateDirectory(captures);

            List<string> models = ReadPortModels(Path.Combine(output, "AssetManifest.csv"));
            Dictionary<Mesh, Material[]> usage = CollectPrefabUsage();

            var json = new StringBuilder();
            json.Append("{\n  \"view\": ").Append(Vec(viewDirection)).Append(",\n  \"models\": [\n");

            // A throwaway single scene; the open scenes are put back afterwards.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var (camera, target) = BuildStage(scene);
                for (int i = 0; i < models.Count; i++)
                {
                    string path = models[i];
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null) { Debug.LogWarning($"[UnrealExport] not a model: {path}"); continue; }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var renderers = ApplyRuntimeMaterials(instance, path, usage);

                    string file = Path.GetFileNameWithoutExtension(path) + ".png";
                    Bounds bounds = Frame(camera, instance);
                    Render(camera, target, Path.Combine(captures, file));

                    json.Append("    {\"fbx\": \"").Append(path).Append("\", \"capture\": \"Captures/").Append(file)
                        .Append("\", \"boundsCenter\": ").Append(Vec(bounds.center))
                        .Append(", \"boundsSize\": ").Append(Vec(bounds.size)).Append(", \"renderers\": [");
                    json.Append(string.Join(", ", renderers.Select(r =>
                        "{\"mesh\": \"" + r.mesh + "\", \"localCenter\": " + Vec(r.localBounds.center) +
                        ", \"localSize\": " + Vec(r.localBounds.size) +
                        ", \"sourceMaterials\": [" + string.Join(", ", r.sourceMaterials.Select(m => "\"" + m + "\"")) +
                        "], \"materials\": [" + string.Join(", ", r.materials.Select(m => "\"" + m + "\"")) + "]}")));
                    json.Append("]}").Append(i + 1 < models.Count ? ",\n" : "\n");

                    UnityEngine.Object.DestroyImmediate(instance);
                }
                UnityEngine.Object.DestroyImmediate(target);
            }
            finally
            {
                if (previousSetup.Length > 0 && previousSetup.Any(s => !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }

            json.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(output, "Gallery.json"), json.ToString(), new UTF8Encoding(false));
            Debug.Log($"[UnrealExport] {models.Count} gallery captures written to {captures}");
        }

        static string ResolveOutput()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-unrealExportDir");
            string output = Path.GetFullPath(at >= 0 && at + 1 < args.Length ? args[at + 1] : DefaultOutput);
            Directory.CreateDirectory(output);
            return output;
        }

        static List<string> ReadPortModels(string manifest)
        {
            var models = new List<string>();
            foreach (string line in File.ReadLines(manifest).Skip(1))
            {
                // Guid,UnityPath,AssetType,Ext,SizeBytes,Action,... (no field before Action holds a comma)
                string[] cells = line.Split(',');
                if (cells.Length > 5 && cells[3].Equals(".fbx", StringComparison.OrdinalIgnoreCase) && cells[5] == "Port")
                    models.Add(cells[1]);
            }
            return models;
        }

        /// <summary>A <see cref="VehicleLivery"/>'s runtime dress for one mesh: its slot overrides and the shipped paint.</summary>
        struct LiveryInfo
        {
            public Material[] overrides;
            public VehicleColorProfile profile;
            public string vehicleId;
        }

        static readonly Dictionary<Mesh, LiveryInfo> liveries = new();

        /// <summary>
        /// Every mesh a prefab renders, with the materials that prefab gives it (first prefab wins, by path).
        /// A <see cref="VehicleLivery"/> on the prefab wins over the renderer's own materials, as at runtime:
        /// its slot overrides go on the first renderer under its model with that many slots (VehiclePaintTarget).
        /// </summary>
        static Dictionary<Mesh, Material[]> CollectPrefabUsage()
        {
            var usage = new Dictionary<Mesh, Material[]>();
            liveries.Clear();
            var paths = AssetDatabase.FindAssets("t:Prefab", prefabRoots).Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (var livery in prefab.GetComponentsInChildren<VehicleLivery>(true))
                    CollectLivery(livery);
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || renderer == null || usage.ContainsKey(filter.sharedMesh)) continue;
                    if (renderer.sharedMaterials.All(m => m == null)) continue;
                    usage[filter.sharedMesh] = renderer.sharedMaterials;
                }
            }
            return usage;
        }

        static void CollectLivery(VehicleLivery livery)
        {
            var so = new SerializedObject(livery);
            var list = so.FindProperty("slotOverrides");
            if (list == null || list.arraySize == 0) return;
            var overrides = new Material[list.arraySize];
            for (int i = 0; i < overrides.Length; i++)
                overrides[i] = list.GetArrayElementAtIndex(i).objectReferenceValue as Material;
            var model = so.FindProperty("model").objectReferenceValue as Transform;
            Transform root = model != null ? model : livery.transform;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || renderer.sharedMaterials.Length != overrides.Length) continue;
                if (!liveries.ContainsKey(filter.sharedMesh))
                    liveries[filter.sharedMesh] = new LiveryInfo
                    {
                        overrides = overrides,
                        profile = so.FindProperty("profile").objectReferenceValue as VehicleColorProfile,
                        vehicleId = so.FindProperty("vehicleId").stringValue,
                    };
                break;
            }
        }

        /// <summary>
        /// Paints <paramref name="renderer"/> as a fresh install shows it: the profile asset's own colours
        /// (VehicleColorProfile's fallback data), never the local save in persistentDataPath.
        /// </summary>
        static void PaintAsShipped(MeshRenderer renderer, LiveryInfo livery)
        {
            if (livery.profile == null) return;
            var field = typeof(VehicleColorProfile).GetField("data",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(livery.profile) is not VehicleColorData shipped) return;
            var target = renderer.gameObject.AddComponent<VehiclePaintTarget>();
            target.Initialize(null, livery.profile);
            shipped.ApplyTo(target, livery.vehicleId);
        }

        struct RendererInfo
        {
            public string mesh;
            /// <summary>The FBX's own material name per submesh (what Unreal names the slot).</summary>
            public string[] sourceMaterials;
            /// <summary>The material the runner renders each submesh with.</summary>
            public string[] materials;
            public Bounds localBounds;
        }

        static List<RendererInfo> ApplyRuntimeMaterials(GameObject instance, string fbxPath, Dictionary<Mesh, Material[]> usage)
        {
            var result = new List<RendererInfo>();
            materialOverrides.TryGetValue(Path.GetFileName(fbxPath), out string[] overridePaths);

            // Remapped materials -> the FBX material name they replace.
            var sourceNames = new Dictionary<UnityEngine.Object, string>();
            if (AssetImporter.GetAtPath(fbxPath) is ModelImporter importer)
                foreach (var pair in importer.GetExternalObjectMap())
                    if (pair.Key.type == typeof(Material) && pair.Value != null)
                        sourceNames[pair.Value] = pair.Key.name;

            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                string[] source = renderer.sharedMaterials
                    .Select(m => m == null ? "" : sourceNames.TryGetValue(m, out string n) ? n : m.name).ToArray();
                bool hasLivery = liveries.TryGetValue(filter.sharedMesh, out LiveryInfo livery);
                if (overridePaths != null)
                    renderer.sharedMaterials = overridePaths.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
                else if (hasLivery)
                    renderer.sharedMaterials = livery.overrides
                        .Select((m, i) => m != null ? m : renderer.sharedMaterials[i]).ToArray();
                else if (usage.TryGetValue(filter.sharedMesh, out var mats))
                    renderer.sharedMaterials = mats;
                // Paths before painting: the paint swaps in unsaved material clones.
                string[] materialPaths = renderer.sharedMaterials
                    .Select(m => m != null ? AssetDatabase.GetAssetPath(m) : "").ToArray();
                if (hasLivery) PaintAsShipped(renderer, livery);
                result.Add(new RendererInfo
                {
                    mesh = filter.sharedMesh.name,
                    sourceMaterials = source,
                    localBounds = filter.sharedMesh.bounds,
                    materials = materialPaths,
                });
            }
            return result;
        }

        static (Camera, RenderTexture) BuildStage(UnityEngine.SceneManagement.Scene scene)
        {
            var camGo = new GameObject("GalleryCamera");
            SceneManager_Move(camGo, scene);
            var camera = camGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);
            camera.fieldOfView = 30f;
            camera.enabled = false;

            var lightGo = new GameObject("GalleryLight");
            SceneManager_Move(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.38f);

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            return (camera, target);
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        static Bounds Frame(Camera camera, GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            camera.transform.position = bounds.center + viewDirection * distance;
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 2f;
            return bounds;
        }

        static void Render(Camera camera, RenderTexture target, string file)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(file, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        static string Vec(Vector3 v) =>
            FormattableString.Invariant($"[{v.x:0.#####}, {v.y:0.#####}, {v.z:0.#####}]");
    }
}
