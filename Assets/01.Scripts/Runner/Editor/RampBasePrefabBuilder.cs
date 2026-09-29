using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates <c>PF_RampBase</c>, the unit ramp the track scales into every ramp's
    /// picture (<c>FeatureSpawnEntry.prefab</c>): 1 m wide, 1 m tall lip, 1 m long,
    /// foot at the origin, surface rising toward +Z. Never overwrites an existing
    /// mesh, material or prefab. Colliders are absent on purpose — detection is
    /// analytic and the ship's ground is <c>TrackColliderBuilder</c>'s wedge — so
    /// restyling here (mesh, material, extra children) never touches gameplay.
    /// </summary>
    public static class RampBasePrefabBuilder
    {
        public const string MeshPath = "Assets/02.Art/01.Models/FiniteRunner/RampBase_Wedge.asset";
        public const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/Ramp_Mat.mat";
        public const string PrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_RampBase.prefab";
        const string TrackPrefabPath = "Assets/03.Prefabs/FiniteRunner/PF_Track.prefab";

        [MenuItem("Tools/FiniteRunner/Create PF_RampBase")]
        public static void CreateFromMenu()
        {
            GameObject prefab = CreateOrLoadPrefab();
            WireEntries(prefab);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"RampBasePrefabBuilder: {PrefabPath} ready and wired to the end ramps and the Jump entry.", prefab);
        }

        static GameObject CreateOrLoadPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = BuildWedge();
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(shader) { name = "Ramp_Mat" };
                var teal = new Color(0.2f, 1f, 0.85f);
                mat.SetColor("_BaseColor", teal);
                mat.SetColor("_EmissionColor", teal * 0.6f);
                mat.EnableKeyword("_EMISSION");
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            var root = new GameObject("PF_RampBase");
            var wedge = new GameObject("Wedge");
            wedge.transform.SetParent(root.transform, false);
            wedge.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = wedge.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>Right-triangle prism: foot (0,0,0) → lip (0,1,1), back face at z = 1, flat underside, x ±0.5.</summary>
        static Mesh BuildWedge()
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();

            // Each face gets its own vertices (hard edges). Corners are wound so
            // Unity's cross(b - a, c - a) points out of the solid.
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.AddRange(new[] { a, b, c, d });
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = verts.Count;
                verts.AddRange(new[] { a, b, c });
                uvs.AddRange(new[] { new Vector2(0, a.z), new Vector2(0, b.z), new Vector2(0, c.z) });
                tris.AddRange(new[] { i, i + 1, i + 2 });
            }

            var f0 = new Vector3(-0.5f, 0f, 0f);  var f1 = new Vector3(0.5f, 0f, 0f);   // foot
            var b0 = new Vector3(-0.5f, 0f, 1f);  var b1 = new Vector3(0.5f, 0f, 1f);   // back bottom
            var l0 = new Vector3(-0.5f, 1f, 1f);  var l1 = new Vector3(0.5f, 1f, 1f);   // lip

            Quad(f0, l0, l1, f1);   // slope (top), normal up/back-tilted toward -Z
            Quad(b1, l1, l0, b0);   // back face, normal +Z
            Quad(f0, f1, b1, b0);   // underside, normal -Y
            Tri(f0, b0, l0);        // left side, normal -X
            Tri(f1, l1, b1);        // right side, normal +X

            var mesh = new Mesh { name = "RampBase_Wedge" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void WireEntries(GameObject prefab)
        {
            // The three end ramps: PF_Track's TrackGenerator.endRamp.prefab.
            GameObject trackRoot = PrefabUtility.LoadPrefabContents(TrackPrefabPath);
            try
            {
                var gen = trackRoot.GetComponentInChildren<TrackGenerator>(true);
                if (gen != null)
                {
                    var so = new SerializedObject(gen);
                    var p = so.FindProperty("endRamp.prefab");
                    if (p != null && p.objectReferenceValue == null)
                    {
                        p.objectReferenceValue = prefab;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(trackRoot, TrackPrefabPath);
                    }
                    var shape = so.FindProperty("trackShape").objectReferenceValue as TrackShapeSettings;
                    if (shape != null) WireJump(shape, prefab);
                }
                else Debug.LogWarning("RampBasePrefabBuilder: no TrackGenerator found in PF_Track.");
            }
            finally { PrefabUtility.UnloadPrefabContents(trackRoot); }
        }

        static void WireJump(TrackShapeSettings shape, GameObject prefab)
        {
            var so = new SerializedObject(shape);
            var table = so.FindProperty("featureTable");
            for (int i = 0; i < table.arraySize; i++)
            {
                var entry = table.GetArrayElementAtIndex(i);
                var name = entry.FindPropertyRelative("name");
                var pf = entry.FindPropertyRelative("prefab");
                if (name != null && name.stringValue == "Jump" && pf.objectReferenceValue == null)
                    pf.objectReferenceValue = prefab;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shape);
        }
    }
}
