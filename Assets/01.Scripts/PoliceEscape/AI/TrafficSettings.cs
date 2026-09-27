using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// Every knob of the civilian traffic in one designer-facing asset:
    /// fleet size and the active radius around the player (the optimization
    /// contract — vehicles only exist inside it), the vehicle pool, shared
    /// driving feel and the random-stop behavior of work vehicles. Drawn
    /// inline by the TrafficManager and every TrafficCarInput.
    /// </summary>
    [CreateAssetMenu(fileName = "TrafficSettings", menuName = "PoliceEscape/Traffic Settings")]
    public class TrafficSettings : ScriptableObject
    {
        // --------------------------------------------------------------- fleet
        [TitleGroup("Fleet")]
        [Tooltip("How many civilian vehicles the TrafficManager keeps alive around the player.")]
        [PropertyRange(0, 300)]
        public int targetVehicleCount = 12;

        [TitleGroup("Fleet")]
        [Tooltip("Vehicles exist only within this radius of the player — the optimization dial. Spawns land between the minimum spawn distance and this.")]
        [PropertyRange(50f, 800f), SuffixLabel("m", true)]
        public float activeRadius = 250f;

        [TitleGroup("Fleet")]
        [Tooltip("Extra distance beyond the active radius before a vehicle is removed — hysteresis so cars on the boundary don't churn.")]
        [PropertyRange(10f, 200f), SuffixLabel("m", true)]
        public float despawnPadding = 50f;

        /// <summary>How far from the player a civilian may be before it is retired: the active radius plus the padding. The one formula — the manager culls by it, the streamer checks its load distance against it.</summary>
        public float DespawnReach => activeRadius + despawnPadding;

        [TitleGroup("Fleet")]
        [Tooltip("Vehicles never spawn closer than this, so they don't pop into view.")]
        [PropertyRange(10f, 200f), SuffixLabel("m", true)]
        public float minSpawnDistance = 60f;

        [TitleGroup("Fleet")]
        [Tooltip("At most this many vehicles spawn per maintenance tick — ramps the fleet in over a few seconds instead of rig-building the whole lot in one hitch frame.")]
        [PropertyRange(1, 10)]
        public int spawnsPerTick = 4;

        // ------------------------------------------------------------ vehicles
        [TitleGroup("Vehicles")]
        [Tooltip("Uniform scale applied to the kit models — 1.73 puts the sedan at real-car length, and trucks come out proportionally bigger.")]
        [PropertyRange(1f, 3f)]
        public float modelScale = 1.73f;

        [TitleGroup("Vehicles")]
        [Required]
        [Tooltip("Handling config shared by all civilians — same physics as everyone else, tamer driving comes from the speeds below.")]
        public CarConfig carConfig;

        [TitleGroup("Vehicles")]
        [TableList(AlwaysExpanded = true)]
        public List<TrafficVehicleDefinition> vehicles = new();

        // ------------------------------------------------------------- driving
        [TitleGroup("Driving")]
        [Tooltip("The driving core shared with the police (AiDrivingProfile): corners, pedal, waypoints, lanes, junctions, braking and the stuck recovery. Traffic's own driving (cruise band, yield whiskers) follows.")]
        [InlineProperty, HideLabel]
        public AiDrivingProfile driving = AiDrivingProfile.TrafficDefaults();

        [TitleGroup("Driving")]
        [Tooltip("Each vehicle picks its personal cruise speed from this band at spawn — traffic that isn't lockstep.")]
        [MinMaxSlider(5f, 80f, true), SuffixLabel("km/h", true)]
        public Vector2 cruiseSpeedBand = new(25f, 40f);

        [TitleGroup("Driving")]
        [Tooltip("Junction yield: a whisker ray this far right of forward sees crossing traffic the forward ray can't. Right-only = priority to the right — of two converging cars, exactly one yields.")]
        [PropertyRange(20f, 60f), SuffixLabel("°", true)]
        public float yieldWhiskerAngle = 40f;

        [TitleGroup("Driving")]
        [Tooltip("Length of the junction-yield whisker ray.")]
        [PropertyRange(3f, 15f), SuffixLabel("m", true)]
        public float yieldWhiskerDistance = 8f;

        public float CruiseMin => cruiseSpeedBand.x;
        public float CruiseMax => cruiseSpeedBand.y;

        // ---------------------------------------------------------- escape car
        [TitleGroup("Escape car")]
        [Tooltip("A fleeing car this far ahead of the player parks and waits — it must never outrun the streamed city (keep well inside the fleet's active radius).")]
        [PropertyRange(60f, 400f), SuffixLabel("m", true)]
        public float fleeHoldDistance = 180f;

        [TitleGroup("Escape car")]
        [Tooltip("Corner speed while fleeing — a getaway driver takes turns far harder than a civilian.")]
        [PropertyRange(10f, 80f), SuffixLabel("km/h", true)]
        public float fleeCornerSpeedKmh = 40f;

        // --------------------------------------------------------------- stops
        [TitleGroup("Stops")]
        [Tooltip("How long a stop-prone vehicle drives between stops (random per cycle).")]
        [MinMaxSlider(3f, 60f, true), SuffixLabel("s", true)]
        public Vector2 stopEveryBand = new(10f, 25f);

        [TitleGroup("Stops")]
        [Tooltip("How long it stays stopped (random per stop).")]
        [MinMaxSlider(1f, 15f, true), SuffixLabel("s", true)]
        public Vector2 stopDurationBand = new(2f, 6f);

        public float StopEveryMin => stopEveryBand.x;
        public float StopEveryMax => stopEveryBand.y;
        public float StopDurationMin => stopDurationBand.x;
        public float StopDurationMax => stopDurationBand.y;

        // --------------------------------------------------------------- debug
        [TitleGroup("Debug")]
        [Tooltip("Log traffic events (stuck escalations, hard recovers, vehicle contacts) to the console — for tuning sessions, off in normal play.")]
        public bool logTrafficEvents;
    }
}
