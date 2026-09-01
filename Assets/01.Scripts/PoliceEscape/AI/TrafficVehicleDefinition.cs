using System;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// One civilian vehicle the traffic system may spawn, from either of two
    /// sources: a finished NPC <see cref="prefab"/> (CarController +
    /// TrafficCarInput on its root — the EVP demo cars converted by
    /// "Build EVP Traffic Prefabs" — instantiated as-is, physics and all), or
    /// a bare <see cref="model"/> FBX (Kenney / Cyberpunk Megapolis) rigged at
    /// runtime by VehicleRigBuilder. Swapping the source or dragging the
    /// weight slider is the whole tuning workflow, same as the building set;
    /// the yaw and scale knobs are what let a real-metre, +X-facing cyberpunk
    /// model share the pool with the ×1.73 Kenney toys. The identity fields
    /// name the car for gameplay: a model gets exactly these, a prefab keeps
    /// its own unless a kind is set here.
    /// </summary>
    [Serializable]
    public class TrafficVehicleDefinition
    {
        [InfoBox("Assign a prefab or a model.", InfoMessageType.Error, "@prefab == null && model == null")]
        [AssetsOnly]
        [Tooltip("A finished NPC prefab — CarController and TrafficCarInput on its root, its own wheels and physics. When set, the model fields are ignored.")]
        public GameObject prefab;

        [AssetsOnly, HideIf("@prefab != null")]
        [Tooltip("Vehicle model with the kit's four named wheels (wheel-front-left …), rigged at spawn. FBX assets can be assigned directly.")]
        public GameObject model;

        [Tooltip("Relative spawn chance among the listed vehicles.")]
        [PropertyRange(0.01f, 10f)]
        public float weight = 1f;

        [Tooltip("Work vehicles (garbage truck, delivery…) randomly pull to a stop for a few seconds before moving on; everyone else keeps rolling.")]
        public bool stopsRandomly;

        [HideIf("@prefab != null")]
        [Tooltip("Yaw applied to the model so it faces the rig's +Z: 0 for the Kenney cars, -90 for the Cyberpunk Megapolis ones (bonnet at +X after import).")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float modelYaw;

        [HideIf("@prefab != null")]
        [Tooltip("Scale for this model alone; 0 uses the settings' Model Scale. Real-metre kits (Cyberpunk Megapolis) want 1.")]
        [PropertyRange(0f, 3f)]
        public float scaleOverride;

        [Tooltip("What this car is. For a model this is stamped on the spawned car; for a prefab it overrides the prefab's own identity only when not Unknown.")]
        public VehicleKind kind;

        [Tooltip("The paint as a named colour, for gameplay logic.")]
        public VehiclePaint paint;

        [Tooltip("The paint as an actual colour — a swatch for UI or tinting.")]
        public Color color = Color.white;

        /// <summary>True when there is something to spawn — a prefab or a model.</summary>
        public bool IsSpawnable => prefab != null || model != null;

        /// <summary>The identity this entry authors (kind Unknown when the prefab's own should stand).</summary>
        public VehicleIdentity Identity => new(kind, paint, color);

        /// <summary>The scale this model is rigged at — its own override, else the pool-wide default.</summary>
        public float Scale(float defaultScale) => scaleOverride > 0f ? scaleOverride : defaultScale;
    }
}
