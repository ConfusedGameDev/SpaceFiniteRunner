using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("How close across the track counts as ALONGSIDE — the band an attack run has to hold, and the band the ship has to break out of to shake one off. " +
                 "With the duel off it is the old catch width instead: inside the catch distance AND this far across is an arrest.")]
        [FormerlySerializedAs("catchLateral")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        public float alongsideLateral = 18f;

        [TitleGroup("Distances")]
        [Tooltip("Tailing the ship this long inside the catch distance is an arrest. Suspended for the whole of an attack run, so it only ever punishes a patrol that sits there WITHOUT committing — keep it long.")]
        [PropertyRange(0f, 20f), SuffixLabel("s", true)]
        public float sustainedCatchSeconds = 7f;

        [TitleGroup("Distances")]
        [Tooltip("Gap below which the patrol's proximity line (once per approach) and the proximity rumble kick in.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float warnDistance = 130f;

        [TitleGroup("Duel")]
        [Tooltip("How much FASTER than the ship the patrol drives while committing to an attack run. This burst is what closes the gap — and it is the telegraph, so keep it visible.")]
        [PropertyRange(1f, 2f), SuffixLabel("x ship speed", true)]
        public float attackRunOverdrive = 1.15f;

        [TitleGroup("Duel")]
        [Tooltip("The patrol only starts an attack run once the gap is inside this. Above it the ordinary rubber band does the work; below it the overdrive takes over.")]
        [PropertyRange(50f, 1500f), SuffixLabel("m", true)]
        public float commitFromDistance = 450f;

        [TitleGroup("Duel")]
        [Tooltip("Minimum seconds between attack runs, counted from the END of the last one. The single number that decides how much of a run is spent duelling.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float commitIntervalSeconds = 12f;

        [TitleGroup("Duel")]
        [Tooltip("An attack run that has not drawn level within this gives up and goes on cooldown — a ship that keeps boosting away is never chased forever.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float commitTimeoutSeconds = 15f;

        [TitleGroup("Duel")]
        [Tooltip("The longitudinal gap at which the patrol counts as alongside and the exchange begins.")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        public float alongsideDistance = 12f;

        [TitleGroup("Duel")]
        [Tooltip("How far to the side of the ship the patrol drives during a run — beside it, not behind it. Keep it well inside the alongside width.")]
        [PropertyRange(0f, 30f), SuffixLabel("m", true)]
        public float flankOffsetMeters = 7f;

        [TitleGroup("Duel")]
        [Tooltip("How long the patrol holds the flank before it shoves. The tug of war takes this hold over later.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float alongsideHoldSeconds = 1.2f;

        [TitleGroup("Duel")]
        [Tooltip("How long the ship has to hold outside the alongside width to break the run off. Short, so a decisive dodge works and a wobble does not.")]
        [PropertyRange(0f, 3f), SuffixLabel("s", true)]
        public float abortGraceSeconds = 0.4f;

        [TitleGroup("Duel")]
        [Tooltip("How fast the patrol drives relative to the ship while backing off after a run — below 1 so it actually opens a gap instead of sitting on the bumper. This is what makes the chase a rhythm rather than a permanent tailgate.")]
        [PropertyRange(0.5f, 1f), SuffixLabel("x ship speed", true)]
        public float breakOffSpeedFactor = 0.85f;

        [TitleGroup("Duel")]
        [Tooltip("Disengage time after a shove or an abort, before the cooldown starts.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float breakOffSeconds = 1f;

        [TitleGroup("Duel")]
        [Tooltip("How long after a run ends before the patrol may commit to another one.")]
        [PropertyRange(0f, 60f), SuffixLabel("s", true)]
        public float attackRunCooldownSeconds = 8f;

        [TitleGroup("Duel")]
        [Tooltip("How far ahead of the SHIP the road has to be clear of ramps, landings, loops, tubes and the final run-up for an attack run to be allowed.")]
        [PropertyRange(50f, 1000f), SuffixLabel("m", true)]
        public float encounterLookaheadMeters = 300f;

        [TitleGroup("Duel")]
        [Tooltip("How far sideways the shove is sized to throw the ship — into the wall on a walled stretch, off the road on an open edge. The shove itself deals no damage; whatever it puts you into does.")]
        [PropertyRange(0f, 40f), SuffixLabel("m", true)]
        public float shoveMeters = 10f;
    }
}
