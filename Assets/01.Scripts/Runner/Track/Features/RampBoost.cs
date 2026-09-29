using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// The takeoff boost a jump ramp gives, authored ON the ramp prefab
    /// (<c>PF_RampBase</c>). <see cref="TrackGenerator"/> reads it when it builds a
    /// jump and hands the numbers to the <see cref="JumpRamp"/>: taking the ramp pays
    /// <see cref="GreenOrbShare"/> of a green orb's boost, and a barrel roll in the
    /// flight pays <see cref="BarrelRollBonus"/> more of the same amount ON LANDING. A
    /// picture-only component — a prefab without it falls back to the jump entry's
    /// own multiplier.
    /// </summary>
    public class RampBoost : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Takeoff boost as a share of a green orb's boost (0.8 = 80 %). The whole jump pays this.")]
        [PropertyRange(0f, 2f)]
        float greenOrbShare = 0.8f;

        [SerializeField]
        [Tooltip("Extra share of the takeoff boost paid on landing if the ship barrel-rolled during the flight (0.2 = jump 100 %, jump + roll 120 %).")]
        [PropertyRange(0f, 1f)]
        float barrelRollBonus = 0.2f;

        public float GreenOrbShare => greenOrbShare;
        public float BarrelRollBonus => barrelRollBonus;
    }
}
