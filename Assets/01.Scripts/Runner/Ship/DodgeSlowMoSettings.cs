using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The tunables of <see cref="DodgeSlowMo"/>, and nothing else: the clutch
    /// dash's slow motion keeps its own asset so it never grows
    /// <c>GameSettings</c>. Drawn inline on the component on <c>PF_Ship</c>,
    /// read live, never cloned. Gameplay never writes it.
    /// </summary>
    [CreateAssetMenu(fileName = "DodgeSlowMo_Settings", menuName = "FiniteRunner/Dodge Slow-Mo Settings")]
    public class DodgeSlowMoSettings : ScriptableObject
    {
        [TitleGroup("Trigger")]
        [Tooltip("Master switch. Off, a dash never slows the clock.")]
        public bool enabled = true;

        [TitleGroup("Trigger")]
        [Tooltip("How close a hit must be, in game seconds, for the dash that avoids it to count as a last-second dodge.")]
        [PropertyRange(0.1f, 3f), SuffixLabel("s", true)]
        public float threatWindowSeconds = 1f;

        [TitleGroup("Trigger")]
        [Tooltip("Oncoming cars count as threats.")]
        public bool dodgeTraffic = true;

        [TitleGroup("Trigger")]
        [Tooltip("Laser beams count as threats.")]
        public bool dodgeLasers = true;

        [TitleGroup("Trigger")]
        [Tooltip("Added to the ship's half width and half height in the forecast — a near-miss by less than this still counts as a threat on the old line, and the new line must clear by it.")]
        [PropertyRange(0f, 5f), SuffixLabel("m", true)]
        public float extraReach = 0.5f;

        [TitleGroup("Trigger")]
        [Tooltip("Below this speed a dash never slows the clock.")]
        [PropertyRange(0f, 3000f), SuffixLabel("km/h", true)]
        public float minSpeedKmh = 200f;

        [TitleGroup("Trigger")]
        [Tooltip("Real seconds after a slow-mo ends before another can start.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float cooldownSeconds = 1f;

        [TitleGroup("Trigger")]
        [Tooltip("Slice of the forecast against a spinning rotor gate, game seconds. Smaller is finer and costs more.")]
        [PropertyRange(0.01f, 0.2f), SuffixLabel("s", true)]
        public float forecastStepSeconds = 0.05f;

        [TitleGroup("Clock")]
        [Tooltip("The world clock during the dodge.")]
        [PropertyRange(0.05f, 1f)]
        public float timeScale = 0.3f;

        [TitleGroup("Clock")]
        [Tooltip("Ease into the slow-mo, real seconds.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        public float blendInSeconds = 0.06f;

        [TitleGroup("Clock")]
        [Tooltip("Ease back to full speed after the dash, real seconds.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float blendOutSeconds = 0.2f;

        public const float KmhPerMs = 3.6f;
    }
}
