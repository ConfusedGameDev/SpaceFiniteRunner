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
        [Tooltip("How many police cars the PatrolManager keeps alive around the player. Lowering it retires the extras on the next maintenance tick.")]
        [PropertyRange(0, 25)]
        public int targetPatrolCount = 5;

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

        /// <summary>
        /// How far from the player a patrol may be before it is retired: the
        /// despawn distance, but never inside the spawn band (+50 m of slack),
        /// or a fresh patrol would be culled the tick it spawns. The one
        /// formula — the manager culls by it, the streamer checks its load
        /// distance against it.
        /// </summary>
        public float DespawnReach => Mathf.Max(despawnDistance, SpawnDistanceMax + 50f);

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
        [Tooltip("The driving core shared with the traffic (AiDrivingProfile): corners, pedal, waypoints, lanes, junctions, braking and the stuck recovery. The police's own driving (patrol and chase speeds, repath, lead) follows.")]
        [InlineProperty, HideLabel]
        public AiDrivingProfile driving = AiDrivingProfile.PursuitDefaults();

        [TitleGroup("Driving")]
        [Tooltip("Cruise speed while wandering on Patrol.")]
        [PropertyRange(10f, 100f), SuffixLabel("km/h", true)]
        public float patrolSpeedKmh = 35f;

        [TitleGroup("Driving")]
        [Tooltip("Target speed while chasing the player.")]
        [PropertyRange(20f, 250f), SuffixLabel("km/h", true)]
        public float chaseSpeedKmh = 90f;

        [TitleGroup("Driving")]
        [Tooltip("Seconds between route recomputations while chasing.")]
        [PropertyRange(0.2f, 5f), SuffixLabel("s", true)]
        public float repathInterval = 1f;

        [TitleGroup("Driving")]
        [Tooltip("Seconds of player velocity added to the chase target — aim where the player is going, not where they are.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float predictionLead = 0.6f;

        // ------------------------------------------------------------- ramming
        [TitleGroup("Ramming")]
        [Tooltip("A charging cruiser never drops below this speed while the player sits in its front arc — so a slow or parked player is still hit hard instead of nosed up to at corner speed.")]
        [PropertyRange(10f, 120f), SuffixLabel("km/h", true)]
        public float ramMinSpeedKmh = 45f;

        [TitleGroup("Ramming")]
        [Tooltip("A charge is spent when the cruiser sits inside the LOW end of this band and has stopped closing on the player; it then reverses until it is the HIGH end away and charges again — the back-up-and-hit-again rhythm.")]
        [MinMaxSlider(3f, 40f, true), SuffixLabel("m", true)]
        public Vector2 ramBackoffBand = new(8f, 18f);

        [TitleGroup("Ramming")]
        [Tooltip("Closing speed below which a cruiser touching the player counts its charge as spent (shoving a slow player along closes at ~0). Only applies while its own speed is under the ram floor — a cruiser still at full tilt is never pulled out of a charge, and one being out-run keeps chasing.")]
        [PropertyRange(2f, 40f), SuffixLabel("km/h", true)]
        public float ramStallSpeedKmh = 12f;

        [TitleGroup("Ramming")]
        [Tooltip("Longest a back-off reverse lasts before the cruiser charges from wherever it got to — a wall or another car behind it must not stall the fight.")]
        [PropertyRange(0.5f, 5f), SuffixLabel("s", true)]
        public float ramBackoffMaxSeconds = 2.5f;

        /// <summary>Inside this distance a stalled cruiser calls its charge spent and backs off.</summary>
        public float RamContactDistance => ramBackoffBand.x;
        /// <summary>A backing-off cruiser reverses until it is this far from the player, then charges again.</summary>
        public float RamBackoffDistance => ramBackoffBand.y;

        // --------------------------------------------------------------- siren
        [ToggleGroup("sirenEnabled", "Siren")]
        [Tooltip("Every cruiser wails while it is in Chase — Patrol and Search are silent, so the siren is the 'spotted' cue. Off = the fleet hunts mute.")]
        public bool sirenEnabled = true;

        [ToggleGroup("sirenEnabled")]
        [Tooltip("The siren loop, played 3D from the car on the FX bus (ducked by pause / loading / cinema, scaled by the SFX slider). Empty = silent.")]
        public AudioClip sirenClip;

        [ToggleGroup("sirenEnabled")]
        [Tooltip("Source volume at full wail. The SFX slider sits on top of this.")]
        [PropertyRange(0f, 1f)]
        public float sirenVolume = 0.7f;

        [ToggleGroup("sirenEnabled")]
        [Tooltip("Linear rolloff band: full volume inside the near distance, silent at the far one — how far away the player hears the police coming.")]
        [MinMaxSlider(1f, 400f, true), SuffixLabel("m", true)]
        public Vector2 sirenDistanceBand = new(8f, 160f);

        [ToggleGroup("sirenEnabled")]
        [Tooltip("Seconds the wail takes to rise when a chase starts and to fall when it ends (lost, dead, or the run is over). Real time.")]
        [PropertyRange(0f, 3f), SuffixLabel("s", true)]
        public float sirenFadeSeconds = 0.4f;

        /// <summary>Inside this distance the siren plays at full volume (band X).</summary>
        public float SirenNearDistance => sirenDistanceBand.x;
        /// <summary>Beyond this distance the siren is silent (band Y).</summary>
        public float SirenFarDistance => sirenDistanceBand.y;
    }
}
