using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// One civilian vehicle the traffic system may spawn — a Kenney or
    /// Cyberpunk Megapolis model FBX rigged at runtime by VehicleRigBuilder.
    /// Swapping the model or dragging the weight slider is the whole tuning
    /// workflow, same as the building set; the yaw and scale knobs are what
    /// let a real-metre, +X-facing cyberpunk car share the pool with the
    /// ×1.73 Kenney toys.
    /// </summary>
    [Serializable]
    public class TrafficVehicleDefinition
    {
        [Required, AssetsOnly]
        [Tooltip("Vehicle model with the kit's four named wheels (wheel-front-left …). FBX assets can be assigned directly.")]
        public GameObject model;

        [Tooltip("Relative spawn chance among the listed vehicles.")]
        [PropertyRange(0.01f, 10f)]
        public float weight = 1f;

        [Tooltip("Work vehicles (garbage truck, delivery…) randomly pull to a stop for a few seconds before moving on; everyone else keeps rolling.")]
        public bool stopsRandomly;

        [Tooltip("Yaw applied to the model so it faces the rig's +Z: 0 for the Kenney cars, -90 for the Cyberpunk Megapolis ones (bonnet at +X after import).")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float modelYaw;

        [Tooltip("Scale for this model alone; 0 uses the settings' Model Scale. Real-metre kits (Cyberpunk Megapolis) want 1.")]
        [PropertyRange(0f, 3f)]
        public float scaleOverride;

        /// <summary>The scale this model is rigged at — its own override, else the pool-wide default.</summary>
        public float Scale(float defaultScale) => scaleOverride > 0f ? scaleOverride : defaultScale;
    }
}
