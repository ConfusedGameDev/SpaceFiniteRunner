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
    /// building set builder) — but that cell fit (cellSize ÷ the unit, ×36.9
    /// in the test city) is the ROAD's scale, not a prop's: the kit's tile
    /// stands for ~7 m of street, so a cone fitted like a tile came out 3.5 m
    /// tall beside real-metre cars. Every prop therefore carries a target
    /// world height (last column of the table) and gets a scaleMultiplier
    /// that undoes the cell fit down to it — the rule KenneyNatureSetBuilder
    /// applies to its human-scale kit. Retune the heights here, re-run, then
    /// rebake the city (props are baked into the prefab). The set is wired
    /// straight into the test
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

        /// <summary>World height of the street light posts, metres (a real one is 7-10 m). Shared with the palm-street set.</summary>
        public const float LightPostHeight = 7.5f;
        /// <summary>World height of the explosive barrel, metres: an oil drum is 0.9 m, a touch taller so it reads as the hazard it is.</summary>
        const float BarrelHeight = 1.1f;

        [MenuItem("Tools/Police Escape/Create Kenney Decoration Set")]
        public static void CreateSet()
        {
            float nativeCell = MeasureFootprint(AssetDatabase.LoadAssetAtPath<GameObject>(RoadReferencePath));
            if (nativeCell <= 0.0001f)
            {
                Debug.LogError($"KenneyDecorationSetBuilder: could not measure {RoadReferencePath} — aborting.");
                return;
            }

            // (file, placement, weight, mass, angularDamping, yawJitter, targetHeight):
            // the physics triplet is the whole feel design — same impact momentum
            // for everyone, mass decides who flies and who stands firm — and the
            // height (metres in the world) is the whole size design.
            (string file, DecorationPlacement placement, float weight, float mass, float angularDamping, float yawJitter, float targetHeight)[] wanted =
            {
                ("light-square.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f, LightPostHeight),
                ("light-curved.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f, LightPostHeight),
                ("construction-cone.fbx", DecorationPlacement.RoadEdge, 6f, 2f, 0.05f, 180f, 0.75f),
                ("construction-barrier.fbx", DecorationPlacement.RoadEdge, 2f, 3000f, 1f, 10f, 1.05f),
                ("construction-light.fbx", DecorationPlacement.RoadEdge, 1f, 250f, 3f, 15f, 1.3f),
            };

            // What the decorator multiplies every prop by; the height fit divides it back out.
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            float cellScale = (settings != null ? settings.cellSize : 36.9f) / nativeCell;
            if (settings == null)
                Debug.LogWarning($"KenneyDecorationSetBuilder: no settings at {TestSettingsPath} — assuming cellSize 36.9 for the prop height math.");

            var definitions = new List<DecorationDefinition>();
            foreach (var (file, placement, weight, mass, angularDamping, yawJitter, targetHeight) in wanted)
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
                    scaleMultiplier = HeightFit(prefab, targetHeight, cellScale),
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
                    scaleMultiplier = HeightFit(barrel, BarrelHeight, cellScale),
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

        /// <summary>The scaleMultiplier that leaves the prop standing targetHeight metres after the decorator's cell fit (0 keeps the kit's own scale).</summary>
        internal static float HeightFit(GameObject prefab, float targetHeight, float cellScale)
        {
            if (targetHeight <= 0f || prefab == null) return 1f;
            float nativeHeight = MeasureHeight(prefab);
            return nativeHeight > 0.0001f && cellScale > 0.0001f ? targetHeight / (nativeHeight * cellScale) : 1f;
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
