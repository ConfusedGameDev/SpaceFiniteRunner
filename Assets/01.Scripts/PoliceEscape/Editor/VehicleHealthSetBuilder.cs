using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Creates the VehicleHealthSettings asset in Resources (where
    /// <see cref="VehicleHealthSettings.Load"/> finds it) and fills its sprite
    /// lists from the Kenney SmokeAndExplosions pack: white puffs for the
    /// first-warning plume, black smoke for a dying car, the explosion sprites
    /// for the death fireball. An existing asset is never overwritten —
    /// hand-tuned numbers survive a re-run; only sprite lists that are EMPTY
    /// are refilled.
    /// </summary>
    public static class VehicleHealthSetBuilder
    {
        const string ResourcesFolder = "Assets/04.Data/Resources";
        const string AssetPath = ResourcesFolder + "/PoliceEscape_VehicleHealth.asset";
        const string LightSmokeFolder = "Assets/02.Art/05.Particles/SmokeAndExplosions/White puff";
        const string HeavySmokeFolder = "Assets/02.Art/05.Particles/SmokeAndExplosions/Black smoke";
        const string ExplosionFolder = "Assets/02.Art/05.Particles/SmokeAndExplosions/Explosion";

        [MenuItem("Tools/Police Escape/Create Vehicle Health Settings")]
        public static void CreateSettings()
        {
            EnsureFolder(ResourcesFolder);
            var settings = AssetDatabase.LoadAssetAtPath<VehicleHealthSettings>(AssetPath);
            bool isNew = settings == null;
            if (isNew) settings = ScriptableObject.CreateInstance<VehicleHealthSettings>();

            if (settings.lightSmokeTextures == null || settings.lightSmokeTextures.Count == 0)
                settings.lightSmokeTextures = LoadSprites(LightSmokeFolder, "light smoke");
            if (settings.heavySmokeTextures == null || settings.heavySmokeTextures.Count == 0)
                settings.heavySmokeTextures = LoadSprites(HeavySmokeFolder, "heavy smoke");
            if (settings.explosionTextures == null || settings.explosionTextures.Count == 0)
                settings.explosionTextures = LoadSprites(ExplosionFolder, "explosion");

            if (isNew) AssetDatabase.CreateAsset(settings, AssetPath);
            else EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"VehicleHealthSetBuilder: {(isNew ? "created" : "refreshed")} {AssetPath} — " +
                      $"{settings.lightSmokeTextures.Count} light smoke, {settings.heavySmokeTextures.Count} heavy smoke, " +
                      $"{settings.explosionTextures.Count} explosion sprites.", settings);
        }

        /// <summary>Every texture in the folder, in name order.</summary>
        static List<Texture2D> LoadSprites(string folder, string label)
        {
            var sprites = new List<Texture2D>();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"VehicleHealthSetBuilder: no {label} art at {folder} — cars will burn without it.");
                return sprites;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture != null) sprites.Add(texture);
            }
            sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return sprites;
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
