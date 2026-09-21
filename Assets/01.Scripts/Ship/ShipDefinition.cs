using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Defines a ship's identity, tuning stats and movement behaviour.
    /// Speed model: the ship launches with an initial impulse, the throttle
    /// accelerates it up to <see cref="cruiseSpeed"/> and no further, the
    /// brake slows it, and ONLY pads and orbs push it past cruise — where
    /// the passive bleed pulls it back down. Speed has no upper cap, so
    /// orbs remain the way to Light Speed, the win condition.
    /// Every stat is an Odin slider with a hand-picked range so ships can be
    /// felt out by dragging instead of guessing numbers.
    /// </summary>
    [CreateAssetMenu(fileName = "ShipDefinition", menuName = "FiniteRunner/Ship Definition")]
    public class ShipDefinition : ScriptableObject
    {
        [TitleGroup("Identity")]
        public string displayName = "Fighter";
        [TitleGroup("Identity"), MultiLineProperty(3), HideLabel]
        public string description;

        [TitleGroup("Speed")]
        [Tooltip("Speed the ship launches with at the start of a run.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m/s", true)]
        public float initialImpulse = 25f;

        [TitleGroup("Speed")]
        [Tooltip("Top speed the throttle alone can reach. Only orbs and pads go past it.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m/s", true)]
        public float cruiseSpeed = 250f;

        [TitleGroup("Speed")]
        [Tooltip("Acceleration at full throttle while below cruise.")]
        [PropertyRange(0f, 300f), SuffixLabel("m/s per s", true)]
        public float thrust = 60f;

        [TitleGroup("Speed")]
        [Tooltip("Deceleration at full brake, at any speed.")]
        [PropertyRange(0f, 500f), SuffixLabel("m/s per s", true)]
        public float brakeDecel = 140f;

        [TitleGroup("Speed")]
        [Tooltip("Speed lost per second below cruise with the throttle released.")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s per s", true)]
        public float coastDrag = 20f;

        [TitleGroup("Speed")]
        [Tooltip("Over-cruise bleed: speed lost per second ABOVE cruise, pulling a boosted ship back down to it. This is the core pressure of the game — boosts must be chained to climb.")]
        [PropertyRange(0f, 50f), SuffixLabel("m/s per s", true)]
        public float passiveDeceleration = 3f;

        [TitleGroup("Speed")]
        [Tooltip("Seconds a throttle or brake KEY takes to ease from nothing to full, so keys are not a harsh 0/1 next to the analog triggers. 0 = instant.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        public float digitalThrottleRampSeconds = 0.15f;

        [TitleGroup("Speed")]
        [Tooltip("How quickly external speed changes (pads, impulses) blend into the current speed, in speed units per second.")]
        [PropertyRange(0.01f, 200f), SuffixLabel("m/s per s", true)]
        public float acceleration = 40f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral speed full steer settles at. Steering is a force against a lateral drag (Handling Response): the force is this × the response, so this stays the top speed across the track whatever the response.")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float lateralSpeed = 8f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral drag, 1/s: full lateral speed is reached in about 1 / this seconds, and a slide dies away at the same rate. Higher = snappier and harder to throw off line; low values feel heavy and drifty.")]
        [PropertyRange(0.01f, 30f)]
        public float handlingResponse = 8f;

        [TitleGroup("Handling")]
        [Tooltip("Grip at a standstill: the lateral acceleration a FLAT sweep may demand before the ship slides outward. Banked sweeps never test it.")]
        [PropertyRange(0f, 500f), SuffixLabel("m/s²", true)]
        public float gripBase = 50f;

        [TitleGroup("Handling")]
        [Tooltip("Extra grip per m/s of speed. The curve's demand grows with speed SQUARED and the grip only with speed, so there is always a speed past which a flat sweep throws the ship — the defaults hold a 490 m sweep up to about 1.3 × cruise. Braking is the answer.")]
        [PropertyRange(0f, 3f), SuffixLabel("m/s² per m/s", true)]
        public float gripPerSpeed = 0.5f;

        [TitleGroup("Handling")]
        [Tooltip("Outward lateral speed past which slipping counts as a SLIDE: shake, rumble, and the speed loss below.")]
        [PropertyRange(0f, 30f), SuffixLabel("m/s", true)]
        public float slideThreshold = 4f;

        [TitleGroup("Handling")]
        [Tooltip("Fraction of the ship's speed a slide scrubs off per second.")]
        [PropertyRange(0f, 1f), SuffixLabel("per s", true)]
        public float slideSpeedLoss = 0.1f;

        [TitleGroup("Dash")]
        [Tooltip("Dash power: how far one lateral dash carries the ship. The dash is a physical shove — a burst of lateral velocity of this × Handling Response, which the lateral drag then eats over exactly this distance — so steering can fight it and it can carry the ship over an open edge.")]
        [PropertyRange(2f, 30f), SuffixLabel("m", true)]
        public float dashDistance = 12f;

        [TitleGroup("Dash")]
        [Tooltip("How long the ship counts as dashing after the shove: the ghost trail's window, and no new dash can start inside it. (How snappy the shove itself is comes from Handling Response — it is mostly over in 2 / response seconds.)")]
        [PropertyRange(0.05f, 1f), SuffixLabel("s", true)]
        public float dashDuration = 0.25f;

        [TitleGroup("Dash")]
        [Tooltip("Fill rate: seconds for the dash meter to recharge from empty to full. The meter starts every run empty.")]
        [PropertyRange(1f, 60f), SuffixLabel("s", true)]
        public float dashRechargeSeconds = 8f;

        [TitleGroup("Dash")]
        [Tooltip("Onion-skin ghosts left behind over one dash.")]
        [PropertyRange(1, 20)]
        public int dashGhostCount = 6;

        [TitleGroup("Dash")]
        [Tooltip("Airborne dash: seconds one full barrel roll takes. In the air the dash IS a barrel roll — the sideways burst is spread over the roll so the two read as one move.")]
        [PropertyRange(0.2f, 1.5f), SuffixLabel("s", true)]
        public float barrelRollSeconds = 0.5f;

        [TitleGroup("Weight")]
        [Tooltip("Scales how much pads affect this ship. 1 = full effect, 2 = pads (boost AND brake) only apply half their effect.")]
        [PropertyRange(0.1f, 5f)]
        public float weight = 1f;

        [TitleGroup("Jumps")]
        [Tooltip("Scales a ramp takeoff: the boost at the lip AND the arc's length and height. 1 = the JumpDefinition as authored; the Store's Jump Strength upgrade multiplies this on the run's clone.")]
        [PropertyRange(0.5f, 3f)]
        public float jumpStrength = 1f;

        [TitleGroup("Hover (visual only)")]
        [Tooltip("How high the ship model floats above the flight line (visual only — pad detection is unaffected).")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        public float hoverHeight = 2f;

        [TitleGroup("Hover (visual only)")]
        [Tooltip("How far the ship bobs up and down around the hover height.")]
        [PropertyRange(0f, 3f), SuffixLabel("m", true)]
        public float bobAmplitude = 0.35f;

        [TitleGroup("Hover (visual only)")]
        [Tooltip("How fast the hover bobbing moves.")]
        [PropertyRange(0f, 10f), SuffixLabel("Hz", true)]
        public float bobFrequency = 1.5f;

        [TitleGroup("Hover (visual only)")]
        [Tooltip("Maximum nose pitch wobble from the hover, in degrees.")]
        [PropertyRange(0f, 10f), SuffixLabel("deg", true)]
        public float hoverPitchDegrees = 2.5f;

        [TitleGroup("Feel")]
        [Tooltip("Maximum roll angle in degrees when steering at full input.")]
        [PropertyRange(0f, 90f), SuffixLabel("deg", true)]
        public float maxBankAngle = 35f;

        [TitleGroup("Feel")]
        [Tooltip("How fast the ship rolls into / out of a bank.")]
        [PropertyRange(0.01f, 20f)]
        public float bankResponse = 6f;

        /// <summary>Lateral velocity of a dash's shove, m/s: sized so the lateral drag (<see cref="handlingResponse"/>) stops it after exactly <see cref="dashDistance"/>.</summary>
        public float DashImpulse => dashDistance * handlingResponse;

        /// <summary>Speed delta a pad of the given raw magnitude applies to this ship.</summary>
        public float ScalePadEffect(float rawMagnitude) => rawMagnitude / weight;
    }
}
