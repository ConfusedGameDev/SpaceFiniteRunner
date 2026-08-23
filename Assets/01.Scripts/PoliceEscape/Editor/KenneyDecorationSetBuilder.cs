using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds a DecorationSet from the Kenney decorator FBXs with the intended
    /// physics feel baked in: light posts heavy and slow to topple, cones
    /// featherweight so they fly, construction barriers near-immovable — plus
    /// the explosive barrel, which is not Kenney art at all but a primitive
    /// cylinder built here, and the fireball sprites its blast draws from. The
    /// native cell unit is measured off the road tile (same rule as the
    /// building set builder), and the set is wired straight into the test
    /// settings — Repopulate on the CityManager shows it without touching the
    /// road layout. Running it again refreshes an existing set in place.
    /// </summary>
    public static class KenneyDecorationSetBuilder
    {
        const string DecoratorsFolder = "Assets/02.Art/01.Models/InfiniteCity/Roads/Decorators";
        const string RoadReferencePath = "Assets/02.Art/01.Models/InfiniteCity/Roads/road-straight.fbx";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string SetPath = SettingsFolder + "/KenneyDecorationSet.asset";
        const string TestSettingsPath = "Assets/04.Data/InfiniteCity/CityTestSettings.asset";
        const string PrefabFolder = "Assets/03.Prefabs/PoliceEscape";
        const string MaterialFolder = "Assets/02.Art/02.Materials/InfiniteCity";
        const string BarrelPrefabPath = PrefabFolder + "/ExplosiveBarrel.prefab";
        const string ExplosionFolder = "Assets/02.Art/05.Particles/SmokeAndExplosions/Explosion";
        const string ConeReference = DecoratorsFolder + "/construction-cone.fbx";

        [MenuItem("Tools/Police Escape/Create Kenney Decoration Set")]
        public static void CreateSet()
        {
            float nativeCell = MeasureFootprint(AssetDatabase.LoadAssetAtPath<GameObject>(RoadReferencePath));
            if (nativeCell <= 0.0001f)
            {
                Debug.LogError($"KenneyDecorationSetBuilder: could not measure {RoadReferencePath} — aborting.");
                return;
            }

            // (file, placement, weight, mass, angularDamping, yawJitter): the physics
            // triplet is the whole design — same impact momentum for everyone,
            // mass decides who flies and who stands firm.
            (string file, DecorationPlacement placement, float weight, float mass, float angularDamping, float yawJitter)[] wanted =
            {
                ("light-square.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f),
                ("light-curved.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f),
                ("construction-cone.fbx", DecorationPlacement.RoadEdge, 6f, 2f, 0.05f, 180f),
                ("construction-barrier.fbx", DecorationPlacement.RoadEdge, 2f, 3000f, 1f, 10f),
                ("construction-light.fbx", DecorationPlacement.RoadEdge, 1f, 250f, 3f, 15f),
            };

            var definitions = new List<DecorationDefinition>();
            foreach (var (file, placement, weight, mass, angularDamping, yawJitter) in wanted)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{DecoratorsFolder}/{file}");
                if (prefab == null)
                {
                    Debug.LogWarning($"KenneyDecorationSetBuilder: '{file}' not found in {DecoratorsFolder} — skipped.");
                    continue;
                }
                definitions.Add(new DecorationDefinition
                {
                    prefab = prefab,
                    placement = placement,
                    weight = weight,
                    mass = mass,
                    angularDamping = angularDamping,
                    yawJitter = yawJitter,
                });
            }

            // The explosive barrel is not part of the Kenney kit — it is a
            // Unity primitive cylinder, built here so the set has no missing
            // reference. Weight 1.5 against the cone's 6: barrels are a hazard
            // you notice, and a street lined with them would stop reading as
            // one. It is authored in the KIT's units (measured off the cone),
            // because the decorator scales every prop by cellSize / nativeCellSize.
            float coneHeight = MeasureHeight(AssetDatabase.LoadAssetAtPath<GameObject>(ConeReference));
            GameObject barrel = BuildBarrelPrefab(coneHeight > 0.0001f ? coneHeight * 1.6f : nativeCell * 0.03f);
            if (barrel != null)
            {
                definitions.Add(new DecorationDefinition
                {
                    prefab = barrel,
                    placement = DecorationPlacement.RoadEdge,
                    weight = 1.5f,
                    mass = 60f,            // light enough to be thrown by its own blast
                    angularDamping = 0.5f,
                    yawJitter = 180f,
                    explosive = true,
                });
            }

            if (definitions.Count == 0)
            {
                Debug.LogError($"KenneyDecorationSetBuilder: no decorator FBXs found in {DecoratorsFolder}.");
                return;
            }

            EnsureFolder(SettingsFolder);
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(SetPath);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<DecorationSet>();
            set.decorations = definitions;
            set.nativeCellSize = nativeCell;
            set.explosionTextures = LoadExplosionSprites();
            if (isNew) AssetDatabase.CreateAsset(set, SetPath);
            else EditorUtility.SetDirty(set);

            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            if (settings != null)
            {
                settings.decorationSet = set;
                EditorUtility.SetDirty(settings);
            }
            else
            {
                Debug.LogWarning($"KenneyDecorationSetBuilder: test settings not found at {TestSettingsPath} — assign the set to your CityGenerationSettings manually.");
            }
            AssetDatabase.SaveAssets();

            Debug.Log($"KenneyDecorationSetBuilder: {definitions.Count} props → {SetPath} (native cell {nativeCell:0.##} m), " +
                      "wired into the test settings. Press Repopulate (or Recalculate) on the CityManager to see them.");
        }

        // ------------------------------------------------------- barrel + fire

        /// <summary>
        /// The explosive barrel: a Unity primitive cylinder on an unscaled
        /// root, so the decorator's own uniform scale multiplies cleanly and
        /// the capsule collider the primitive ships with stays honest. The
        /// base sits at y = 0 — props are placed on the curb, not through it.
        /// </summary>
        static GameObject BuildBarrelPrefab(float height)
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            var root = new GameObject("ExplosiveBarrel");
            try
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.name = "Barrel";
                body.transform.SetParent(root.transform, false);
                // Unity's cylinder is 2 units tall and 1 across: half the
                // height, and a bit under two thirds of it for the girth.
                float girth = height * 0.62f;
                body.transform.localScale = new Vector3(girth, height * 0.5f, girth);
                body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                body.GetComponent<MeshRenderer>().sharedMaterial =
                    CreateOrUpdateMaterial("ExplosiveBarrel", new Color(0.86f, 0.24f, 0.07f));

                return PrefabUtility.SaveAsPrefabAsset(root, BarrelPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Material CreateOrUpdateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The nine fireball frames, in name order — one is picked per blast.</summary>
        static List<Texture2D> LoadExplosionSprites()
        {
            var sprites = new List<Texture2D>();
            if (!AssetDatabase.IsValidFolder(ExplosionFolder))
            {
                Debug.LogWarning($"KenneyDecorationSetBuilder: no explosion art at {ExplosionFolder} — barrels will blast without a fireball.");
                return sprites;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ExplosionFolder }))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture != null) sprites.Add(texture);
            }
            sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return sprites;
        }

        /// <summary>Height of a prefab's combined renderer bounds, in its native units.</summary>
        static float MeasureHeight(GameObject prefab)
        {
            if (prefab == null) return 0f;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds.size.y;
        }

        /// <summary>XZ footprint (largest side) of a prefab's combined renderer bounds, in its native units.</summary>
        static float MeasureFootprint(GameObject prefab)
        {
            if (prefab == null) return 0f;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return Mathf.Max(bounds.size.x, bounds.size.z);
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
