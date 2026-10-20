using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The tunables of <see cref="TrackReveal"/>: where along the road ahead
    /// the track assembles itself, and how a piece flies in. Distances are
    /// metres of TRACK ahead of the reveal's reference (the ship, or a race's
    /// leader), never seconds: at Light Speed a time-based band would sit
    /// wholly inside the fog, while a distance band just plays faster. Keep
    /// <see cref="startAhead"/> inside the distance fog's end, or nothing of
    /// it is ever seen. Drawn inline on the component, read live, never
    /// cloned; gameplay never writes it.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackReveal_Settings", menuName = "FiniteRunner/Track Reveal Settings")]
    public class TrackRevealSettings : ScriptableObject
    {
        [TitleGroup("Reveal")]
        [Tooltip("Master switch. Off, the whole track stands assembled, as before.")]
        public bool revealEnabled = true;

        [TitleGroup("Reveal")]
        [Tooltip("Metres ahead of the reference where a piece STARTS rising. The runner's fog ends at ~480 m: keep this inside it.")]
        [PropertyRange(50f, 1500f), SuffixLabel("m", true)]
        public float startAhead = 450f;

        [TitleGroup("Reveal")]
        [Tooltip("Metres a piece takes to seat once the front passes it. Seated at Start Ahead − Band (+ its stagger).")]
        [PropertyRange(10f, 1000f), SuffixLabel("m", true)]
        public float band = 300f;

        [TitleGroup("Reveal")]
        [Tooltip("Random extra delay per piece, metres: pieces land one by one instead of as a wall.")]
        [PropertyRange(0f, 300f), SuffixLabel("m", true)]
        public float stagger = 60f;

        [TitleGroup("Reveal")]
        [Tooltip("While the reference is slower than this, the front creeps ahead on its own until the visible road is seated (Start Ahead + Band) — so nothing hangs mid-air at a standstill or on the start line.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m/s", true)]
        public float creepSpeed = 150f;

        [TitleGroup("Motion")]
        [Tooltip("How far below its rest a piece starts, metres along its own up.")]
        [PropertyRange(0f, 200f), SuffixLabel("m", true)]
        public float drop = 40f;

        [TitleGroup("Motion")]
        [Tooltip("How far a piece starts tipped about its own X axis, degrees (the side is random per piece).")]
        [PropertyRange(0f, 90f), SuffixLabel("°", true)]
        public float tiltDegrees = 25f;

        [TitleGroup("Motion")]
        [Tooltip("Scale a piece starts at (1 = full size all the way).")]
        [PropertyRange(0f, 1f)]
        public float startScale = 0.3f;

        [TitleGroup("Motion")]
        [Tooltip("Overshoot of the landing: 0 = a plain ease out, higher = the piece rises past its rest and settles back.")]
        [PropertyRange(0f, 3f)]
        public float overshoot = 1.2f;
    }
}
