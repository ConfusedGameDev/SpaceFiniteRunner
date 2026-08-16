using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// Every difficulty and behavior knob of the police in one designer-facing
    /// asset: fleet size, detection, chase driving and recovery. Drawn inline
    /// by the PatrolManager and every PoliceCarInput, so pursuit balancing
    /// happens live in play mode. Escalation over time (wanted levels) will
    /// layer on top of these later.
    /// </summary>
    [CreateAssetMenu(fileName = "PursuitSettings", menuName = "PoliceEscape/Pursuit Settings")]
    public class PursuitSettings : ScriptableObject
    {
        // --------------------------------------------------------------- fleet
        [TitleGroup("Fleet")]
        [Tooltip("How many police cars the PatrolManager keeps alive around the player.")]
        [PropertyRange(0, 8)]
        public int targetPatrolCount = 2;

        [TitleGroup("Fleet")]
        [Tooltip("Patrols spawn on a road cell this far from the player: min keeps them out of plain sight, max keeps them relevant.")]
        [MinMaxSlider(30f, 600f, true), SuffixLabel("m", true)]
        public Vector2 spawnDistanceBand = new(100f, 250f);

        [TitleGroup("Fleet")]
        [Tooltip("A patrol farther than this from the player is removed (a fresh one spawns closer).")]
        [PropertyRange(100f, 1500f), SuffixLabel("m", true)]
        public float despawnDistance = 450f;

        public float SpawnDistanceMin => spawnDistanceBand.x;
        public float SpawnDistanceMax => spawnDistanceBand.y;

        // ----------------------------------------------------------- detection
        [TitleGroup("Detection")]
        [Tooltip("Maximum distance at which a patrol can spot the player — still needs line of sight (buildings block it).")]
        [PropertyRange(10f, 300f), SuffixLabel("m", true)]
        public float detectionRange = 90f;

        [TitleGroup("Detection")]
        [Tooltip("Seconds of broken line of sight before a chasing patrol drops to Search.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float loseSightSeconds = 3f;

        [TitleGroup("Detection")]
        [Tooltip("How long a patrol sweeps around the player's last known position before giving up back to Patrol — this window is what makes escaping feel earned.")]
        [PropertyRange(3f, 60f), SuffixLabel("s", true)]
        public float searchDuration = 15f;

        // ------------------------------------------------------------- driving
        [TitleGroup("Driving")]
        [Tooltip("Cruise speed while wandering on Patrol.")]
        [PropertyRange(10f, 100f), SuffixLabel("km/h", true)]
        public float patrolSpeedKmh = 35f;

        [TitleGroup("Driving")]
        [Tooltip("Target speed while chasing the player.")]
        [PropertyRange(20f, 250f), SuffixLabel("km/h", true)]
        public float chaseSpeedKmh = 90f;

        [TitleGroup("Driving")]
        [Tooltip("Speed the AI slows to for sharp turns — the 'slow into corners' dial.")]
        [PropertyRange(5f, 80f), SuffixLabel("km/h", true)]
        public float cornerSpeedKmh = 18f;

        [TitleGroup("Driving")]
        [Tooltip("Throttle per km/h of speed error. Higher = twitchier pedal work.")]
        [PropertyRange(0.02f, 1f)]
        public float throttleGain = 0.15f;

        [TitleGroup("Driving")]
        [Tooltip("A waypoint counts as reached inside this radius.")]
        [PropertyRange(2f, 20f), SuffixLabel("m", true)]
        public float waypointReachDistance = 6f;

        [TitleGroup("Driving")]
        [Tooltip("Right-hand lane discipline while patrolling/searching: fraction of a cell kept to the right of the road center, so cruisers pass oncoming traffic instead of blocking it. Chase ignores lanes.")]
        [PropertyRange(0f, 0.35f)]
        public float laneOffsetFraction = 0.18f;

        [TitleGroup("Driving")]
        [Tooltip("Seconds between route recomputations while chasing.")]
        [PropertyRange(0.2f, 5f), SuffixLabel("s", true)]
        public float repathInterval = 1f;

        [TitleGroup("Driving")]
        [Tooltip("Seconds of player velocity added to the chase target — aim where the player is going, not where they are.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float predictionLead = 0.6f;

        [TitleGroup("Driving")]
        [Tooltip("Brake when another car sits within this distance dead ahead — the v1 anti-pileup rule (proper avoidance comes later).")]
        [PropertyRange(2f, 30f), SuffixLabel("m", true)]
        public float forwardBrakeDistance = 9f;

        [TitleGroup("Driving")]
        [Tooltip("Emergency wall brake: stop only when a static obstacle sits closer than this on the forward ray (head-on and while not mid-turn) — smaller than the vehicle brake distance so buildings at junctions don't stall the cruiser.")]
        [PropertyRange(1f, 8f), SuffixLabel("m", true)]
        public float wallBrakeDistance = 3.5f;

        // ------------------------------------------------------------ recovery
        [TitleGroup("Recovery")]
        [Tooltip("Seconds of wanting to move while standing still before the patrol decides it is stuck.")]
        [PropertyRange(0.5f, 5f), SuffixLabel("s", true)]
        public float stuckSeconds = 1.5f;

        [TitleGroup("Recovery")]
        [Tooltip("How long a stuck patrol reverses (with opposite steering) before replanning.")]
        [PropertyRange(0.5f, 4f), SuffixLabel("s", true)]
        public float reverseSeconds = 1.4f;

        [TitleGroup("Recovery")]
        [Tooltip("Last resort outside Chase: a patrol that has made no net progress this long (reverse-crash loops included) is snapped onto the nearest road cell instead of grinding forever.")]
        [PropertyRange(5f, 30f), SuffixLabel("s", true)]
        public float hardRecoverSeconds = 15f;
    }
}
