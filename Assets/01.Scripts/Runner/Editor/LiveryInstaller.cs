using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Customize;
using ConfusedGameDev.FiniteRunner.Livery;
using ConfusedGameDev.FiniteRunner.Store;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// <c>Tools → FiniteRunner → Customize → Install Ship Livery</c>: puts a
    /// <see cref="VehicleLivery"/> on every prefab that flies the
    /// nabucodonosor — the runner's <c>PF_Ship</c> and the standalone
    /// <c>HoverShip</c> (which <c>ShipSystem</c> nests) — so the player's saved
    /// colours show in every scene that uses them, <c>FiniteRunner_Test</c>
    /// included. The livery's materials are the customize scene's own list
    /// for the ship (read off <see cref="CustomizeSettings"/>, one source of
    /// truth), its model the nested fbx instance holding the six-slot
    /// renderer. Idempotent: a prefab that has one is re-configured.
    /// </summary>
    public static class LiveryInstaller
    {
        const string SettingsPath = "Assets/04.Data/Customize/CustomizeSettings.asset";
        const string ProfilePath = "Assets/04.Data/Customize/VehicleColorProfile.asset";
        static readonly string[] ShipPrefabs =
        {
            "Assets/03.Prefabs/FiniteRunner/PF_Ship.prefab",
            "Assets/03.Prefabs/Runner/HoverShip.prefab",
        };

        [MenuItem("Tools/FiniteRunner/Customize/Install Ship Livery")]
        public static void InstallShip()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CustomizeSettings>(SettingsPath);
            var profile = AssetDatabase.LoadAssetAtPath<VehicleColorProfile>(ProfilePath);
            CustomizeVehicleEntry entry = settings != null
                ? settings.vehicles.Find(v => v != null && v.model != null && v.model.modelId == UpgradeIds.ShipNabucodonosor)
                : null;
            if (entry == null || profile == null)
            {
                Debug.LogError("Install Ship Livery: no ship entry / colour profile in 04.Data/Customize — " +
                               "run Tools → FiniteRunner → Create Customize Vehicle Scene first.");
                return;
            }

            int installed = 0;
            foreach (string path in ShipPrefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"Install Ship Livery: no prefab at {path}, skipped.");
                    continue;
                }
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform model = FindModel(root, entry.slotOverrides.Count);
                    if (model == null)
                    {
                        Debug.LogWarning($"Install Ship Livery: {path} has no renderer with {entry.slotOverrides.Count} material slots, skipped.");
                        continue;
                    }
                    var livery = root.GetComponent<VehicleLivery>();
                    if (livery == null) livery = root.AddComponent<VehicleLivery>();
                    livery.Configure(entry.model.modelId, profile, model, entry.slotOverrides);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    installed++;
                    Debug.Log($"Install Ship Livery: {path} — painting '{model.name}' as {entry.model.modelId}.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Install Ship Livery: {installed} prefab(s) wear the saved colours.");
        }

        // The model is the nested fbx instance holding the renderer with the
        // livery's slot count (the ship's one six-slot renderer), else that
        // renderer's own object.
        static Transform FindModel(GameObject root, int slotCount)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is not MeshRenderer && r is not SkinnedMeshRenderer) continue;
                if (r.sharedMaterials.Length != slotCount) continue;
                GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                return instanceRoot != null && instanceRoot != root ? instanceRoot.transform : r.transform;
            }
            return null;
        }
    }
}
