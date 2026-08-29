using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Builds the nature sets from the Kenney NaturePack (Tools → Police
    /// Escape → Create Kenney Nature Sets): a Park NatureSet (trees, bushes,
    /// rocks, flowers over grass tiles with a walking path), a Beach NatureSet
    /// (palms, grasses, rocks) and a palm street DecorationSet that lines
    /// avenues with palms through the ordinary CityDecorator. The scale trap
    /// this tool exists to solve: the nature kit is HUMAN-scale (a tree ≈ 1.3
    /// units) while a city cell is a whole street tile, so scaling props by
    /// cellSize ÷ tile would make 45 m trees — every prop instead gets a
    /// scaleMultiplier computed from a target real-world height and its
    /// measured native height. Masses follow the decoration physics dial:
    /// heavier than the car stops it, lighter flies. Re-running refreshes
    /// existing sets in place.
    /// </summary>
    public static class KenneyNatureSetBuilder
    {
        const string NatureFolder = "Assets/02.Art/01.Models/InfiniteCity/NaturePack";
        const string DecoratorsFolder = "Assets/02.Art/01.Models/InfiniteCity/Roads/Decorators";
        const string RoadReferencePath = "Assets/02.Art/01.Models/InfiniteCity/Roads/road-straight.fbx";
        const string GroundTile = "ground_grass.fbx";
        const string PathTile = "ground_pathStraight.fbx";
        const string SettingsFolder = "Assets/04.Data/InfiniteCity";
        const string ParkSetPath = SettingsFolder + "/KenneyNatureSet_Park.asset";
        const string BeachSetPath = SettingsFolder + "/KenneyNatureSet_Beach.asset";
        const string PalmStreetSetPath = SettingsFolder + "/KenneyDecorationSet_Palms.asset";
        const string TestSettingsPath = SettingsFolder + "/CityTestSettings.asset";

        /// <summary>One prop request: file, placement, weight, physics feel and the height it should stand in the world.</summary>
        readonly struct Prop
        {
            public readonly string File;
            public readonly DecorationPlacement Placement;
            public readonly float Weight, Mass, AngularDamping, YawJitter, TargetHeight;

            public Prop(string file, DecorationPlacement placement, float weight, float mass, float angularDamping, float yawJitter, float targetHeight)
            {
                File = file;
                Placement = placement;
                Weight = weight;
                Mass = mass;
                AngularDamping = angularDamping;
                YawJitter = yawJitter;
                TargetHeight = targetHeight;
            }
        }

        [MenuItem("Tools/Police Escape/Create Kenney Nature Sets")]
        public static void CreateSets()
        {
            float nativeCell = MeasureFootprint(Load(NatureFolder, GroundTile));
            if (nativeCell <= 0.0001f)
            {
                Debug.LogError($"KenneyNatureSetBuilder: could not measure {NatureFolder}/{GroundTile} — is the NaturePack imported?");
                return;
            }
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(TestSettingsPath);
            float cellSize = settings != null ? settings.cellSize : 36.9f;
            float cellScale = cellSize / nativeCell; // what the placer multiplies every prop by
            if (settings == null)
                Debug.LogWarning($"KenneyNatureSetBuilder: no settings at {TestSettingsPath} — assuming cellSize {cellSize} for the prop height math.");

            // ---- Park: trees over grass, a path through the middle, fenced edges.
            Prop[] parkProps =
            {
                new("tree_default.fbx", DecorationPlacement.LotInterior, 3f, 1800f, 4f, 180f, 10f),
                new("tree_oak.fbx", DecorationPlacement.LotInterior, 2f, 2000f, 4f, 180f, 11f),
                new("tree_detailed.fbx", DecorationPlacement.LotInterior, 2f, 1800f, 4f, 180f, 10f),
                new("tree_fat.fbx", DecorationPlacement.LotInterior, 1.5f, 1800f, 4f, 180f, 9f),
                new("tree_pineTallA.fbx", DecorationPlacement.LotInterior, 1f, 2200f, 4f, 180f, 13f),
                new("tree_small.fbx", DecorationPlacement.LotInterior, 2f, 900f, 4f, 180f, 6f),
                new("plant_bush.fbx", DecorationPlacement.LotInterior, 3f, 8f, 0.5f, 180f, 1.6f),
                new("plant_bushLarge.fbx", DecorationPlacement.LotInterior, 2f, 15f, 0.5f, 180f, 2.2f),
                new("flower_redA.fbx", DecorationPlacement.LotInterior, 2f, 1f, 0.1f, 180f, 0.7f),
                new("flower_yellowA.fbx", DecorationPlacement.LotInterior, 2f, 1f, 0.1f, 180f, 0.7f),
                new("flower_purpleA.fbx", DecorationPlacement.LotInterior, 2f, 1f, 0.1f, 180f, 0.7f),
                new("rock_largeA.fbx", DecorationPlacement.LotInterior, 1f, 1600f, 2f, 180f, 2.2f),
                new("stone_smallFlatA.fbx", DecorationPlacement.LotInterior, 1f, 200f, 1f, 180f, 0.8f),
                new("statue_obelisk.fbx", DecorationPlacement.LotInterior, 0.3f, 3000f, 5f, 45f, 5f),
                new("fence_simple.fbx", DecorationPlacement.LotPerimeter, 4f, 90f, 1f, 5f, 1.1f),
                new("tree_default.fbx", DecorationPlacement.LotPerimeter, 1f, 1800f, 4f, 180f, 10f),
            };
            NatureSet park = SaveNatureSet(ParkSetPath, BuildProps(NatureFolder, parkProps, cellScale), nativeCell);
            park.groundTilePrefab = Load(NatureFolder, GroundTile);
            park.pathTilePrefab = Load(NatureFolder, PathTile);
            park.interiorDensity = 0.4f;
            park.perimeterDensity = 0.55f;
            park.clearingRadius = 0.8f;
            EditorUtility.SetDirty(park);

            // ---- Beach: palms and coastal scatter, no fences, no path.
            Prop[] beachProps =
            {
                new("tree_palm.fbx", DecorationPlacement.LotInterior, 3f, 1200f, 3f, 180f, 11f),
                new("tree_palmBend.fbx", DecorationPlacement.LotInterior, 2f, 1200f, 3f, 180f, 10f),
                new("tree_palmShort.fbx", DecorationPlacement.LotInterior, 2f, 900f, 3f, 180f, 7f),
                new("tree_palmTall.fbx", DecorationPlacement.LotInterior, 2f, 1400f, 3f, 180f, 13f),
                new("grass_large.fbx", DecorationPlacement.LotInterior, 3f, 2f, 0.1f, 180f, 0.8f),
                new("rock_smallA.fbx", DecorationPlacement.LotInterior, 1f, 150f, 1f, 180f, 0.9f),
                new("flower_yellowB.fbx", DecorationPlacement.LotInterior, 1f, 1f, 0.1f, 180f, 0.7f),
                new("tree_palmShort.fbx", DecorationPlacement.LotPerimeter, 2f, 900f, 3f, 180f, 7f),
                new("rock_tallB.fbx", DecorationPlacement.LotPerimeter, 1f, 1200f, 2f, 180f, 2.5f),
            };
            NatureSet beach = SaveNatureSet(BeachSetPath, BuildProps(NatureFolder, beachProps, cellScale), nativeCell);
            beach.groundTilePrefab = Load(NatureFolder, GroundTile);
            beach.pathTilePrefab = null;
            beach.interiorDensity = 0.3f;
            beach.perimeterDensity = 0.4f;
            beach.clearingRadius = 0.6f;
            EditorUtility.SetDirty(beach);

            // ---- Palm streets: an ordinary DecorationSet — palms on the
            // sidewalk edges, the familiar light posts on the corners — so the
            // existing CityDecorator lines beachfront avenues with palms with
            // zero new runtime code. The road-kit light posts are already at
            // street scale (multiplier 1); the palms get the height math.
            var palmDefs = new List<DecorationDefinition>();
            AddProp(palmDefs, DecoratorsFolder, new Prop("light-curved.fbx", DecorationPlacement.IntersectionCorner, 3f, 350f, 4f, 0f, 0f), 1f);
            foreach (Prop prop in new[]
            {
                new Prop("tree_palm.fbx", DecorationPlacement.RoadEdge, 3f, 1200f, 3f, 25f, 12f),
                new Prop("tree_palmTall.fbx", DecorationPlacement.RoadEdge, 2f, 1400f, 3f, 25f, 14f),
                new Prop("tree_palmShort.fbx", DecorationPlacement.RoadEdge, 2f, 900f, 3f, 25f, 8f),
            })
                AddProp(palmDefs, NatureFolder, prop, cellScale);

            var palmSet = AssetDatabase.LoadAssetAtPath<DecorationSet>(PalmStreetSetPath);
            bool palmNew = palmSet == null;
            if (palmNew) palmSet = ScriptableObject.CreateInstance<DecorationSet>();
            palmSet.decorations = palmDefs;
            palmSet.nativeCellSize = MeasureFootprint(AssetDatabase.LoadAssetAtPath<GameObject>(RoadReferencePath));
            palmSet.density = 0.45f;
            if (palmNew) AssetDatabase.CreateAsset(palmSet, PalmStreetSetPath);
            else EditorUtility.SetDirty(palmSet);

            AssetDatabase.SaveAssets();
            Debug.Log($"KenneyNatureSetBuilder: built Park + Beach nature sets and the palm street set (native cell {nativeCell:0.##}, cell scale ×{cellScale:0.#}). " +
                      "Run 'Create District Assets' (again) to wire them into the districts, then bake.");
        }

        // -------------------------------------------------------------- helpers

        static List<DecorationDefinition> BuildProps(string folder, Prop[] props, float cellScale)
        {
            var definitions = new List<DecorationDefinition>();
            foreach (Prop prop in props) AddProp(definitions, folder, prop, cellScale);
            return definitions;
        }

        /// <summary>Add one prop, its scaleMultiplier derived so it stands TargetHeight meters after the placer's cell scale (0 = keep the kit's own scale).</summary>
        static void AddProp(List<DecorationDefinition> definitions, string folder, Prop prop, float cellScale)
        {
            GameObject prefab = Load(folder, prop.File);
            if (prefab == null)
            {
                Debug.LogWarning($"KenneyNatureSetBuilder: '{prop.File}' not found in {folder} — skipped.");
                return;
            }
            float multiplier = 1f;
            if (prop.TargetHeight > 0f)
            {
                float nativeHeight = MeasureHeight(prefab);
                if (nativeHeight > 0.0001f && cellScale > 0.0001f)
                    multiplier = prop.TargetHeight / (nativeHeight * cellScale);
            }
            definitions.Add(new DecorationDefinition
            {
                prefab = prefab,
                placement = prop.Placement,
                weight = prop.Weight,
                mass = prop.Mass,
                angularDamping = prop.AngularDamping,
                yawJitter = prop.YawJitter,
                scaleMultiplier = multiplier,
            });
        }

        static NatureSet SaveNatureSet(string path, List<DecorationDefinition> definitions, float nativeCell)
        {
            var set = AssetDatabase.LoadAssetAtPath<NatureSet>(path);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<NatureSet>();
            set.decorations = definitions;
            set.nativeCellSize = nativeCell;
            if (isNew) AssetDatabase.CreateAsset(set, path);
            else EditorUtility.SetDirty(set);
            return set;
        }

        static GameObject Load(string folder, string file) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{file}");

        static float MeasureHeight(GameObject prefab)
        {
            if (prefab == null) return 0f;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds.size.y;
        }

        static float MeasureFootprint(GameObject prefab)
        {
            if (prefab == null) return 0f;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return Mathf.Max(bounds.size.x, bounds.size.z);
        }
    }
}
