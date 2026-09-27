using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// The driving core every city AI car shares — corner speed, pedal gain,
    /// waypoint reach, lane discipline, junction choice, braking distances and
    /// the stuck recovery. The police (<see cref="PursuitSettings.driving"/>)
    /// and the traffic (<see cref="TrafficSettings.driving"/>) each author one:
    /// the same knobs with different values, so a cruiser and a taxi drive
    /// the same way and only their tuning differs (refactor Step 9.5 — the 11
    /// fields used to be declared twice, once per settings class). What only
    /// one fleet has (chase speed and ramming; cruise band, stops and the
    /// escape car) stays on its own settings.
    /// </summary>
    [Serializable]
    public class AiDrivingProfile
    {
        [Tooltip("Speed the car slows to for sharp turns — the 'slow into corners' dial.")]
        [PropertyRange(5f, 80f), SuffixLabel("km/h", true)]
        public float cornerSpeedKmh = 18f;

        [Tooltip("Throttle per km/h of speed error. Higher = twitchier pedal work.")]
        [PropertyRange(0.02f, 1f)]
        public float throttleGain = 0.15f;

        [Tooltip("A waypoint counts as reached inside this radius.")]
        [PropertyRange(2f, 20f), SuffixLabel("m", true)]
        public float waypointReachDistance = 6f;

        [Tooltip("Right-hand lane discipline: fraction of a cell kept to the right of the road centre, so cars pass oncoming traffic instead of blocking it. (A chasing cruiser ignores lanes.)")]
        [PropertyRange(0f, 0.35f)]
        public float laneOffsetFraction = 0.18f;

        [Tooltip("Absolute cap on the lane offset — keeps very wide cells from pushing the lane onto the sidewalk. On the city's ~37 m cells the fraction above lands under this cap.")]
        [PropertyRange(1f, 12f), SuffixLabel("m", true)]
        public float laneOffsetMaxMeters = 8f;

        [Tooltip("Chance the car carries straight on through a junction when it can — the rest of the time it turns. Reverse is never picked outside dead ends.")]
        [PropertyRange(0f, 1f)]
        public float straightBias = 0.45f;

        [Tooltip("Brake when another car sits within this distance dead ahead.")]
        [PropertyRange(2f, 30f), SuffixLabel("m", true)]
        public float forwardBrakeDistance = 9f;

        [Tooltip("Emergency wall brake: stop only when a static obstacle sits closer than this on the forward ray (head-on and while not mid-turn) — smaller than the vehicle brake distance so buildings at junctions don't stall the car.")]
        [PropertyRange(1f, 8f), SuffixLabel("m", true)]
        public float wallBrakeDistance = 3.5f;

        [Tooltip("Seconds of wanting to move while standing still before the car decides it is stuck.")]
        [PropertyRange(0.5f, 5f), SuffixLabel("s", true)]
        public float stuckSeconds = 1.5f;

        [Tooltip("How long a stuck car reverses (with opposite steering) before replanning.")]
        [PropertyRange(0.5f, 4f), SuffixLabel("s", true)]
        public float reverseSeconds = 1.4f;

        [Tooltip("Last resort: a car that has made no net progress this long (reverse-crash loops included) is snapped onto the nearest road cell instead of grinding forever.")]
        [PropertyRange(5f, 30f), SuffixLabel("s", true)]
        public float hardRecoverSeconds = 15f;

        /// <summary>The right-hand lane offset on a grid of <paramref name="cellSize"/>: the fraction of a cell, capped.</summary>
        public float LaneOffset(float cellSize) => Mathf.Min(cellSize * laneOffsetFraction, laneOffsetMaxMeters);

        /// <summary>The police's authored defaults.</summary>
        public static AiDrivingProfile PursuitDefaults() => new();

        /// <summary>The traffic's authored defaults: gentler pedal, earlier braking, more patience.</summary>
        public static AiDrivingProfile TrafficDefaults() => new()
        {
            cornerSpeedKmh = 14f,
            throttleGain = 0.12f,
            straightBias = 0.55f,
            forwardBrakeDistance = 10f,
            stuckSeconds = 2f,
            reverseSeconds = 1.2f,
            hardRecoverSeconds = 12f,
        };
    }
}
