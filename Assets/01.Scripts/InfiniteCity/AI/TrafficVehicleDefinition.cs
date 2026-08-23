using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// One civilian vehicle the traffic system may spawn — a Kenney model
    /// FBX rigged at runtime by VehicleRigBuilder. Swapping the model or
    /// dragging the weight slider is the whole tuning workflow, same as the
    /// building set.
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
    }
}
