using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools.UnrealExport
{
    /// <summary>
    /// Writes <b>Data/PrefabLayout/&lt;Prefab&gt;.json</b> for every prefab the exported
    /// data references (the orb tiers, the laser system, …): the prefab flattened to a
    /// list of nodes in its ROOT's space, so Unreal can rebuild the picture without a
    /// prefab system. Written in the data format (unityType, guid, fields), keyed on the
    /// prefab's own guid, so a data field pointing at the prefab imports as a reference
    /// to the layout asset.
    /// <list type="bullet">
    /// <item>An FBX model instance is ONE node whose placement is the matrix that puts
    /// the Unreal mesh where Unity draws the model (the M1 import moved each mesh's pivot
    /// to the model's bounds centre, at the model's own root scale).</item>
    /// <item>A built-in mesh (Plane, Cube, Sphere, …) is a node naming it.</item>
    /// <item>Every other transform (ShootPoint, SheetPivot, empties) is a node too, with
    /// its script and collider list, so code that finds children by name still can.</item>
    /// </list>
    /// Positions are metres, Unity axes; the root's own scale is written apart
    /// (<c>rootScale</c>), since Instantiate keeps it.
    /// </summary>
    public static class UnrealPrefabLayout
    {
        public const string UnityType = "PrefabLayout";

        [MenuItem("Tools/Unreal Export/Export Prefab Layouts")]
        public static void ExportMenu()
        {
            string output = Path.GetFullPath("../SpaceFiniteRunnerUE/SpaceFiniteRunnerUE/Import");
            int n = Export(output, CollectReferenced());
            Debug.Log($"[UnrealExport] {n} prefab layouts written to {output}");
        }

        /// <summary>Prefabs referenced directly by the runner data under Assets/04.Data.</summary>
        public static List<string> CollectReferenced(IEnumerable<ScriptableObject> assets = null)
        {
            IEnumerable<string> sources = assets != null
                ? assets.Select(AssetDatabase.GetAssetPath)
                : AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/04.Data" }).Select(AssetDatabase.GUIDToAssetPath);
            var prefabs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in sources)
                foreach (string dep in AssetDatabase.GetDependencies(path, false))
                    if (dep.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) prefabs.Add(dep);
            return prefabs.ToList();
        }

        public static int Export(string output, List<string> prefabs)
        {
            string dir = Path.Combine(output, "Data", UnityType);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            int written = 0;
            foreach (string path in prefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string json = Write(path, root);
                    File.WriteAllText(Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".json"), json, new UTF8Encoding(false));
                    written++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return written;
        }

        static string Write(string path, GameObject root)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"unityType\": \"").Append(UnityType).Append("\",\n");
            sb.Append("  \"name\": ").Append(Quote(Path.GetFileNameWithoutExtension(path))).Append(",\n");
            sb.Append("  \"guid\": \"").Append(AssetDatabase.AssetPathToGUID(path)).Append("\",\n");
            sb.Append("  \"path\": ").Append(Quote(path)).Append(",\n");
            sb.Append("  \"fields\": {\n");
            sb.Append("    \"rootScale\": ").Append(Vec(root.transform.localScale)).Append(",\n");
            sb.Append("    \"rootComponents\": ").Append(Components(root.transform)).Append(",\n");
            sb.Append("    \"nodes\": [");

            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            bool first = true;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                if (InsideModel(t)) continue;

                string modelPath = ModelRootPath(t);
                Matrix4x4 m = toRoot * t.localToWorldMatrix;
                string mesh = "";
                var materials = new List<Material>();
                bool rendered = false;
                if (modelPath != null)
                {
                    m = m * ModelToMesh(modelPath);
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        if (ModelRootPath(r.transform) == modelPath || InsideModelOf(r.transform, t))
                        {
                            materials.AddRange(r.sharedMaterials);
                            rendered |= r.enabled && r.gameObject.activeSelf;
                        }
                }
                else
                {
                    var filter = t.GetComponent<MeshFilter>();
                    var renderer = t.GetComponent<Renderer>();
                    if (filter != null && filter.sharedMesh != null)
                        mesh = AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith("Library/") || AssetDatabase.GetAssetPath(filter.sharedMesh) == ""
                            ? filter.sharedMesh.name : AssetDatabase.GetAssetPath(filter.sharedMesh) + ":" + filter.sharedMesh.name;
                    if (renderer is SpriteRenderer sprite && sprite.sprite != null) mesh = "Sprite:" + sprite.sprite.name;
                    if (renderer != null) { materials.AddRange(renderer.sharedMaterials); rendered = renderer.enabled; }
                }

                Decompose(m, out Vector3 pos, out Quaternion rot, out Vector3 scale);
                sb.Append(first ? "\n" : ",\n");
                first = false;
                sb.Append("      {");
                sb.Append("\"name\": ").Append(Quote(t.name));
                sb.Append(", \"path\": ").Append(Quote(PathOf(t, root.transform)));
                sb.Append(", \"active\": ").Append(ActiveInPrefab(t, root.transform) ? "true" : "false");
                sb.Append(", \"rendered\": ").Append(rendered ? "true" : "false");
                sb.Append(", \"model\": ").Append(modelPath != null ? Ref(AssetDatabase.LoadMainAssetAtPath(modelPath)) : "null");
                sb.Append(", \"mesh\": ").Append(Quote(mesh));
                sb.Append(", \"materials\": [").Append(string.Join(", ", materials.Select(Ref))).Append("]");
                sb.Append(", \"position\": ").Append(Vec(pos));
                sb.Append(", \"rotation\": ").Append(Vec(rot));
                sb.Append(", \"scale\": ").Append(Vec(scale));
                sb.Append(", \"components\": ").Append(Components(t));
                sb.Append(", \"colliders\": ").Append(Colliders(t));
                sb.Append("}");
            }
            sb.Append("\n    ]\n  }\n}\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------- models
        static bool IsModel(string assetPath) =>
            assetPath != null && (assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || assetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase));

        /// <summary>The model path when t is the root of an FBX model instance, else null.</summary>
        static string ModelRootPath(Transform t)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) return null;
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
            return IsModel(source) ? source : null;
        }

        /// <summary>t comes from inside a model (a sub-object of an FBX), not a model root and not added by a prefab.</summary>
        static bool InsideModel(Transform t)
        {
            if (ModelRootPath(t) != null) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t);
            return source != null && IsModel(AssetDatabase.GetAssetPath(source));
        }

        static bool InsideModelOf(Transform t, Transform modelRoot) => InsideModel(t) && t.IsChildOf(modelRoot);

        /// <summary>
        /// From the Unreal mesh's space to the model root's local space: the M1 import put the mesh's
        /// origin at the bounds centre of the model placed at the origin with no rotation and its own
        /// root scale (UnrealGalleryCapture), so mesh = S⁻¹ · T(centre).
        /// </summary>
        static Matrix4x4 ModelToMesh(string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Vector3 s = model.transform.localScale;
            Matrix4x4 rootAtOrigin = Matrix4x4.Scale(s);
            Matrix4x4 toModel = model.transform.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 m = rootAtOrigin * toModel * r.transform.localToWorldMatrix;
                Bounds local = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            Vector3 inv = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            return Matrix4x4.Scale(inv) * Matrix4x4.Translate(any ? bounds.center : Vector3.zero);
        }

        // ------------------------------------------------------------- writing
        static void Decompose(Matrix4x4 m, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = m.GetColumn(3);
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) scale.x = -scale.x; // a mirror goes on x
            rot = scale.y > 1e-8f && scale.z > 1e-8f ? Quaternion.LookRotation(z / scale.z, y / scale.y) : Quaternion.identity;
        }

        static bool ActiveInPrefab(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Components(Transform t) =>
            "[" + string.Join(", ", t.GetComponents<MonoBehaviour>().Where(c => c != null).Select(c => Quote(c.GetType().Name))) + "]";

        static string Colliders(Transform t)
        {
            var items = new List<string>();
            foreach (var c in t.GetComponents<Collider>())
            {
                switch (c)
                {
                    case BoxCollider b: items.Add($"{{\"type\": \"Box\", \"trigger\": {Bool(b.isTrigger)}, \"center\": {Vec(b.center)}, \"size\": {Vec(b.size)}}}"); break;
                    case SphereCollider s: items.Add($"{{\"type\": \"Sphere\", \"trigger\": {Bool(s.isTrigger)}, \"center\": {Vec(s.center)}, \"radius\": {F(s.radius)}}}"); break;
                    case CapsuleCollider k: items.Add($"{{\"type\": \"Capsule\", \"trigger\": {Bool(k.isTrigger)}, \"center\": {Vec(k.center)}, \"radius\": {F(k.radius)}, \"height\": {F(k.height)}, \"direction\": {k.direction}}}"); break;
                    case MeshCollider mc: items.Add($"{{\"type\": \"Mesh\", \"trigger\": {Bool(mc.isTrigger)}, \"convex\": {Bool(mc.convex)}}}"); break;
                }
            }
            return "[" + string.Join(", ", items) + "]";
        }

        static string Ref(UnityEngine.Object target)
        {
            if (target == null) return "null";
            string path = AssetDatabase.GetAssetPath(target);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string guid, out long fileId);
            return $"{{\"$ref\": {{\"guid\": \"{guid}\", \"fileId\": {fileId}, \"path\": {Quote(path)}, \"name\": {Quote(target.name)}, \"type\": \"{target.GetType().Name}\", \"main\": {Bool(AssetDatabase.IsMainAsset(target))}}}}}";
        }

        static string Bool(bool b) => b ? "true" : "false";
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Vec(Vector3 v) => $"{{\"x\": {F(v.x)}, \"y\": {F(v.y)}, \"z\": {F(v.z)}}}";
        static string Vec(Quaternion q) => $"{{\"x\": {F(q.x)}, \"y\": {F(q.y)}, \"z\": {F(q.z)}, \"w\": {F(q.w)}}}";

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
