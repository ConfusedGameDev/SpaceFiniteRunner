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

        [TitleGroup("Chase")]
        [Tooltip("Share of every speed-up the ship collects that the patrol gets at once (0.7 = a +100 km/h orb also gives the patrol +70 km/h). " +
                 "Measured on the ship's actual gain (after its weight), so boosts can't buy the breathing room the rubber band's catch-up accel would otherwise leave. Brakes are never shared. 0 = off.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("x ship boost", true)]
        public float boostShare = 0.7f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral speed the patrol's full steer settles at — the same force-against-drag steering as the ship. Below the ship's, so the player can out-dodge it.")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float lateralSpeed = 22f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral drag, 1/s: how fast its steering settles.")]
        [PropertyRange(0.01f, 30f)]
        public float handlingResponse = 6f;

        [TitleGroup("Handling")]
        [Tooltip("Grip at a standstill on a flat sweep (the ship's rule). The driver brakes to the speed this grip holds, so lower = it slows more for flat sweeps.")]
        [PropertyRange(0f, 500f), SuffixLabel("m/s²", true)]
        public float gripBase = 50f;

        [TitleGroup("Handling")]
        [Tooltip("Extra grip per m/s of speed (the ship's rule).")]
        [PropertyRange(0f, 3f), SuffixLabel("m/s² per m/s", true)]
        public float gripPerSpeed = 0.5f;

        [TitleGroup("Handling")]
        [Tooltip("How hard the patrol can brake for a flat sweep ahead.")]
        [PropertyRange(0f, 500f), SuffixLabel("m/s per s", true)]
        public float brakeDecel = 140f;

        [TitleGroup("Driver")]
        [Tooltip("How far ahead (in seconds at its speed) the driver looks for a flat sweep to brake for.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float curveLookaheadSeconds = 4f;

        [TitleGroup("Driver")]
        [Tooltip("How far ahead (seconds) it looks for boost orbs it can still reach.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float orbLookaheadSeconds = 2.5f;

        [TitleGroup("Driver")]
        [Tooltip("How strongly a reachable orb pulls its line away from the ship's: 0 = it only ever chases, 1 = it goes straight for the orb. Fades out as the gap closes — up close it wants the ship.")]
        [PropertyRange(0f, 1f)]
        public float orbSeekWeight = 0.6f;

        [TitleGroup("Driver")]
        [Tooltip("Share of an orb's boost the patrol gains when it collects one ITSELF (the orb is used up). Separate from Boost Share, which is its cut of the SHIP's pickups.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("x orb boost", true)]
        public float orbBoostShare = 0.5f;

        [TitleGroup("Driver")]
        [Tooltip("How far ahead (seconds) it decides about a ramp in its line: round it if the sideways travel still fits, otherwise line up and jump it.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float rampLookaheadSeconds = 2.5f;

        [TitleGroup("Distances")]
        [Tooltip("Meters behind the start line the patrol launches from.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float startGap = 250f;

        [TitleGroup("Distances")]
        [Tooltip("Gap inside which the patrol can catch the ship. Inside it the patrol stops gaining and sits on the ship's tail, steering for it. Keep it below the warn distance.")]
        [PropertyRange(0f, 100f), SuffixLabel("m", true)]
        public float catchDistance = 10f;

        [TitleGroup("Distances")]
        [Tooltip("Inside the catch distance, the patrol must ALSO be within this far of the ship across the track to catch it — so a last-moment dodge works.")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        public float catchLateral = 18f;

        [TitleGroup("Distances")]
        [Tooltip("...but dodging is not an escape: this long inside the catch distance is a catch whatever the sideways gap.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float sustainedCatchSeconds = 1.5f;

        [TitleGroup("Distances")]
        [Tooltip("Gap below which the patrol's proximity line (once per approach) and the proximity rumble kick in.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float warnDistance = 130f;
    }
}
