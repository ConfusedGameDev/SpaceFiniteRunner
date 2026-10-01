using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Traffic
{
    /// <summary>
    /// One kind of oncoming car: the model it shows, how often it is picked,
    /// how it sits on the road and how big it is for contact. The model's own
    /// transform is discarded (a prefab dragged out of a scene carries that
    /// scene's pose) and every collider under it is stripped — traffic is
    /// analytic, like the patrol.
    /// </summary>
    [Serializable]
    public class TrafficVehicle
    {
        [Tooltip("The car's model. Instantiated under the pooled car with its transform reset and every collider stripped.")]
        [Required, TableColumnWidth(160)]
        public GameObject prefab;

        [Tooltip("Share of the picks, relative to the other entries (they need not sum to anything).")]
        [PropertyRange(0f, 10f), TableColumnWidth(70)]
        public float weight = 1f;

        [Tooltip("Size of the model. The Cyberpunk air-traffic cars are about 5 m long at 1; ~3.5 makes them as long as the ship.")]
        [PropertyRange(0.1f, 10f), TableColumnWidth(70)]
        public float scale = 3.5f;

        [Tooltip("Yaw that turns the model's nose down its own +Z. The Cyberpunk models are built along X and want -90.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true), TableColumnWidth(80)]
        public float yawOffset = -90f;

        [Tooltip("Height of the car above the road, along the track's up.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true), TableColumnWidth(70)]
        public float hoverHeight = 2f;

        [Tooltip("Half the car's length along the track, for contact.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true), TableColumnWidth(70)]
        public float halfLength = 9f;

        [Tooltip("Half the car's width across the track, for contact and for the gap it needs past an obstacle.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("m", true), TableColumnWidth(70)]
        public float halfWidth = 4f;

        [Tooltip("The car's height above its hover line, for contact: a ship flying higher than hover + this clears it.")]
        [PropertyRange(0.5f, 15f), SuffixLabel("m", true), TableColumnWidth(70)]
        public float height = 5f;

        public bool IsSpawnable => prefab != null && weight > 0f;
    }

    /// <summary>
    /// Every tunable of the runner's oncoming traffic (OncomingTrafficPRD.md):
    /// the cars it can show, how many are on the road at once, where they
    /// spawn and recycle, and how fast they drive. A level opts in through
    /// <c>RunnerLevelDefinition.traffic</c>; the <see cref="TrafficSystem"/>
    /// clones this at bind and only ever reads the clone, so gameplay never
    /// writes the asset. All speeds are m/s.
    /// </summary>
    [CreateAssetMenu(fileName = "TrafficDefinition", menuName = "FiniteRunner/Traffic Definition")]
    public class TrafficDefinition : ScriptableObject
    {
        [TitleGroup("Vehicles")]
        [Tooltip("The cars traffic picks from, by weight. Every entry is prewarmed maxActive times when a level binds, so a long list costs memory, not hitches.")]
        [TableList(AlwaysExpanded = true)]
        public List<TrafficVehicle> vehicles = new();

        [TitleGroup("Fleet")]
        [Tooltip("Cars on the road at once.")]
        [PropertyRange(0, 24)]
        public int maxActive = 8;

        [TitleGroup("Fleet")]
        [Tooltip("Least gap between a new car and the nearest one already on the road, metres — rolled per spawn inside this band.")]
        [MinMaxSlider(20f, 600f, true)]
        public Vector2 spawnSpacing = new(80f, 220f);

        [TitleGroup("Fleet")]
        [Tooltip("A new car appears this many seconds of the ship's travel ahead of it, so it is past the fog at any speed. Clamped to the built road.")]
        [PropertyRange(1f, 10f), SuffixLabel("s", true)]
        public float spawnAheadSeconds = 4f;

        [TitleGroup("Fleet")]
        [Tooltip("Never spawn closer than this ahead of the ship. When the built road does not reach this far, the spawn waits.")]
        [PropertyRange(200f, 2000f), SuffixLabel("m", true)]
        public float minSpawnAhead = 700f;

        [TitleGroup("Fleet")]
        [Tooltip("A car goes back to the pool once it is this far behind the ship.")]
        [PropertyRange(5f, 200f), SuffixLabel("m", true)]
        public float despawnBehind = 40f;

        [TitleGroup("Fleet")]
        [Tooltip("No new car once the ship has this much track or less left. Cars already on the road drive on; none ever spawns on the final run-up.")]
        [PropertyRange(0f, 5000f), SuffixLabel("m", true)]
        public float noSpawnNearEnd = 1500f;

        [TitleGroup("Fleet")]
        [Tooltip("When HIT HYPERSPACE clears the road, cars this close ahead of the ship explode (harmlessly); the ones further off just vanish.")]
        [PropertyRange(100f, 2000f), SuffixLabel("m", true)]
        public float visibleRange = 600f;

        [TitleGroup("Driving")]
        [Tooltip("Cruise speed of a car toward the ship, rolled once per spawn inside this band.")]
        [MinMaxSlider(10f, 200f, true), SuffixLabel("m/s", true)]
        public Vector2 speedBand = new(40f, 90f);

        [TitleGroup("Driving")]
        [Tooltip("How fast a car changes lane, metres per second across the road.")]
        [PropertyRange(1f, 30f), SuffixLabel("m/s", true)]
        public float lateralSpeed = 8f;

        [TitleGroup("Driving")]
        [Tooltip("How far down the road a car plans round ramps and laser gates, metres. Long enough for the lane change to finish: at the slowest car speed it must cover the widest move at the lateral speed above.")]
        [PropertyRange(20f, 400f), SuffixLabel("m", true)]
        [InfoBox("Too short for the slowest car to change lane across half the road — cars may clip obstacles.", InfoMessageType.Warning, nameof(LookaheadTooShort))]
        public float avoidLookahead = 120f;

        [TitleGroup("Driving")]
        [Tooltip("Clearance a car keeps from a ramp's side or a laser's beam and emitters, metres, on top of its own half width.")]
        [PropertyRange(0f, 6f), SuffixLabel("m", true)]
        public float avoidMargin = 1.5f;

        [TitleGroup("Driving")]
        [Tooltip("Clearance a car keeps from an edge with no wall, metres, on top of its own half width. Cars never fall.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        public float edgeMargin = 4f;

        [TitleGroup("Impact")]
        [Tooltip("Size of a car's explosion, as a multiple of the ship's own (GameSettings explosion). The textures, lifetime and particle count are the ship's.")]
        [PropertyRange(0.2f, 3f), SuffixLabel("x ship explosion", true)]
        public float explosionScale = 0.8f;

        // Half a typical road (30 m) at the slowest speed: the move must fit in the lookahead's time.
        bool LookaheadTooShort => MinSpeed > 0f && lateralSpeed * avoidLookahead / MinSpeed < 15f;

        /// <summary>Least gap to the nearest car at spawn (band X), metres.</summary>
        public float MinSpawnSpacing => spawnSpacing.x;
        /// <summary>Most gap to the nearest car at spawn (band Y), metres.</summary>
        public float MaxSpawnSpacing => spawnSpacing.y;
        /// <summary>Slowest car, m/s (band X).</summary>
        public float MinSpeed => speedBand.x;
        /// <summary>Fastest car, m/s (band Y).</summary>
        public float MaxSpeed => speedBand.y;

        /// <summary>True when at least one entry can be spawned.</summary>
        public bool HasVehicles
        {
            get
            {
                if (vehicles == null) return false;
                foreach (var v in vehicles)
                    if (v != null && v.IsSpawnable) return true;
                return false;
            }
        }
    }
}
