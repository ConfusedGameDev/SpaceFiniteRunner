using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Look of the police cruiser visual — materials and proportions for the
    /// primitive-built cop car. All cruiser look tunables live on this asset;
    /// add new knobs here, not on the PolicePatrol component.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniteRunner/Patrol Visual Settings")]
    public class PatrolVisualSettings : ScriptableObject
    {
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
        [Tooltip("Blue light; the red one is mirrored on X.")]
        public Vector3 lightPosition = new(0.55f, 1.35f, -0.4f);
        [PropertyRange(0.1f, 2f)] public float lightDiameter = 0.7f;
    }
}
