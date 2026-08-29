using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Population;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// One-click bootstrap for the district system (Tools → Police Escape →
    /// Create District Assets): creates the five stock districts — Downtown,
    /// Midrise, Suburb, Park, Beachfront — each with a purpose-tuned
    /// BlockSettings, and wires the seeded radial map into the
    /// CityDefinition (downtown anchor → midrise ring → weighted outskirts).
    /// Follows the set builders' refresh contract: re-running RESETS the
    /// stock districts and their BlockSettings to these tuned defaults
    /// (assets are updated in place, never deleted), while the
    /// CityDefinition's district wiring only fills fields that are still
    /// empty — so the seeded map's hand-arrangement survives, but hand edits
    /// inside the stock assets do not (duplicate an asset to keep a custom
    /// district). Building sets are picked up from the
    /// KenneyBuildingSetBuilder outputs and nature sets from the
    /// KenneyNatureSetBuilder outputs when they exist.
    /// </summary>
    public static class DistrictAssetBuilder
    {
        const string DataFolder = "Assets/04.Data/InfiniteCity";
        const string DistrictsFolder = DataFolder + "/Districts";
        const string DefinitionPath = DataFolder + "/CityDefinition.asset";

        const string MidriseBuildingsPath = DataFolder + "/KenneyBuildingSet.asset";
        const string DowntownBuildingsPath = DataFolder + "/KenneyBuildingSet_Downtown.asset";
        const string SuburbBuildingsPath = DataFolder + "/KenneyBuildingSet_Suburb.asset";
        const string ParkNaturePath = DataFolder + "/KenneyNatureSet_Park.asset";
        const string BeachNaturePath = DataFolder + "/KenneyNatureSet_Beach.asset";
        const string PalmDecorationsPath = DataFolder + "/KenneyDecorationSet_Palms.asset";

        [MenuItem("Tools/Police Escape/Create District Assets")]
        public static void CreateDistricts()
        {
            EnsureFolder(DistrictsFolder);

            var midriseBuildings = AssetDatabase.LoadAssetAtPath<BuildingSet>(MidriseBuildingsPath);
            var downtownBuildings = AssetDatabase.LoadAssetAtPath<BuildingSet>(DowntownBuildingsPath);
            var suburbBuildings = AssetDatabase.LoadAssetAtPath<BuildingSet>(SuburbBuildingsPath);
            var parkNature = AssetDatabase.LoadAssetAtPath<NatureSet>(ParkNaturePath);
            var beachNature = AssetDatabase.LoadAssetAtPath<NatureSet>(BeachNaturePath);
            var palmDecorations = AssetDatabase.LoadAssetAtPath<DecorationSet>(PalmDecorationsPath);
            if (downtownBuildings == null || suburbBuildings == null)
                Debug.LogWarning("DistrictAssetBuilder: district building sets missing — run 'Create Kenney Building Set' first (districts are wired anyway, with null sets falling back to the city-wide one).");
            if (parkNature == null)
                Debug.LogWarning("DistrictAssetBuilder: nature sets missing — run 'Create Kenney Nature Sets' first (parks will have no props until they exist and this tool is re-run).");

            // ---- per-district interior settings
            BlockSettings downtownBlocks = EnsureBlockSettings("Downtown", s =>
            {
                s.connectorDensity = 0.9f;
                s.turnProbability = 0.5f;
                s.allowDeadEnds = false;
                s.placeFeatures = true;
                s.overpassChance = 0.6f;
                s.forkChance = 0.6f;
                s.buildingSet = downtownBuildings;
                s.buildingDensityMultiplier = 1.1f;
                s.decorationDensityMultiplier = 1.2f;
            });
            BlockSettings midriseBlocks = EnsureBlockSettings("Midrise", s =>
            {
                s.connectorDensity = 0.6f;
                s.turnProbability = 0.35f;
                s.allowDeadEnds = false;
                s.placeFeatures = true;
                s.overpassChance = 0.5f;
                s.forkChance = 0.5f;
                s.buildingSet = midriseBuildings;
            });
            BlockSettings suburbBlocks = EnsureBlockSettings("Suburb", s =>
            {
                s.connectorDensity = 0.35f;
                s.turnProbability = 0.5f;
                s.allowDeadEnds = true;
                s.placeFeatures = true;
                s.overpassChance = 0.1f;
                s.forkChance = 0.3f;
                s.buildingSet = suburbBuildings;
                s.buildingDensityMultiplier = 0.9f;
                s.decorationDensityMultiplier = 0.7f;
            });
            BlockSettings parkBlocks = EnsureBlockSettings("Park", s =>
            {
                s.connectorDensity = 0.15f;
                s.turnProbability = 0.6f;
                s.allowDeadEnds = true;
                s.placeFeatures = false;
                // No buildings: the multiplier (not a null set) empties the lots,
                // so the fallback chain needs no null-handling special case.
                s.buildingDensityMultiplier = 0f;
                s.decorationDensityMultiplier = 0.3f;
            });
            BlockSettings beachBlocks = EnsureBlockSettings("Beachfront", s =>
            {
                s.connectorDensity = 0.5f;
                s.turnProbability = 0.4f;
                s.allowDeadEnds = false;
                s.placeFeatures = true;
                s.overpassChance = 0.2f;
                s.forkChance = 0.5f;
                s.buildingSet = midriseBuildings;
                s.decorationSet = palmDecorations;
                s.buildingDensityMultiplier = 0.85f;
            });

            // ---- the districts
            DistrictDefinition downtown = EnsureDistrict("Downtown", d =>
            {
                d.displayName = "Downtown";
                d.mapColor = new Color(0.55f, 0.5f, 0.9f);
                d.useSecondaryArterials = true;
                d.interiorSettings = downtownBlocks;
            });
            DistrictDefinition midrise = EnsureDistrict("Midrise", d =>
            {
                d.displayName = "Midrise";
                d.mapColor = new Color(0.6f, 0.6f, 0.65f);
                d.useSecondaryArterials = false;
                d.interiorSettings = midriseBlocks;
            });
            DistrictDefinition suburb = EnsureDistrict("Suburb", d =>
            {
                d.displayName = "Suburb";
                d.mapColor = new Color(0.75f, 0.7f, 0.5f);
                d.useSecondaryArterials = false;
                d.interiorSettings = suburbBlocks;
                d.curveChance = 0.6f;
                d.maxCurvedAvenues = 1;
            });
            DistrictDefinition park = EnsureDistrict("Park", d =>
            {
                d.displayName = "Park";
                d.mapColor = new Color(0.4f, 0.8f, 0.4f);
                d.useSecondaryArterials = false;
                d.interiorSettings = parkBlocks;
                d.isPark = true;
                d.natureSet = parkNature;
            });
            DistrictDefinition beachfront = EnsureDistrict("Beachfront", d =>
            {
                d.displayName = "Beachfront";
                d.mapColor = new Color(0.95f, 0.85f, 0.5f);
                d.useSecondaryArterials = false;
                d.interiorSettings = beachBlocks;
                d.parkLotChance = 0.15f;
                d.natureSet = beachNature;
                d.curveChance = 0.7f;
                d.maxCurvedAvenues = 1;
            });

            // ---- wire the seeded map — only fields still empty, so hand edits survive
            var definition = AssetDatabase.LoadAssetAtPath<CityDefinition>(DefinitionPath);
            if (definition != null)
            {
                bool changed = false;
                if (definition.downtownDistrict == null) { definition.downtownDistrict = downtown; changed = true; }
                if (definition.innerRingDistrict == null) { definition.innerRingDistrict = midrise; changed = true; }
                if (definition.defaultDistrict == null) { definition.defaultDistrict = midrise; changed = true; }
                if (definition.outerDistricts.Count == 0)
                {
                    definition.outerDistricts.Add(new CityDefinition.WeightedDistrict { district = suburb, weight = 3f });
                    definition.outerDistricts.Add(new CityDefinition.WeightedDistrict { district = beachfront, weight = 1.5f });
                    definition.outerDistricts.Add(new CityDefinition.WeightedDistrict { district = park, weight = 1f });
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(definition);
            }
            else
            {
                Debug.LogWarning($"DistrictAssetBuilder: no CityDefinition at {DefinitionPath} — districts created but not wired.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("DistrictAssetBuilder: districts ready — Downtown (dense secondary grid), Midrise, Suburb, Park, Beachfront. " +
                      "Open the City Designer to see the seeded map, then Bake City.");
        }

        static BlockSettings EnsureBlockSettings(string districtName, System.Action<BlockSettings> tune)
        {
            string path = $"{DistrictsFolder}/BlockSettings_{districtName}.asset";
            var settings = AssetDatabase.LoadAssetAtPath<BlockSettings>(path);
            bool isNew = settings == null;
            if (isNew) settings = ScriptableObject.CreateInstance<BlockSettings>();
            tune(settings);
            if (isNew) AssetDatabase.CreateAsset(settings, path);
            else EditorUtility.SetDirty(settings);
            return settings;
        }

        static DistrictDefinition EnsureDistrict(string districtName, System.Action<DistrictDefinition> tune)
        {
            string path = $"{DistrictsFolder}/District_{districtName}.asset";
            var district = AssetDatabase.LoadAssetAtPath<DistrictDefinition>(path);
            bool isNew = district == null;
            if (isNew) district = ScriptableObject.CreateInstance<DistrictDefinition>();
            tune(district);
            if (isNew) AssetDatabase.CreateAsset(district, path);
            else EditorUtility.SetDirty(district);
            return district;
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
