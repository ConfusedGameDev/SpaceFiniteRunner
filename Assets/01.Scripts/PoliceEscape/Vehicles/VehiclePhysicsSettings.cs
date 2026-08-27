using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The one switch between the two vehicle physics backends: the built-in
    /// WheelCollider sim (CarController's own drive/grip code) and Edy's
    /// Vehicle Physics (EVP5), installed side by side for comparison. Every
    /// car — player, police and traffic — reads it once when its rig comes
    /// alive (CarController.Start), so flipping it affects newly spawned cars;
    /// reload the scene to convert the whole fleet. Loaded from Resources so
    /// no scene wiring is needed; without the asset the built-in sim runs.
    /// </summary>
    [CreateAssetMenu(menuName = "PoliceEscape/Vehicle Physics Settings", fileName = "PoliceEscape_VehiclePhysics")]
    public class VehiclePhysicsSettings : ScriptableObject
    {
        const string ResourcePath = "PoliceEscape_VehiclePhysics";

        public enum Backend { BuiltIn, EdyVehiclePhysics }

        [Tooltip("Which physics sim drives every car. Built In = the project's own WheelCollider code; " +
                 "Edy Vehicle Physics = the EVP5 plugin on the same wheels. Applied to cars as they spawn — " +
                 "reload the scene to convert cars already on the road.")]
        [EnumToggleButtons]
        public Backend backend = Backend.BuiltIn;

        static VehiclePhysicsSettings cached;

        /// <summary>The shipped asset from Resources, or an in-memory default (built-in backend).</summary>
        public static VehiclePhysicsSettings Current
        {
            get
            {
                if (cached == null)
                {
                    cached = Resources.Load<VehiclePhysicsSettings>(ResourcePath);
                    if (cached == null) cached = CreateInstance<VehiclePhysicsSettings>();
                }
                return cached;
            }
        }

        /// <summary>True when new car rigs should be driven by EVP5 instead of the built-in sim.</summary>
        public static bool UseEvp => Current.backend == Backend.EdyVehiclePhysics;

        /// <summary>
        /// Convert every live car to the selected backend on the spot — the
        /// debug menu calls this when its toggle row flips, so comparing the
        /// two sims never needs a scene reload. Parked scenery cars
        /// (DefaultVehicle keeps their CarController disabled) are left alone;
        /// they are pure rigidbodies in both modes.
        /// </summary>
        [Button("Apply To Live Cars", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
        public static void ApplyToLiveCars()
        {
            bool evp = UseEvp;
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (car.enabled) car.SetBackend(evp);
        }
    }
}
