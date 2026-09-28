using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Store;
namespace ConfusedGameDev.FiniteRunner.Customize
{
    /// <summary>
    /// One vehicle the customize scene can paint: the Store's model entry
    /// (id, name, prefab, preview seat) plus the materials to put in the
    /// model's slots before it is painted. The ship's raw fbx carries its own
    /// embedded materials; the overrides give it the Store's six RealToon
    /// parts, so each part gets its own slider.
    /// </summary>
    [System.Serializable]
    public class CustomizeVehicleEntry
    {
        [Tooltip("Id, name, prefab and preview seat — the Store's model entry. modelId keys the saved colours: never rename.")]
        [HideLabel, InlineProperty]
        public StoreModel model = new();

        [Tooltip("Materials for the first renderer that has exactly this many slots, in slot order. Empty = keep the prefab's own materials.")]
        [AssetsOnly]
        public List<Material> slotOverrides = new();
    }

    /// <summary>
    /// The customize-vehicle test scene's knobs and vehicle list, in one
    /// asset (the project's "tunables live in ScriptableObjects" rule): the
    /// turntable and zoom feel, the hue slider's speed and how a hue becomes a
    /// colour, and the swap transition. Read live, never written by play.
    /// </summary>
    [CreateAssetMenu(fileName = "CustomizeSettings", menuName = "FiniteRunner/Customize/Customize Settings")]
    public class CustomizeSettings : ScriptableObject
    {
        [Title("Vehicles")]
        [Tooltip("The vehicles D-pad up / down cycles through, in order. The first is shown on load.")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        public List<CustomizeVehicleEntry> vehicles = new();

        [Title("Turntable")]
        [Tooltip("Degrees per second at full right-stick X.")]
        [PropertyRange(30f, 360f)] public float stickDegreesPerSecond = 120f;
        [Tooltip("Degrees per pixel of a left-mouse drag.")]
        [PropertyRange(0.05f, 1f)] public float dragDegreesPerPixel = 0.25f;
        [Tooltip("Idle spin once the input stops. 0 = no spin.")]
        [PropertyRange(0f, 60f)] public float autoSpinDegreesPerSecond = 12f;
        [Tooltip("Seconds without rotate input before the idle spin resumes.")]
        [PropertyRange(0.5f, 10f)] public float idleResumeSeconds = 3f;
        [Tooltip("Yaw the view resets to (right stick press).")]
        [PropertyRange(-180f, 180f)] public float defaultYaw = -35f;

        [Title("Zoom")]
        [Tooltip("Camera distance to the vehicle, near/far, in metres.")]
        [MinMaxSlider(1f, 20f, true)] public Vector2 zoomBand = new(3.5f, 11f);
        [Tooltip("Metres per second at full right-stick Y.")]
        [PropertyRange(1f, 20f)] public float zoomSpeed = 6f;
        [Tooltip("Metres per mouse-wheel notch.")]
        [PropertyRange(0.1f, 3f)] public float zoomPerScrollNotch = 0.6f;
        [Tooltip("Seconds the camera takes to settle on a new distance.")]
        [PropertyRange(0.01f, 0.5f)] public float zoomSmoothing = 0.08f;

        [Title("Colour")]
        [Tooltip("Hue turns per second at full left-stick X (1 = the whole bar).")]
        [PropertyRange(0.05f, 1.5f)] public float hueSpeed = 0.35f;
        [Tooltip("Stick deflection that counts as a push. How a hue becomes a colour is the VehicleColorProfile's, shared with every VehicleLivery.")]
        [PropertyRange(0.05f, 0.6f)] public float stickDeadZone = 0.25f;

        [Title("Swap")]
        [Tooltip("Seconds the slider column takes to slide out (and again to slide back in) on a vehicle change.")]
        [PropertyRange(0.05f, 1f)] public float swapSlideSeconds = 0.22f;
        [Tooltip("How far left the column slides out, in canvas units.")]
        [PropertyRange(200f, 1600f)] public float swapSlideDistance = 900f;

        /// <summary>Nearest zoom distance.</summary>
        public float ZoomMin => zoomBand.x;
        /// <summary>Farthest zoom distance.</summary>
        public float ZoomMax => zoomBand.y;
    }
}
