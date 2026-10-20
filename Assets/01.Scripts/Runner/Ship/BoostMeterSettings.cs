using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The tunables of <see cref="BoostMeter"/>, and nothing else: the held
    /// boost keeps its own asset so it never grows <c>GameSettings</c>. Drawn
    /// inline on the component on <c>PF_Ship</c>, read live, never cloned.
    /// Gameplay never writes it; the debug menu's BOOST METER page does.
    /// </summary>
    [CreateAssetMenu(fileName = "BoostMeter_Settings", menuName = "FiniteRunner/Boost Meter Settings")]
    public class BoostMeterSettings : ScriptableObject
    {
        [TitleGroup("Boost")]
        [Tooltip("Master switch. Off, Boost does nothing and the HUD hides the bar.")]
        public bool meterEnabled = true;

        [TitleGroup("Boost")]
        [Tooltip("Extra thrust while Boost is held. It can push past cruise; above cruise the ship's passive bleed still pulls against it, and takes the speed back once Boost is let go.")]
        [PropertyRange(0f, 1500f), SuffixLabel("km/h per s", true)]
        public float potenceKmhPerSecond = 300f;

        [TitleGroup("Meter")]
        [Tooltip("Share of the meter a second of boosting uses. 0.25 = four seconds from full to empty.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("/ s", true)]
        public float drainPerSecond = 0.25f;

        [TitleGroup("Meter")]
        [Tooltip("Seconds Boost must go unpressed before the meter starts refilling. Every press — an empty one too — starts it again.")]
        [PropertyRange(0f, 15f), SuffixLabel("s", true)]
        public float cooldownSeconds = 5f;

        [TitleGroup("Meter")]
        [Tooltip("Share of the meter refilled per second once the cooldown is over.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("/ s", true)]
        public float refillPerSecond = 0.2f;

        [TitleGroup("Feedback")]
        [Tooltip("Rumble for a press on an empty meter: low motor, high motor, seconds.")]
        public Vector3 emptyPressRumble = new(0.5f, 0.2f, 0.15f);

        public const float KmhPerMs = 3.6f;
    }
}
