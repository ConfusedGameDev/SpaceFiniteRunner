using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Every chase tunable of the police patrol in one designer-facing asset,
    /// assigned to the scene's <see cref="PolicePatrol"/> object. All speeds
    /// are stored in m/s like the rest of the sim (UI converts with ×3.6).
    /// The patrol clones this at init and only ever reads the clone, so the
    /// debug menu can tweak a live chase without touching the asset on disk.
    /// Every stat is an Odin slider with a hand-picked range, same style as
    /// <see cref="ShipDefinition"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "PatrolDefinition", menuName = "FiniteRunner/Patrol Definition")]
    public class PatrolDefinition : ScriptableObject
    {
        [TitleGroup("Chase")]
        [Tooltip("Launch speed and the rubber band's initial floor — the patrol never goes slower than this. Keep it above the ship's launch speed.")]
        [PropertyRange(1f, 600f), SuffixLabel("m/s", true)]
        public float baseSpeed = 97f;

        [TitleGroup("Chase")]
        [Tooltip("How much the rubber band's floor grows per second — the chase tightens the longer the run lasts.")]
        [PropertyRange(0f, 15f), SuffixLabel("m/s per s", true)]
        public float ramp = 0.8f;

        [TitleGroup("Chase")]
        [Tooltip("Rubber band: the patrol chases the ship's current speed times this factor (1.05 = always 5% faster), but never below the floor above.")]
        [PropertyRange(0.5f, 2f), SuffixLabel("x ship speed", true)]
        public float rubberBand = 1.05f;

        [TitleGroup("Chase")]
        [Tooltip("How fast the patrol's speed adapts toward its rubber-band target. Lower = boosts buy more breathing room before the patrol matches them.")]
        [PropertyRange(0.5f, 150f), SuffixLabel("m/s per s", true)]
        public float catchUpAccel = 16.7f;

        [TitleGroup("Distances")]
        [Tooltip("Meters behind the start line the patrol launches from.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float startGap = 250f;

        [TitleGroup("Distances")]
        [Tooltip("Gap that counts as caught — the run is over. Keep it below the warn distance.")]
        [PropertyRange(0f, 100f), SuffixLabel("m", true)]
        public float catchDistance = 10f;

        [TitleGroup("Distances")]
        [Tooltip("Gap below which the patrol's proximity line (once per approach) and the proximity rumble kick in.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float warnDistance = 130f;
    }
}
