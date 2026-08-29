using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds the water layer's art hand-off (Tools → Police Escape → Create
    /// Kenney Shoreline Set): a <see cref="ShorelineSet"/> from the
    /// NaturePack's rock cliff pieces — straight edge, inner and outer
    /// corner, waterfall — with every piece's renderer bounds measured and
    /// stored, so the placer can stand them at a real-world height instead
    /// of the cell fit (the kit is human-scale; a cell is a whole street
    /// tile); plus the transparent URP Lit water material the water blocks'
    /// surface quads use. Both are wired into the generation settings the
    /// City Designer's definition points at. Re-running refreshes the set's
    /// piece list and bounds in place but keeps hand-tuned rotation offsets
    /// (matched by prefab), and never overwrites a water material that
    /// already exists — swap it for a Shader Graph water freely.
    /// </summary>
    public static class KenneyShorelineSetBuilder
    {
        const string NatureFolder = "Assets/02.Art/01.Models/InfiniteCity/NaturePack";
        const string MaterialFolder = "Assets/02.Art/02.Materials/InfiniteCity";
        const string WaterMaterialPath = MaterialFolder + "/Water.mat";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string SetPath = SettingsFolder + "/KenneyShorelineSet.asset";
        const string DefinitionPath = SettingsFolder + "/CityDefinition.asset";
        const string TestSettingsPath = SettingsFolder + "/CityTestSettings.asset";

        readonly struct Request
        {
            public readonly string File;
            public readonly float Weight;
            public readonly float RotationOffset;

            public Request(string file, float weight, float rotationOffset)
            {
                File = file;
                Weight = weight;
                RotationOffset = rotationOffset;
            }
        }

        [MenuItem("Tools/Police Escape/Create Kenney Shoreline Set")]
        public static void Create()
        {
            var set = AssetDatabase.LoadAssetAtPath<ShorelineSet>(SetPath);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<ShorelineSet>();

            set.edges = Build(set.edges, new[]
            {
                new Request("cliff_rock.fbx", 3f, 0f),
                new Request("cliff_large_rock.fbx", 1f, 0f),
            });
            set.innerCorners = Build(set.innerCorners, new[] { new Request("cliff_cornerInner_rock.fbx", 1f, 0f) });
            set.outerCorners = Build(set.outerCorners, new[] { new Request("cliff_corner_rock.fbx", 1f, 0f) });
            set.waterfalls = Build(set.waterfalls, new[] { new Request("cliff_waterfall_rock.fbx", 1f, 0f) });

            if (isNew) AssetDatabase.CreateAsset(set, SetPath);
            else EditorUtility.SetDirty(set);

            Material water = EnsureWaterMaterial();

            // Wire into the generation asset the designer actually bakes with,
            // falling back to the test settings by path.
            var definition = AssetDatabase.LoadAssetAtPath<CityDefinition>(DefinitionPath);
            CityGenerationSettings settings = definition != null && definition.generation != null
                ? definition.generation
                : AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            if (settings != null)
            {
                settings.shorelineSet = set;
                if (settings.waterMaterial == null) settings.waterMaterial = water;
                EditorUtility.SetDirty(settings);
            }
            else
            {
                Debug.LogWarning($"KenneyShorelineSetBuilder: no CityGenerationSettings found at {DefinitionPath} or {TestSettingsPath} — assign {SetPath} and {WaterMaterialPath} by hand.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"KenneyShorelineSetBuilder: built {SetPath} ({set.edges.Count} edge, {set.innerCorners.Count} inner, {set.outerCorners.Count} outer, {set.waterfalls.Count} waterfall pieces) and {WaterMaterialPath}. " +
                      "Bake a water block, check the cliff faces look at the sea, and fix any piece's rotationOffset on the set.", set);
        }

        /// <summary>Rebuild a slot list from requests, keeping the rotation offset of any piece already in the list (the hand-tuned part).</summary>
        static List<ShorelineSet.Piece> Build(List<ShorelineSet.Piece> existing, Request[] requests)
        {
            var pieces = new List<ShorelineSet.Piece>();
            foreach (Request request in requests)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/{request.File}");
                if (prefab == null)
                {
                    Debug.LogWarning($"KenneyShorelineSetBuilder: '{request.File}' not found in {NatureFolder} — skipped.");
                    continue;
                }
                Bounds bounds = MeasureBounds(prefab);
                if (bounds.size.y <= 0.0001f)
                {
                    Debug.LogWarning($"KenneyShorelineSetBuilder: '{request.File}' has no renderers to measure — skipped.");
                    continue;
                }
                float offset = request.RotationOffset;
                if (existing != null)
                    foreach (ShorelineSet.Piece old in existing)
                        if (old?.prefab == prefab) offset = old.rotationOffset;
                pieces.Add(new ShorelineSet.Piece
                {
                    prefab = prefab,
                    weight = request.Weight,
                    rotationOffset = offset,
                    nativeBounds = bounds,
                });
            }
            return pieces;
        }

        /// <summary>Combined renderer bounds of a prefab asset in its own space (its root sits at the origin, so world bounds are local bounds).</summary>
        static Bounds MeasureBounds(GameObject prefab)
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>A transparent, glossy blue URP Lit — created once, never overwritten, so an artist's replacement survives re-runs.</summary>
        static Material EnsureWaterMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader) { name = "Water" };
            // URP Lit's transparent surface set-up, as its inspector would apply it.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetFloat("_Smoothness", 0.92f);
            material.SetFloat("_Metallic", 0f);
            var color = new Color(0.08f, 0.32f, 0.55f, 0.78f);
            material.SetColor("_BaseColor", color);
            material.color = color;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/02.Art/02.Materials", "InfiniteCity");
            AssetDatabase.CreateAsset(material, WaterMaterialPath);
            return material;
        }
    }
}
