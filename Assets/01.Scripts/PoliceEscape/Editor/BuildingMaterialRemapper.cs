using System.IO;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Points every building FBX at the shared colormap material via importer
    /// remaps: each source material slot on each model is remapped to
    /// Buildings/colormap.mat, so the whole kit is tinted from one material
    /// (and the SRP Batcher gets one shader state for the entire city).
    /// Remaps live in the .meta files, so they survive reimports; re-run
    /// after dropping new building FBXs into the folder.
    /// </summary>
    public static class BuildingMaterialRemapper
    {
        const string BuildingsFolder = "Assets/02.Art/01.Models/InfiniteCity/Buildings";
        const string MaterialPath = "Assets/02.Art/02.Materials/InfiniteCity/colormap.mat";

        [MenuItem("Tools/Police Escape/Remap Building Materials")]
        public static void Remap()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError($"BuildingMaterialRemapper: material not found at {MaterialPath}.");
                return;
            }

            int models = 0, slots = 0;
            foreach (string file in Directory.GetFiles(BuildingsFolder, "*.fbx"))
            {
                string path = file.Replace('\\', '/');
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                bool changed = false;
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>())
                foreach (Material sourceMaterial in renderer.sharedMaterials)
                {
                    if (sourceMaterial == null || sourceMaterial == material) continue;
                    importer.AddRemap(
                        new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceMaterial.name),
                        material);
                    changed = true;
                    slots++;
                }

                if (!changed) continue;
                importer.SaveAndReimport();
                models++;
            }
            Debug.Log($"BuildingMaterialRemapper: remapped {slots} material slot(s) on {models} model(s) to {MaterialPath}.");
        }
    }
}
