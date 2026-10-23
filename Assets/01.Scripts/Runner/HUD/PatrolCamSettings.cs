using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Tunables of the debug patrol camera (<see cref="PatrolCamFeed"/>): the
    /// picture-in-picture's place and size on the 1080p reference screen, the
    /// follow camera's framing, and the NO SIGNAL dead screen. All patrol cam
    /// knobs live on this asset — add new ones here, not on the component.
    /// Read live, so a slider moves the feed while it plays.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniteRunner/Patrol Cam Settings")]
    public class PatrolCamSettings : ScriptableObject
    {
        [TitleGroup("Feed")]
        [Tooltip("Show the feed from the first frame. Off = hidden until F2.")]
        public bool startVisible;
        [Tooltip("The feed's plate on the 1920×1080 reference screen, anchored top-left.")]
        [PropertyRange(160f, 960f)] public float width = 440f;
        [PropertyRange(90f, 540f)] public float height = 248f;
        [Tooltip("Distance of the plate from the screen's left and top edges, px.")]
        [PropertyRange(0f, 200f)] public float margin = 24f;
        [Tooltip("Width of the frame round the picture, px.")]
        [PropertyRange(0f, 12f)] public float frameWidth = 3f;
        public Color frameColor = new(1f, 0.25f, 0.2f, 0.9f);

        [TitleGroup("Render texture")]
        [Tooltip("Read when the feed is built (the next run): the picture's resolution.")]
        [PropertyRange(160, 1280)] public int textureWidth = 640;
        [PropertyRange(90, 720)] public int textureHeight = 360;

        [TitleGroup("Camera")]
        [PropertyRange(20f, 100f)] public float fieldOfView = 55f;
        [Tooltip("Where the camera sits in the cruiser's own frame (x right, y up, z forward — negative is behind).")]
        public Vector3 followOffset = new(0f, 12f, -38f);
        [Tooltip("Height above the cruiser's flight line the camera aims at, m.")]
        [PropertyRange(0f, 20f)] public float lookHeight = 3f;
        [Tooltip("How fast the camera swings round after the cruiser's heading (1/s). The position is never lagged — at Light Speed a lagged camera is a kilometre behind.")]
        [PropertyRange(0.5f, 30f)] public float followSharpness = 6f;
        [PropertyRange(0.1f, 5f)] public float nearClip = 0.5f;
        [PropertyRange(200f, 5000f)] public float farClip = 2000f;

        [TitleGroup("No signal")]
        [InfoBox("Shown when there is no patrol to film: the chase is off, it ran off the end of the track, or it is hidden across a kill. Empty = the dark plate with — NO SIGNAL — over static.")]
        [PreviewField(64)] public Texture noSignalImage;
        [Tooltip("Strength of the static behind NO SIGNAL (0 = none).")]
        [PropertyRange(0f, 1f)] public float staticStrength = 0.18f;
    }
}
