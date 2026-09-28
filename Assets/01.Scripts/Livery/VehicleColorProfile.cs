using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Livery
{
    /// <summary>One painted part: the hue on the bar, or untinted = the authored colour.</summary>
    [Serializable]
    public class MaterialColorEntry
    {
        public string slotId;
        public float hue;
        public bool tinted;
    }

    /// <summary>Every painted part of one vehicle, keyed by the vehicle's model id.</summary>
    [Serializable]
    public class VehicleColorEntry
    {
        public string vehicleId;
        public List<MaterialColorEntry> materials = new();
    }

    /// <summary>
    /// The saved paint jobs: plain JsonUtility data (lists, no dictionaries —
    /// the profile's shape). A part with no entry shows its authored colour.
    /// </summary>
    [Serializable]
    public class VehicleColorData
    {
        public int version = 1;
        public List<VehicleColorEntry> vehicles = new();

        /// <summary>The saved entry for one part, or null.</summary>
        public MaterialColorEntry Find(string vehicleId, string slotId)
        {
            foreach (VehicleColorEntry v in vehicles)
            {
                if (v == null || v.vehicleId != vehicleId) continue;
                foreach (MaterialColorEntry m in v.materials)
                    if (m != null && m.slotId == slotId) return m;
            }
            return null;
        }

        /// <summary>Writes one part's colour, creating its entries as needed.</summary>
        public void Set(string vehicleId, string slotId, float hue, bool tinted)
        {
            VehicleColorEntry vehicle = vehicles.Find(v => v != null && v.vehicleId == vehicleId);
            if (vehicle == null)
            {
                vehicle = new VehicleColorEntry { vehicleId = vehicleId };
                vehicles.Add(vehicle);
            }
            MaterialColorEntry entry = vehicle.materials.Find(m => m != null && m.slotId == slotId);
            if (entry == null)
            {
                entry = new MaterialColorEntry { slotId = slotId };
                vehicle.materials.Add(entry);
            }
            entry.hue = hue;
            entry.tinted = tinted;
        }

        /// <summary>Paints every slot of <paramref name="target"/> from this data: its saved hue, else its authored colour.</summary>
        public void ApplyTo(VehiclePaintTarget target, string vehicleId)
        {
            if (target == null) return;
            foreach (VehiclePaintTarget.PaintSlot slot in target.Slots)
            {
                MaterialColorEntry saved = Find(vehicleId, slot.Id);
                if (saved != null && saved.tinted) slot.SetHue(saved.hue);
                else slot.ResetToDefault();
            }
        }

        /// <summary>A deep copy, so the working set never aliases the saved one.</summary>
        public VehicleColorData Clone() => JsonUtility.FromJson<VehicleColorData>(JsonUtility.ToJson(this)) ?? new VehicleColorData();
    }

    /// <summary>
    /// Where the paint jobs are saved and loaded, and the rule that turns a
    /// saved hue into a colour (so the customize scene and every vehicle that
    /// wears a <see cref="VehicleLivery"/> paint alike). The runtime copy is
    /// <c>persistentDataPath/vehicle_colors.json</c> (written tmp-then-swap,
    /// like the player profile); in the Editor an explicit SAVE also writes
    /// the same data into this asset so it can be inspected and committed.
    /// Load prefers the JSON and falls back to the asset, so a build ships
    /// with the asset's colours. Only the player's SAVE writes either — play
    /// never mutates the asset on its own.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleColorProfile", menuName = "FiniteRunner/Customize/Vehicle Color Profile")]
    public class VehicleColorProfile : ScriptableObject
    {
        const string FileName = "vehicle_colors.json";

        [Tooltip("Saturation floor of a painted part — keeps white and grey parts from staying grey.")]
        [Range(0f, 1f)] public float minSaturation = 0.75f;
        [Tooltip("Brightness floor of a painted part — keeps black parts (glass) from staying black.")]
        [Range(0f, 1f)] public float minValue = 0.55f;

        [SerializeField] VehicleColorData data = new();

        /// <summary>The JSON save's full path.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>A fresh copy of the last save (the JSON, else the asset's data).</summary>
        public VehicleColorData LoadSaved() => Load(this);

        /// <summary>A fresh copy of the last save: the JSON, else <paramref name="fallback"/>'s data, else empty.</summary>
        public static VehicleColorData Load(VehicleColorProfile fallback)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<VehicleColorData>(File.ReadAllText(FilePath));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{nameof(VehicleColorProfile)}: could not read {FilePath} — using the asset's colours. {e.Message}");
            }
            return fallback != null && fallback.data != null ? fallback.data.Clone() : new VehicleColorData();
        }

        /// <summary>Writes <paramref name="colors"/> as the new last save.</summary>
        public void Save(VehicleColorData colors)
        {
            if (colors == null) return;
            string json = JsonUtility.ToJson(colors, true);
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(VehicleColorProfile)}: could not write {FilePath}. {e.Message}");
            }

#if UNITY_EDITOR
            data = colors.Clone();
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#endif
        }
    }
}
