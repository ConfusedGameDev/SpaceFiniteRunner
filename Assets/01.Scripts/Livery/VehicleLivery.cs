using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Livery
{
    /// <summary>
    /// Wears the player's saved paint job on a vehicle prefab, in any scene.
    /// On Awake it puts the livery's materials on the <see cref="model"/>
    /// (the same slot overrides the customize scene paints, so the vehicle
    /// looks here as it did there), makes the model paintable
    /// (<see cref="VehiclePaintTarget"/>) and applies the last save for its
    /// <see cref="vehicleId"/>. Reads the save once and never writes it.
    /// Anything that swaps the model's materials later (the respawn blink,
    /// the dash ghosts) reads them live, so the paint comes back with them.
    /// </summary>
    public class VehicleLivery : MonoBehaviour
    {
        [Tooltip("The model id the customize scene saves this vehicle's colours under (e.g. ship.nabucodonosor). Never rename.")]
        [SerializeField] string vehicleId;

        [Tooltip("The save's fallback asset and the hue-to-colour rule. Without it only the JSON save is read.")]
        [SerializeField] VehicleColorProfile profile;

        [Tooltip("The model to paint — only mesh renderers under it are touched. Empty = this object.")]
        [SerializeField] Transform model;

        [Tooltip("Materials for the model's renderer with exactly this many slots, in slot order — the customize scene's list for this vehicle.")]
        [SerializeField] List<Material> slotOverrides = new();

        /// <summary>The painted model, or null before Awake.</summary>
        public VehiclePaintTarget Target { get; private set; }

        public string VehicleId => vehicleId;

        /// <summary>Wires the livery (the editor installer's entry).</summary>
        public void Configure(string id, VehicleColorProfile colorProfile, Transform modelRoot, IList<Material> overrides)
        {
            vehicleId = id;
            profile = colorProfile;
            model = modelRoot;
            slotOverrides = overrides != null ? new List<Material>(overrides) : new List<Material>();
        }

        void Awake()
        {
            Transform root = model != null ? model : transform;
            Target = root.GetComponent<VehiclePaintTarget>();
            if (Target == null) Target = root.gameObject.AddComponent<VehiclePaintTarget>();
            Target.Initialize(slotOverrides, profile);
            Reload();
        }

        /// <summary>Re-reads the save and repaints (a scene that just saved can call it).</summary>
        public void Reload()
        {
            if (Target == null) return;
            VehicleColorProfile.Load(profile).ApplyTo(Target, vehicleId);
        }
    }
}
