using Sirenix.OdinInspector;
using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Defines a ship's identity, tuning stats and movement behaviour.
    /// The ship never accelerates on its own: it launches with an initial
    /// impulse and constantly bleeds speed until a pad (or card effect)
    /// feeds it more. When speed reaches zero, the run is over. Speed has
    /// no upper cap — reaching Light Speed is the win condition.
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
        [Tooltip("Speed lost per second when not touching any pad. This is the core pressure of the game.")]
        [PropertyRange(0f, 50f), SuffixLabel("m/s per s", true)]
        public float passiveDeceleration = 3f;

        [TitleGroup("Speed")]
        [Tooltip("How quickly external speed changes (pads, impulses) blend into the current speed, in speed units per second.")]
        [PropertyRange(0.01f, 200f), SuffixLabel("m/s per s", true)]
        public float acceleration = 40f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral movement speed across the track, in units per second, at full steer input.")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float lateralSpeed = 8f;

        [TitleGroup("Handling")]
        [Tooltip("Responsiveness of the steering. Higher values reach full lateral speed faster; low values feel heavy and drifty.")]
        [PropertyRange(0.01f, 30f)]
        public float handlingResponse = 8f;

        [TitleGroup("Dash")]
        [Tooltip("Dash power: how far one lateral dash carries the ship.")]
        [PropertyRange(2f, 30f), SuffixLabel("m", true)]
        public float dashDistance = 12f;

        [TitleGroup("Dash")]
        [Tooltip("Dash speed: how long the burst lasts. Shorter = snappier.")]
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

        [TitleGroup("Weight")]
        [Tooltip("Scales how much pads affect this ship. 1 = full effect, 2 = pads (boost AND brake) only apply half their effect.")]
        [PropertyRange(0.1f, 5f)]
        public float weight = 1f;

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

        /// <summary>Speed delta a pad of the given raw magnitude applies to this ship.</summary>
        public float ScalePadEffect(float rawMagnitude) => rawMagnitude / weight;
    }
}
