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

        // ------------------------------------------------------------- effects
        // Sound and skid-mark assets for the player's car in EVP mode, wired to
        // the EVP5 demo assets (the L200's setup). Referenced from here rather
        // than loaded by path because none of them live in Resources.
        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Engine loop, pitched over simulated RPM and gears.")]
        public AudioClip engineClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Tire skid loop — brakes, handbrake and hard cornering.")]
        public AudioClip skidClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Rumble loop while rolling over soft (offroad) surfaces.")]
        public AudioClip offroadClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Body scraping along a hard surface (wall grinding).")]
        public AudioClip hardDragClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Body dragging over a soft surface.")]
        public AudioClip softDragClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Wind noise over speed.")]
        public AudioClip windClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("One-shot suspension bump.")]
        public AudioClip bumpClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("One-shot hard collision.")]
        public AudioClip hardImpactClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("One-shot soft collision.")]
        public AudioClip softImpactClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("One-shot scratch while grinding a wall.")]
        public AudioClip scratchClip;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Skid-mark decal material (EVP's URP-ported tire marks shader). No material = no marks.")]
        public Material tireMarksMaterial;

        [TitleGroup("EVP effects (player car)")]
        [Tooltip("Burnout rev loop for the BUILT-IN backend (EVP mode revs its live engine loop instead). Empty = the engine clip pitched up.")]
        public AudioClip burnoutClip;

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
