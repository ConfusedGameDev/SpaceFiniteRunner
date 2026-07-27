using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Owns the win condition: the ship must cross the goal as close as
    /// possible to the target speed. Grades the run into result tiers.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] TrackGenerator generator;
        [SerializeField] TuningScreen tuningScreen;

        [Header("Win condition")]
        [Tooltip("Speed the ship should have when crossing the goal, in km/h.")]
        [SerializeField, Min(1f)] float targetSpeedKmh = 400f;

        [Header("Result tiers — max % off target for each grade")]
        [SerializeField, Range(0f, 100f)] float perfectPercent = 2f;
        [SerializeField, Range(0f, 100f)] float awesomePercent = 5f;
        [SerializeField, Range(0f, 100f)] float clearPercent = 10f;
        [SerializeField, Range(0f, 100f)] float almostTherePercent = 20f;

        public float TargetSpeedKmh => targetSpeedKmh;
        public string ResultLabel { get; private set; }
        public bool RunOver => motor != null && (motor.HasFinished || motor.HasStopped);

        void Update()
        {
            if (motor == null || ResultLabel != null) return;

            if (motor.HasStopped)
            {
                ResultLabel = "FAIL — out of speed";
                return;
            }

            if (!motor.HasFinished) return;

            float finalKmh = motor.FinalSpeed * 3.6f;
            float offPercent = Mathf.Abs(finalKmh - targetSpeedKmh) / targetSpeedKmh * 100f;

            string grade =
                offPercent <= perfectPercent    ? "PERFECT" :
                offPercent <= awesomePercent    ? "AWESOME" :
                offPercent <= clearPercent      ? "CLEAR" :
                offPercent <= almostTherePercent ? "ALMOST THERE" :
                                                  "FAIL";

            ResultLabel = $"{grade}  —  {finalKmh:0} km/h (target {targetSpeedKmh:0})";
        }

        /// <summary>Resets the run; regenerates the track first when randomize is enabled.</summary>
        public void Restart()
        {
            ResultLabel = null;
            if (generator != null) generator.RegenerateIfRandom();
            motor.Launch();

            // Reopen ship setup so points can be re-allocated; it re-launches on START.
            if (tuningScreen != null) tuningScreen.Show();
        }
    }
}
