using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Look of the police cruiser visual: an authored model prefab when one is
    /// assigned, else the primitive-built cop car the materials and proportions
    /// below describe. The two light spheres are always built from code, so
    /// the light bar blinks and the ram kick reads on either. All cruiser look
    /// tunables live on this asset; add new knobs here, not on the
    /// PolicePatrol component.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniteRunner/Patrol Visual Settings")]
    public class PatrolVisualSettings : ScriptableObject
    {
        [TitleGroup("Model")]
        [Tooltip("The cruiser's model. Instantiated under the Visual child with its own transform RESET (a prefab dragged out of a scene carries that scene's pose), every collider under it stripped — the patrol never has one. Empty = the primitive cop car below.")]
        public GameObject modelPrefab;

        [TitleGroup("Model")]
        [Tooltip("Scale of the model prefab, on top of the overall scale below — THE size knob for the cruiser (the prefab's own transform is discarded, so scaling the prefab does nothing). " +
                 "The light bar's position and diameter scale with it. Read once when the patrol is built, so a change bites on the next run, not live. " +
                 "The shipped car is 5.18 m long at 1: at 2.3 × the 1.6 overall it is 19 m, the ship's own length.")]
        [PropertyRange(0.1f, 5f)] public float modelScale = 1f;

        [TitleGroup("Model")]
        [Tooltip("Yaw that turns the model's nose down the track (+Z). A model built along X wants ±90.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)] public float modelYawOffset = -90f;

        [TitleGroup("Model")]
        [Tooltip("Where the model sits relative to the cruiser's flight line, in the Visual child's local metres.")]
        public Vector3 modelLocalOffset = Vector3.zero;

        [TitleGroup("Materials")]
        [Required] public Material bodyMaterial;
        [Required] public Material trimMaterial;
        [Required] public Material redLightMaterial;
        [Required] public Material blueLightMaterial;

        [TitleGroup("Proportions")]
        [PropertyRange(0.5f, 4f)] public float overallScale = 1.6f;
        public Vector3 hullSize = new(3f, 0.9f, 6f);
        public Vector3 cabinPosition = new(0f, 0.7f, -0.4f);
        public Vector3 cabinSize = new(2f, 0.7f, 2.6f);
        [Tooltip("Right skid; the left one is mirrored on X.")]
        public Vector3 skidPosition = new(1.8f, -0.1f, 0f);
        public Vector3 skidSize = new(0.6f, 0.5f, 4f);
        [Tooltip("Blue light; the red one is mirrored on X. With a model prefab these are in the MODEL's own metres (multiplied by its scale, like the diameter), so they stay on the roof whatever size the car is; with the primitive car they are Visual-local.")]
        public Vector3 lightPosition = new(0.55f, 1.35f, -0.4f);
        [PropertyRange(0.1f, 2f)] public float lightDiameter = 0.7f;
    }
}
